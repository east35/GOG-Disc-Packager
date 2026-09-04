using System.Text.RegularExpressions;

namespace GogDisc.Core;

public static class SetupFamilyScanner
{
    public static SetupFamily Scan(string setupExecutable, string? extrasDirectory = null, bool includePatchesAsExtras = false)
    {
        var setupPath = Path.GetFullPath(setupExecutable);
        if (!File.Exists(setupPath))
            throw new FileNotFoundException("The selected GOG setup executable does not exist.", setupPath);

        var setupName = Path.GetFileName(setupPath);
        if (!setupName.StartsWith("setup_", StringComparison.OrdinalIgnoreCase) ||
            !setupName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a stock GOG setup_*.exe file. Patch executables are packaged separately.");

        var directory = Path.GetDirectoryName(setupPath)!;
        var familyName = Path.GetFileNameWithoutExtension(setupPath);
        if (new FileInfo(setupPath).Length == 0)
            throw new InvalidDataException($"The selected setup executable is empty: {setupName}");
        var companionPattern = new Regex(
            $"^{Regex.Escape(familyName)}-(?<number>[0-9]+)\\.bin$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var installers = new List<SourcePackageFile>
        {
            new(setupPath, setupName, new FileInfo(setupPath).Length, PackageFileKind.Installer, 0)
        };

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            var match = companionPattern.Match(name);
            if (!match.Success) continue;

            var info = new FileInfo(path);
            if (info.Length == 0)
                throw new InvalidDataException($"Required installer part is empty: {name}");

            installers.Add(new SourcePackageFile(
                info.FullName,
                name,
                info.Length,
                PackageFileKind.Installer,
                int.Parse(match.Groups["number"].Value)));
        }

        var incomplete = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (incomplete.Length > 0)
            throw new InvalidDataException($"Incomplete files belong to this setup family: {string.Join(", ", incomplete.Select(Path.GetFileName))}");

        installers = installers
            .OrderBy(file => file.Sequence)
            .ThenBy(file => file.RelativePath, NaturalStringComparer.Instance)
            .ToList();
        var sequences = installers.Where(file => file.Sequence > 0).Select(file => file.Sequence).ToArray();
        if (sequences.Length > 0 && !sequences.SequenceEqual(Enumerable.Range(1, sequences[^1])))
            throw new InvalidDataException("The setup family's numbered BIN files contain a gap.");

        var patches = Directory.EnumerateFiles(directory, "patch_*.exe", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, NaturalStringComparer.Instance)
            .ToArray();

        var extras = new List<SourcePackageFile>();
        if (!string.IsNullOrWhiteSpace(extrasDirectory))
        {
            var extrasRoot = Path.GetFullPath(extrasDirectory);
            if (!Directory.Exists(extrasRoot))
                throw new DirectoryNotFoundException($"Extras directory does not exist: {extrasRoot}");

            var sequence = 0;
            foreach (var path in Directory.EnumerateFiles(extrasRoot, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, NaturalStringComparer.Instance))
            {
                var name = Path.GetFileName(path);
                var extension = Path.GetExtension(path);
                var isPatchFile = name.StartsWith("patch_", StringComparison.OrdinalIgnoreCase);
                if (Path.GetFullPath(path).Equals(setupPath, StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("setup_", StringComparison.OrdinalIgnoreCase) && extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                    isPatchFile && !includePatchesAsExtras ||
                    extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) && !isPatchFile ||
                    extension.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
                    continue;
                var info = new FileInfo(path);
                extras.Add(new SourcePackageFile(
                    info.FullName,
                    Path.GetRelativePath(extrasRoot, info.FullName),
                    info.Length,
                    PackageFileKind.Extra,
                    sequence++));
            }
        }

        return new SetupFamily
        {
            SetupExecutable = setupPath,
            FamilyName = familyName,
            InstallerFiles = installers,
            Extras = extras,
            ExcludedPatches = patches
        };
    }
}
