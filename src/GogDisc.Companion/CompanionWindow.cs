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
    private readonly Button _quit = new() { Content = "Quit companion" };
    private readonly Button _cancel = new() { Content = "Cancel operation", IsEnabled = false };
    private LoadedDisc? _media;
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
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*"), Margin = new Thickness(0, 4) };
            var icon = new Image { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Top };
            try { icon.Source = new Bitmap(DesktopIntegration.IconPath(game!)); } catch { }
            row.Children.Add(icon);
            var title = new TextBlock { Text = game!.Package.Title, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(title, 1); row.Children.Add(title); return row;
        });
        var actions = Buttons(_play, _install, _target, _shortcuts);
        var extras = Buttons(_saveExtras, _extras);
        var management = new Expander { Header = "Manage game", Content = Buttons(_runtime, _uninstall, _remove, _logs) };
        var footer = Buttons(_choose, _cancel, _quit);
        var info = new StackPanel { Spacing = 16, Margin = new Thickness(24) };
        foreach (var control in new Control[] { _title, _cover, _details, _status, _progress, actions, extras, management, footer }) info.Children.Add(control);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*"), Margin = new Thickness(16) };
        grid.Children.Add(_library);
        var scroll = new ScrollViewer { Content = info };
        Grid.SetColumn(scroll, 1); grid.Children.Add(scroll); Content = grid;
        Closing += (_, e) => { e.Cancel = true; if (!IsBusy) Hide(); else _status.Text = "Finish or cancel the operation before closing."; };
        _library.SelectionChanged += (_, _) => { if (!_refreshing && !IsBusy && _library.SelectedItem is LibraryGame game) SelectGame(game); };
        _install.Click += async (_, _) => await RunOperationAsync("Staging", InstallAsync);
        _play.Click += async (_, _) => await RunOperationAsync("Play", PlayAsync);
        _target.Click += async (_, _) => await RunOperationAsync("Choose executable", ChooseTargetAsync);
        _shortcuts.Click += async (_, _) => await RunOperationAsync("Shortcuts", async () =>
        {
            var desktop = await ConfirmAsync("Create shortcuts", "Add an application-menu shortcut and a desktop shortcut?", "Create both");
            if (!desktop) return;
            DesktopIntegration.CreateShortcuts(_game!, true);
            _status.Text = "Shortcuts created with the game icon. Your desktop may ask you to trust its shortcut.";
        });
        _saveExtras.Click += async (_, _) => await RunOperationAsync("Extras", SaveExtrasAsync);
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
            _status.Text = $"Disc {media.Disc.DiscNumber} of {media.Disc.TotalDiscCount} ready. " +
                (Supported(game) ? "Install / resume verifies and stages this disc. First-time runtime provisioning may require a download." : "Key Media and DLC installation are not supported in this preview.");
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        Show(); Activate();
    }

    private static bool Supported(LibraryGame game) => game.Package.DeploymentType == PackageDeploymentType.OfflineMedia && game.Package.ProductType == PackageProductType.BaseGame;
    private void SelectGame(LibraryGame game)
    {
        _game = game;
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
            _status.Text = "Cancelled. Saved staging is retained and will be verified when you resume.";
            PhaseLog.Write(game.Package.PackageId, _phase, "Cancelled");
        }
        catch (Exception ex)
        {
            _status.Text = $"{_phase} failed: {ex.Message}\nLog: {CompanionPaths.LogPath(game.Package.PackageId)}";
            PhaseLog.Write(game.Package.PackageId, _phase, ex.ToString());
        }
        finally { _operation = null; RefreshLibrary(); UpdateButtons(); }
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

    private async Task ChooseTargetAsync()
    {
        var game = _game!;
        var candidates = await Task.Run(() => LibraryStore.Executables(game).ToArray(), _operation!.Token);
        var choice = new ComboBox { ItemsSource = candidates, SelectedIndex = candidates.Length == 1 ? 0 : -1, HorizontalAlignment = HorizontalAlignment.Stretch };
        var browse = new Button { Content = "Browse prefix…" };
        browse.Click += async (_, _) =>
        {
            var prefix = CompanionPaths.PrefixRoot(game.Package.PackageId);
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
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Remove the library entry and companion-created shortcuts? Cached installers and extras are retained.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(delete);
        if (!await DialogAsync("Remove game", panel, "Remove")) return;
        var deletePrefix = delete.IsChecked == true;
        await Task.Run(() => LibraryStore.Remove(game, deletePrefix));
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
