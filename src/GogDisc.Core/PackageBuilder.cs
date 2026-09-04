using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GogDisc.Core;

public static class PackageBuilder
{
    public static async Task<PackageBuildResult> BuildAsync(
        PackageBuildRequest request,
        IProgress<PackagingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var safeName = SanitizeFileName($"{request.Title} {request.Version}".Trim());
        var finalRoot = Path.Combine(Path.GetFullPath(request.OutputDirectory), safeName);
        if (Directory.Exists(finalRoot) && Directory.EnumerateFileSystemEntries(finalRoot).Any())
            throw new IOException($"Output package already exists and is not empty: {finalRoot}");

        var buildRoot = finalRoot + $".building-{Guid.NewGuid():N}";
        Directory.CreateDirectory(buildRoot);
        var packageId = Guid.NewGuid().ToString("N");
        var totalBytes = request.Plan.InstallerBytes + request.Plan.ExtrasBytes;
        long completedBytes = 0;

        try
        {
            var manifest = new PackageManifest
            {
                PackageId = packageId,
                Title = request.Title.Trim(),
                Version = request.Version.Trim(),
                ProductType = request.ProductType,
                RequiredDiscCount = request.Plan.RequiredDiscCount,
                TotalDiscCount = request.Plan.Discs.Count,
                InstallerRelativePath = request.SetupFamily.InstallerFiles[0].RelativePath,
                IconFile = "game.ico",
                DiscLayout = request.Plan.Discs.Select(disc => new PackageDiscInfo
                {
                    DiscNumber = disc.Number,
                    MediaName = disc.MediaName,
                    CapacityBytes = disc.CapacityBytes
                }).ToList(),
                InstallDetectionNames = [request.Title.Trim()]
            };

            var discDirectories = new Dictionary<int, string>();
            foreach (var disc in request.Plan.Discs)
            {
                var discRoot = Path.Combine(buildRoot, $"Disc {disc.Number:00} of {request.Plan.Discs.Count:00}");
                Directory.CreateDirectory(discRoot);
                discDirectories[disc.Number] = discRoot;
                File.Copy(request.LauncherExecutable, Path.Combine(discRoot, "Launch.exe"), true);
                IconFile.Create(request.IconImage, Path.Combine(discRoot, "game.ico"));
            }

            if (!string.IsNullOrWhiteSpace(request.BackgroundImage) && File.Exists(request.BackgroundImage))
            {
                var backgroundName = "background" + Path.GetExtension(request.BackgroundImage).ToLowerInvariant();
                manifest.BackgroundFile = backgroundName;
                foreach (var root in discDirectories.Values)
                    File.Copy(request.BackgroundImage, Path.Combine(root, backgroundName), true);
            }

            if (!string.IsNullOrWhiteSpace(request.CoverImage) && File.Exists(request.CoverImage))
            {
                var coverName = "cover" + Path.GetExtension(request.CoverImage).ToLowerInvariant();
                manifest.CoverFile = coverName;
                foreach (var root in discDirectories.Values)
                    File.Copy(request.CoverImage, Path.Combine(root, coverName), true);
            }

            foreach (var disc in request.Plan.Discs)
            {
                foreach (var source in disc.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var prefix = source.Kind == PackageFileKind.Installer ? "Payload" : "Extras";
                    var discPath = source.PartCount <= 1
                        ? Path.Combine(prefix, source.RelativePath)
                        : Path.Combine(prefix, ".gog-parts", source.RelativePath + $".part-{source.PartIndex:D4}-of-{source.PartCount:D4}");
                    var destination = Path.Combine(discDirectories[disc.Number], discPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    var hash = await CopyAndHashAsync(source.FullPath, destination, source.SourceOffset, source.Size, bytes =>
                    {
                        progress?.Report(new PackagingProgress(
                            $"Building Disc {disc.Number} of {request.Plan.Discs.Count}",
                            source.RelativePath,
                            completedBytes + bytes,
                            totalBytes));
                    }, cancellationToken);
                    completedBytes += source.Size;

                    manifest.Files.Add(new PackageFileEntry
                    {
                        RelativePath = source.RelativePath.Replace('\\', '/'),
                        DiscPath = discPath.Replace('\\', '/'),
                        Size = source.Size,
                        Sha256 = hash,
                        DiscNumber = disc.Number,
                        Kind = source.Kind,
                        SourceOffset = source.SourceOffset,
                        SourceSize = source.SourceSize == 0 ? source.Size : source.SourceSize,
                        PartIndex = source.PartIndex,
                        PartCount = source.PartCount
                    });
                }
            }

            var packageBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonFiles.Options);
            var packageHash = Hashing.Sha256Bytes(packageBytes);
            foreach (var disc in request.Plan.Discs)
            {
                var root = discDirectories[disc.Number];
                await File.WriteAllBytesAsync(Path.Combine(root, "package.json"), packageBytes, cancellationToken);
                var discManifest = new DiscManifest
                {
                    PackageId = packageId,
                    Version = manifest.Version,
                    DiscNumber = disc.Number,
                    TotalDiscCount = manifest.TotalDiscCount,
                    Role = disc.Role,
                    MediaName = disc.MediaName,
                    CapacityBytes = disc.CapacityBytes,
                    PackageManifestSha256 = packageHash,
                    Files = manifest.Files.Where(file => file.DiscNumber == disc.Number).ToList()
                };
                JsonFiles.Write(Path.Combine(root, "disc.json"), discManifest);
                await File.WriteAllTextAsync(Path.Combine(root, "autorun.inf"), AutorunText(manifest), Encoding.ASCII, cancellationToken);
            }

            await File.WriteAllTextAsync(Path.Combine(buildRoot, "BURNING-INSTRUCTIONS.txt"), BurningInstructions(manifest), cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(finalRoot)!);
            if (Directory.Exists(finalRoot)) Directory.Delete(finalRoot);
            Directory.Move(buildRoot, finalRoot);
            progress?.Report(new PackagingProgress("Complete", "", totalBytes, totalBytes));
            return new PackageBuildResult(finalRoot, manifest);
        }
        catch
        {
            if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, true);
            throw;
        }
    }

    private static async Task<string> CopyAndHashAsync(
        string source,
        string destination,
        long sourceOffset,
        long length,
        Action<long> progress,
        CancellationToken cancellationToken)
    {
        var partial = destination + ".partial";
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        await using var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long copied = 0;
        input.Seek(sourceOffset, SeekOrigin.Begin);
        while (copied < length)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - copied)), cancellationToken);
            if (read == 0) throw new EndOfStreamException($"Unexpected end of source file: {source}");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer, 0, read);
            copied += read;
            progress(copied);
        }
        await output.FlushAsync(cancellationToken);
        output.Close();
        File.Move(partial, destination);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void ValidateRequest(PackageBuildRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("A title is required.");
        if (string.IsNullOrWhiteSpace(request.Version)) throw new ArgumentException("A version is required.");
        if (!File.Exists(request.LauncherExecutable)) throw new FileNotFoundException("Published Launch.exe was not found.", request.LauncherExecutable);
        if (request.Plan.Discs.Count == 0) throw new InvalidDataException("The package plan contains no discs.");
    }

    private static string AutorunText(PackageManifest manifest)
    {
        var title = manifest.Title.Replace("\r", " ").Replace("\n", " ");
        return $"[AutoRun]\r\nopen=Launch.exe\r\nicon=game.ico\r\nlabel={title}\r\naction=Install or play {title}\r\n";
    }

    private static string BurningInstructions(PackageManifest manifest) =>
        $"{manifest.Title} {manifest.Version}{Environment.NewLine}" +
        $"{manifest.TotalDiscCount} burn-ready disc folder(s){Environment.NewLine}{Environment.NewLine}" +
        "Disc layout:\r\n" +
        string.Join("\r\n", manifest.DiscLayout.Select(disc => $"  Disc {disc.DiscNumber:00}: {disc.MediaName}")) + "\r\n\r\n" +
        "Burn the CONTENTS of each Disc folder to its own disc using UDF 2.50 or later.\r\n" +
        "Use a distinct volume label ending in the disc number, and enable your burning software's verify-after-write option.\r\n" +
        "Keep Launch.exe, package.json, disc.json, autorun.inf, game.ico, artwork, Payload, and Extras at the disc root.\r\n" +
        "Windows may ignore AutoRun; in that case, open the disc and run Launch.exe manually.\r\n";

    public static string SanitizeFileName(string value)
    {
        foreach (var character in Path.GetInvalidFileNameChars()) value = value.Replace(character, '_');
        return string.IsNullOrWhiteSpace(value) ? "GOG Package" : value.Trim();
    }
}
