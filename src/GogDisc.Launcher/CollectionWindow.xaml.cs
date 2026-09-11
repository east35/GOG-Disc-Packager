using System.IO;
using System.Windows;
using GogDisc.Core;

namespace GogDisc.Launcher;

public partial class CollectionWindow : Window
{
    private readonly PackageManifest _collection;
    private readonly string _cacheRoot;
    private readonly string? _discRoot;

    public CollectionWindow(PackageManifest collection, string cacheRoot, string? discRoot)
    {
        _collection = collection;
        _cacheRoot = cacheRoot;
        _discRoot = discRoot;
        InitializeComponent();
        CollectionTitle.Text = collection.Title;
        CollectionSummary.Text = $"{collection.CollectionGames.Count} games • {collection.Version}";
        GameList.ItemsSource = collection.CollectionGames;
        EjectButton.Visibility = OpticalDriveEjector.IsOpticalDrive(discRoot) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Game_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CollectionGameManifest game) return;
        var package = new PackageManifest
        {
            SchemaVersion = 2,
            PackageId = game.GameId,
            Title = game.Title,
            Version = game.Version,
            ProductType = PackageProductType.BaseGame,
            DeploymentType = PackageDeploymentType.OfflineMedia,
            InstallerRelativePath = game.InstallerRelativePath,
            ExtrasRelativePath = game.ExtrasRelativePath,
            RequiredDiscCount = 1,
            TotalDiscCount = 1,
            BackgroundFile = _collection.BackgroundFile,
            CoverFile = _collection.CoverFile,
            IconFile = _collection.IconFile,
            DiscLayout = _collection.DiscLayout,
            InstallDetectionNames = game.InstallDetectionNames,
            Files = game.Files
        };
        var window = new MainWindow(package, _cacheRoot, _discRoot, _collection.PackageId);
        Application.Current.MainWindow = window;
        window.Show();
        Close();
    }

    private void Eject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OpticalDriveEjector.Eject(_discRoot);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Disc couldn’t be ejected", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
