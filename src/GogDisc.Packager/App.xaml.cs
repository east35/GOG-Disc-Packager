using System.Windows;

namespace GogDisc.Packager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        if (e.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            window.Show();
            window.Close();
            Shutdown(0);
            return;
        }
        window.Show();
    }
}
