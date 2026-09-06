namespace GogDisc.Core;

public sealed class KeyInstallMarker
{
    public string PackageId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public static class KeyInstallOwnership
{
    public const string MarkerName = ".gog-key-install.json";

    public static void Mark(string installRoot, PackageManifest package)
    {
        if (package.DeploymentType != PackageDeploymentType.GogKeyMedia || package.GogKeyProduct is null)
            throw new InvalidOperationException("Only GOG Key Media installations can be marked.");
        Directory.CreateDirectory(installRoot);
        JsonFiles.Write(Path.Combine(installRoot, MarkerName), new KeyInstallMarker
        {
            PackageId = package.PackageId,
            ProductId = package.GogKeyProduct.ProductId,
            CreatedAt = DateTimeOffset.Now
        });
    }

    public static bool IsOwned(string installRoot, PackageManifest package)
    {
        try
        {
            var fullRoot = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (fullRoot == Path.GetPathRoot(fullRoot)?.TrimEnd(Path.DirectorySeparatorChar)) return false;
            var markerPath = Path.Combine(fullRoot, MarkerName);
            if (!File.Exists(markerPath) || package.GogKeyProduct is null) return false;
            var marker = JsonFiles.Read<KeyInstallMarker>(markerPath);
            return marker.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase) &&
                   marker.ProductId.Equals(package.GogKeyProduct.ProductId, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool AdoptKnownInstall(InstallState state, PackageManifest package)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(state.InstallLocation) || string.IsNullOrWhiteSpace(state.PlayTarget) ||
                !Directory.Exists(state.InstallLocation) || !File.Exists(state.PlayTarget) ||
                !state.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase)) return false;
            var root = Path.GetFullPath(state.InstallLocation).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(state.PlayTarget).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
            Mark(state.InstallLocation, package);
            return true;
        }
        catch { return false; }
    }

    public static void Remove(string installRoot, PackageManifest package)
    {
        if (!IsOwned(installRoot, package))
            throw new InvalidOperationException("This installation is not marked as owned by GOG Disc Packager and cannot be removed automatically.");
        Directory.Delete(Path.GetFullPath(installRoot), true);
    }
}
