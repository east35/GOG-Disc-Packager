namespace GogDisc.Core;

public static class AppPaths
{
    public static string Root => OperatingSystem.IsLinux()
        ? Path.Combine(Xdg("XDG_DATA_HOME", ".local/share"), "gog-disc-companion")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GOG Disc Tool");
    public static string Runtime => Path.Combine(Root, "Runtime");
    public static string State => Path.Combine(Root, "State");
    public static string Staging => Path.Combine(Root, "Staging");
    public static string Logs => Path.Combine(Root, "Logs");
    public static string GogRuntime => Path.Combine(Root, "GOG Runtime");
    public static string GogAuth => OperatingSystem.IsLinux()
        ? Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), "gog-disc-companion", "gog-auth.json")
        : Path.Combine(GogRuntime, "auth.json");
    public static string LauncherSettings => Path.Combine(Root, "launcher-settings.json");

    public static string PackageState(string packageId) => Path.Combine(State, PackageBuilder.SanitizeFileName(packageId) + ".json");
    public static string PackageLog(string packageId) => Path.Combine(Logs, PackageBuilder.SanitizeFileName(packageId) + ".log");

    private static string Xdg(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback)
            : Path.GetFullPath(value);
    }
}
