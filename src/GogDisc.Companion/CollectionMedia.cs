using GogDisc.Core;

namespace GogDisc.Companion;

internal static class CollectionMedia
{
    public static LoadedDisc ForGame(LoadedDisc source, CollectionGameManifest game)
    {
        if (!source.Package.CollectionGames.Contains(game))
            throw new InvalidDataException("This game is not part of the selected collection.");
        var package = new PackageManifest
        {
            SchemaVersion = source.Package.SchemaVersion,
            PackageId = source.Package.PackageId + "-" + game.GameId,
            Title = game.Title,
            Version = game.Version,
            ProductType = PackageProductType.BaseGame,
            DeploymentType = PackageDeploymentType.OfflineMedia,
            InstallerRelativePath = game.InstallerRelativePath,
            RequiredDiscCount = 1,
            TotalDiscCount = 1,
            BackgroundFile = source.Package.BackgroundFile,
            CoverFile = source.Package.CoverFile,
            IconFile = source.Package.IconFile,
            ExtrasRelativePath = game.ExtrasRelativePath,
            InstallDetectionNames = game.InstallDetectionNames,
            Files = game.Files
        };
        var disc = new DiscManifest
        {
            SchemaVersion = source.Disc.SchemaVersion,
            PackageId = package.PackageId,
            Version = package.Version,
            DiscNumber = 1,
            TotalDiscCount = 1,
            Files = game.Files
        };
        return new LoadedDisc(source.Root, package, disc);
    }

    public static LoadedDisc? ForPackageId(LoadedDisc source, string packageId)
    {
        var game = source.Package.CollectionGames.FirstOrDefault(candidate =>
            source.Package.PackageId + "-" + candidate.GameId == packageId);
        return game is null ? null : ForGame(source, game);
    }
}
