using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GogDisc.Core;
using Microsoft.Win32;

namespace GogDisc.Launcher;

public partial class MainWindow : Window
{
    private static readonly Brush ActiveLabelBrush = new SolidColorBrush(Color.FromRgb(126, 31, 230));
    private static readonly Brush NormalLabelBrush = new SolidColorBrush(Color.FromRgb(28, 28, 28));
    private static readonly Brush MutedLabelBrush = new SolidColorBrush(Color.FromRgb(112, 112, 112));
    private static readonly Brush WaitingBrush = new SolidColorBrush(Color.FromRgb(198, 198, 198));

    private readonly PackageManifest _package;
    private readonly string _cacheRoot;
    private readonly string? _initialDiscRoot;
    private readonly FileLog _log;
    private readonly List<TextBlock> _discLabels = [];
    private string? _activeDiscRoot;
    private InstallState? _installState;
    private CancellationTokenSource? _operation;
    private string _stagingRoot;
    private string _installParent;
    private string _temporaryParent;
    private long _operationStarted;
    private bool _uninstallerRunning;

    public MainWindow(PackageManifest package, string cacheRoot, string? initialDiscRoot)
    {
        _package = package;
        _cacheRoot = cacheRoot;
        _initialDiscRoot = initialDiscRoot;
        _activeDiscRoot = initialDiscRoot;
        _log = new FileLog(AppPaths.PackageLog(package.PackageId));
        _temporaryParent = AppPaths.Staging;
        _stagingRoot = StagingPath(_temporaryParent);
        _installParent = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "GOG Games");

