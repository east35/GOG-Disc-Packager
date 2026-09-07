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
    private string _backupParent;
    private long _estimateSampleTaken;
    private long _estimateSampleBytes = -1;
    private double? _estimateRate;
    private bool _uninstallerRunning;
    private GogDownloadEstimate? _keyEstimate;
    private TimeSpan? _lastRemaining;
    private TaskCompletionSource<string?>? _authenticationCodeCompletion;

    private bool IsKeyMedia => _package.DeploymentType == PackageDeploymentType.GogKeyMedia;

    /// <summary>Some GOG products ship only as offline installers; for those the backup path is the only one that works.</summary>
    private bool OfflineBackupOnly => IsKeyMedia && _package.GogKeyProduct?.SupportsDirectDownload == false;

    /// <summary>An add-on disc from a split set. It extends an existing install rather than creating one.</summary>
    private bool IsDlcDisc => IsKeyMedia && _package.GogKeyProduct?.DiscRole == KeyDiscRole.Dlc;
    private bool DownloadOfflineBackup => IsKeyMedia && (OfflineBackupOnly || DownloadOfflineBackupBox.IsChecked == true);

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
        _backupParent = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GOG Offline Backups");

        InitializeComponent();
        // Hero (312) + separator (1) + footer (98) + the 12px shadow margin on both edges.
        ContentScroller.MaxHeight = Math.Max(160d, SystemParameters.WorkArea.Height - 435d);
        EjectButton.Visibility = OpticalDriveEjector.IsOpticalDrive(_activeDiscRoot)
            ? Visibility.Visible : Visibility.Collapsed;
        Title = $"Install {_package.Title}";
        TitleText.Text = _package.Title;
        SizeText.Text = IsKeyMedia ? "Install size: calculated after GOG sign-in" : $"Installation files: {FormatBytes(RequiredInstallerBytes())}";
        RequiredSpaceText.Text = IsKeyMedia ? "Disk space required: calculated from the current GOG build" : $"Estimated space needed: ~{FormatBytes(RequiredInstallerBytes() * 2)}";
        LoadArtwork();
        BuildDiscLabels();
        RefreshHome();
        Activated += MainWindow_Activated;
        ContentRendered += async (_, _) => await LoadKeyEstimateAsync();
        _log.Write($"Launcher opened. Initial media: {_initialDiscRoot ?? "none"}");
        if (IsKeyMedia)
            _log.Write(GogGalaxyDetection.FindInstallation() is { } galaxy
                ? $"GOG Galaxy detected at {galaxy}; independent browser authentication remains available."
                : "GOG Galaxy was not detected; independent browser authentication will be used.");
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
        LoadWindowIcon();
        CoverPlaceholder.Visibility = CoverImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadWindowIcon()
    {
        if (string.IsNullOrWhiteSpace(_package.IconFile)) return;
        var path = SafePaths.ResolveUnderRoot(_cacheRoot, _package.IconFile);
        if (!File.Exists(path)) return;
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Icon = decoder.Frames.OrderByDescending(frame => frame.PixelWidth * frame.PixelHeight).FirstOrDefault();
        }
        catch (Exception ex) { _log.Write("Could not load the game icon: " + ex.Message); }
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

    /// <summary>An add-on disc must locate the BASE game, which a direct GOG download records only in our own
    /// state file — gogdl writes no uninstall entry and no shortcut, so generic discovery finds nothing.</summary>
    private InstallState? FindBaseGameInstall()
    {
        var product = _package.GogKeyProduct;
        if (string.IsNullOrWhiteSpace(product?.BaseProductId)) return null;
        var baseTitle = product.BaseTitle ?? _package.Title;
        return InstallDiscovery.Discover(new PackageManifest
        {
            PackageId = $"gog-{product.BaseProductId}",
            Title = baseTitle,
            InstallDetectionNames = [baseTitle]
        });
    }

    private void RefreshHome()
    {
        _installState = IsDlcDisc ? FindBaseGameInstall() : InstallDiscovery.Discover(_package);
        var baseGamePresent = _installState is not null &&
                              !string.IsNullOrWhiteSpace(_installState.PlayTarget) &&
                              File.Exists(_installState.PlayTarget);
        // An add-on disc detects the BASE game, so a hit means "ready to add", never "already installed".
        var installed = baseGamePresent && !IsDlcDisc;

        ProgressPanel.Visibility = Visibility.Collapsed;
        AuthenticationSection.Visibility = Visibility.Collapsed;
        OperationActions.Visibility = Visibility.Collapsed;
        DefaultPanel.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        InstalledPanel.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        DefaultActions.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        InstalledActions.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        KeyOptionsSection.Visibility = !installed && IsKeyMedia && !OfflineBackupOnly ? Visibility.Visible : Visibility.Collapsed;
        KeyDestinationSeparator.Visibility = !installed && IsKeyMedia && !OfflineBackupOnly ? Visibility.Visible : Visibility.Collapsed;
        var usesTemporaryBackup = !installed && (_package.RequiredDiscCount > 1 || DownloadOfflineBackup);
        TemporaryLocationSection.Visibility = !IsKeyMedia && usesTemporaryBackup ? Visibility.Visible : Visibility.Collapsed;
        KeyBackupLocationSection.Visibility = !installed && DownloadOfflineBackup ? Visibility.Visible : Visibility.Collapsed;
        KeyScrollBoundary.Visibility = Visibility.Collapsed;
        RequiredSpaceText.Visibility = IsKeyMedia
            ? DownloadOfflineBackup ? Visibility.Visible : Visibility.Collapsed
            : usesTemporaryBackup ? Visibility.Visible : Visibility.Collapsed;

        if (installed)
        {
            if (IsKeyMedia && !KeyInstallOwnership.IsOwned(_installState!.InstallLocation ?? "", _package))
                KeyInstallOwnership.AdoptKnownInstall(_installState, _package);
            var location = _installState!.InstallLocation ?? Path.GetDirectoryName(_installState.PlayTarget!) ?? "Installed";
            InstalledLocationText.Text = location;
            InstalledFreeSpaceText.Text = GetFreeSpaceText(location);
            var canRemoveKeyInstall = IsKeyMedia && !string.IsNullOrWhiteSpace(_installState.InstallLocation) &&
                                      KeyInstallOwnership.IsOwned(_installState.InstallLocation, _package);
            UninstallButton.Visibility = string.IsNullOrWhiteSpace(_installState.UninstallCommand) && !canRemoveKeyInstall
                ? Visibility.Collapsed : Visibility.Visible;
            var hasExtras = IsKeyMedia
                ? _package.GogKeyProduct?.AvailableExtras != 0
                : _package.Files.Any(file => file.Kind == PackageFileKind.Extra);
            ExtrasButton.Visibility = hasExtras ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumnSpan(PlayButton, hasExtras ? 1 : 3);
        }
        else
        {
            if (IsDlcDisc)
            {
                var baseTitle = _package.GogKeyProduct?.BaseTitle ?? "the base game";
                var folder = baseGamePresent
                    ? _installState!.InstallLocation ?? Path.GetDirectoryName(_installState.PlayTarget!)
                    : null;
                DestinationLabel.Text = $"This add-on installs into your {baseTitle} folder:";
                DestinationText.Text = folder ?? $"{baseTitle} is not installed yet";
                FreeSpaceText.Text = folder is null ? "Install Disc 1 first." : GetFreeSpaceText(folder);
                ChooseDestinationButton.Visibility = Visibility.Collapsed;
                InstallButton.Content = "Install add-on";
                InstallButton.IsEnabled = folder is not null;
            }
            else
            {
                DestinationText.Text = _installParent;
                FreeSpaceText.Text = GetFreeSpaceText(_installParent);
            }
            TemporaryLocationText.Text = _stagingRoot;
            TemporaryFreeSpaceText.Text = GetFreeSpaceText(_temporaryParent);
            KeyBackupLocationText.Text = BackupRoot;
            KeyBackupFreeSpaceText.Text = GetFreeSpaceText(_backupParent);
            var hasExtras = IsKeyMedia
                ? _package.GogKeyProduct?.AvailableExtras != 0
                : _package.Files.Any(file => file.Kind == PackageFileKind.Extra);
            PreInstallExtrasButton.Visibility = hasExtras ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumnSpan(InstallButton, hasExtras ? 1 : 3);
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null) return;
        _operation = new CancellationTokenSource();
        if (_package.RequiredDiscCount > 1) ShowCopying(1);
        else ShowInstalling(1);
        try
        {
            if (IsKeyMedia)
            {
                await InstallKeyMediaAsync(_operation.Token);
                return;
            }
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

    private void DownloadOfflineBackup_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && _operation is null)
        {
            RefreshHome();
            ContentScroller.ScrollToTop();
            UpdateKeySpaceText();
        }
    }

    private void ContentScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        KeyScrollBoundary.Visibility = DownloadOfflineBackup && e.ExtentHeight > e.ViewportHeight && e.VerticalOffset > 0.5
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadKeyEstimateAsync()
    {
        if (!IsKeyMedia || OfflineBackupOnly || !GogAuthentication.HasCredentials() || _package.GogKeyProduct is null) return;
        try
        {
            _keyEstimate = await new GogDlRuntime().GetEstimateAsync(_package.GogKeyProduct, CancellationToken.None);
            SizeText.Text = $"Install size: {FormatBytes(_keyEstimate.InstalledBytes)}";
            UpdateKeySpaceText();
        }
        catch (Exception ex) { _log.Write("Could not pre-load current GOG size metadata: " + ex.Message); }
    }

    private void UpdateKeySpaceText()
    {
        RequiredSpaceText.Visibility = DownloadOfflineBackup ? Visibility.Visible : Visibility.Collapsed;
        if (_keyEstimate is null) return;
        if (DownloadOfflineBackup)
            RequiredSpaceText.Text = $"Disk space required: ~{FormatBytes(checked(_keyEstimate.InstalledBytes * 2))} (backup + game)";
    }

    private async Task InstallKeyMediaAsync(CancellationToken cancellationToken)
    {
        var product = _package.GogKeyProduct ?? throw new InvalidDataException("GOG Key Media identity is missing.");
        await EnsureGogAuthenticationAsync(cancellationToken);
        if (DownloadOfflineBackup) await DownloadOfflineBackupAndInstallAsync(product, cancellationToken);
        else await DirectInstallAsync(product, cancellationToken);
    }

    private async Task EnsureGogAuthenticationAsync(CancellationToken cancellationToken)
    {
        var runtime = new GogDlRuntime();
        await runtime.EnsureCurrentAsync(cancellationToken);
        if (GogAuthentication.HasCredentials()) return;
        Process.Start(new ProcessStartInfo(GogAuthentication.LoginUrl) { UseShellExecute = true });
        var code = await WaitForGogCodeAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(code)) throw new OperationCanceledException("GOG sign-in was cancelled.");
        AuthenticationContinueButton.IsEnabled = false;
        EstimateText.Text = "Code accepted, continuing installation…";
        await runtime.AuthenticateAsync(code, cancellationToken);
        AuthenticationSection.Visibility = Visibility.Collapsed;
        if (!GogAuthentication.HasCredentials()) throw new UnauthorizedAccessException("GOG sign-in did not complete.");
    }

    private async Task DirectInstallAsync(GogKeyProduct product, CancellationToken cancellationToken)
    {
        ShowOperationPanels();
        OperationStatusButton.Content = "Downloading from GOG…";
        CopyProgress.IsIndeterminate = true;
        // An add-on disc installs into the base game's folder, so it needs that game present first.
        var target = product.DiscRole == KeyDiscRole.Dlc
            ? RequireBaseGameFolder(product)
            : Path.Combine(_installParent, PackageBuilder.SanitizeFileName(_package.Title));
        if (product.DiscRole != KeyDiscRole.Dlc && Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new IOException($"The destination already contains files: {target}");
        var runtime = new GogDlRuntime();
        _keyEstimate = await runtime.GetEstimateAsync(product, cancellationToken);
        EnsureDestinationSpace(target, _keyEstimate.InstalledBytes > 0 ? _keyEstimate.InstalledBytes : _keyEstimate.DownloadBytes);
        SizeText.Text = $"Install size: {FormatBytes(_keyEstimate.InstalledBytes)}";
        UpdateKeySpaceText();
        await runtime.InstallAsync(product, target, ReportGogProgress(), cancellationToken);

        // gogdl records installed state in %APPDATA%\heroic_gogdl\manifests, not in the game folder. If that
        // folder was removed behind its back it reports "nothing to do" and downloads nothing; repair is the
        // command that verifies against disk rather than trusting the manifest.
        if (product.DiscRole != KeyDiscRole.Dlc && FindGameExecutable(target) is null)
        {
            // Same thing from the user's side — a first install — so keep the wording. The distinction
            // between download and repair is a gogdl detail and belongs in the log, not on a button.
            _log.Write("gogdl had nothing to do but no game is present; repairing against its manifest.");
            EstimateText.Text = "Estimated time remaining: Calculating…";
            CopyProgress.IsIndeterminate = true;
            await runtime.RepairAsync(product, target, ReportGogProgress(), cancellationToken);
        }
        ConfirmDownloadedInstallation(target);
    }

    private static string? FindGameExecutable(string target) => Directory.Exists(target)
        ? Directory.EnumerateFiles(target, "*.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => !Path.GetFileName(path).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
        : null;

    /// <summary>Turns gogdl's console chatter into the same status the disc-copy path shows. Its raw lines go to
    /// the log only — they are diagnostics, and reading like an error is worse than showing nothing.</summary>
    private Progress<GogProcessProgress> ReportGogProgress() => new(value =>
    {
        _log.Write("gogdl: " + value.Message);
        if (value.Percent is { } percent)
        {
            CopyProgress.IsIndeterminate = false;
            CopyProgress.Value = percent;
        }
        if (value.Remaining is { } remaining) _lastRemaining = remaining;
        EstimateText.Text = _lastRemaining is { } eta
            ? $"Estimated time remaining: {FormatRemaining(eta)}"
            : "Estimated time remaining: Calculating…";
    });

    private static string FormatRemaining(TimeSpan remaining) => remaining.TotalHours >= 1
        ? $"{(int)remaining.TotalHours} hr {remaining.Minutes} min"
        : $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} Minutes";

    /// <summary>Locates the base game an add-on disc extends, or explains which disc to install first.</summary>
    private string RequireBaseGameFolder(GogKeyProduct product)
    {
        var baseTitle = product.BaseTitle ?? "the base game";
        var state = _installState;
        var folder = state?.InstallLocation ?? Path.GetDirectoryName(state?.PlayTarget ?? "");
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new InvalidOperationException(
                $"{baseTitle} is not installed yet.\n\nInstall Disc 1 ({baseTitle}) first — this add-on installs into that game's folder.");

        // gogdl rewrites this folder to match the product it is given, so the user is told before it touches
        // an existing installation. An earlier build deleted a whole game here; never install one silently.
        if (MessageBox.Show(this,
                $"Adding {_package.Title} re-checks every file in:\n\n{folder}\n\n" +
                $"{baseTitle} must stay installed there while this runs, and files that do not belong to " +
                "the game or this add-on may be replaced.\n\nContinue?",
                "Add to an existing installation", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            throw new OperationCanceledException("The add-on installation was cancelled.");
        return folder;
    }

    private async Task DownloadOfflineBackupAndInstallAsync(GogKeyProduct product, CancellationToken cancellationToken)
    {
        ShowOperationPanels();
        OperationStatusButton.Content = "Downloading GOG backup…";
        var client = await GogAccountDownloads.CreateAsync(cancellationToken);
        var files = (await client.GetFilesAsync(product, cancellationToken)).Where(file => !file.IsExtra).ToList();
        if (files.Count == 0) throw new InvalidOperationException("GOG did not return a Windows offline installer for this product and language.");
        var backupRoot = BackupRoot;
        Directory.CreateDirectory(backupRoot);
        var total = files.Sum(file => file.Size);
        _keyEstimate = await new GogDlRuntime().GetEstimateAsync(product, cancellationToken);
        ValidateBackupAndInstallSpace(backupRoot, total, _keyEstimate.InstalledBytes);
        SizeText.Text = $"Install size: {FormatBytes(_keyEstimate.InstalledBytes)}";
        UpdateKeySpaceText();

        // GOG serves the real filename from the downlink, so resolve every name before checking what is on disk.
        EstimateText.Text = "Checking your GOG downloads…";
        CopyProgress.IsIndeterminate = true;
        var planned = new List<(GogAccountFile File, string Path)>();
        foreach (var file in files)
            planned.Add((file, await client.ResolveDestinationAsync(
                file, Path.Combine(backupRoot, SafeDownloadName(file.Name, file.Id)), cancellationToken)));

        if (planned.All(entry => IsCompleteFile(entry.Path, entry.File.Size)) && !ConfirmReuseExistingBackup(backupRoot))
        {
            foreach (var entry in planned) File.Delete(entry.Path);
            _log.Write("Existing offline backup discarded at the user's request; downloading the current build.");
        }

        long completed = 0;
        var downloaded = new List<string>();
        foreach (var (file, destination) in planned)
        {
            downloaded.Add(await client.DownloadAsync(file, destination, new Progress<long>(bytes =>
            {
                CopyProgress.IsIndeterminate = total <= 0;
                if (total > 0) CopyProgress.Value = Math.Min(100, (completed + bytes) * 100d / total);
                EstimateText.Text = $"Downloading {file.Name}";
            }), cancellationToken));
            completed += new FileInfo(downloaded[^1]).Length;
        }

        // Pick from this build's files, never the folder: a kept backup can still hold an older build's setup.
        var installer = downloaded.Where(path => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, NaturalStringComparer.Instance).FirstOrDefault()
            ?? throw new FileNotFoundException("The downloaded offline backup did not contain a setup executable.");
        await RunInstallerAsync(installer, null, cancellationToken);
        _log.Write($"Offline backup kept at {backupRoot}.");
    }

    private static bool IsCompleteFile(string path, long expectedSize) =>
        expectedSize > 0 && File.Exists(path) && new FileInfo(path).Length == expectedSize;

    private bool ConfirmReuseExistingBackup(string backupRoot) =>
        MessageBox.Show(this,
            $"A complete offline backup is already stored at:\n\n{backupRoot}\n\n" +
            "Reuse it, or download the current build from GOG again?\n\n" +
            "Yes — install from the existing backup\nNo — download again and replace it",
            "Existing offline backup found", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void ConfirmDownloadedInstallation(string target)
    {
        if (IsDlcDisc)
        {
            ConfirmAddOnInstallation(target);
            return;
        }
        var executable = FindGameExecutable(target)
            ?? throw new InvalidOperationException("The GOG download completed, but no installed game executable was found.");
        KeyInstallOwnership.Mark(target, _package);
        _installState = InstallDiscovery.SaveManualTarget(_package, executable, target);
        _log.Write("Direct GOG installation confirmed.");
        RefreshHome();
    }

    /// <summary>An add-on must leave the base game intact and must not claim its folder: overwriting the
    /// ownership marker would break the base game's uninstall and aim a DLC removal at the whole install.</summary>
    private void ConfirmAddOnInstallation(string target)
    {
        var baseTitle = _package.GogKeyProduct?.BaseTitle ?? "the base game";
        var playTarget = _installState?.PlayTarget;
        if (string.IsNullOrWhiteSpace(playTarget) || !File.Exists(playTarget))
        {
            _log.Write($"ERROR add-on install left {baseTitle} missing from {target}.");
            throw new InvalidOperationException(
                $"{baseTitle} is no longer installed in:\n\n{target}\n\n" +
                "The add-on download removed or replaced it instead of adding to it. Reinstall Disc 1 before retrying.");
        }
        _log.Write($"Add-on installed into {target}; {baseTitle} intact, ownership unchanged.");
        RefreshHome();
        MessageBox.Show(this, $"{_package.Title} was added to {baseTitle}.",
            "Add-on installed", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string SafeDownloadName(string name, string id)
    {
        var candidate = Path.GetFileName(id);
        if (string.IsNullOrWhiteSpace(candidate)) candidate = Path.GetFileName(name);
        return PackageBuilder.SanitizeFileName(candidate);
    }

    private async Task<string?> WaitForGogCodeAsync(CancellationToken cancellationToken)
    {
        ShowOperationPanels();
        AuthenticationSection.Visibility = Visibility.Visible;
        AuthenticationCodeText.Clear();
        AuthenticationContinueButton.IsEnabled = true;
        AuthenticationCodeText.Focus();
        EstimateText.Text = "Waiting for GOG sign-in…";
        OperationStatusButton.Content = "Waiting for URL or Code…";
        OperationStatusButton.IsEnabled = false;
        _authenticationCodeCompletion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => _authenticationCodeCompletion.TrySetCanceled(cancellationToken));
        try { return await _authenticationCodeCompletion.Task; }
        finally { _authenticationCodeCompletion = null; OperationStatusButton.IsEnabled = true; }
    }

    private void AuthenticationContinue_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AuthenticationCodeText.Text)) return;
        _authenticationCodeCompletion?.TrySetResult(AuthenticationCodeText.Text);
    }

    private static void EnsureDestinationSpace(string path, long requiredBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? throw new IOException("Cannot determine the destination drive.");
        var available = new DriveInfo(root).AvailableFreeSpace;
        const long headroom = 1024L * 1024 * 1024;
        if (requiredBytes > 0 && available < requiredBytes + headroom)
            throw new IOException($"The destination needs {FormatBytes(requiredBytes + headroom)}, but {root} has {FormatBytes(available)} free.");
    }

    private void ValidateBackupAndInstallSpace(string backupPath, long backupBytes, long installedBytes)
    {
        var backupDrive = Path.GetPathRoot(Path.GetFullPath(backupPath)) ?? throw new IOException("Cannot determine the backup drive.");
        var installTarget = Path.Combine(_installParent, PackageBuilder.SanitizeFileName(_package.Title));
        var installDrive = Path.GetPathRoot(Path.GetFullPath(installTarget)) ?? throw new IOException("Cannot determine the destination drive.");
        const long headroom = 1024L * 1024 * 1024;
        if (backupDrive.Equals(installDrive, StringComparison.OrdinalIgnoreCase))
        {
            var required = checked(backupBytes + installedBytes + headroom);
            var available = new DriveInfo(backupDrive).AvailableFreeSpace;
            if (available < required) throw new IOException($"Backup and installation together need {FormatBytes(required)}, but {backupDrive} has {FormatBytes(available)} free.");
            return;
        }
        EnsureDestinationSpace(backupPath, backupBytes);
        EnsureDestinationSpace(installTarget, installedBytes);
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
        var target = Path.Combine(_installParent, PackageBuilder.SanitizeFileName(_package.Title));

        while (true)
        {
            var exitCode = await StartSetupAsync(installer, target, cancellationToken);
            if (exitCode == 0) break;

            // Setup never reaches its wizard without administrator rights, so an unapproved prompt is by far the most
            // likely reason for the codes Inno reports before it initializes: 1 (failed to initialize) and 2 (cancelled
            // before copying began). Retry has to be offered here — sending someone back to Install to guess what went
            // wrong is how a first attempt turns into troubleshooting.
            var permissionLikely = exitCode is ElevationDeniedExitCode or 1 or 2;
            var explanation = permissionLikely
                ? "Windows didn't get permission to run the GOG setup, so nothing was installed.\n\n" +
                  "The permission prompt closes on its own after about two minutes, so it may have timed out while it " +
                  "was hidden behind another window."
                : $"The GOG installer stopped with code {exitCode}, so nothing was installed.";

            if (MessageBox.Show(this,
                    $"{explanation}\n\nThe copied files were kept. Try setup again?",
                    "Setup didn't finish", MessageBoxButton.YesNo,
                    permissionLikely ? MessageBoxImage.Warning : MessageBoxImage.Error) != MessageBoxResult.Yes)
                throw new OperationCanceledException();

            _log.Write($"Retrying setup after exit code {exitCode}.");
        }

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

    /// <summary>Windows returns ERROR_CANCELLED when the elevation prompt is declined or times out unanswered.</summary>
    private const int ElevationDeniedExitCode = 1223;

    /// <summary>Runs the original setup elevated. GOG installs to a folder at the drive root, so setup always needs
    /// administrator rights; asking for them here means an unanswered prompt comes back immediately as ERROR_CANCELLED
    /// instead of leaving setup to sit for the prompt's two-minute timeout and then exit with a code that reads like a
    /// cancelled wizard.</summary>
    private async Task<int> StartSetupAsync(string installer, string target, CancellationToken cancellationToken)
    {
        EstimateText.Text = "Approve the Windows permission prompt to continue…";
        // Inno Setup reports only an exit code, which says nothing about why setup stopped. Its own /LOG next to ours
        // is the only record of what the wizard actually did.
        var setupLog = Path.Combine(AppPaths.Logs, PackageBuilder.SanitizeFileName(_package.PackageId) + "-setup.log");
        Directory.CreateDirectory(AppPaths.Logs);
        _log.Write($"Launching original installer elevated: {installer}");

        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(installer)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(installer)!,
                Arguments = $"/DIR=\"{target.Replace("\"", "")}\" /LOG=\"{setupLog}\""
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ElevationDeniedExitCode)
        {
            _log.Write("The Windows permission prompt was declined or timed out before setup started.");
            return ElevationDeniedExitCode;
        }
        if (process is null) throw new InvalidOperationException("Windows could not start the GOG installer.");

        using (process)
        {
            EstimateText.Text = "Completing installation with the original GOG setup…";
            _log.Write($"Setup's own log: {setupLog}");
            await process.WaitForExitAsync(cancellationToken);
            _log.Write($"Original installer exited with code {process.ExitCode}.");
            return process.ExitCode;
        }
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
        _lastRemaining = null;
        _estimateSampleBytes = -1;
        _estimateRate = null;
        ContentScroller.ScrollToTop();
        KeyScrollBoundary.Visibility = Visibility.Collapsed;
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

    /// <summary>Estimates from the rate measured since the previous sample rather than from an average over the whole
    /// operation. Bytes credited the instant a disc starts — files an earlier attempt already staged and verified —
    /// arrive with no elapsed copying behind them, and waiting for a disc swap is not copy time; averaging either into
    /// the rate made the second disc report minutes when it had most of an hour to go.</summary>
    private void UpdateEstimate(long completed, long total)
    {
        if (completed <= 0 || total <= completed) return;
        var now = Stopwatch.GetTimestamp();
        if (_estimateSampleBytes < 0)
        {
            _estimateSampleBytes = completed;
            _estimateSampleTaken = now;
            return;
        }

        var seconds = Stopwatch.GetElapsedTime(_estimateSampleTaken, now).TotalSeconds;
        if (seconds < 2) return;
        var rate = (completed - _estimateSampleBytes) / seconds;
        _estimateSampleBytes = completed;
        _estimateSampleTaken = now;
        if (rate < 1) return;

        _estimateRate = _estimateRate is { } previous ? previous * 0.7 + rate * 0.3 : rate;
        EstimateText.Text = "Estimated time remaining: " + FormatRemaining(TimeSpan.FromSeconds((total - completed) / _estimateRate.Value));
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
        if (_uninstallerRunning || _installState is null) return;
        var ownedKeyInstall = IsKeyMedia && !string.IsNullOrWhiteSpace(_installState.InstallLocation) &&
                              KeyInstallOwnership.IsOwned(_installState.InstallLocation, _package);
        if (string.IsNullOrWhiteSpace(_installState.UninstallCommand) && !ownedKeyInstall) return;
        var question = ownedKeyInstall && string.IsNullOrWhiteSpace(_installState.UninstallCommand)
            ? $"Uninstall {_package.Title} and remove its downloaded game folder?"
            : $"Open the registered uninstaller for {_package.Title}?";
        if (MessageBox.Show(this, question, "Uninstall",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            _uninstallerRunning = true;
            UninstallButton.IsEnabled = false;
            if (ownedKeyInstall && string.IsNullOrWhiteSpace(_installState.UninstallCommand))
            {
                var installLocation = _installState.InstallLocation!;
                await Task.Run(() => KeyInstallOwnership.Remove(installLocation, _package));
                _log.Write($"Removed owned Key Media installation: {installLocation}");
                return;
            }
            using var process = Process.Start(ProcessCommands.FromRegisteredCommand(_installState.UninstallCommand!))
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
        if (IsKeyMedia) { await DownloadKeyExtrasAsync(); return; }
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

    private void ChooseBackupLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where to keep the offline backup", Multiselect = false };
        if (Directory.Exists(_backupParent)) dialog.InitialDirectory = _backupParent;
        if (dialog.ShowDialog(this) != true) return;
        _backupParent = dialog.FolderName;
        KeyBackupLocationText.Text = BackupRoot;
        KeyBackupFreeSpaceText.Text = GetFreeSpaceText(_backupParent);
    }

    private string StagingPath(string parent) => Path.Combine(parent,
        PackageBuilder.SanitizeFileName($"{_package.Title} Backup"));

    private string BackupRoot => Path.Combine(_backupParent, PackageBuilder.SanitizeFileName(_package.Title));

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        WindowClip.Rect = new Rect(0, 0, ActualWidth - 24, ActualHeight - 24);
        if (IsLoaded && e.HeightChanged) Top += (e.PreviousSize.Height - e.NewSize.Height) / 2d;
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

    private async Task DownloadKeyExtrasAsync()
    {
        if (_operation is not null) return;
        _operation = new CancellationTokenSource();
        try
        {
            await EnsureGogAuthenticationAsync(_operation.Token);
            var client = await GogAccountDownloads.CreateAsync(_operation.Token);
            var extras = (await client.GetFilesAsync(_package.GogKeyProduct!, _operation.Token)).Where(file => file.IsExtra).ToList();
            if (extras.Count == 0) { MessageBox.Show(this, "GOG lists no extras for this product.", "Extras", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var selected = SelectExtras(extras);
            if (selected.Count == 0) return;
            var folder = new OpenFolderDialog { Title = "Choose where to download the selected GOG extras", Multiselect = false };
            if (folder.ShowDialog(this) != true) return;
            ShowOperationPanels();
            foreach (var extra in selected)
                _ = await client.DownloadAsync(extra, Path.Combine(folder.FolderName, SafeDownloadName(extra.Name, extra.Id)), null, _operation.Token);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder.FolderName}\"") { UseShellExecute = true });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Extras couldn’t be downloaded", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _operation.Dispose(); _operation = null; RefreshHome(); }
    }

    private IReadOnlyList<GogAccountFile> SelectExtras(IReadOnlyList<GogAccountFile> extras)
    {
        var checks = extras.Select(extra => (Extra: extra, Box: new CheckBox { Content = extra.Name, IsChecked = true, Margin = new Thickness(4) })).ToList();
        var list = new StackPanel { Margin = new Thickness(12) };
        foreach (var item in checks) list.Children.Add(item.Box);
        var ok = new Button { Content = "Download selected", IsDefault = true, Margin = new Thickness(12), Width = 150 };
        var root = new DockPanel(); DockPanel.SetDock(ok, Dock.Bottom); root.Children.Add(ok);
        root.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var dialog = new Window { Title = $"Extras for {_package.Title}", Owner = this, Content = root, Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => dialog.DialogResult = true;
        return dialog.ShowDialog() == true ? checks.Where(item => item.Box.IsChecked == true).Select(item => item.Extra).ToList() : [];
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
