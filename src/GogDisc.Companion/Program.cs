using GogDisc.Core;
using Avalonia;
using GogDisc.Companion;

if (args.Length == 2 && args[0] == "play")
{
    try
    {
        using var lease = OperationLease.Acquire(args[1]);
        var game = LibraryStore.Load(args[1]) ?? throw new IOException("Game is not in the library.");
        var target = LibraryStore.ValidateTarget(game, game.PlayTarget ?? "");
        var code = await UmuInstaller.RunAsync(game, target, "Play", CancellationToken.None);
        game.LastLaunchAt = DateTimeOffset.Now; LibraryStore.Save(game);
        if (code != 0) throw new IOException($"Game exited with code {code}. See {CompanionPaths.LogPath(game.Package.PackageId)}");
        return code;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        if (!OperatingSystem.IsLinux()) return 1;
        CompanionApp.StartupError = ex.Message;
        args = [];
    }
}
if ((args.Length > 0 && args[0] != "--watch") || !OperatingSystem.IsLinux())
    return await CompanionCli.RunAsync(args);

using var instance = new Mutex(true, $"gog-disc-companion-{Environment.UserName}", out var isFirstInstance);
if (!isFirstInstance)
{
    if (args.Length == 0)
    {
        Directory.CreateDirectory(CompanionPaths.StateRoot);
        File.WriteAllText(CompanionPaths.ActivationPath, CompanionApp.StartupError ?? "");
    }
    return 0;
}
CompanionApp.StartHidden = args.Contains("--watch");
BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
return 0;

static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<CompanionApp>()
    .UsePlatformDetect()
    .LogToTrace();

internal static class CompanionCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Any(arg => arg is "--help" or "-h")) return Usage();
            var command = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : "inspect";
            if (command == "help") return Usage();
            var discRoot = Option(args, "--disc");
            var media = discRoot is null ? DiscoverOne() : DiscMedia.Load(discRoot);
            PrintMedia(media);
            if (command.Equals("inspect", StringComparison.OrdinalIgnoreCase)) return 0;
            if (!command.Equals("stage", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Unknown command: {command}");
            if (media.Package.DeploymentType != PackageDeploymentType.OfflineMedia)
                throw new InvalidDataException("GOG Key Media is not supported by the Linux companion yet.");

            var stagingRoot = Option(args, "--staging") ?? CompanionPaths.StagingRoot(media.Package.PackageId);
            Console.WriteLine($"Staging to: {stagingRoot}");
            var state = StagingStateStore.LoadOrCreate(stagingRoot, media.Package);
            var lastPercent = -1;
            var progress = new Progress<CopyProgress>(value =>
            {
                var percent = (int)value.DiscPercent;
                if (percent == lastPercent) return;
                lastPercent = percent;
                Console.Write($"\r{percent,3}%  {value.FileName}   ");
            });
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancel;
            try
            {
                await StagingCopier.CopyDiscAsync(media, stagingRoot, state, progress, cancellation.Token);
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }
            Console.WriteLine("\nDisc staged and SHA-256 verified.");
            var needed = await StagingCopier.RevalidateAsync(media.Package, stagingRoot, state, cancellation.Token);
            var remaining = StagingCopier.RemainingBytes(media.Package, stagingRoot, state);
            if (needed.Count == 0)
                Console.WriteLine($"Installer ready: {SafePaths.ResolveUnderRoot(stagingRoot, media.Package.InstallerRelativePath)}");
            else
                Console.WriteLine($"Insert package disc {string.Join(", ", needed)} to continue ({FormatBytes(remaining)} remaining).");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Staging cancelled. Verified files will be reused next time.");
            return 130;
        }
    }

    private static LoadedDisc DiscoverOne()
    {
        var packages = MediaDiscovery.FindMountedPackages();
        return packages.Count switch
        {
            0 => throw new InvalidDataException("No mounted GOG Disc Tool package was found. Use --disc <folder>."),
            1 => packages[0],
            _ => throw new InvalidDataException("Several packages are mounted. Use --disc <folder>.")
        };
    }

    private static void PrintMedia(LoadedDisc media)
    {
        Console.WriteLine($"{media.Package.Title} {media.Package.Version}");
        Console.WriteLine($"Package: {media.Package.PackageId} (schema {media.Package.SchemaVersion})");
        Console.WriteLine($"Disc: {media.Disc.DiscNumber} of {media.Disc.TotalDiscCount} — {media.Disc.Role}");
        Console.WriteLine($"Files: {media.Disc.Files.Count} ({FormatBytes(media.Disc.Files.Sum(file => file.Size))})");
        Console.WriteLine($"Media root: {media.Root}");
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        if (index + 1 >= args.Length) throw new ArgumentException($"{name} requires a value.");
        return args[index + 1];
    }

    private static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d:N1} MiB";

    private static int Usage()
    {
        Console.WriteLine("GOG Disc Companion (early Linux proof)\n");
        Console.WriteLine("  gog-disc-companion                     Open library");
        Console.WriteLine("  gog-disc-companion --watch             Watch mounted discs in the background");
        Console.WriteLine("  gog-disc-companion play <package-id>   Launch a saved game");
        Console.WriteLine("  gog-disc-companion inspect [--disc <mounted-folder>]");
        Console.WriteLine("  gog-disc-companion stage [--disc <mounted-folder>] [--staging <folder>]");
        return 0;
    }
}
