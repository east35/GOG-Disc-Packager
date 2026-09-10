namespace GogDisc.Core;

public static class InstallStateStore
{
    public static InstallState? Load(string packageId)
    {
        var path = AppPaths.PackageState(packageId);
        try { return File.Exists(path) ? JsonFiles.Read<InstallState>(path) : null; }
        catch { return null; }
    }

    public static void Save(InstallState state) => JsonFiles.Write(AppPaths.PackageState(state.PackageId), state);
}

public static class StagingStateStore
{
    public const string FileName = ".gog-disc-staging.json";

    public static StagingState LoadOrCreate(string stagingRoot, PackageManifest package)
    {
        var path = Path.Combine(stagingRoot, FileName);
        try
        {
            var state = File.Exists(path) ? JsonFiles.Read<StagingState>(path) : null;
            if (state is not null && state.PackageId == package.PackageId && state.Version == package.Version)
                return state;
        }
        catch { }

        return new StagingState { PackageId = package.PackageId, Version = package.Version };
    }

    public static void Save(string stagingRoot, StagingState state)
    {
        Directory.CreateDirectory(stagingRoot);
        var path = Path.Combine(stagingRoot, FileName);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                System.Text.Json.JsonSerializer.Serialize(stream, state, JsonFiles.Options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
