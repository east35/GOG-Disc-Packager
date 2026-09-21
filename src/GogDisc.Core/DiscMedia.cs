namespace GogDisc.Core;

public sealed record LoadedDisc(string Root, PackageManifest Package, DiscManifest Disc);
public sealed record DiscMediaFailure(string Root, string Message);
public sealed record DiscMediaSearchResult(LoadedDisc? Media, IReadOnlyList<DiscMediaFailure> Failures);

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
        if (package.DeploymentType == PackageDeploymentType.GogKeyMedia)
            (package.GogKeyProduct ?? throw new InvalidDataException("GOG Key Media identity is missing.")).Validate();
        var disc = JsonFiles.Read<DiscManifest>(discPath);
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

    public static LoadedDisc? Find(string packageId, int discNumber, string? preferredRoot = null)
        => Search(packageId, discNumber, preferredRoot).Media;

    public static DiscMediaSearchResult Search(string packageId, int discNumber, string? preferredRoot = null)
    {
        var roots = new List<(string Root, bool Expected)>();
        if (!string.IsNullOrWhiteSpace(preferredRoot) && Directory.Exists(preferredRoot))
            roots.Add((preferredRoot, true));
        roots.AddRange(DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.CDRom && drive.IsReady)
            .Select(drive => (drive.RootDirectory.FullName, false)));

        var failures = new List<DiscMediaFailure>();
        foreach (var candidate in roots
                     .GroupBy(candidate => candidate.Root, StringComparer.OrdinalIgnoreCase)
                     .Select(group => (Root: group.Key, Expected: group.Any(candidate => candidate.Expected))))
        {
            var root = candidate.Root;
            // Ignore unrelated data discs. The bootstrap root is expected to contain metadata; other optical
            // roots become candidates only when at least one GOG Disc Tool metadata file is visible.
            if (!candidate.Expected &&
                !File.Exists(Path.Combine(root, "package.json")) &&
                !File.Exists(Path.Combine(root, "disc.json")))
                continue;
            try
            {
                var loaded = Load(root);
                if (loaded.Package.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase) && loaded.Disc.DiscNumber == discNumber)
                    return new DiscMediaSearchResult(loaded, failures);
            }
            catch (Exception ex)
            {
                failures.Add(new DiscMediaFailure(root, ex.Message));
            }
        }
        return new DiscMediaSearchResult(null, failures);
    }
}
