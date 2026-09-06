using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using GogDisc.Core;
using Microsoft.Win32;

namespace GogDisc.Packager;

public partial class MainWindow : Window
{
    private SetupFamily? _family;
    private PackagePlan? _plan;
    private CancellationTokenSource? _cancellation;

    public MainWindow()
    {
        InitializeComponent();
        OutputBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GOG Disc Packages");
        LauncherBox.Text = FindLauncher();
    }

    private void BrowseSetup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "GOG setup (setup_*.exe)|setup_*.exe|Executables (*.exe)|*.exe" };
        if (dialog.ShowDialog() != true) return;
        SetupBox.Text = dialog.FileName;
        InferFields(dialog.FileName);
        ResetScan();
    }

    private void BrowseExtras_Click(object sender, RoutedEventArgs e) => BrowseFolderInto(ExtrasBox);
    private void BrowseOutput_Click(object sender, RoutedEventArgs e) => BrowseFolderInto(OutputBox);

    private void BrowseBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog() == true) BackgroundBox.Text = dialog.FileName;
    }

    private void BrowseCover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Cover art (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog() == true) CoverBox.Text = dialog.FileName;
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Icon image (*.ico;*.png)|*.ico;*.png" };
        if (dialog.ShowDialog() == true) IconBox.Text = dialog.FileName;
    }

    private void BrowseLauncher_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Published launcher (Launch.exe)|Launch.exe|Executable (*.exe)|*.exe" };
        if (dialog.ShowDialog() == true) LauncherBox.Text = dialog.FileName;
    }

    private static void BrowseFolderInto(TextBox textBox)
    {
        var dialog = new OpenFolderDialog { Multiselect = false };
        if (Directory.Exists(textBox.Text)) dialog.InitialDirectory = textBox.Text;
        if (dialog.ShowDialog() == true) textBox.Text = dialog.FolderName;
    }

    private void MediaBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomCapacityBox is not null)
            CustomCapacityBox.Visibility = MediaBox.SelectedIndex == 9 ? Visibility.Visible : Visibility.Collapsed;
        if (CustomCapacityLabel is not null)
            CustomCapacityLabel.Visibility = MediaBox.SelectedIndex == 9 ? Visibility.Visible : Visibility.Collapsed;
        if (MixedMediaPanel is not null)
            MixedMediaPanel.Visibility = MediaBox.SelectedIndex == 8 ? Visibility.Visible : Visibility.Collapsed;
        ResetScan();
    }

    private void MediaInventoryBox_TextChanged(object sender, TextChangedEventArgs e) => ResetScan();

    private void Scan_Click(object sender, RoutedEventArgs e) => ScanPackage();

    private bool ScanPackage()
    {
        try
        {
            _family = SetupFamilyScanner.Scan(SetupBox.Text, EmptyToNull(ExtrasBox.Text), IncludePatchesBox.IsChecked == true);
            string? economySuggestion = null;
            if (MediaBox.SelectedIndex == 8)
            {
                var inventory = MediaCatalog.ParseInventory(MediaInventoryBox.Text);
                var suggestion = MediaCatalog.Suggest(_family.InstallerBytes + _family.ExtrasBytes, inventory);
                _plan = DiscPlanner.CreateMixed(_family, suggestion.Discs);
                economySuggestion =
                    $"Economy suggestion: {suggestion.Summary}\n" +
                    $"Combined usable space: {FormatBytes(suggestion.UsableBytes)}; unused: {FormatBytes(suggestion.UnusedBytes)}\n";
            }
            else
            {
                _plan = DiscPlanner.Create(_family, GetCapacity(), GetReserve());
            }
            var excluded = _family.ExcludedPatches.Count == 0
                ? "No historical patch executables found."
                : $"Excluded {_family.ExcludedPatches.Count} historical patch executable(s):\n  " +
                  string.Join("\n  ", _family.ExcludedPatches.Select(Path.GetFileName));
            SummaryText.Text =
                $"{_family.InstallerFiles.Count} required file(s), {FormatBytes(_family.InstallerBytes)}\n" +
                $"{_family.Extras.Count} optional extra file(s), {FormatBytes(_family.ExtrasBytes)}\n" +
                economySuggestion +
                $"Plan: {_plan.RequiredDiscCount} required installer disc(s), {_plan.Discs.Count} total disc(s)\n" +
                $"Disc layout: {string.Join(", ", _plan.Discs.Select(disc => $"Disc {disc.Number} {disc.MediaName}"))}\n\n{excluded}";
            Log($"Scanned {_family.FamilyName}.");
            BuildButton.IsEnabled = true;
            StatusText.Text = "Package plan is valid";
            return true;
        }
        catch (Exception ex)
        {
            ResetScan();
            ShowError(ex.Message);
            return false;
        }
    }

    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        if (!ScanPackage()) return;
        if (string.IsNullOrWhiteSpace(TitleBox.Text) || string.IsNullOrWhiteSpace(VersionBox.Text))
        {
            ShowError("Enter a title and version before building.");
            return;
        }

        SetBusy(true);
        _cancellation = new CancellationTokenSource();
        string? temporaryIcon = null;
        try
        {
            var preparedIcon = IconPreparation.Prepare(EmptyToNull(IconBox.Text), out temporaryIcon);
            var progress = new Progress<PackagingProgress>(value =>
            {
                BuildProgress.Value = value.Percent;
                StatusText.Text = $"{value.Activity}: {value.CurrentFile}";
            });
            var result = await PackageBuilder.BuildAsync(new PackageBuildRequest
            {
                Title = TitleBox.Text,
                Version = VersionBox.Text,
                ProductType = ProductTypeBox.SelectedIndex == 1 ? PackageProductType.Dlc : PackageProductType.BaseGame,
                SetupFamily = _family!,
                Plan = _plan!,
                OutputDirectory = OutputBox.Text,
                LauncherExecutable = LauncherBox.Text,
                BackgroundImage = EmptyToNull(BackgroundBox.Text),
                CoverImage = EmptyToNull(CoverBox.Text),
                IconImage = preparedIcon
            }, progress, _cancellation.Token);
            Log($"Complete: {result.PackageDirectory}");
            StatusText.Text = "Disc folders built and verified";
            MessageBox.Show(this, $"Disc folders are ready:\n\n{result.PackageDirectory}", "Build complete", MessageBoxButton.OK, MessageBoxImage.Information);
            if (OpenWhenCompleteBox.IsChecked == true)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{result.PackageDirectory}\"") { UseShellExecute = true });
            if (ResetAfterBuildBox.IsChecked == true)
                ClearBackupInputs();
        }
        catch (OperationCanceledException)
        {
            Log("Build cancelled; partial build output was removed.");
            StatusText.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            if (temporaryIcon is not null && File.Exists(temporaryIcon)) File.Delete(temporaryIcon);
            _cancellation?.Dispose();
            _cancellation = null;
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void SetBusy(bool busy)
    {
        ScanButton.IsEnabled = !busy;
        BuildButton.IsEnabled = !busy && _plan is not null;
        CancelButton.IsEnabled = busy;
    }

    private void ResetScan()
    {
        _family = null;
        _plan = null;
        if (BuildButton is not null) BuildButton.IsEnabled = false;
    }

    private void ClearBackupInputs()
    {
        SetupBox.Clear();
        TitleBox.Clear();
        VersionBox.Clear();
        ProductTypeBox.SelectedIndex = 0;
        ExtrasBox.Clear();
        IncludePatchesBox.IsChecked = false;
        BackgroundBox.Clear();
        CoverBox.Clear();
        IconBox.Clear();
        SummaryText.Text = "Select a stock setup_*.exe, then scan the package.";
        BuildProgress.Value = 0;
        ResetScan();
        StatusText.Text = "Ready for a new backup";
    }

    private long GetCapacity()
    {
        return MediaBox.SelectedIndex switch
        {
            0 => DiscPlanner.Cd650CapacityBytes,
            1 => DiscPlanner.CdCapacityBytes,
            2 => DiscPlanner.Dvd5CapacityBytes,
            3 => DiscPlanner.Dvd9CapacityBytes,
            4 => DiscPlanner.Bd25CapacityBytes,
            5 => DiscPlanner.Bd50CapacityBytes,
            6 => DiscPlanner.Bd100CapacityBytes,
            7 => DiscPlanner.Bd128CapacityBytes,
            _ => checked((long)(decimal.Parse(CustomCapacityBox.Text, CultureInfo.InvariantCulture) * 1_000_000_000m))
        };
    }

    private long GetReserve() => MediaBox.SelectedIndex is 0 or 1
        ? DiscPlanner.CdReserveBytes
        : DiscPlanner.DefaultReserveBytes;

    private void InferFields(string setupPath)
    {
        var stem = Path.GetFileNameWithoutExtension(setupPath)["setup_".Length..];
        var buildMarker = stem.LastIndexOf("_(", StringComparison.Ordinal);
        if (buildMarker > 0) stem = stem[..buildMarker];
        var parts = stem.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var versionIndex = Array.FindIndex(parts, part => part.Length > 0 && char.IsDigit(part[0]) && part.Contains('.'));
        if (versionIndex < 1) versionIndex = parts.Length;
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
            TitleBox.Text = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(string.Join(" ", parts.Take(versionIndex)));
        if (string.IsNullOrWhiteSpace(VersionBox.Text) && versionIndex < parts.Length)
            VersionBox.Text = parts[versionIndex];
    }

    private static string FindLauncher()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "LauncherPayload", "Launch.exe");
        return File.Exists(bundled) ? bundled : "";
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string FormatBytes(long value) => $"{value / 1024d / 1024d / 1024d:N2} GiB";

    private void Log(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:T}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void ShowError(string message)
    {
        Log("ERROR: " + message);
        StatusText.Text = "Error";
        MessageBox.Show(this, message, "GOG Disc Packager", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
