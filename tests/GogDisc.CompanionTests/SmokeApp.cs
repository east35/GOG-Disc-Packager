using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using GogDisc.Companion;
using GogDisc.Core;

internal sealed class SmokeApp : Application
{
    public static string Output { get; set; } = "";
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new CompanionWindow(() => desktop.Shutdown());
        desktop.MainWindow = window;
        var root = Path.Combine(CompanionPaths.DataRoot, "fixture-disc"); Directory.CreateDirectory(root);
        IconFile.Create(null, Path.Combine(root, "game.ico"));
        var package = new PackageManifest { PackageId = "smoke", Title = "Preview test game", Version = "1", IconFile = "game.ico" };
        window.ShowPackage(new LoadedDisc(root, package, new DiscManifest { DiscNumber = 1, TotalDiscCount = 2 }));
        DispatcherTimer.RunOnce(() =>
        {
            try
            {
                var bytes = File.ReadAllBytes(DesktopIntegration.IconPath(new LibraryGame { Package = package }));
                if (!bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                    throw new Exception("Cached game icon is not PNG");
                // A later disc visit must use the local PNG without reading the original ICO again.
                File.WriteAllText(Path.Combine(root, "game.ico"), "unreadable replacement");
                DesktopIntegration.CacheArtwork(new LoadedDisc(root, package, new DiscManifest()));
                if (!File.ReadAllBytes(DesktopIntegration.IconPath(new LibraryGame { Package = package })).SequenceEqual(bytes))
                    throw new Exception("The cached icon was replaced during a later disc visit");
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
                bitmap.Render(window); bitmap.Save(Output);
                Console.WriteLine("PASS: GUI startup, packaged app icon, one-time ICO conversion/cache reuse, game window rendering");
                desktop.Shutdown(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); desktop.Shutdown(1); }
        }, TimeSpan.FromSeconds(1));
        base.OnFrameworkInitializationCompleted();
    }
}
