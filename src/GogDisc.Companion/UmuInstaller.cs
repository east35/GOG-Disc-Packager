using System.Diagnostics;

namespace GogDisc.Companion;

internal static class UmuInstaller
{
    public static bool IsAvailable => FindOnPath("umu-run") is not null;

    public static string? ExistingRuntime(LibraryGame game)
    {
        if (game.ProtonPath is not null) return game.ProtonPath;
        var versionFile = Path.Combine(CompanionPaths.PrefixRoot(game.Package.PackageId), "version");
        if (!File.Exists(versionFile)) return null;
        var version = File.ReadAllText(versionFile).Trim();
        if (version.Contains('/') || version.Contains('\\') || version is "" or "." or "..") return null;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[] { Path.Combine(home, ".local/share/Steam/compatibilitytools.d", version),
            Path.Combine(home, ".steam/root/compatibilitytools.d", version) }
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "proton")));
    }

    public static async Task<int> RunAsync(LibraryGame game, string target, string phase, CancellationToken cancellationToken)
    {
        var executable = FindOnPath("umu-run") ??
            throw new FileNotFoundException("umu-run is not installed. Install umu-launcher and try again.");
        var prefix = CompanionPaths.PrefixRoot(game.Package.PackageId);
        Directory.CreateDirectory(prefix);
        var runtime = ExistingRuntime(game);
        if (runtime is null && (phase != "Setup" || File.Exists(Path.Combine(prefix, "version"))))
            throw new IOException("The runtime for this prefix could not be found. Restore it or select its Proton folder.");
        if (runtime is not null && !File.Exists(Path.Combine(runtime, "proton")))
            throw new IOException($"The recorded runtime is missing: {runtime}. Restore it or select its Proton folder.");
        if (runtime is not null) { game.ProtonPath = runtime; LibraryStore.Save(game); }
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(target)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(target);
        start.Environment["WINEPREFIX"] = prefix;
        start.Environment["GAMEID"] = "umu-default";
        start.Environment["STORE"] = "gog";
        start.Environment["UMU_LOG"] = "1";
        if (runtime is not null) start.Environment["PROTONPATH"] = runtime;
        PhaseLog.Write(game.Package.PackageId, phase, $"Starting {target}; prefix={prefix}; runtime={runtime ?? "first-run provisioning"}");
        using var process = Process.Start(start) ?? throw new IOException("Could not start umu-run.");
        // Both streams use one synchronized logger; every line is persisted immediately.
        var stdout = PumpAsync(process.StandardOutput, game, phase);
        var stderr = PumpAsync(process.StandardError, game, phase);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            PhaseLog.Write(game.Package.PackageId, phase, "Cancelled; process tree stopped.");
            throw;
        }
        await Task.WhenAll(stdout, stderr);
        game.ProtonPath ??= ExistingRuntime(game);
        LibraryStore.Save(game);
        PhaseLog.Write(game.Package.PackageId, phase, $"umu-run exited with {process.ExitCode}; runtime={game.ProtonPath}");
        return process.ExitCode;
    }

    private static async Task PumpAsync(StreamReader reader, LibraryGame game, string phase)
    {
        while (await reader.ReadLineAsync() is { } line)
            PhaseLog.Write(game.Package.PackageId, phase, line);
    }

    private static string? FindOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
}
