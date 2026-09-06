using System.Text;
using System.Text.Json;

namespace GogDisc.Core;

public static class KeyMediaBuilder
{
    public static async Task<PackageBuildResult> BuildAsync(KeyMediaBuildRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Discs.Count == 0) throw new InvalidDataException("No Key Media discs were requested.");
        foreach (var disc in request.Discs) disc.Product.Validate();
        if (!File.Exists(request.LauncherExecutable))
            throw new FileNotFoundException("Published Launch.exe was not found.", request.LauncherExecutable);

        var setTitle = (request.SetTitle ?? request.Discs[0].Product.Title).Trim();
        var finalRoot = Path.Combine(Path.GetFullPath(request.OutputDirectory),
            PackageBuilder.SanitizeFileName($"{setTitle} GOG (Key Media)"));
        if (Directory.Exists(finalRoot) && Directory.EnumerateFileSystemEntries(finalRoot).Any())
            throw new IOException($"Output package already exists and is not empty: {finalRoot}");
        var buildRoot = finalRoot + $".building-{Guid.NewGuid():N}";
        Directory.CreateDirectory(buildRoot);

        try
        {
            var manifests = new List<PackageManifest>();
            var instructions = new StringBuilder($"{Clean(setTitle)} - GOG Key Media\r\n\r\n");
            var multiple = request.Discs.Count > 1;

            for (var index = 0; index < request.Discs.Count; index++)
            {
                var disc = request.Discs[index];
                var number = index + 1;
                var discFolder = multiple
                    ? Path.Combine(buildRoot, PackageBuilder.SanitizeFileName($"Disc {number} - {disc.Product.Title.Trim()}"))
                    : buildRoot;
                var mediaRoot = Path.Combine(discFolder, "Key Media");
                Directory.CreateDirectory(mediaRoot);
                manifests.Add(await WriteDiscAsync(request, disc, mediaRoot, cancellationToken));
                instructions.Append(multiple
                    ? $"Disc {number} - {Clean(disc.Product.Title)}: burn the CONTENTS of \"{Path.GetFileName(discFolder)}\\Key Media\".\r\n"
                    : "Burn the CONTENTS of the Key Media folder to any filesystem-based physical medium.\r\n");
            }

            if (multiple)
                instructions.Append("\r\nInstall Disc 1 first; the add-on discs install into that game folder.\r\n");
            instructions.Append("\r\nThis media contains no game payload or GOG credentials. " +
                                "Internet access and ownership on GOG are required to install.\r\n");
            await File.WriteAllTextAsync(Path.Combine(buildRoot, "BURNING-INSTRUCTIONS.txt"), instructions.ToString(), cancellationToken);

            if (Directory.Exists(finalRoot)) Directory.Delete(finalRoot);
            Directory.Move(buildRoot, finalRoot);
            return new PackageBuildResult(finalRoot, manifests[0]);
        }
        catch
        {
            if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, true);
            throw;
        }
    }

    /// <summary>Each disc is a standalone package, so its internal disc number is always 1 — the set
    /// position lives in the folder name, not in the manifest.</summary>
    private static async Task<PackageManifest> WriteDiscAsync(KeyMediaBuildRequest request, KeyMediaDisc disc,
        string mediaRoot, CancellationToken cancellationToken)
    {
        var product = disc.Product;
        var manifest = new PackageManifest
        {
            SchemaVersion = 2,
            PackageId = $"gog-{product.ProductId}",
            Title = product.Title.Trim(),
            Version = request.Version.Trim(),
            ProductType = product.DiscRole == KeyDiscRole.Dlc ? PackageProductType.Dlc : PackageProductType.BaseGame,
            DeploymentType = PackageDeploymentType.GogKeyMedia,
            GogKeyProduct = product,
            RequiredDiscCount = 0,
            TotalDiscCount = 1,
            IconFile = "game.ico",
            InstallDetectionNames = BuildDetectionNames(product),
            DiscLayout = [new PackageDiscInfo { DiscNumber = 1, MediaName = "Key Media", CapacityBytes = 0 }]
        };
        File.Copy(request.LauncherExecutable, Path.Combine(mediaRoot, "Launch.exe"), true);
        IconFile.Create(disc.IconImage, Path.Combine(mediaRoot, "game.ico"));
        CopyArtwork(disc.BackgroundImage, "background", mediaRoot, name => manifest.BackgroundFile = name);
        CopyArtwork(disc.CoverImage, "cover", mediaRoot, name => manifest.CoverFile = name);

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
        var title = Clean(manifest.Title);
        await File.WriteAllTextAsync(Path.Combine(mediaRoot, "autorun.inf"),
            $"[AutoRun]\r\nopen=Launch.exe\r\nicon=game.ico\r\nlabel={title}\r\naction=Install or play {title}\r\n",
            Encoding.ASCII, cancellationToken);
        return manifest;
    }

    /// <summary>A DLC disc detects the base game's installation, since that is the folder it installs into.</summary>
    private static List<string> BuildDetectionNames(GogKeyProduct product) =>
        product.DiscRole == KeyDiscRole.Dlc
            ? [product.BaseTitle ?? product.Title.Trim()]
            : [product.Title.Trim(), product.Slug.Replace('_', ' ')];

    private static string Clean(string value) => value.Replace("\r", " ").Replace("\n", " ").Trim();

    private static void CopyArtwork(string? source, string stem, string mediaRoot, Action<string> setName)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return;
        var name = stem + Path.GetExtension(source).ToLowerInvariant();
        File.Copy(source, Path.Combine(mediaRoot, name), true);
        setName(name);
    }
}
