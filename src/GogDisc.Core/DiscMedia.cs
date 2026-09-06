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
