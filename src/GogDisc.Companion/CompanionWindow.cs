using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using GogDisc.Core;

namespace GogDisc.Companion;

internal sealed class CompanionWindow : Window
{
    private readonly ListBox _library = new() { MinWidth = 190 };
    private readonly Image _cover = new() { Height = 220, Stretch = Stretch.Uniform };
    private readonly TextBlock _title = new() { Text = "Your games", FontSize = 28, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Text = "Insert a disc or choose its folder to get started.", TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 10, IsVisible = false };
    private readonly Button _install = new() { Content = "Install / resume" };
    private readonly Button _play = new() { Content = "Play" };
    private readonly Button _target = new() { Content = "Choose game executable…" };
    private readonly Button _shortcuts = new() { Content = "Create shortcuts" };
    private readonly Button _extras = new() { Content = "Open saved extras" };
    private readonly Button _saveExtras = new() { Content = "Save disc extras" };
    private readonly Button _uninstall = new() { Content = "Uninstall…" };
    private readonly Button _remove = new() { Content = "Remove from library…" };
    private readonly Button _runtime = new() { Content = "Choose Proton folder…" };
    private readonly Button _logs = new() { Content = "Open log" };
    private readonly Button _choose = new() { Content = "Choose disc folder…" };
    private readonly Button _hide = new() { Content = "Hide window" };
    private readonly Button _quit = new() { Content = "Quit and stop watching" };
    private readonly Button _cancel = new() { Content = "Cancel operation", IsEnabled = false };
    private LoadedDisc? _media;
    private LoadedDisc? _collectionMedia;
    private LibraryGame? _game;
    private CancellationTokenSource? _operation;
    private bool _refreshing;
    private string _phase = "";
    public bool IsBusy => _operation is not null;

    public CompanionWindow(Action quitAction)
    {
        Title = "GOG Disc Companion";
        Width = 1000; Height = 730; MinWidth = 850; MinHeight = 600;
        Background = new SolidColorBrush(Color.Parse("#15131A"));
        SetIcon(CompanionPaths.AppIcon);
        _library.ItemTemplate = new FuncDataTemplate<LibraryGame>((game, _) =>
        {
            // Avalonia can request a template with a null item while recycling a row.
            if (game?.Package is null) return new Grid();
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*"), Margin = new Thickness(0, 4) };
            var icon = new Image { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Top };
            try { icon.Source = new Bitmap(DesktopIntegration.IconPath(game)); } catch { }
            row.Children.Add(icon);
            var title = new TextBlock { Text = game.Package.Title, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(title, 1); row.Children.Add(title); return row;
        });
        var actions = Buttons(_play, _install, _target, _shortcuts);
        var extras = Buttons(_saveExtras, _extras);
        var management = new Expander { Header = "Manage game", Content = Buttons(_runtime, _uninstall, _remove, _logs) };
        var footer = Buttons(_choose, _cancel, _hide, _quit);
        var info = new StackPanel { Spacing = 16, Margin = new Thickness(24) };
        foreach (var control in new Control[] { _title, _cover, _details, _status, _progress, actions, extras, management, footer }) info.Children.Add(control);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*"), Margin = new Thickness(16) };
        grid.Children.Add(_library);
        var scroll = new ScrollViewer { Content = info };
        Grid.SetColumn(scroll, 1); grid.Children.Add(scroll); Content = grid;
        Closing += (_, e) => { e.Cancel = true; if (!IsBusy) Hide(); else _status.Text = "Finish or cancel the operation before closing."; };
        _library.SelectionChanged += (_, _) => { if (!_refreshing && !IsBusy && _library.SelectedItem is LibraryGame game) SelectGame(game); };
        _install.Click += async (_, _) => await RunOperationAsync("Install", _game?.Package.DeploymentType == PackageDeploymentType.GogKeyMedia ? InstallKeyMediaAsync : InstallAsync);
        _play.Click += async (_, _) => await RunOperationAsync("Play", PlayAsync);
        _target.Click += async (_, _) => await RunOperationAsync("Choose executable", ChooseTargetAsync);
        _shortcuts.Click += async (_, _) => await RunOperationAsync("Shortcuts", async () =>
        {
            var desktop = await ConfirmAsync("Create shortcuts", "Add an application-menu shortcut and a desktop shortcut?", "Create both");
            if (!desktop) return;
            DesktopIntegration.CreateShortcuts(_game!, true);
            _status.Text = "Shortcuts created with the game icon. Your desktop may ask you to trust its shortcut.";
        });
        _saveExtras.Click += async (_, _) => await RunOperationAsync("Extras", _game?.Package.DeploymentType == PackageDeploymentType.GogKeyMedia ? DownloadKeyExtrasAsync : SaveExtrasAsync);
        _extras.Click += async (_, _) => await RunOperationAsync("Extras", () =>
        {
            var root = Path.Combine(CompanionPaths.PackageRoot(_game!.Package.PackageId), "extras");
            if (!Directory.Exists(root)) throw new IOException("No extras saved yet. Insert the extras disc and choose Save disc extras.");
            DesktopIntegration.Open(root); return Task.CompletedTask;
        });
        _logs.Click += async (_, _) => await RunOperationAsync("Logs", () =>
        { DesktopIntegration.Open(CompanionPaths.LogPath(_game!.Package.PackageId)); return Task.CompletedTask; });
        _runtime.Click += async (_, _) => await RunOperationAsync("Runtime", ChooseRuntimeAsync);
        _uninstall.Click += async (_, _) => await RunOperationAsync("Uninstall", UninstallAsync);
        _remove.Click += async (_, _) => await RunOperationAsync("Remove", RemoveAsync);
        _choose.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a mounted game disc" });
            if (folders.Count > 0) try { ShowPackage(DiscMedia.Load(folders[0].Path.LocalPath)); } catch (Exception ex) { _status.Text = ex.Message; }
        };
        _cancel.Click += (_, _) => _operation?.Cancel();
        _hide.Click += (_, _) => { if (!IsBusy) Hide(); };
        _quit.Click += (_, _) => { if (!IsBusy) quitAction(); };
        RefreshLibrary(); UpdateButtons();
    }

