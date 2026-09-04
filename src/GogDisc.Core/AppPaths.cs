namespace GogDisc.Core;

public static class AppPaths
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GOG Disc Tool");
    public static string Runtime => Path.Combine(Root, "Runtime");
    public static string State => Path.Combine(Root, "State");
    public static string Staging => Path.Combine(Root, "Staging");
    public static string Logs => Path.Combine(Root, "Logs");

    public static string PackageState(string packageId) => Path.Combine(State, PackageBuilder.SanitizeFileName(packageId) + ".json");
    public static string PackageLog(string packageId) => Path.Combine(Logs, PackageBuilder.SanitizeFileName(packageId) + ".log");
}
