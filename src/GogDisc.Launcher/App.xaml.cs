using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GogDisc.Core;

namespace GogDisc.Launcher;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private DispatcherTimer? _activationTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var args = ArgumentMap.Parse(e.Args);
            if (args.ContainsKey("self-test"))
            {
                DiscMedia.Load(args.GetValueOrDefault("disc-root") ?? AppContext.BaseDirectory);
                Shutdown(0);
                return;
            }
            if (!args.ContainsKey("local"))
            {
                BootstrapLocal(args);
                Shutdown();
                return;
            }

            var packagePath = Required(args, "package");
            var package = JsonFiles.Read<PackageManifest>(packagePath);
            var key = new string(package.PackageId.Where(char.IsLetterOrDigit).ToArray());
            _mutex = new Mutex(true, $"Local\\GogDiscTool.{key}", out var created);
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\GogDiscTool.Activate.{key}");
            if (!created)
            {
                _activateEvent.Set();
                Shutdown();
                return;
            }

            var window = new MainWindow(package, Path.GetDirectoryName(packagePath)!, args.GetValueOrDefault("disc-root"));
            MainWindow = window;
            _activationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _activationTimer.Tick += (_, _) =>
            {
                if (_activateEvent.WaitOne(0))
                {
                    window.Show();
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Activate();
                    window.Topmost = true;
                    window.Topmost = false;
                }
            };
            _activationTimer.Start();
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "GOG Disc Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationTimer?.Stop();
        _activateEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void BootstrapLocal(Dictionary<string, string?> args)
    {
        var discRoot = args.GetValueOrDefault("disc-root") ?? AppContext.BaseDirectory;
        var media = DiscMedia.Load(discRoot);
        var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1";
        var currentExe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running launcher.");
        using var executableStream = File.OpenRead(currentExe);
        var executableId = Convert.ToHexString(SHA256.HashData(executableStream)).ToLowerInvariant()[..12];
        var runtimeRoot = Path.Combine(AppPaths.Runtime, $"{appVersion}-{executableId}", media.Package.PackageId);
        Directory.CreateDirectory(runtimeRoot);
        var localExe = Path.Combine(runtimeRoot, "Launch.exe");
        CacheFiles.Copy(currentExe, localExe);

        var packagePath = Path.Combine(runtimeRoot, "package.json");
        CacheFiles.Copy(Path.Combine(media.Root, "package.json"), packagePath);
        foreach (var asset in new[] { media.Package.BackgroundFile, media.Package.CoverFile, media.Package.IconFile })
        {
            if (string.IsNullOrWhiteSpace(asset)) continue;
            var source = SafePaths.ResolveUnderRoot(media.Root, asset);
            if (File.Exists(source)) CacheFiles.Copy(source, SafePaths.ResolveUnderRoot(runtimeRoot, asset));
        }

        var start = new ProcessStartInfo(localExe)
        {
            UseShellExecute = true,
            WorkingDirectory = runtimeRoot,
            Arguments = $"--local --package {Quote(packagePath)} --disc-root {Quote(media.Root)}"
        };
        Process.Start(start);
    }

    private static string Required(Dictionary<string, string?> args, string name) =>
        args.GetValueOrDefault(name) ?? throw new ArgumentException($"Missing --{name} argument.");

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"") + '"';
}

internal static class ArgumentMap
{
    public static Dictionary<string, string?> Parse(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--")) continue;
            var name = args[index][2..];
            result[name] = index + 1 < args.Length && !args[index + 1].StartsWith("--") ? args[++index] : null;
        }
        return result;
    }
}
