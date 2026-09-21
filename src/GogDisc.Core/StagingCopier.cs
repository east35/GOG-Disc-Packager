using System.Diagnostics;
using System.Security.Cryptography;

namespace GogDisc.Core;

public sealed record CopyProgress(string FileName, long DiscBytesCopied, long DiscBytesTotal, long FileBytesCopied, long FileBytesTotal)
{
    public double DiscPercent => DiscBytesTotal == 0 ? 0 : DiscBytesCopied * 100d / DiscBytesTotal;
}

public static class StagingCopier
{
    public static async Task CopyDiscAsync(
        LoadedDisc media,
        string stagingRoot,
        StagingState state,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken,
        PackageFileKind kind = PackageFileKind.Installer)
    {
        Directory.CreateDirectory(stagingRoot);
        var entries = media.Disc.Files.Where(file => file.Kind == kind).ToList();
        var total = entries.Sum(file => file.Size);
        long completed = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = SafePaths.ResolveUnderRoot(media.Root, entry.DiscPath);
            var destination = SafePaths.ResolveUnderRoot(stagingRoot, entry.RelativePath);
            var multipart = PartCount(entry) > 1;
            var assemblyPath = destination + ".gog-assembling";
            var key = VerificationKey(entry);

            if (IsVerified(entry, destination, assemblyPath, state) &&
                await HasExpectedHashAsync(entry, destination, assemblyPath, cancellationToken))
            {
                completed += entry.Size;
                progress?.Report(new CopyProgress(entry.RelativePath, completed, total, entry.Size, entry.Size));
                FinalizeIfComplete(media.Package, entry, destination, assemblyPath, state);
                continue;
            }
            state.VerifiedFiles.Remove(key);

            await using var input = await OpenDiscFileAsync(source, entry, cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var outputPath = multipart ? (File.Exists(destination) ? destination : assemblyPath) : destination + ".partial";
            if (!multipart && File.Exists(outputPath)) File.Delete(outputPath);

            await using var output = new FileStream(outputPath, multipart ? FileMode.OpenOrCreate : FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 1024 * 1024, true);
            if (multipart) output.Seek(entry.SourceOffset, SeekOrigin.Begin);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            long fileCopied = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hasher.AppendData(buffer, 0, read);
                fileCopied += read;
                progress?.Report(new CopyProgress(entry.RelativePath, completed + fileCopied, total, fileCopied, entry.Size));
            }
            await output.FlushAsync(cancellationToken);
            output.Flush(flushToDisk: true);
            output.Close();

            var actualHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            if (!actualHash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                if (!multipart) File.Delete(outputPath);
                throw new IOException($"Verification failed for {entry.RelativePath}. Clean the disc and try again.");
            }

            if (!multipart)
            {
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(outputPath, destination);
            }
            state.VerifiedFiles.Add(key);
            StagingStateStore.Save(stagingRoot, state);
            FinalizeIfComplete(media.Package, entry, destination, assemblyPath, state);
            completed += entry.Size;
        }
    }

    /// <summary>How long a disc file may stay unreadable before the media is called bad.</summary>
    public static TimeSpan DriveSettleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Windows lists a newly inserted disc as soon as its filesystem mounts, which is before the drive has
    /// finished spinning up: for a few seconds the payload reads as missing or short. Failing on the first look turned
    /// an ordinary disc swap into a failed install that only a manual retry fixed, so wait the drive out — a file that
    /// is genuinely absent still fails, just later.</summary>
    private static async Task<FileStream> OpenDiscFileAsync(string source, PackageFileEntry entry, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(source) && new FileInfo(source).Length == entry.Size)
                    return new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            if (Stopwatch.GetElapsedTime(started) >= DriveSettleTimeout)
                throw new IOException($"Missing or incorrectly sized disc file: {entry.DiscPath}");
            await Task.Delay(500, cancellationToken);
        }
    }

    public static async Task<IReadOnlyList<int>> RevalidateAsync(PackageManifest package, string stagingRoot,
        StagingState state, CancellationToken cancellationToken)
    {
        var needed = new SortedSet<int>();
        foreach (var entry in package.Files.Where(f => f.Kind == PackageFileKind.Installer))
        {
            var destination = SafePaths.ResolveUnderRoot(stagingRoot, entry.RelativePath);
            if (IsVerified(entry, destination, destination + ".gog-assembling", state) &&
                await HasExpectedHashAsync(entry, destination, destination + ".gog-assembling", cancellationToken)) continue;
            state.VerifiedFiles.Remove(VerificationKey(entry));
            needed.Add(entry.DiscNumber);
        }
        StagingStateStore.Save(stagingRoot, state);
        return needed.ToArray();
    }

    public static long RemainingBytes(PackageManifest package, string stagingRoot, StagingState state) =>
        package.Files.Where(file => file.Kind == PackageFileKind.Installer)
            .Where(file =>
            {
                var destination = SafePaths.ResolveUnderRoot(stagingRoot, file.RelativePath);
                return !IsVerified(file, destination, destination + ".gog-assembling", state);
            })
            .Sum(file => file.Size);

    public static bool IsEntryVerified(PackageFileEntry entry, string stagingRoot, StagingState state)
    {
        var destination = SafePaths.ResolveUnderRoot(stagingRoot, entry.RelativePath);
        return IsVerified(entry, destination, destination + ".gog-assembling", state);
    }

    private static bool IsVerified(PackageFileEntry entry, string destination, string assemblyPath, StagingState state)
    {
        if (!state.VerifiedFiles.Contains(VerificationKey(entry))) return false;
        if (PartCount(entry) <= 1)
            return File.Exists(destination) && new FileInfo(destination).Length == entry.Size;
        if (File.Exists(destination) && new FileInfo(destination).Length == SourceSize(entry)) return true;
        return File.Exists(assemblyPath) && new FileInfo(assemblyPath).Length >= entry.SourceOffset + entry.Size;
    }

    private static async Task<bool> HasExpectedHashAsync(
        PackageFileEntry entry,
        string destination,
        string assemblyPath,
        CancellationToken cancellationToken)
    {
        var path = PartCount(entry) <= 1 || File.Exists(destination) ? destination : assemblyPath;
        if (!File.Exists(path)) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (PartCount(entry) > 1) stream.Seek(entry.SourceOffset, SeekOrigin.Begin);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long remaining = entry.Size;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0) return false;
            hasher.AppendData(buffer, 0, read);
            remaining -= read;
        }
        var actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        return actual.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void FinalizeIfComplete(
        PackageManifest package,
        PackageFileEntry entry,
        string destination,
        string assemblyPath,
        StagingState state)
    {
        if (PartCount(entry) <= 1) return;
        var parts = package.Files.Where(file => file.Kind == entry.Kind &&
            file.RelativePath.Equals(entry.RelativePath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (parts.Any(part => !state.VerifiedFiles.Contains(VerificationKey(part)))) return;
        if (!File.Exists(assemblyPath) && File.Exists(destination) && new FileInfo(destination).Length == SourceSize(entry)) return;
        if (!File.Exists(assemblyPath) || new FileInfo(assemblyPath).Length != SourceSize(entry))
            throw new IOException($"Reassembled file has an unexpected size: {entry.RelativePath}");
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(assemblyPath, destination);
    }

    private static string VerificationKey(PackageFileEntry entry) =>
        PartCount(entry) <= 1 ? entry.RelativePath : $"{entry.RelativePath}|part:{entry.PartIndex:D4}";

    private static int PartCount(PackageFileEntry entry) => Math.Max(1, entry.PartCount);
    private static long SourceSize(PackageFileEntry entry) => entry.SourceSize > 0 ? entry.SourceSize : entry.Size;
}