        InitializeComponent();
        EjectButton.Visibility = OpticalDriveEjector.IsOpticalDrive(_activeDiscRoot)
            ? Visibility.Visible : Visibility.Collapsed;
        Title = $"Install {_package.Title}";
        TitleText.Text = _package.Title;
        SizeText.Text = $"Installation files: {FormatBytes(RequiredInstallerBytes())}";
        RequiredSpaceText.Text = $"Estimated space needed: ~{FormatBytes(RequiredInstallerBytes() * 2)}";
        LoadArtwork();
        BuildDiscLabels();
        RefreshHome();
        Activated += MainWindow_Activated;
        _log.Write($"Launcher opened. Initial media: {_initialDiscRoot ?? "none"}");
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (_operation is null && !_uninstallerRunning)
            RefreshHome();
    }

    private void LoadArtwork()
    {
        LoadImage(_package.BackgroundFile, BackgroundImage);
        LoadImage(_package.CoverFile, CoverImage);
        CoverPlaceholder.Visibility = CoverImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadImage(string relativePath, Image target)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        var path = SafePaths.ResolveUnderRoot(_cacheRoot, relativePath);
        if (!File.Exists(path)) return;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        target.Source = bitmap;
    }

    private void BuildDiscLabels()
    {
        DiscLabelsGrid.Children.Clear();
        DiscLabelsGrid.ColumnDefinitions.Clear();
        _discLabels.Clear();
        var count = Math.Max(1, _package.RequiredDiscCount);
        for (var index = 0; index < count; index++)
        {
            DiscLabelsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var label = new TextBlock
            {
                Text = count == 1 ? "Installing…" : $"Disc {index + 1}",
                FontSize = 14,
                Foreground = index == 0 ? ActiveLabelBrush : MutedLabelBrush,
                HorizontalAlignment = index == 0 ? HorizontalAlignment.Left :
                    index == count - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center
            };
            Grid.SetColumn(label, index);
            DiscLabelsGrid.Children.Add(label);
            _discLabels.Add(label);
        }
    }

    private void RefreshHome()
    {
        _installState = InstallDiscovery.Discover(_package);
        var installed = _installState is not null &&
                        !string.IsNullOrWhiteSpace(_installState.PlayTarget) &&
                        File.Exists(_installState.PlayTarget);

        ProgressPanel.Visibility = Visibility.Collapsed;
        OperationActions.Visibility = Visibility.Collapsed;
        DefaultPanel.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        InstalledPanel.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        DefaultActions.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        InstalledActions.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        var usesTemporaryBackup = !installed && _package.RequiredDiscCount > 1;
        TemporaryLocationSection.Visibility = usesTemporaryBackup ? Visibility.Visible : Visibility.Collapsed;
        RequiredSpaceText.Visibility = usesTemporaryBackup ? Visibility.Visible : Visibility.Collapsed;
        SetExpanded(usesTemporaryBackup);

        if (installed)
        {
            var location = _installState!.InstallLocation ?? Path.GetDirectoryName(_installState.PlayTarget!) ?? "Installed";
            InstalledLocationText.Text = location;
            InstalledFreeSpaceText.Text = GetFreeSpaceText(location);
            UninstallButton.Visibility = string.IsNullOrWhiteSpace(_installState.UninstallCommand)
                ? Visibility.Collapsed : Visibility.Visible;
            var hasExtras = _package.Files.Any(file => file.Kind == PackageFileKind.Extra);
            ExtrasButton.Visibility = hasExtras ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumnSpan(PlayButton, hasExtras ? 1 : 3);
        }
        else
        {
            DestinationText.Text = _installParent;
            FreeSpaceText.Text = GetFreeSpaceText(_installParent);
            TemporaryLocationText.Text = _stagingRoot;
            TemporaryFreeSpaceText.Text = GetFreeSpaceText(_temporaryParent);
            var hasExtras = _package.Files.Any(file => file.Kind == PackageFileKind.Extra);
            PreInstallExtrasButton.Visibility = hasExtras ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumnSpan(InstallButton, hasExtras ? 1 : 3);
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null) return;
        _operation = new CancellationTokenSource();
        _operationStarted = Stopwatch.GetTimestamp();
        if (_package.RequiredDiscCount > 1) ShowCopying(1);
        else ShowInstalling(1);
        try
        {
            if (_package.RequiredDiscCount == 1)
            {
                var disc = await WaitForDiscAsync(1, _operation.Token);
                var installerEntry = _package.Files.Single(file => file.Kind == PackageFileKind.Installer &&
                    file.RelativePath.Equals(_package.InstallerRelativePath, StringComparison.OrdinalIgnoreCase));
                CopyProgress.IsIndeterminate = true;
                EstimateText.Text = "Preparing the original GOG installer…";
                var installer = SafePaths.ResolveUnderRoot(disc.Root, installerEntry.DiscPath);
                await RunInstallerAsync(installer, null, _operation.Token);
            }
            else
            {
                await StageAndInstallAsync(_operation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _log.Write("Operation cancelled; verified staging retained.");
            RefreshHome();
        }
        catch (Exception ex)
        {
            _log.Write("ERROR " + ex);
            RefreshHome();
            MessageBox.Show(this, ex.Message + "\n\nVerified temporary files were retained for retry.",
                "Installation couldn’t continue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
        }
    }

    private async Task StageAndInstallAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_stagingRoot);
        var state = StagingStateStore.LoadOrCreate(_stagingRoot, _package);
        EnsureFreeSpace(StagingCopier.RemainingBytes(_package, _stagingRoot, state));
        var total = RequiredInstallerBytes();

        for (var discNumber = 1; discNumber <= _package.RequiredDiscCount; discNumber++)
        {
            var discEntries = _package.Files.Where(file => file.Kind == PackageFileKind.Installer && file.DiscNumber == discNumber).ToList();
            if (discEntries.All(file => StagingCopier.IsEntryVerified(file, _stagingRoot, state)))
            {
                UpdateDiscLabels(discNumber + 1);
                continue;
            }

            var disc = await WaitForDiscAsync(discNumber, cancellationToken);
            ShowCopying(discNumber);
            var completedBefore = _package.Files.Where(file => file.Kind == PackageFileKind.Installer &&
                StagingCopier.IsEntryVerified(file, _stagingRoot, state)).Sum(file => file.Size);
            var report = new Progress<CopyProgress>(value =>
            {
                var completed = completedBefore + value.DiscBytesCopied;
                CopyProgress.Value = total == 0 ? 0 : completed * 100d / total;
                UpdateEstimate(completed, total);
                UpdateDiscLabels(discNumber);
            });
            _log.Write($"Copying disc {discNumber} from {disc.Root}.");
            await StagingCopier.CopyDiscAsync(disc, _stagingRoot, state, report, cancellationToken);
            _log.Write($"Disc {discNumber} verified.");
        }

        CopyProgress.Value = 100;
        UpdateDiscLabels(_package.RequiredDiscCount + 1);
        var installer = SafePaths.ResolveUnderRoot(_stagingRoot, _package.InstallerRelativePath);
        await RunInstallerAsync(installer, _stagingRoot, cancellationToken);
    }

    private async Task<LoadedDisc> WaitForDiscAsync(int discNumber, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var disc = DiscMedia.Find(_package.PackageId, discNumber, _initialDiscRoot);
            if (disc is not null)
            {
                _activeDiscRoot = disc.Root;
                EjectButton.Visibility = OpticalDriveEjector.IsOpticalDrive(_activeDiscRoot)
                    ? Visibility.Visible : Visibility.Collapsed;
                return disc;
            }
            ShowWaiting(discNumber);
            await Task.Delay(900, cancellationToken);
        }
    }

    private async Task RunInstallerAsync(string installer, string? stagingRoot, CancellationToken cancellationToken)
    {
        if (!File.Exists(installer)) throw new FileNotFoundException("The original GOG installer was not found.", installer);
        ShowInstalling(_package.RequiredDiscCount);
        CopyProgress.IsIndeterminate = true;
        EstimateText.Text = "Completing installation with the original GOG setup…";
        _log.Write($"Launching original installer: {installer}");
        var target = Path.Combine(_installParent, PackageBuilder.SanitizeFileName(_package.Title));
        using var process = Process.Start(new ProcessStartInfo(installer)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installer)!,
            Arguments = $"/DIR=\"{target.Replace("\"", "")}\""
        }) ?? throw new InvalidOperationException("Windows could not start the GOG installer.");
        await process.WaitForExitAsync(cancellationToken);
        _log.Write($"Original installer exited with code {process.ExitCode}.");
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"The GOG installer exited with code {process.ExitCode}.");

        _installState = InstallDiscovery.Discover(_package);
        if (_installState is null || string.IsNullOrWhiteSpace(_installState.PlayTarget) || !File.Exists(_installState.PlayTarget))
        {
            var dialog = new OpenFileDialog
            {
                Title = $"Locate the installed {_package.Title} executable",
                Filter = "Game executable (*.exe)|*.exe|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) == true)
                _installState = InstallDiscovery.SaveManualTarget(_package, dialog.FileName);
        }

        if (_installState is null || string.IsNullOrWhiteSpace(_installState.PlayTarget) || !File.Exists(_installState.PlayTarget))
            throw new InvalidOperationException("Setup closed successfully, but the installed game could not be confirmed.");

        if (stagingRoot is not null)
        {
            try { DeleteOwnedStaging(stagingRoot); }
            catch (Exception ex) { _log.Write("Installation succeeded, but staging cleanup failed: " + ex.Message); }
        }
        _log.Write("Installation confirmed; owned staging removed.");
        RefreshHome();
    }

    private void ShowInstalling(int discNumber)
    {
        ShowOperationPanels();
        CopyProgress.IsIndeterminate = false;
        OperationStatusButton.Content = "Installing…";
        OperationStatusButton.Background = ActiveLabelBrush;
        OperationStatusButton.Foreground = Brushes.White;
        EstimateText.Text = "Estimated time remaining: Calculating…";
        UpdateDiscLabels(discNumber);
    }

    private void ShowCopying(int discNumber)
    {
        ShowOperationPanels();
        CopyProgress.IsIndeterminate = false;
        OperationStatusButton.Content = "Copying installation files…";
        OperationStatusButton.Background = ActiveLabelBrush;
        OperationStatusButton.Foreground = Brushes.White;
        EstimateText.Text = "Estimated time remaining: Calculating…";
        UpdateDiscLabels(discNumber);
    }

    private void ShowWaiting(int discNumber)
    {
        ShowOperationPanels();
        CopyProgress.IsIndeterminate = false;
        var mediaName = _package.DiscLayout.FirstOrDefault(disc => disc.DiscNumber == discNumber)?.MediaName;
        var mediaHint = string.IsNullOrWhiteSpace(mediaName) ? "" : $" ({mediaName})";
        OperationStatusButton.Content = $"Insert Disc {discNumber}{mediaHint}…";
        OperationStatusButton.Background = WaitingBrush;
        OperationStatusButton.Foreground = Brushes.Black;
        EstimateText.Text = $"Waiting for Disc {discNumber} of {_package.RequiredDiscCount}{mediaHint}";
        UpdateDiscLabels(discNumber);
    }

    private void ShowOperationPanels()
    {
        SetExpanded(false);
        DefaultPanel.Visibility = Visibility.Collapsed;
        InstalledPanel.Visibility = Visibility.Collapsed;
        DefaultActions.Visibility = Visibility.Collapsed;
        InstalledActions.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        OperationActions.Visibility = Visibility.Visible;
    }

    private void UpdateDiscLabels(int currentDisc)
    {
        for (var index = 0; index < _discLabels.Count; index++)
            _discLabels[index].Foreground = index + 1 < currentDisc ? NormalLabelBrush :
                index + 1 == currentDisc ? ActiveLabelBrush : MutedLabelBrush;
    }

    private void UpdateEstimate(long completed, long total)
    {
        if (completed <= 0 || total <= completed) return;
        var elapsed = Stopwatch.GetElapsedTime(_operationStarted).TotalSeconds;
        if (elapsed < 2) return;
        var remaining = TimeSpan.FromSeconds((total - completed) / (completed / elapsed));
        EstimateText.Text = remaining.TotalHours >= 1
            ? $"Estimated time remaining: {(int)remaining.TotalHours} hr {remaining.Minutes} min"
            : $"Estimated time remaining: {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} Minutes";
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_installState?.PlayTarget) || !File.Exists(_installState.PlayTarget))
        {
            RefreshHome();
            return;
        }
        Process.Start(new ProcessStartInfo(_installState.PlayTarget)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(_installState.PlayTarget)!
        });
        _log.Write($"Launched game: {_installState.PlayTarget}");
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_uninstallerRunning || string.IsNullOrWhiteSpace(_installState?.UninstallCommand)) return;
        if (MessageBox.Show(this, $"Open the registered uninstaller for {_package.Title}?", "Uninstall",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            _uninstallerRunning = true;
            UninstallButton.IsEnabled = false;
            using var process = Process.Start(ProcessCommands.FromRegisteredCommand(_installState.UninstallCommand))
                ?? throw new InvalidOperationException("Windows could not start the registered uninstaller.");
            _log.Write("Opened registered uninstaller.");
            await process.WaitForExitAsync();
            _log.Write($"Registered uninstaller exited with code {process.ExitCode}; refreshing install status.");
        }
        catch (Exception ex)
        {
            _log.Write("Could not monitor the registered uninstaller: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Uninstaller couldn’t start", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _uninstallerRunning = false;
            RefreshHome();
            UninstallButton.IsEnabled = true;
        }
    }

    private async void Extras_Click(object sender, RoutedEventArgs e)
    {
        var discs = _package.Files.Where(file => file.Kind == PackageFileKind.Extra)
            .Select(file => file.DiscNumber).Distinct().Order().ToList();
        if (discs.Count == 0) return;
        _operation = new CancellationTokenSource();
        try
        {
            foreach (var number in discs)
            {
                var media = DiscMedia.Find(_package.PackageId, number, _initialDiscRoot);
                if (media is null) continue;
                OpenExtras(media);
                return;
            }
            var expected = discs[0];
            var found = await WaitForDiscAsync(expected, _operation.Token);
            OpenExtras(found);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _operation.Dispose();
            _operation = null;
            RefreshHome();
        }
    }

    private static void OpenExtras(LoadedDisc media)
    {
        var extras = Path.Combine(media.Root, "Extras");
        if (Directory.Exists(extras))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{extras}\"") { UseShellExecute = true });
    }

    private void ChooseInstallLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where to install the game", Multiselect = false };
        if (Directory.Exists(_installParent)) dialog.InitialDirectory = _installParent;
        if (dialog.ShowDialog(this) != true) return;
        _installParent = dialog.FolderName;
        DestinationText.Text = _installParent;
        FreeSpaceText.Text = GetFreeSpaceText(_installParent);
    }

    private void ChooseTemporaryLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where temporary installation files are stored", Multiselect = false };
        if (Directory.Exists(_temporaryParent)) dialog.InitialDirectory = _temporaryParent;
        if (dialog.ShowDialog(this) != true) return;
        _temporaryParent = dialog.FolderName;
        _stagingRoot = StagingPath(_temporaryParent);
        TemporaryLocationText.Text = _stagingRoot;
        TemporaryFreeSpaceText.Text = GetFreeSpaceText(_temporaryParent);
    }

    private string StagingPath(string parent) => Path.Combine(parent,
        PackageBuilder.SanitizeFileName($"{_package.Title} Backup"));

    private void SetExpanded(bool expanded)
    {
        var newHeight = expanded ? 724d : 564d;
        ContentRow.Height = new GridLength(expanded ? 303d : 143d);
        WindowClip.Rect = new Rect(0, 0, Width - 24, newHeight - 24);
        if (Math.Abs(Height - newHeight) < 0.1) return;
        if (IsLoaded) Top += (Height - newHeight) / 2d;
        Height = newHeight;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private void Eject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OpticalDriveEjector.Eject(_activeDiscRoot);
            _log.Write($"Ejected optical media from {Path.GetPathRoot(_activeDiscRoot)}.");
        }
        catch (Exception ex)
        {
            _log.Write("Could not eject optical media: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Disc couldn’t be ejected", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Hero_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_operation is null) return;
        if (MessageBox.Show(this, "Cancel the current operation and close the installer?\n\nVerified files will be kept so installation can resume later.",
                "Close installer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        _operation.Cancel();
    }

    private void EnsureFreeSpace(long requiredBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(_stagingRoot)) ?? throw new IOException("Cannot determine the staging drive.");
        var drive = new DriveInfo(root);
        const long headroom = 1024L * 1024 * 1024;
        if (drive.AvailableFreeSpace < requiredBytes + headroom)
            throw new IOException($"Temporary storage needs {FormatBytes(requiredBytes + headroom)}, but {drive.Name} has {FormatBytes(drive.AvailableFreeSpace)} free.");
    }

    private void DeleteOwnedStaging(string stagingRoot)
    {
        var statePath = Path.Combine(stagingRoot, StagingStateStore.FileName);
        if (!File.Exists(statePath)) return;
        var state = JsonFiles.Read<StagingState>(statePath);
        if (!state.PackageId.Equals(_package.PackageId, StringComparison.OrdinalIgnoreCase)) return;
        Directory.Delete(stagingRoot, true);
    }

    private static string GetFreeSpaceText(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (root is null) return "Disk space unavailable";
            return $"{new DriveInfo(root).AvailableFreeSpace / 1_000_000_000d:N1} GB disk space remaining";
        }
        catch { return "Disk space unavailable"; }
    }

    private long RequiredInstallerBytes() => _package.Files.Where(file => file.Kind == PackageFileKind.Installer).Sum(file => file.Size);
    private static string FormatBytes(long value) => $"{value / 1_000_000_000d:N1} GB";
}
