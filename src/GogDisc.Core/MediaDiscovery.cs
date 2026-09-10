namespace GogDisc.Core;

public static class MediaDiscovery
{
    public static IReadOnlyList<LoadedDisc> FindMountedPackages()
    {
        var candidates = new HashSet<string>(PathComparer);
        foreach (var root in CandidateMountRoots())
        {
            if (!Directory.Exists(root)) continue;
            AddIfPackage(root, candidates);
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(root))
                {
                    AddIfPackage(directory, candidates);
                    try
                    {
                        foreach (var child in Directory.EnumerateDirectories(directory))
                            AddIfPackage(child, candidates);
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return candidates.Select(path =>
        {
            try { return DiscMedia.Load(path); }
            catch { return null; }
        }).OfType<LoadedDisc>().ToList();
    }

    private static IEnumerable<string> CandidateMountRoots()
    {
        if (OperatingSystem.IsLinux())
        {
            var user = Environment.UserName;
            yield return Path.Combine("/run/media", user);
            yield return Path.Combine("/media", user);
            yield return "/mnt";
        }

        foreach (var drive in DriveInfo.GetDrives())
            if (drive.IsReady) yield return drive.RootDirectory.FullName;
    }

    private static void AddIfPackage(string path, ISet<string> candidates)
    {
        if (File.Exists(Path.Combine(path, "package.json")) && File.Exists(Path.Combine(path, "disc.json")))
            candidates.Add(Path.GetFullPath(path));
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
