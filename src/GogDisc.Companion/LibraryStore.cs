using System.Text.Json;
using GogDisc.Core;

namespace GogDisc.Companion;

internal sealed class LibraryGame
{
    public PackageManifest Package { get; set; } = new();
    public string? PlayTarget { get; set; }
    public string? ProtonPath { get; set; }
    public string Status { get; set; } = "Ready to stage";
    public DateTimeOffset? InstalledAt { get; set; }
    public DateTimeOffset? LastLaunchAt { get; set; }
    public string? DesktopShortcut { get; set; }
    public override string ToString() => Package.Title + (PlayTarget is null ? " — " + Status : "");
}

internal static class LibraryStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static LibraryGame? Load(string id)
    {
        var path = Path.Combine(CompanionPaths.PackageRoot(id), "installation.json");
        if (!File.Exists(path)) return null;
        var game = JsonSerializer.Deserialize<LibraryGame>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The installation record is empty.");
        if (game.Package.PackageId != id) throw new InvalidDataException("Installation identity mismatch.");
        return game;
    }

    public static IReadOnlyList<LibraryGame> List()
    {
        if (!Directory.Exists(CompanionPaths.LibraryRoot)) return [];
        var games = new List<LibraryGame>();
        foreach (var directory in Directory.EnumerateDirectories(CompanionPaths.LibraryRoot))
        {
            try { if (Load(Path.GetFileName(directory)) is { } game) games.Add(game); }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            { PhaseLog.Write("library", "Read record", ex.Message); }
        }
        return games.OrderBy(g => g.Package.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static void Save(LibraryGame game)
    {
        var root = CompanionPaths.PackageRoot(game.Package.PackageId);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "installation.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, game, Options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static IEnumerable<string> Executables(LibraryGame game)
    {
        var root = Path.Combine(CompanionPaths.PrefixRoot(game.Package.PackageId), "drive_c");
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
        }).Where(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
          .Where(p => !Path.GetRelativePath(root, p).StartsWith("windows/", StringComparison.OrdinalIgnoreCase))
          .Where(p => !Path.GetFileName(p).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
          .Where(p => !Path.GetFileName(p).StartsWith("setup", StringComparison.OrdinalIgnoreCase))
          .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string ValidateTarget(LibraryGame game, string target)
    {
        var root = CompanionPaths.PrefixRoot(game.Package.PackageId);
        var full = SafePaths.ResolveUnderRoot(root, Path.GetRelativePath(root, Path.GetFullPath(target)));
        EnsureNoLinks(root, full);
        if (!File.Exists(full) || !full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose an existing Windows executable inside this game's prefix.");
        return full;
    }

    public static void Remove(LibraryGame game, bool deletePrefix)
    {
        if (deletePrefix)
        {
            var prefix = CompanionPaths.PrefixRoot(game.Package.PackageId);
            EnsureNoLinks(CompanionPaths.DataRoot, prefix);
            if (Directory.Exists(prefix)) Directory.Delete(prefix, recursive: true);
        }
        DesktopIntegration.RemoveShortcuts(game);
        File.Delete(Path.Combine(CompanionPaths.PackageRoot(game.Package.PackageId), "installation.json"));
    }

    internal static void EnsureNoLinks(string root, string path)
    {
        for (var current = Path.GetFullPath(path); ; current = Path.GetDirectoryName(current)!)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A symbolic link is not allowed for this operation.");
            if (current == Path.GetPathRoot(current)) break;
        }
    }
}

internal static class PhaseLog
{
    private static readonly object Gate = new();
    public static void Write(string id, string phase, string message)
    {
        lock (Gate)
        {
            var path = CompanionPaths.LogPath(id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{phase}] {message.Replace('\r', ' ').Replace('\n', ' ')}\n");
        }
    }
}
