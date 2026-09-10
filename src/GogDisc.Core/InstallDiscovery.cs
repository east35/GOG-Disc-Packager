using Microsoft.Win32;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GogDisc.Core;

public static class InstallDiscovery
{
    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public static InstallState? Discover(PackageManifest package)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var saved = InstallStateStore.Load(package.PackageId);
        if (saved is not null && IsUseful(saved)) return saved;

        var names = package.InstallDetectionNames.Append(package.Title).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var rootName in UninstallRoots)
                {
                    using var root = baseKey.OpenSubKey(rootName);
                    if (root is null) continue;
                    foreach (var subName in root.GetSubKeyNames())
                    {
                        using var entry = root.OpenSubKey(subName);
                        var displayName = entry?.GetValue("DisplayName") as string;
                        if (displayName is null || !names.Any(name => FuzzyContains(displayName, name))) continue;
                        var installLocation = entry?.GetValue("InstallLocation") as string;
                        var displayIcon = CleanDisplayIcon(entry?.GetValue("DisplayIcon") as string);
                        var uninstall = (entry?.GetValue("QuietUninstallString") ?? entry?.GetValue("UninstallString")) as string;
                        var play = FindPlayTarget(names, installLocation, displayIcon);
                        var state = new InstallState
                        {
                            PackageId = package.PackageId,
                            Title = package.Title,
                            InstallLocation = Directory.Exists(installLocation) ? installLocation : Path.GetDirectoryName(play),
                            PlayTarget = play,
                            UninstallCommand = uninstall,
                            InstalledAt = DateTimeOffset.Now
                        };
                        if (IsUseful(state))
                        {
                            InstallStateStore.Save(state);
                            return state;
                        }
                    }
                }
            }
            catch { }
        }

        var shortcut = FindShortcut(names);
        if (shortcut is null) return null;
        var shortcutState = new InstallState
        {
            PackageId = package.PackageId,
            Title = package.Title,
            PlayTarget = shortcut,
            InstallLocation = Path.GetDirectoryName(shortcut),
            InstalledAt = DateTimeOffset.Now
        };
        InstallStateStore.Save(shortcutState);
        return shortcutState;
    }

    public static InstallState SaveManualTarget(PackageManifest package, string target, string? installLocation = null)
    {
        var current = InstallStateStore.Load(package.PackageId) ?? new InstallState { PackageId = package.PackageId, Title = package.Title };
        current.PlayTarget = Path.GetFullPath(target);
        current.InstallLocation = string.IsNullOrWhiteSpace(installLocation) ? Path.GetDirectoryName(current.PlayTarget) : Path.GetFullPath(installLocation);
        current.InstalledAt = DateTimeOffset.Now;
        InstallStateStore.Save(current);
        return current;
    }

    private static bool IsUseful(InstallState state) =>
        (!string.IsNullOrWhiteSpace(state.PlayTarget) && File.Exists(state.PlayTarget)) ||
        (!string.IsNullOrWhiteSpace(state.UninstallCommand) && !string.IsNullOrWhiteSpace(state.InstallLocation));

    private static string? FindPlayTarget(string[] names, string? installLocation, string? displayIcon)
    {
        var shortcut = FindShortcut(names);
        if (shortcut is not null) return shortcut;
        if (File.Exists(displayIcon) && IsLaunchable(displayIcon!) && !IsUtilityExecutable(displayIcon!)) return displayIcon;
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation)) return null;

        var tokens = names.SelectMany(NormalizedTokens).Where(token => token.Length >= 3).Distinct().ToArray();
        try
        {
            return Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.AllDirectories)
                .Where(path => !IsUtilityExecutable(path))
                .OrderByDescending(path => tokens.Count(token => Normalize(Path.GetFileNameWithoutExtension(path)).Contains(token)))
                .ThenBy(path => path.Count(character => character == Path.DirectorySeparatorChar))
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static string? FindShortcut(string[] names)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var shortcut in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    if (!names.Any(name => FuzzyContains(Path.GetFileNameWithoutExtension(shortcut), name))) continue;
                    var target = ResolveShortcut(shortcut);
                    if (File.Exists(target) && !IsUtilityExecutable(target!)) return target;
                }
            }
            catch { }
        }
        return null;
    }

    private static string? ResolveShortcut(string shortcut)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(shortcut);
            return (string)link.TargetPath;
        }
        catch { return null; }
    }

    private static string? CleanDisplayIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = value.Trim().Trim('"');
        var index = cleaned.LastIndexOf(',');
        if (index > 2 && int.TryParse(cleaned[(index + 1)..], out _)) cleaned = cleaned[..index].Trim('"');
        return Environment.ExpandEnvironmentVariables(cleaned);
    }

    private static bool IsUtilityExecutable(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("setup", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("patch", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("report", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLaunchable(string path) =>
        Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase);

    private static bool FuzzyContains(string candidate, string expected)
    {
        var normalizedCandidate = Normalize(candidate);
        var normalizedExpected = Normalize(expected);
        return normalizedCandidate.Contains(normalizedExpected, StringComparison.OrdinalIgnoreCase) ||
               NormalizedTokens(expected).Where(token => token.Length >= 3).All(normalizedCandidate.Contains);
    }

    private static IEnumerable<string> NormalizedTokens(string value) =>
        Regex.Split(Normalize(value), @"\s+").Where(token => token.Length > 0);

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
}

public static class ProcessCommands
{
    public static ProcessStartInfo FromRegisteredCommand(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        string fileName;
        string arguments;
        if (command.StartsWith('"'))
        {
            var closing = command.IndexOf('"', 1);
            if (closing < 0) throw new InvalidDataException("Invalid registered command.");
            fileName = command[1..closing];
            arguments = command[(closing + 1)..].Trim();
        }
        else
        {
            var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe < 0) throw new InvalidDataException("Registered command has no executable.");
            fileName = command[..(exe + 4)].Trim();
            arguments = command[(exe + 4)..].Trim();
        }
        return new ProcessStartInfo(fileName, arguments) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(fileName) ?? "" };
    }
}
