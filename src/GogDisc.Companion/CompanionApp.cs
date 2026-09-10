using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using GogDisc.Core;

namespace GogDisc.Companion;

internal sealed class CompanionApp : Application
{
    public static bool StartHidden { get; set; }
    public static string? StartupError { get; set; }
    private CompanionWindow? _window;
    private DispatcherTimer? _mediaTimer;
    private string? _visibleMediaKey;

    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _window = new CompanionWindow(() => desktop.Shutdown());
            _mediaTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _mediaTimer.Tick += (_, _) => ScanMedia();
            _mediaTimer.Start();
            ScanMedia();
            if (!StartHidden) _window.ShowLibrary();
            if (StartupError is not null) _window.ShowError(StartupError);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void ScanMedia()
    {
        if (_window!.IsBusy) return;
        if (File.Exists(CompanionPaths.ActivationPath))
        {
            var error = File.ReadAllText(CompanionPaths.ActivationPath);
            File.Delete(CompanionPaths.ActivationPath);
            _window.ShowLibrary();
            if (!string.IsNullOrWhiteSpace(error)) _window.ShowError(error);
        }
        var media = MediaDiscovery.FindMountedPackages().FirstOrDefault();
        if (media is null)
        {
            _visibleMediaKey = null;
            return;
        }

        var key = $"{media.Package.PackageId}:{media.Disc.DiscNumber}:{media.Root}";
        if (key == _visibleMediaKey) return;
        _visibleMediaKey = key;
        _window!.ShowPackage(media);
    }
}
