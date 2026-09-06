using System.Text;
using System.Text.Json;

namespace GogDisc.Core;

public static class KeyMediaBuilder
{
    public static async Task<PackageBuildResult> BuildAsync(KeyMediaBuildRequest request, CancellationToken cancellationToken = default)
    {
        request.Product.Validate();
        if (!File.Exists(request.LauncherExecutable))
            throw new FileNotFoundException("Published Launch.exe was not found.", request.LauncherExecutable);

        var finalRoot = Path.Combine(Path.GetFullPath(request.OutputDirectory),
            PackageBuilder.SanitizeFileName($"{request.Product.Title} GOG (Key Media)"));
        if (Directory.Exists(finalRoot) && Directory.EnumerateFileSystemEntries(finalRoot).Any())
            throw new IOException($"Output package already exists and is not empty: {finalRoot}");
        var buildRoot = finalRoot + $".building-{Guid.NewGuid():N}";
        var mediaRoot = Path.Combine(buildRoot, "Key Media");
        Directory.CreateDirectory(mediaRoot);

        try
        {
            var manifest = new PackageManifest
            {
                SchemaVersion = 2,
                PackageId = $"gog-{request.Product.ProductId}",
                Title = request.Product.Title.Trim(),
                Version = request.Version.Trim(),
                ProductType = PackageProductType.BaseGame,
                DeploymentType = PackageDeploymentType.GogKeyMedia,
                GogKeyProduct = request.Product,
                RequiredDiscCount = 0,
                TotalDiscCount = 1,
                IconFile = "game.ico",
                InstallDetectionNames = [request.Product.Title.Trim(), request.Product.Slug.Replace('_', ' ')],
                DiscLayout = [new PackageDiscInfo { DiscNumber = 1, MediaName = "Key Media", CapacityBytes = 0 }]
            };
            File.Copy(request.LauncherExecutable, Path.Combine(mediaRoot, "Launch.exe"), true);
            IconFile.Create(request.IconImage, Path.Combine(mediaRoot, "game.ico"));
            CopyArtwork(request.BackgroundImage, "background", mediaRoot, name => manifest.BackgroundFile = name);
            CopyArtwork(request.CoverImage, "cover", mediaRoot, name => manifest.CoverFile = name);

            var packageBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonFiles.Options);
            await File.WriteAllBytesAsync(Path.Combine(mediaRoot, "package.json"), packageBytes, cancellationToken);
            JsonFiles.Write(Path.Combine(mediaRoot, "disc.json"), new DiscManifest
            {
                PackageId = manifest.PackageId,
                Version = manifest.Version,
                DiscNumber = 1,
                TotalDiscCount = 1,
                Role = DiscRole.Installer,
                MediaName = "Key Media",
                PackageManifestSha256 = Hashing.Sha256Bytes(packageBytes)
            });
            var title = manifest.Title.Replace("\r", " ").Replace("\n", " ");
            await File.WriteAllTextAsync(Path.Combine(mediaRoot, "autorun.inf"),
                $"[AutoRun]\r\nopen=Launch.exe\r\nicon=game.ico\r\nlabel={title}\r\naction=Install or play {title}\r\n", Encoding.ASCII, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(buildRoot, "BURNING-INSTRUCTIONS.txt"),
                $"{title} - GOG Key Media\r\n\r\nBurn the CONTENTS of the Key Media folder to any filesystem-based physical medium.\r\n" +
                "This media contains no game payload or GOG credentials. Internet access and ownership on GOG are required to install.\r\n", cancellationToken);
            if (Directory.Exists(finalRoot)) Directory.Delete(finalRoot);
            Directory.Move(buildRoot, finalRoot);
            return new PackageBuildResult(finalRoot, manifest);
        }
        catch
        {
            if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, true);
            throw;
        }
    }

    private static void CopyArtwork(string? source, string stem, string mediaRoot, Action<string> setName)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return;
        var name = stem + Path.GetExtension(source).ToLowerInvariant();
        File.Copy(source, Path.Combine(mediaRoot, name), true);
        setName(name);
    }
}
