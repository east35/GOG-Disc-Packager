using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using GogDisc.Core;

namespace GogDisc.Companion;

internal static class DesktopIntegration
{
    public static string IconPath(LibraryGame game)
    {
        var icon = Path.Combine(CompanionPaths.PackageRoot(game.Package.PackageId), "icon.png");
        return File.Exists(icon) ? icon : CompanionPaths.AppIcon;
    }

    public static void CacheArtwork(LoadedDisc media)
    {
        var root = CompanionPaths.PackageRoot(media.Package.PackageId);
        Directory.CreateDirectory(root);
        var cachedIcon = Path.Combine(root, "icon.png");
        if (!File.Exists(cachedIcon) && !string.IsNullOrWhiteSpace(media.Package.IconFile))
        {
            var source = SafePaths.ResolveUnderRoot(media.Root, media.Package.IconFile);
            LibraryStore.EnsureNoLinks(media.Root, source);
            if (File.Exists(source))
            {
                // Avalonia decodes both ICO and PNG; desktop environments receive a portable PNG.
                using var input = File.OpenRead(source);
                var icon = new WindowIcon(input);
                var temporary = cachedIcon + ".tmp";
                try
                {
                    using (var output = File.Create(temporary)) icon.Save(output);
                    File.Move(temporary, cachedIcon, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        if (!File.Exists(Path.Combine(root, "cover")) && !string.IsNullOrWhiteSpace(media.Package.CoverFile))
        {
            var source = SafePaths.ResolveUnderRoot(media.Root, media.Package.CoverFile);
            LibraryStore.EnsureNoLinks(media.Root, source);
            if (File.Exists(source)) File.Copy(source, Path.Combine(root, "cover"), true);
        }
    }

    internal static string Value(string text) => text.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    internal static string Argument(string text)
    {
        // Exec is parsed by the desktop environment, never a shell. Escape both desktop-value and Exec layers.
        var quoted = "\"" + text.Replace("%", "%%").Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("`", "\\`").Replace("$", "\\$") + "\"";
        return Value(quoted);
    }

    public static string ShortcutText(LibraryGame game, string companion) =>
        "[Desktop Entry]\nType=Application\n" +
        $"Name={Value(game.Package.Title)}\nExec={Argument(companion)} play {Argument(game.Package.PackageId)}\n" +
        $"Icon={Value(IconPath(game))}\nCategories=Game;\nTerminal=false\nStartupNotify=true\n";

    public static void CreateShortcuts(LibraryGame game, bool desktop)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "gog-disc-companion");
        if (!File.Exists(executable)) throw new IOException("Shortcuts require a published companion build.");
        Directory.CreateDirectory(CompanionPaths.ApplicationsRoot);
        var name = "gog-disc-game-" + CompanionPaths.SafeId(game.Package.PackageId) + ".desktop";
        var contents = ShortcutText(game, executable);
        File.WriteAllText(Path.Combine(CompanionPaths.ApplicationsRoot, name), contents, new UTF8Encoding(false));
        if (desktop)
        {
            var start = new ProcessStartInfo("xdg-user-dir") { UseShellExecute = false, RedirectStandardOutput = true };
            start.ArgumentList.Add("DESKTOP");
            using var process = Process.Start(start) ?? throw new IOException("Could not locate the desktop folder.");
            var directory = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0 || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
                throw new IOException("The desktop folder could not be located. The application-menu shortcut was created.");
            game.DesktopShortcut = Path.Combine(directory, name);
            File.WriteAllText(game.DesktopShortcut, contents, new UTF8Encoding(false));
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(game.DesktopShortcut,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        LibraryStore.Save(game);
    }

    public static void RemoveShortcuts(LibraryGame game)
    {
        var name = "gog-disc-game-" + CompanionPaths.SafeId(game.Package.PackageId) + ".desktop";
        DeleteOwnedShortcut(Path.Combine(CompanionPaths.ApplicationsRoot, name), game);
        if (game.DesktopShortcut is { } path && Path.GetFileName(path) == name) DeleteOwnedShortcut(path, game);
        game.DesktopShortcut = null;
    }

    private static void DeleteOwnedShortcut(string path, LibraryGame game)
    {
        if (File.Exists(path) && File.ReadAllText(path).Contains($" play {Argument(game.Package.PackageId)}\n", StringComparison.Ordinal))
            File.Delete(path);
    }

    public static void Open(string path)
    {
        var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
        start.ArgumentList.Add(path);
        Process.Start(start)?.Dispose();
    }
}
