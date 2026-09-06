using System.Text.Json.Serialization;

namespace GogDisc.Core;

public enum PackageProductType
{
    BaseGame,
    Dlc
}

public enum PackageDeploymentType
{
    OfflineMedia,
    GogKeyMedia
}

public sealed class GogKeyProduct
{
    public string Format { get; set; } = "gog-key-disc";
    public int Schema { get; set; } = 1;
    public string ProductId { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Platform { get; set; } = "windows";
    public string Language { get; set; } = "en";
    public int? AvailableExtras { get; set; }

    public void Validate()
    {
        if (Format != "gog-key-disc" || Schema != 1)
            throw new InvalidDataException("This GOG Key Media manifest format is not supported.");
        if (string.IsNullOrWhiteSpace(ProductId) || !ProductId.All(char.IsDigit))
            throw new InvalidDataException("The GOG product ID is invalid.");
        if (string.IsNullOrWhiteSpace(Title)) throw new InvalidDataException("The GOG product title is missing.");
        if (Platform != "windows") throw new InvalidDataException("Only Windows GOG Key Media is currently supported.");
        if (string.IsNullOrWhiteSpace(Language)) throw new InvalidDataException("The GOG product language is missing.");
    }
}

public enum PackageFileKind
{
    Installer,
    Extra
}

public enum DiscRole
{
    Installer,
    Extras
}

public sealed class PackageManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string PackageId { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Version { get; set; } = "";
    public PackageProductType ProductType { get; set; }
    public PackageDeploymentType DeploymentType { get; set; }
    public GogKeyProduct? GogKeyProduct { get; set; }
    public string InstallerRelativePath { get; set; } = "";
    public int RequiredDiscCount { get; set; }
    public int TotalDiscCount { get; set; }
    public string BackgroundFile { get; set; } = "";
    public string CoverFile { get; set; } = "";
    public string IconFile { get; set; } = "game.ico";
    public List<PackageDiscInfo> DiscLayout { get; set; } = [];
    public List<string> InstallDetectionNames { get; set; } = [];
    public List<PackageFileEntry> Files { get; set; } = [];
}

public sealed class KeyMediaBuildRequest
{
    public required GogKeyProduct Product { get; init; }
    public required string OutputDirectory { get; init; }
    public required string LauncherExecutable { get; init; }
    public string Version { get; init; } = "Current GOG build";
    public string? BackgroundImage { get; init; }
    public string? CoverImage { get; init; }
    public string? IconImage { get; init; }
}

public sealed class DiscManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string PackageId { get; set; } = "";
    public string Version { get; set; } = "";
    public int DiscNumber { get; set; }
    public int TotalDiscCount { get; set; }
    public DiscRole Role { get; set; }
    public string MediaName { get; set; } = "";
    public long CapacityBytes { get; set; }
    public string PackageManifestSha256 { get; set; } = "";
    public List<PackageFileEntry> Files { get; set; } = [];
}

public sealed class PackageDiscInfo
{
    public int DiscNumber { get; set; }
    public string MediaName { get; set; } = "";
    public long CapacityBytes { get; set; }
}

public sealed class PackageFileEntry
{
    public string RelativePath { get; set; } = "";
    public string DiscPath { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public int DiscNumber { get; set; }
    public PackageFileKind Kind { get; set; }
    public long SourceOffset { get; set; }
    public long SourceSize { get; set; }
    public int PartIndex { get; set; } = 1;
    public int PartCount { get; set; } = 1;
}

public sealed class InstallState
{
    public string PackageId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? InstallLocation { get; set; }
    public string? PlayTarget { get; set; }
    public string? UninstallCommand { get; set; }
    public DateTimeOffset? InstalledAt { get; set; }
}

public sealed class StagingState
{
    public string PackageId { get; set; } = "";
    public string Version { get; set; } = "";
    public HashSet<string> VerifiedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record SourcePackageFile(
    string FullPath,
    string RelativePath,
    long Size,
    PackageFileKind Kind,
    int Sequence)
{
    public long SourceOffset { get; set; }
    public long SourceSize { get; set; }
    public int PartIndex { get; set; } = 1;
    public int PartCount { get; set; } = 1;
}

public sealed class SetupFamily
{
    public required string SetupExecutable { get; init; }
    public required string FamilyName { get; init; }
    public required IReadOnlyList<SourcePackageFile> InstallerFiles { get; init; }
    public required IReadOnlyList<SourcePackageFile> Extras { get; init; }
    public required IReadOnlyList<string> ExcludedPatches { get; init; }
    public long InstallerBytes => InstallerFiles.Sum(file => file.Size);
    public long ExtrasBytes => Extras.Sum(file => file.Size);
}

public sealed class PlannedDisc
{
    public required int Number { get; init; }
    public required string MediaName { get; init; }
    public required long CapacityBytes { get; init; }
    public required long ReserveBytes { get; init; }
    public DiscRole Role { get; set; }
    public List<SourcePackageFile> Files { get; } = [];
    public long UsedBytes => Files.Sum(file => file.Size);
}

public sealed class PackagePlan
{
    public required IReadOnlyList<PlannedDisc> Discs { get; init; }
    public required int RequiredDiscCount { get; init; }
    public long InstallerBytes { get; init; }
    public long ExtrasBytes { get; init; }
}

public sealed class PackageBuildRequest
{
    public required string Title { get; init; }
    public required string Version { get; init; }
    public required PackageProductType ProductType { get; init; }
    public required SetupFamily SetupFamily { get; init; }
    public required PackagePlan Plan { get; init; }
    public required string OutputDirectory { get; init; }
    public required string LauncherExecutable { get; init; }
    public string? BackgroundImage { get; init; }
    public string? CoverImage { get; init; }
    public string? IconImage { get; init; }
}

public sealed record PackagingProgress(
    string Activity,
    string CurrentFile,
    long CompletedBytes,
    long TotalBytes)
{
    [JsonIgnore]
    public double Percent => TotalBytes == 0 ? 0 : CompletedBytes * 100d / TotalBytes;
}

public sealed record PackageBuildResult(string PackageDirectory, PackageManifest Manifest);
