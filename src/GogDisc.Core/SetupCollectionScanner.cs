using System.Diagnostics;

namespace GogDisc.Core;

public static class SetupCollectionScanner
{
    public static SetupCollection Scan(string rootDirectory)
    {
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Collection folder does not exist: {root}");

        var games = new List<SetupCollectionGame>();
        foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(path => path, NaturalStringComparer.Instance))
        {
            // The installer may sit anywhere in the game folder ("Base Game\", "Installer\", or loose); only the
            // Extras folder is off limits, since bonus content can itself contain setup files.
            var extras = Path.Combine(directory, "Extras");
            var setups = Directory.EnumerateFiles(directory, "setup_*.exe", SearchOption.AllDirectories)
                .Where(path => !IsUnder(path, extras))
                .OrderBy(path => path, NaturalStringComparer.Instance)
                .ToArray();
            if (setups.Length == 0) continue;
            if (setups.Length > 1)
                throw new InvalidDataException(
                    $"{Path.GetFileName(directory)} contains more than one setup executable:\n" +
                    string.Join("\n", setups.Select(path => "  " + Path.GetRelativePath(directory, path))) +
                    "\n\nKeep one game per folder. Bonus content can go in an Extras folder.");

            var family = SetupFamilyScanner.Scan(setups[0], Directory.Exists(extras) ? extras : null);
            games.Add(new SetupCollectionGame(Path.GetFileName(directory), InferVersion(setups[0]), family));
        }

        if (games.Count < 2)
            throw new InvalidDataException("A collection needs at least two game folders, each containing one stock setup_*.exe (in any subfolder except Extras).");
        var duplicateFolder = games.GroupBy(game => PackageBuilder.SanitizeFileName(game.Title), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateFolder is not null)
            throw new InvalidDataException($"Collection game folders produce the same disc name: {string.Join(", ", duplicateFolder.Select(game => game.Title))}");
        return new SetupCollection { RootDirectory = root, Games = games };
    }

    private static bool IsUnder(string path, string directory) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string InferVersion(string setupPath)
    {
        var embedded = FileVersionInfo.GetVersionInfo(setupPath).ProductVersion;
        if (!string.IsNullOrWhiteSpace(embedded))
        {
            var metadata = embedded.IndexOf(".[", StringComparison.Ordinal);
            if (metadata > 0) embedded = embedded[..metadata];
            return embedded.Replace('_', ' ').Trim();
        }
        var stem = Path.GetFileNameWithoutExtension(setupPath);
        var buildMarker = stem.LastIndexOf("_(", StringComparison.Ordinal);
        if (buildMarker > 0) stem = stem[..buildMarker];
        var parts = stem.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var version = parts.LastOrDefault(part => part.Length > 0 && char.IsDigit(part[0]) && part.Any(char.IsDigit));
        return version ?? "GOG offline installer";
    }
}
