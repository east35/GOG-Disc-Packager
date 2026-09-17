namespace GogDisc.Core;

public sealed record LoadedDisc(string Root, PackageManifest Package, DiscManifest Disc);

public static class DiscMedia
{
    public static LoadedDisc Load(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        var packagePath = Path.Combine(fullRoot, "package.json");
        var discPath = Path.Combine(fullRoot, "disc.json");
        if (!File.Exists(packagePath) || !File.Exists(discPath))
            throw new InvalidDataException("This media does not contain GOG Disc Tool metadata.");

        var packageBytes = File.ReadAllBytes(packagePath);
        var package = System.Text.Json.JsonSerializer.Deserialize<PackageManifest>(packageBytes, JsonFiles.Options)
            ?? throw new InvalidDataException("package.json is invalid.");
        ValidatePackage(package);
        if (package.DeploymentType == PackageDeploymentType.GogKeyMedia)
            (package.GogKeyProduct ?? throw new InvalidDataException("GOG Key Media identity is missing.")).Validate();
        var disc = JsonFiles.Read<DiscManifest>(discPath);
        if (disc.SchemaVersion is < 1 or > 2)
            throw new InvalidDataException($"Unsupported disc manifest schema {disc.SchemaVersion}.");
        if (!string.Equals(package.PackageId, disc.PackageId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The package and disc identifiers do not match.");
        if (!string.Equals(Hashing.Sha256Bytes(packageBytes), disc.PackageManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The package manifest hash does not match this disc.");
        if (disc.DiscNumber < 1 || disc.DiscNumber > package.TotalDiscCount)
            throw new InvalidDataException("The disc number is outside the package range.");
        var expected = package.Files.Where(file => file.DiscNumber == disc.DiscNumber)
            .OrderBy(file => file.DiscPath, StringComparer.OrdinalIgnoreCase).ToArray();
        var actual = disc.Files.OrderBy(file => file.DiscPath, StringComparer.OrdinalIgnoreCase).ToArray();
        if (expected.Length != actual.Length || expected.Where((entry, index) =>
                !entry.DiscPath.Equals(actual[index].DiscPath, StringComparison.OrdinalIgnoreCase) ||
                entry.Size != actual[index].Size ||
                !entry.Sha256.Equals(actual[index].Sha256, StringComparison.OrdinalIgnoreCase) ||
                entry.Kind != actual[index].Kind ||
                entry.SourceOffset != actual[index].SourceOffset ||
                entry.PartIndex != actual[index].PartIndex ||
                entry.PartCount != actual[index].PartCount).Any())
            throw new InvalidDataException("The disc file manifest does not match the package manifest.");
        return new LoadedDisc(fullRoot, package, disc);
    }

    private static void ValidatePackage(PackageManifest package)
    {
        if (package.SchemaVersion is < 1 or > 2)
            throw new InvalidDataException($"Unsupported package manifest schema {package.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(package.PackageId) || string.IsNullOrWhiteSpace(package.Title))
            throw new InvalidDataException("The package manifest is missing its identity.");
        if (package.TotalDiscCount < 1 || package.RequiredDiscCount < 0 ||
            package.RequiredDiscCount > package.TotalDiscCount ||
            package.DeploymentType == PackageDeploymentType.OfflineMedia && package.RequiredDiscCount < 1)
            throw new InvalidDataException("The package manifest has invalid disc counts.");
        foreach (var file in package.Files)
        {
            _ = SafePaths.ResolveUnderRoot(Path.GetTempPath(), file.RelativePath);
            _ = SafePaths.ResolveUnderRoot(Path.GetTempPath(), file.DiscPath);
            if (file.Size < 0 || file.SourceOffset < 0 || file.SourceSize < 0 ||
                file.DiscNumber < 1 || file.DiscNumber > package.TotalDiscCount ||
                file.PartIndex < 1 || file.PartCount < 1 || file.PartIndex > file.PartCount ||
                string.IsNullOrWhiteSpace(file.Sha256))
                throw new InvalidDataException($"Invalid package file entry: {file.RelativePath}");
        }
        var collectionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var game in package.CollectionGames)
        {
            if (string.IsNullOrWhiteSpace(game.GameId) || !game.GameId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ||
                !collectionIds.Add(game.GameId) || string.IsNullOrWhiteSpace(game.Title) ||
                string.IsNullOrWhiteSpace(game.InstallerRelativePath) || game.Files.Count == 0)
                throw new InvalidDataException("A collection game is missing required metadata.");
            _ = SafePaths.ResolveUnderRoot(Path.GetTempPath(), game.InstallerRelativePath);
            foreach (var file in game.Files)
                if (!package.Files.Any(candidate => candidate.RelativePath == file.RelativePath &&
                    candidate.DiscPath == file.DiscPath && candidate.Size == file.Size &&
                    candidate.Sha256 == file.Sha256 && candidate.DiscNumber == file.DiscNumber &&
                    candidate.Kind == file.Kind && candidate.SourceOffset == file.SourceOffset &&
                    candidate.SourceSize == file.SourceSize && candidate.PartIndex == file.PartIndex &&
                    candidate.PartCount == file.PartCount))
                    throw new InvalidDataException($"Collection file is not part of this package: {file.RelativePath}");
            if (!game.Files.Any(file => file.RelativePath == game.InstallerRelativePath && file.Kind == PackageFileKind.Installer))
                throw new InvalidDataException("A collection game has no matching setup file.");
        }
    }

    public static LoadedDisc? Find(string packageId, int discNumber, string? preferredRoot = null)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredRoot) && Directory.Exists(preferredRoot)) roots.Add(preferredRoot);
        roots.AddRange(DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.CDRom && drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName));

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var loaded = Load(root);
                if (loaded.Package.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase) && loaded.Disc.DiscNumber == discNumber)
                    return loaded;
            }
            catch { }
        }
        return null;
    }
}
