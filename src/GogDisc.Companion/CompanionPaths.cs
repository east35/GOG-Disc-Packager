namespace GogDisc.Companion;

internal static class CompanionPaths
{
    public static string StagingRoot(string packageId) => Path.Combine(CacheRoot, "staging", SafeId(packageId));
    public static string PrefixRoot(string packageId) => Path.Combine(DataRoot, "prefixes", SafeId(packageId));
    public static string LogPath(string packageId) => Path.Combine(StateRoot, "logs", SafeId(packageId) + ".log");
    public static string LibraryRoot => Path.Combine(DataRoot, "library");
    public static string PackageRoot(string id) => Path.Combine(LibraryRoot, SafeId(id));
    public static string ApplicationsRoot => Path.Combine(Xdg("XDG_DATA_HOME", ".local/share"), "applications");
    public static string ActivationPath => Path.Combine(StateRoot, "show-library");
    public static string AppIcon => Path.Combine(AppContext.BaseDirectory, "AppIcon.png");
    public static string CacheRoot => Path.Combine(Xdg("XDG_CACHE_HOME", ".cache"), "gog-disc-companion");
    public static string DataRoot => Path.Combine(Xdg("XDG_DATA_HOME", ".local/share"), "gog-disc-companion");
    public static string StateRoot => Path.Combine(Xdg("XDG_STATE_HOME", ".local/state"), "gog-disc-companion");

    private static string Xdg(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback)
            : Path.GetFullPath(value);
    }

    public static string SafeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new InvalidDataException("Invalid package identity.");
        return value;
    }
}
