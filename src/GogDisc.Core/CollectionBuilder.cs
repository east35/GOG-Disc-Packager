using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GogDisc.Core;

public static class CollectionBuilder
{
    public static async Task<PackageBuildResult> BuildAsync(CollectionBuildRequest request,
        IProgress<PackagingProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("A collection title is required.");
        if (string.IsNullOrWhiteSpace(request.Version)) throw new ArgumentException("A collection version is required.");
        if (!File.Exists(request.LauncherExecutable)) throw new FileNotFoundException("Published Launch.exe was not found.", request.LauncherExecutable);
        if (request.Collection.TotalBytes + request.ReserveBytes > request.CapacityBytes)
            throw new InvalidDataException($"The collection needs {FormatBytes(request.Collection.TotalBytes + request.ReserveBytes)}, including the safety reserve, but the selected disc holds {FormatBytes(request.CapacityBytes)}.");

        var safeName = PackageBuilder.SanitizeFileName($"{request.Title} {request.Version}".Trim());
        var finalRoot = Path.Combine(Path.GetFullPath(request.OutputDirectory), safeName);
        if (Directory.Exists(finalRoot) && Directory.EnumerateFileSystemEntries(finalRoot).Any())
            throw new IOException($"Output package already exists and is not empty: {finalRoot}");
        var buildRoot = finalRoot + $".building-{Guid.NewGuid():N}";
        var discRoot = Path.Combine(buildRoot, "Disc 01 of 01");
        Directory.CreateDirectory(discRoot);

        try
        {
            var packageId = Guid.NewGuid().ToString("N");
            var manifest = new PackageManifest
            {
                SchemaVersion = 2,
                PackageId = packageId,
                Title = request.Title.Trim(),
                Version = request.Version.Trim(),
                DeploymentType = PackageDeploymentType.OfflineMedia,
                RequiredDiscCount = 1,
                TotalDiscCount = 1,
                IconFile = "game.ico",
                DiscLayout = [new PackageDiscInfo { DiscNumber = 1, MediaName = request.MediaName, CapacityBytes = request.CapacityBytes }]
            };
            File.Copy(request.LauncherExecutable, Path.Combine(discRoot, "Launch.exe"), true);
            IconFile.Create(request.IconImage, Path.Combine(discRoot, "game.ico"));
            CopyArtwork(request.BackgroundImage, "background", name => manifest.BackgroundFile = name);
            CopyArtwork(request.CoverImage, "cover", name => manifest.CoverFile = name);

            long completed = 0;
            foreach (var game in request.Collection.Games)
            {
                var gameId = Guid.NewGuid().ToString("N");
                var folder = PackageBuilder.SanitizeFileName(game.Title);
                var gameManifest = new CollectionGameManifest
                {
                    GameId = gameId,
                    Title = game.Title,
                    Version = game.Version,
                    InstallerRelativePath = $"Games/{folder}/{game.Family.InstallerFiles[0].RelativePath}",
                    ExtrasRelativePath = $"Extras/{folder}",
                    InstallDetectionNames = [game.Title]
                };
                foreach (var source in game.Family.InstallerFiles.Concat(game.Family.Extras))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var kindFolder = source.Kind == PackageFileKind.Installer ? "Games" : "Extras";
                    var relative = $"{kindFolder}/{folder}/{source.RelativePath}".Replace('\\', '/');
                    var destination = SafePaths.ResolveUnderRoot(discRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    var hash = await CopyFileAndHashAsync(source.FullPath, destination, bytes => progress?.Report(new PackagingProgress(
                        $"Adding {game.Title}", source.RelativePath, completed + bytes, request.Collection.TotalBytes)), cancellationToken);
                    completed += source.Size;
                    var entry = new PackageFileEntry
                    {
                        RelativePath = relative,
                        DiscPath = relative,
                        Size = source.Size,
                        Sha256 = hash,
                        DiscNumber = 1,
                        Kind = source.Kind,
                        SourceSize = source.Size
                    };
                    manifest.Files.Add(entry);
                    gameManifest.Files.Add(entry);
                }
                manifest.CollectionGames.Add(gameManifest);
            }

            var packageBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonFiles.Options);
            await File.WriteAllBytesAsync(Path.Combine(discRoot, "package.json"), packageBytes, cancellationToken);
            JsonFiles.Write(Path.Combine(discRoot, "disc.json"), new DiscManifest
            {
                SchemaVersion = 2,
                PackageId = packageId,
                Version = manifest.Version,
                DiscNumber = 1,
                TotalDiscCount = 1,
                Role = DiscRole.Installer,
                MediaName = request.MediaName,
                CapacityBytes = request.CapacityBytes,
                PackageManifestSha256 = Hashing.Sha256Bytes(packageBytes),
                Files = manifest.Files
            });
            await File.WriteAllTextAsync(Path.Combine(discRoot, "autorun.inf"),
                $"[AutoRun]\r\nopen=Launch.exe\r\nicon=game.ico\r\nlabel={request.Title.Replace("\r", " ").Replace("\n", " ")}\r\naction=Browse {request.Title}\r\n", Encoding.ASCII, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(buildRoot, "BURNING-INSTRUCTIONS.txt"),
                $"{request.Title} {request.Version}\r\n{request.Collection.Games.Count} games on one {request.MediaName}\r\n\r\nBurn the CONTENTS of Disc 01 of 01 using UDF 2.50 or later and enable verify-after-write.\r\n", cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(finalRoot)!);
            if (Directory.Exists(finalRoot)) Directory.Delete(finalRoot);
            Directory.Move(buildRoot, finalRoot);
            progress?.Report(new PackagingProgress("Complete", "", request.Collection.TotalBytes, request.Collection.TotalBytes));
            return new PackageBuildResult(finalRoot, manifest);

            void CopyArtwork(string? source, string stem, Action<string> assign)
            {
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return;
                var name = stem + Path.GetExtension(source).ToLowerInvariant();
                File.Copy(source, Path.Combine(discRoot, name), true);
                assign(name);
            }
        }
        catch
        {
            if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, true);
            throw;
        }
    }

    private static async Task<string> CopyFileAndHashAsync(string source, string destination, Action<long> progress, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        var buffer = new byte[1024 * 1024];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long copied = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer, 0, read);
            copied += read;
            progress(copied);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string FormatBytes(long value) => $"{value / 1024d / 1024d / 1024d:N2} GiB";
}