    private static WrapPanel Buttons(params Button[] buttons)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in buttons) { button.Margin = new Thickness(0, 0, 8, 8); panel.Children.Add(button); }
        return panel;
    }

    public void ShowError(string message) { _status.Text = message; Show(); Activate(); }

    public void ShowLibrary()
    {
        if (!IsBusy)
        {
            RefreshLibrary(); _game = null; Title = "GOG Disc Companion";
            _title.Text = "Your games"; _details.Text = "Select a game from the library, or insert a disc.";
            _status.Text = ""; SetIcon(CompanionPaths.AppIcon);
            if (_cover.Source is IDisposable old) old.Dispose(); _cover.Source = null;
            UpdateButtons();
        }
        Show(); Activate();
    }

    public void ShowPackage(LoadedDisc media)
    {
        if (IsBusy) return;
        if (media.Package.CollectionGames.Count > 0)
        {
            _ = ChooseCollectionGameAsync(media);
            return;
        }
        try
        {
            var game = LibraryStore.Load(media.Package.PackageId) ?? new LibraryGame { Package = media.Package };
            if (game.Package.Version != media.Package.Version)
                throw new InvalidDataException("This package identity is already stored with another version.");
            _media = media;
            LibraryStore.Save(game);
            try { DesktopIntegration.CacheArtwork(media); }
            catch (Exception ex) { PhaseLog.Write(game.Package.PackageId, "Artwork", ex.Message); }
            PhaseLog.Write(game.Package.PackageId, "Media", $"Disc {media.Disc.DiscNumber}/{media.Disc.TotalDiscCount}: {media.Root}");
            RefreshLibrary(); SelectGame(game);
            _status.Text = game.Package.DeploymentType == PackageDeploymentType.GogKeyMedia
                ? "Key Media ready. Installation requires internet access and ownership on GOG."
                : $"Disc {media.Disc.DiscNumber} of {media.Disc.TotalDiscCount} ready. Install / resume verifies and stages this disc.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        Show(); Activate();
    }

    private async Task ChooseCollectionGameAsync(LoadedDisc media)
    {
        try
        {
            _collectionMedia = media;
            Show(); Activate();
            var games = media.Package.CollectionGames;
            var choices = new ComboBox
            {
                ItemsSource = games.Select(game => $"{game.Title} — {game.Version}").ToArray(),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            if (await DialogAsync(media.Package.Title, choices, "Choose game") && choices.SelectedIndex >= 0)
                ShowPackage(CollectionMedia.ForGame(media, games[choices.SelectedIndex]));
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private static bool Supported(LibraryGame game) => game.Package.DeploymentType == PackageDeploymentType.GogKeyMedia ||
        game.Package.DeploymentType == PackageDeploymentType.OfflineMedia && game.Package.ProductType == PackageProductType.BaseGame;
    private void SelectGame(LibraryGame game)
    {
        _game = game;
        var collectionDisc = _collectionMedia is null ? null : CollectionMedia.ForPackageId(_collectionMedia, game.Package.PackageId);
        if (collectionDisc is not null) _media = collectionDisc;
        else if (_media?.Package.PackageId != game.Package.PackageId) _media = null;
        _title.Text = game.Package.Title;
        Title = game.Package.Title + " — GOG Disc Companion";
        _details.Text = $"Version {game.Package.Version}\n{game.Status}";
        _status.Text = game.PlayTarget is null ? "Choose an existing game executable or insert its disc to install." : "Ready to play.";
        if (_cover.Source is IDisposable old) old.Dispose();
        _cover.Source = null;
        var cover = Path.Combine(CompanionPaths.PackageRoot(game.Package.PackageId), "cover");
        try { _cover.Source = new Bitmap(File.Exists(cover) ? cover : DesktopIntegration.IconPath(game)); } catch { }
        SetIcon(DesktopIntegration.IconPath(game)); UpdateButtons();
    }

    private void SetIcon(string path) { try { if (File.Exists(path)) Icon = new WindowIcon(path); } catch { } }
    private void RefreshLibrary()
    {
        _refreshing = true;
        try { _library.ItemsSource = LibraryStore.List(); }
        finally { _refreshing = false; }
    }

    private void UpdateButtons()
    {
        var ready = !IsBusy && _game is not null;
        foreach (var b in new[] { _target, _shortcuts, _extras, _runtime, _uninstall, _remove, _logs }) b.IsEnabled = ready;
        _play.IsEnabled = ready && _game!.PlayTarget is not null && UmuInstaller.IsAvailable;
        _shortcuts.IsEnabled = _uninstall.IsEnabled = ready && _game!.PlayTarget is not null;
        _install.IsEnabled = ready && Supported(_game!) && _media?.Package.PackageId == _game!.Package.PackageId;
        _saveExtras.IsEnabled = ready && _media?.Package.PackageId == _game!.Package.PackageId && _media.Disc.Files.Any(f => f.Kind == PackageFileKind.Extra);
        if (ready && _game!.Package.DeploymentType == PackageDeploymentType.GogKeyMedia) _saveExtras.IsEnabled = true;
        if (ready && _game!.Package.GogKeyProduct?.DiscRole == KeyDiscRole.Dlc) _target.IsEnabled = false;
        _install.Content = _game?.Package.DeploymentType == PackageDeploymentType.GogKeyMedia ? "Download / install" : "Install / resume";
        _saveExtras.Content = _game?.Package.DeploymentType == PackageDeploymentType.GogKeyMedia ? "Download GOG extras" : "Save disc extras";
        _choose.IsEnabled = _quit.IsEnabled = _library.IsEnabled = !IsBusy;
        _cancel.IsEnabled = IsBusy;
        _progress.IsVisible = IsBusy;
    }

    private async Task RunOperationAsync(string phase, Func<Task> action)
    {
        if (IsBusy || _game is null) return;
        var game = _game;
        _phase = phase;
        using var cancellation = new CancellationTokenSource(); _operation = cancellation; UpdateButtons();
        try
        {
            using var lease = OperationLease.Acquire(game.Package.PackageId);
            _game = LibraryStore.Load(game.Package.PackageId) ?? game;
            PhaseLog.Write(game.Package.PackageId, phase, "Started"); await action();
        }
        catch (OperationCanceledException)
        {
            MarkIncomplete();
            _status.Text = _game?.Package.DeploymentType == PackageDeploymentType.GogKeyMedia
                ? "Cancelled. Any completed downloads are retained for retry."
                : "Cancelled. Saved staging is retained and will be verified when you resume.";
            PhaseLog.Write(game.Package.PackageId, _phase, "Cancelled");
        }
        catch (Exception ex)
        {
            MarkIncomplete();
            _status.Text = $"{_phase} failed: {ex.Message}\nLog: {CompanionPaths.LogPath(game.Package.PackageId)}";
            PhaseLog.Write(game.Package.PackageId, _phase, ex.ToString());
        }
        finally
        {
            _operation = null;
            RefreshLibrary();
            if (_game is not null) _details.Text = $"Version {_game.Package.Version}\n{_game.Status}";
            UpdateButtons();
        }
    }

    private void MarkIncomplete()
    {
        if (_game is null || _game.PlayTarget is not null) return;
        _game.Status = "Incomplete; resume available";
        try { LibraryStore.Save(_game); } catch { /* Keep the original operation error visible. */ }
    }

    private IProgress<CopyProgress> CopyProgress() => new Progress<CopyProgress>(p =>
    { _progress.IsIndeterminate = false; _progress.Value = p.DiscPercent; _status.Text = $"Verifying {p.FileName}… {p.DiscPercent:N0}%"; });

    private async Task InstallAsync()
    {
        var game = _game!; var media = _media!; var token = _operation!.Token;
        var root = CompanionPaths.StagingRoot(game.Package.PackageId);
        var state = StagingStateStore.LoadOrCreate(root, game.Package);
        Directory.CreateDirectory(root);
        var remaining = StagingCopier.RemainingBytes(game.Package, root, state);
        if (new DriveInfo(root).AvailableFreeSpace < remaining) throw new IOException("Not enough free space for the remaining installer data.");
        game.Status = "Staging in progress"; LibraryStore.Save(game);
        await StagingCopier.CopyDiscAsync(media, root, state, CopyProgress(), token);
        PhaseLog.Write(game.Package.PackageId, "Staging", $"Disc {media.Disc.DiscNumber} copied and verified");
        _phase = "Verification";
        _status.Text = "Checking saved installer data from all discs…"; _progress.IsIndeterminate = true;
        var needed = await StagingCopier.RevalidateAsync(game.Package, root, state, token);
        if (needed.Count > 0)
        {
            game.Status = "Needs disc " + string.Join(", ", needed); LibraryStore.Save(game);
            _status.Text = game.Status + ". Insert a listed disc and choose Install / resume.";
            PhaseLog.Write(game.Package.PackageId, "Staging", game.Status); return;
        }
        game.Status = "Installer verified"; LibraryStore.Save(game);
        if (!await ConfirmAsync("Run installer", "Installer data is verified. Setup needs additional space for the game and its Windows environment; installed size is unknown. The first run may download a runtime and dependencies.", "Run setup")) return;
        token.ThrowIfCancellationRequested();
        _phase = "Setup";
        game.Status = "Setup in progress"; LibraryStore.Save(game);
        _status.Text = "Complete setup in the GOG installer window…";
        var code = await UmuInstaller.RunAsync(game, SafePaths.ResolveUnderRoot(root, game.Package.InstallerRelativePath), "Setup", token);
        game.Status = code == 0 ? "Setup finished; choose game executable" : $"Setup exited with code {code}; retry available";
        LibraryStore.Save(game); _status.Text = game.Status;
        if (code == 0) await ChooseTargetAsync();
    }

    private async Task InstallKeyMediaAsync()
    {
        var game = _game!;
        var product = game.Package.GogKeyProduct ?? throw new InvalidDataException("GOG Key Media identity is missing.");
        var keepBackup = new CheckBox { Content = "Keep offline installer files and run GOG setup through Proton", IsChecked = product.SupportsDirectDownload == false };
        var options = new StackPanel { Spacing = 12 };
        options.Children.Add(new TextBlock
        {
            Text = "This disc contains a game key, not the installer. GOG sign-in, ownership, and a download are required. " +
                "The download size will be checked before game files are written.", TextWrapping = TextWrapping.Wrap
        });
        if (product.DiscRole != KeyDiscRole.Dlc && product.SupportsDirectDownload != false) options.Children.Add(keepBackup);
        if (!await DialogAsync("Install from GOG", options, "Continue")) return;
        var token = _operation!.Token;
        _phase = "GOG sign-in";
        await EnsureGogAuthenticationAsync(token);
        if (product.DiscRole == KeyDiscRole.Dlc)
        {
            await InstallKeyDlcAsync(game, product, token);
            return;
        }
        if (keepBackup.IsChecked == true || product.SupportsDirectDownload == false)
            await InstallKeyBackupAsync(game, product, token);
        else
            await InstallKeyDirectAsync(game, product, token);
    }

    private async Task EnsureGogAuthenticationAsync(CancellationToken token)
    {
        var runtime = new GogDlRuntime();
        _status.Text = "Preparing the GOG download helper…";
        await runtime.EnsureCurrentAsync(token);
        if (GogAuthentication.HasCredentials())
        {
            try { await runtime.RefreshAuthenticationAsync(token); return; }
            catch (UnauthorizedAccessException) { /* The saved token was revoked; ask for a fresh sign-in. */ }
        }
        DesktopIntegration.Open(GogAuthentication.LoginUrl);
        var code = new TextBox { Watermark = "Paste the GOG authorization URL or code", MinWidth = 500 };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Sign in on the GOG page opened in your browser, then paste its returned URL or code here.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(code);
        if (!await DialogAsync("GOG sign-in", panel, "Sign in") || string.IsNullOrWhiteSpace(code.Text))
            throw new OperationCanceledException("GOG sign-in was cancelled.");
        _status.Text = "Completing GOG sign-in…";
        await runtime.AuthenticateAsync(code.Text, token);
        if (!GogAuthentication.HasCredentials()) throw new UnauthorizedAccessException("GOG sign-in did not complete.");
    }

    private IProgress<GogProcessProgress> KeyProgress(LibraryGame game, string label) => new Progress<GogProcessProgress>(value =>
    {
        PhaseLog.Write(game.Package.PackageId, "GOG", value.Message);
        if (value.Percent is { } percent) { _progress.IsIndeterminate = false; _progress.Value = percent; }
        _status.Text = value.Percent is { } progress ? $"{label}: {progress:N0}%" : label;
    });

    private async Task InstallKeyDirectAsync(LibraryGame game, GogKeyProduct product, CancellationToken token)
    {
        var target = CompanionPaths.DownloadRoot(game.Package.PackageId);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        LibraryStore.EnsureNoLinks(CompanionPaths.DataRoot, target);
        _phase = "GOG download";
        _status.Text = "Checking current GOG download size…";
        var runtime = new GogDlRuntime();
        var estimate = await runtime.GetEstimateAsync(product, token);
        var needed = Math.Max(estimate.DownloadBytes, estimate.InstalledBytes);
        if (new DriveInfo(Path.GetDirectoryName(target)!).AvailableFreeSpace < needed)
            throw new IOException("Not enough free space for the GOG game download.");
        game.Status = "Downloading from GOG"; LibraryStore.Save(game);
        _progress.IsIndeterminate = true;
        await runtime.InstallAsync(product, target, KeyProgress(game, "Downloading game"), token);
        if (game.PlayTarget is not null && !Path.GetFullPath(game.PlayTarget).StartsWith(target + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            DesktopIntegration.RemoveShortcuts(game);
            game.PlayTarget = null;
        }
        game.GameDirectory = target;
        if (!LibraryStore.Executables(game).Any())
        {
            PhaseLog.Write(game.Package.PackageId, "GOG", "No playable executable found after download; repairing against on-disk files.");
            await runtime.RepairAsync(product, target, KeyProgress(game, "Repairing game download"), token);
        }
        game.Status = "Downloaded; choose game executable";
        LibraryStore.Save(game);
        _status.Text = game.Status;
        await ChooseTargetAsync();
    }

    private async Task<IReadOnlyList<string>> DownloadKeyBackupFilesAsync(LibraryGame game, GogKeyProduct product, CancellationToken token)
    {
        _phase = "GOG backup";
        var client = await GogAccountDownloads.CreateAsync(token);
        var files = (await client.GetFilesAsync(product, token)).Where(file => !file.IsExtra).ToArray();
        if (files.Length == 0) throw new IOException("GOG did not provide offline installer files for this product and language.");
        var root = CompanionPaths.BackupRoot(game.Package.PackageId);
        LibraryStore.EnsureNoLinks(CompanionPaths.DataRoot, root);
        Directory.CreateDirectory(root);
        var planned = new List<(GogAccountFile File, string Path)>();
        foreach (var file in files)
        {
            var path = await client.ResolveDestinationAsync(file, Path.Combine(root, "download"), token);
            if (Path.GetFileName(path) == "download" || planned.Any(item => item.Path == path))
                throw new InvalidDataException("GOG returned duplicate or unnamed installer files.");
            planned.Add((file, path));
        }
        var total = planned.Sum(item => item.File.Size);
        if (new DriveInfo(root).AvailableFreeSpace < total)
            throw new IOException("Not enough free space for the offline installer backup.");
        _progress.IsIndeterminate = true;
        var result = new List<string>();
        foreach (var (file, path) in planned)
        {
            token.ThrowIfCancellationRequested();
            _status.Text = "Downloading " + Path.GetFileName(path) + "…";
            var saved = await client.DownloadAsync(file, path, null, token);
            if (file.Size > 0 && new FileInfo(saved).Length != file.Size)
                throw new IOException("Downloaded installer size does not match GOG metadata: " + Path.GetFileName(saved));
            result.Add(saved);
            PhaseLog.Write(game.Package.PackageId, "GOG backup", "Saved " + Path.GetFileName(saved));
        }
        return result;
    }

    private async Task InstallKeyBackupAsync(LibraryGame game, GogKeyProduct product, CancellationToken token)
    {
        var files = await DownloadKeyBackupFilesAsync(game, product, token);
        var setups = files.Where(path => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(path).StartsWith("setup", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (setups.Length != 1) throw new IOException("The GOG backup has no unique setup executable. The downloaded files were retained.");
        if (!await ConfirmAsync("Run GOG setup", "The downloaded installer files are saved. Run setup in this game's Proton prefix?", "Run setup")) return;
        _phase = "Setup";
        game.Status = "Setup in progress"; LibraryStore.Save(game);
        var code = await UmuInstaller.RunAsync(game, setups[0], "Setup", token);
        if (code == 0 && game.GameDirectory is not null)
        {
            DesktopIntegration.RemoveShortcuts(game);
            game.GameDirectory = null;
            game.PlayTarget = null;
        }
        game.Status = code == 0 ? "Setup finished; choose game executable" : $"Setup exited with code {code}; retry available";
        LibraryStore.Save(game); _status.Text = game.Status;
        if (code == 0) await ChooseTargetAsync();
    }

    private async Task InstallKeyDlcAsync(LibraryGame game, GogKeyProduct product, CancellationToken token)
    {
        var baseId = "gog-" + product.BaseProductId;
        var baseGame = LibraryStore.Load(baseId) ?? throw new IOException($"Install {product.BaseTitle ?? "the base game"} first.");
        if (baseGame.PlayTarget is null || !File.Exists(baseGame.PlayTarget))
            throw new IOException("The base game installation is missing. Restore it before installing DLC.");
        if (baseGame.GameDirectory is { } directory)
        {
            if (product.SupportsDirectDownload == false)
                throw new IOException("This DLC has no direct GOG download. Its offline installer needs a base game installed through GOG setup in a Proton prefix.");
            if (directory != CompanionPaths.DownloadRoot(baseId))
                throw new InvalidDataException("The base game's download folder is not owned by the companion.");
            LibraryStore.EnsureNoLinks(CompanionPaths.DataRoot, directory);
            if (!await ConfirmAsync("Update base game", "GOG will sync the base game's downloaded folder while adding this DLC. Files outside the selected GOG build may be changed. Continue?", "Add DLC")) return;
            _phase = "GOG DLC";
            var runtime = new GogDlRuntime();
            var estimate = await runtime.GetEstimateAsync(product, token);
            if (new DriveInfo(directory).AvailableFreeSpace < Math.Max(estimate.DownloadBytes, estimate.InstalledBytes))
                throw new IOException("Not enough free space for the GOG DLC download.");
            await runtime.InstallAsync(product, directory, KeyProgress(game, "Downloading DLC"), token);
            if (baseGame.PlayTarget is not null && !File.Exists(baseGame.PlayTarget))
            {
                baseGame.PlayTarget = null;
                baseGame.Status = "Game updated; choose game executable";
                LibraryStore.Save(baseGame);
            }
        }
        else
        {
            var files = await DownloadKeyBackupFilesAsync(game, product, token);
            var setups = files.Where(path => Path.GetFileName(path).StartsWith("setup", StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (setups.Length != 1) throw new IOException("The GOG DLC backup has no unique setup executable.");
            if (!await ConfirmAsync("Run DLC setup", "Run this DLC installer in the base game's Proton prefix?", "Run setup")) return;
            _phase = "DLC setup";
            var code = await UmuInstaller.RunAsync(baseGame, setups[0], "Setup", token);
            if (code != 0) throw new IOException($"DLC setup exited with code {code}.");
        }
        game.Status = "Installed into " + baseGame.Package.Title;
        game.InstalledAt = DateTimeOffset.Now;
        LibraryStore.Save(game); _status.Text = game.Status;
    }

    private async Task ChooseTargetAsync()
    {
        var game = _game!;
        var candidates = await Task.Run(() => LibraryStore.Executables(game).ToArray(), _operation!.Token);
        var choice = new ComboBox { ItemsSource = candidates, SelectedIndex = candidates.Length == 1 ? 0 : -1, HorizontalAlignment = HorizontalAlignment.Stretch };
        var browse = new Button { Content = game.GameDirectory is null ? "Browse prefix…" : "Browse downloaded game…" };
        browse.Click += async (_, _) =>
        {
            var prefix = game.GameDirectory ?? CompanionPaths.PrefixRoot(game.Package.PackageId);
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose the game's Windows executable",
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(prefix),
                FileTypeFilter = [new FilePickerFileType("Windows executable") { Patterns = ["*.exe"] }]
            });
            if (files.Count > 0) { choice.ItemsSource = new[] { files[0].Path.LocalPath }; choice.SelectedIndex = 0; }
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Confirm the executable that starts the game. Its prefix and runtime will be saved.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(choice); panel.Children.Add(browse);
        if (!await DialogAsync("Game executable", panel, "Save game") || choice.SelectedItem is not string target) return;
        _operation!.Token.ThrowIfCancellationRequested();
        game.PlayTarget = LibraryStore.ValidateTarget(game, target);
        game.ProtonPath ??= UmuInstaller.ExistingRuntime(game);
        game.InstalledAt ??= DateTimeOffset.Now; game.Status = "Installed";
        LibraryStore.Save(game);
        DesktopIntegration.CreateShortcuts(game, false);
        _status.Text = "Game saved. Play is available, and an application-menu shortcut was created.";
    }

    private async Task PlayAsync()
    {
        var game = _game!;
        var target = LibraryStore.ValidateTarget(game, game.PlayTarget ?? "");
        _status.Text = "Game running…";
        var code = await UmuInstaller.RunAsync(game, target, "Play", _operation!.Token);
        game.LastLaunchAt = DateTimeOffset.Now; LibraryStore.Save(game);
        _status.Text = $"Game exited with code {code}.";
    }

    private async Task SaveExtrasAsync()
    {
        var game = _game!;
        var root = Path.Combine(CompanionPaths.PackageRoot(game.Package.PackageId), "extras");
        var state = StagingStateStore.LoadOrCreate(root, game.Package);
        await StagingCopier.CopyDiscAsync(_media!, root, state, CopyProgress(), _operation!.Token, PackageFileKind.Extra);
        _status.Text = "This disc's extras have been saved and verified.";
    }

    private async Task DownloadKeyExtrasAsync()
    {
        var game = _game!;
        var product = game.Package.GogKeyProduct ?? throw new InvalidDataException("GOG Key Media identity is missing.");
        var token = _operation!.Token;
        await EnsureGogAuthenticationAsync(token);
        _phase = "GOG extras";
        var client = await GogAccountDownloads.CreateAsync(token);
        var files = (await client.GetFilesAsync(product, token)).Where(file => file.IsExtra).ToArray();
        if (files.Length == 0) throw new IOException("GOG did not list extras for this product.");
        var root = Path.Combine(CompanionPaths.PackageRoot(game.Package.PackageId), "extras");
        LibraryStore.EnsureNoLinks(CompanionPaths.DataRoot, root);
        Directory.CreateDirectory(root);
        if (new DriveInfo(root).AvailableFreeSpace < files.Sum(file => file.Size))
            throw new IOException("Not enough free space for the GOG extras.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var path = await client.ResolveDestinationAsync(file, Path.Combine(root, "download"), token);
            if (Path.GetFileName(path) == "download" || !paths.Add(path))
                throw new InvalidDataException("GOG returned duplicate or unnamed extras.");
            _status.Text = "Downloading " + Path.GetFileName(path) + "…";
            var saved = await client.DownloadAsync(file, path, null, token);
            if (file.Size > 0 && new FileInfo(saved).Length != file.Size)
                throw new IOException("Downloaded extra size does not match GOG metadata: " + Path.GetFileName(saved));
        }
        _status.Text = "GOG extras saved. Choose Open saved extras to view them.";
    }

    private async Task ChooseRuntimeAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose an installed Proton version folder" });
        if (folders.Count == 0) return;
        var path = folders[0].Path.LocalPath;
        if (!File.Exists(Path.Combine(path, "proton"))) throw new IOException("This folder does not contain Proton.");
        if (!await ConfirmAsync("Change runtime", "Changing Proton can modify this game's Windows environment. Use this version for future setup, play, and uninstall operations?", "Use version")) return;
        _game!.ProtonPath = path; LibraryStore.Save(_game);
        _status.Text = "Runtime saved: " + Path.GetFileName(path);
    }

    private async Task UninstallAsync()
    {
        var game = _game!;
        if (game.GameDirectory is { } downloaded)
        {
            if (downloaded != CompanionPaths.DownloadRoot(game.Package.PackageId))
                throw new InvalidDataException("The downloaded game folder is not owned by this library entry.");
            if (!await ConfirmAsync("Remove downloaded game", "Permanently delete this downloaded game folder? Saved games inside that folder will also be deleted. The Proton prefix and offline backups are retained.", "Delete game files")) return;
            LibraryStore.EnsureNoLinks(CompanionPaths.DataRoot, downloaded);
            await Task.Run(() => Directory.Delete(downloaded, recursive: true), _operation!.Token);
            DesktopIntegration.RemoveShortcuts(game);
            game.PlayTarget = null; game.GameDirectory = null; game.Status = "Uninstalled";
            LibraryStore.Save(game); _status.Text = "Downloaded game files removed. Prefix and backups retained.";
            return;
        }
        var directory = Path.GetDirectoryName(LibraryStore.ValidateTarget(game, game.PlayTarget ?? ""))!;
        var candidates = Directory.EnumerateFiles(directory).Where(p => Path.GetFileName(p).StartsWith("unins", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1) throw new IOException("No unique uninstaller found beside the game. Use Remove from library for an explicit prefix-removal option.");
        var target = LibraryStore.ValidateTarget(game, candidates[0]);
        if (!await ConfirmAsync("Uninstall game", "Run the game's uninstaller? Review its options for saved games. The companion keeps its prefix, cached installers, and extras.", "Run uninstaller")) return;
        var code = await UmuInstaller.RunAsync(game, target, "Uninstall", _operation!.Token);
        if (code == 0 && !File.Exists(game.PlayTarget))
        {
            DesktopIntegration.RemoveShortcuts(game); game.PlayTarget = null; game.Status = "Uninstalled"; LibraryStore.Save(game);
            _status.Text = "Game removed. Prefix, installer backups, and extras retained.";
        }
        else _status.Text = $"Uninstaller exited with code {code}. Installation record retained; executable removal was not confirmed.";
    }

    private async Task RemoveAsync()
    {
        var game = _game!;
        var delete = new CheckBox { Content = "Also permanently delete this game's prefix, including saves stored inside it", IsChecked = false };
        var deleteDownload = new CheckBox { Content = "Also permanently delete downloaded game files, including saves stored there", IsChecked = false };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Remove the library entry and companion-created shortcuts? Installer backups, extras, and downloaded game files are retained unless you select deletion below.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(delete);
        if (game.GameDirectory is not null) panel.Children.Add(deleteDownload);
        if (!await DialogAsync("Remove game", panel, "Remove")) return;
        var deletePrefix = delete.IsChecked == true;
        await Task.Run(() => LibraryStore.Remove(game, deletePrefix, deleteDownload.IsChecked == true));
        _game = null; _title.Text = "Your games"; _details.Text = ""; _status.Text = "Library entry removed.";
        Title = "GOG Disc Companion"; SetIcon(CompanionPaths.AppIcon);
        if (_cover.Source is IDisposable old) old.Dispose(); _cover.Source = null;
    }

    private Task<bool> ConfirmAsync(string title, string text, string action) => DialogAsync(title,
        new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, action);

    private async Task<bool> DialogAsync(string title, Control body, string action)
    {
        var dialog = new Window { Title = title, Width = 650, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon };
        var yes = new Button { Content = action }; var no = new Button { Content = "Cancel" };
        yes.Click += (_, _) => dialog.Close(true); no.Click += (_, _) => dialog.Close(false);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(yes); actions.Children.Add(no);
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 20 };
        panel.Children.Add(body); panel.Children.Add(actions); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }
}
