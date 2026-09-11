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
            var setups = Directory.EnumerateFiles(directory, "setup_*.exe", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetFileName(path).StartsWith("patch_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, NaturalStringComparer.Instance)
                .ToArray();
            if (setups.Length == 0) continue;
            if (setups.Length > 1)
                throw new InvalidDataException($"{Path.GetFileName(directory)} contains more than one setup executable. Put each game in its own folder.");

            var extras = Path.Combine(directory, "Extras");
            var family = SetupFamilyScanner.Scan(setups[0], Directory.Exists(extras) ? extras : null);
            games.Add(new SetupCollectionGame(Path.GetFileName(directory), InferVersion(setups[0]), family));
        }

        if (games.Count < 2)
            throw new InvalidDataException("A collection needs at least two game folders, each containing one stock setup_*.exe.");
        var duplicateFolder = games.GroupBy(game => PackageBuilder.SanitizeFileName(game.Title), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateFolder is not null)
            throw new InvalidDataException($"Collection game folders produce the same disc name: {string.Join(", ", duplicateFolder.Select(game => game.Title))}");
        return new SetupCollection { RootDirectory = root, Games = games };
    }

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
