namespace GogDisc.Core;

public sealed class LauncherSettings
{
    public bool KeepOpenInBackground { get; set; }
}

public static class LauncherSettingsStore
{
    public static LauncherSettings Load()
    {
        try
        {
            return File.Exists(AppPaths.LauncherSettings)
                ? JsonFiles.Read<LauncherSettings>(AppPaths.LauncherSettings)
                : new LauncherSettings();
        }
        catch
        {
            // A preference should never prevent a disc from opening.
            return new LauncherSettings();
        }
    }

    public static void Save(LauncherSettings settings) => JsonFiles.Write(AppPaths.LauncherSettings, settings);
}
