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
    private SetupCollection? _collection;
    private PackagePlan? _plan;
    private CancellationTokenSource? _cancellation;
    private GogCatalogProduct? _resolvedGame;
    private List<GogKeyProduct>? _validatedKeyDiscs;
    private readonly List<DlcOption> _dlcs = [];

    public MainWindow()
    {
        InitializeComponent();
        OutputBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GOG Disc Packages");
        LauncherBox.Text = FindLauncher();
        UpdateAccountStatus();
    }

    private void BrowseSetup_Click(object sender, RoutedEventArgs e)
    {
        if (IsCollection)
        {
            BrowseFolderInto(SetupBox);
            if (string.IsNullOrWhiteSpace(TitleBox.Text) && Directory.Exists(SetupBox.Text))
                TitleBox.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(SetupBox.Text));
            if (string.IsNullOrWhiteSpace(VersionBox.Text)) VersionBox.Text = "1.0";
            ResetScan();
            return;
        }
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

    private void GameLookupBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _resolvedGame = null;
        if (ResolvedGameBox is not null) ResolvedGameBox.ItemsSource = null;
        ResetScan();
    }

    private async void FindGame_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // The public catalog lists bundle SKUs that carry no build and omits some base games,
            // so search the account's own library — you must own a title to package it anyway.
            IReadOnlyList<GogCatalogProduct> products;
            // The library search needs this app's own sign-in, so offer it before falling back to the catalog.
            if (IsKeyMedia) PromptForSignIn();
            if (GogAuthentication.HasCredentials())
            {
                StatusText.Text = "Searching your GOG library…";
                products = await new GogLibraryClient().SearchAsync(GameLookupBox.Text, CancellationToken.None);
                if (products.Count == 0)
                    throw new InvalidOperationException(
                        $"No owned GOG game matches \"{GameLookupBox.Text.Trim()}\". Only games on your GOG account can be packaged.");
            }
            else
            {
                StatusText.Text = "Searching the GOG catalog…";
                products = await new GogCatalogClient().SearchAsync(GameLookupBox.Text);
                if (products.Count == 0) throw new InvalidOperationException("No matching GOG products were found. Try the full store URL or a more exact title.");
            }
            ResolvedGameBox.ItemsSource = products;
            ResolvedGameBox.SelectedIndex = 0;
            StatusText.Text = products.Count == 1 ? "GOG product found" : $"Choose from {products.Count} matching GOG products";
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void ResolvedGameBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _resolvedGame = ResolvedGameBox.SelectedItem as GogCatalogProduct;
        if (_resolvedGame is null) return;
        TitleBox.Text = _resolvedGame.Title;
        ResetScan();
        await LoadOwnedDlcsAsync();
    }

    private async Task LoadOwnedDlcsAsync()
    {
        _dlcs.Clear();
        DlcList.ItemsSource = null;
        if (_resolvedGame is null || !IsKeyMedia) { UpdateKeyLayoutHint(); return; }
        if (!GogAuthentication.HasCredentials())
        {
            DlcHint.Text = "Sign in to GOG (button above) to list the add-ons your account owns.";
            UpdateKeyLayoutHint();
            return;
        }
        try
        {
            DlcHint.Text = "Checking which add-ons your account owns…";
            var owned = await new GogDlRuntime().GetOwnedDlcsAsync(GetKeyProduct(), CancellationToken.None);
            foreach (var dlc in owned)
                _dlcs.Add(new DlcOption { ProductId = dlc.ProductId, Title = dlc.Title, ArtVisibility = ArtVisibility });
            DlcList.ItemsSource = _dlcs;
            DlcHint.Text = _dlcs.Count == 0
                ? "Your account owns no add-ons for this game."
                : $"{_dlcs.Count} add-on(s) owned. Clear any you do not want on the media.";
        }
        catch (Exception ex)
        {
            DlcHint.Text = "Add-ons could not be listed: " + ex.Message;
        }
        UpdateKeyLayoutHint();
    }

    private Visibility ArtVisibility => MultiDiscOption?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void KeyLayout_Changed(object sender, RoutedEventArgs e)
    {
        foreach (var dlc in _dlcs) dlc.ArtVisibility = ArtVisibility;
        UpdateKeyLayoutHint();
        ResetScan();
    }

    private void UpdateKeyLayoutHint()
    {
        if (KeyLayoutHint is null) return;
        var selected = _dlcs.Count(dlc => dlc.Selected);
        KeyLayoutHint.Text = MultiDiscOption?.IsChecked == true
            ? selected == 0
                ? "One disc will be built. Select add-ons above to add their discs."
                : $"{selected + 1} discs: Disc 1 is the base game, then one per add-on. Each add-on disc installs into the game folder Disc 1 creates."
            : selected == 0
                ? "One disc installing the base game only."
                : $"One disc installing the base game and {selected} add-on(s).";
    }

    private void BrowseDlcArt_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DlcOption dlc) return;
        // Same order as the main form: background, cover, icon.
        var background = new OpenFileDialog { Title = $"Background art for the {dlc.Title} disc (cancel to skip)", Filter = ImageFilter };
        if (background.ShowDialog() == true) dlc.BackgroundImage = background.FileName;
        var cover = new OpenFileDialog { Title = $"Cover art for the {dlc.Title} disc (cancel to skip)", Filter = ImageFilter };
        if (cover.ShowDialog() == true) dlc.CoverImage = cover.FileName;
        var icon = new OpenFileDialog { Title = $"Disc icon for {dlc.Title} (cancel to skip)", Filter = "Icons and images|*.ico;*.png;*.jpg;*.jpeg" };
        if (icon.ShowDialog() == true) dlc.IconImage = icon.FileName;
        ResetScan();
    }

    private const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp";

    private bool IsCollection => DeploymentTypeBox.SelectedIndex == 1;
    private bool IsKeyMedia => DeploymentTypeBox.SelectedIndex == 2;

    private void DeploymentTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeyIdentityPanel is null) return;
        var key = IsKeyMedia;
        var collection = IsCollection;
        KeyAccountPanel.Visibility = key ? Visibility.Visible : Visibility.Collapsed;
        UpdateAccountStatus();
        KeyIdentityPanel.Visibility = key ? Visibility.Visible : Visibility.Collapsed;
        KeyDlcPanel.Visibility = key ? Visibility.Visible : Visibility.Collapsed;
        // Key Media derives the product type from each disc's role, so the picker would be a no-op there.
        foreach (var control in new FrameworkElement[] { SetupLabel, SetupBox, SetupBrowseButton, MediaLabel, MediaPanel })
            control.Visibility = key ? Visibility.Collapsed : Visibility.Visible;
        foreach (var control in new FrameworkElement[] { ExtrasLabel, ExtrasPanel, ExtrasBrowseButton, ProductTypeLabel, ProductTypeBox })
            control.Visibility = key || collection ? Visibility.Collapsed : Visibility.Visible;
        SetupLabel.Text = collection ? "Collection folder" : "GOG setup";
        SetupBrowseButton.Content = collection ? "Choose folder…" : "Browse…";
        CollectionFolderHint.Visibility = collection ? Visibility.Visible : Visibility.Collapsed;
        SummaryText.Text = key
            ? "Enter the durable GOG product identity. The generated media will contain no game payload or account data."
            : collection ? "Choose a parent folder containing one subfolder per game, then scan the collection."
            : "Select a stock setup_*.exe, then scan the package.";
        ScanButton.Content = key ? "Validate Game-Key Disc" : collection ? "Scan collection" : "Scan package";
        BuildButton.Content = key ? "Build Game-Key Disc folder" : collection ? "Build collection disc" : "Build disc folders";
        ResetScan();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanPackageAsync();

    private async Task<bool> ScanPackageAsync()
    {
        try
        {
            if (IsKeyMedia)
            {
                var product = GetKeyProduct();
                product.Validate();
                _family = null;
                _plan = null;
                if (!await ValidateKeyProductAsync(product)) return false;
                BuildButton.IsEnabled = true;
                return true;
            }
            if (IsCollection)
            {
                if (MediaBox.SelectedIndex == 8)
                    throw new InvalidOperationException("The nightly collection prototype currently supports one disc, not mixed or multi-disc media.");
                _family = null;
                _plan = null;
                _collection = SetupCollectionScanner.Scan(SetupBox.Text);
                var capacity = GetCapacity();
                var reserve = GetReserve();
                if (_collection.TotalBytes + reserve > capacity)
                    throw new InvalidDataException($"The collection payload plus safety reserve is {FormatBytes(_collection.TotalBytes + reserve)}, larger than the selected {FormatBytes(capacity)} disc.");
                SummaryText.Text =
                    $"{_collection.Games.Count} games, {FormatBytes(_collection.TotalBytes)}\n" +
                    $"One disc; {FormatBytes(capacity - reserve - _collection.TotalBytes)} usable space remains\n\n" +
                    string.Join("\n", _collection.Games.Select(game => $"• {game.Title} ({FormatBytes(game.Family.InstallerBytes)})"));
                Log($"Scanned collection with {_collection.Games.Count} games.");
                BuildButton.IsEnabled = true;
                StatusText.Text = "Collection plan is valid";
                return true;
            }
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

    /// <summary>Confirms GOG can actually deliver this product before anything is burned to physical media.</summary>
    private async Task<bool> ValidateKeyProductAsync(GogKeyProduct product)
    {
        var selected = _dlcs.Where(dlc => dlc.Selected).ToList();
        var split = MultiDiscOption.IsChecked == true;
        var discs = BuildDiscProducts(product, selected, split);
        var heading = $"GOG Key Media for {product.Title}\nProduct ID: {product.ProductId}\n" +
                      $"Platform/language: {product.Platform}/{product.Language}\n" +
                      $"Layout: {(split ? $"{discs.Count} disc(s), one per product" : "one disc")}\n\n";

        // Validation is the last gate before media is burned, so ask for the sign-in rather than skipping the check.
        if (!GogAuthentication.HasCredentials()) PromptForSignIn();
        if (!GogAuthentication.HasCredentials())
        {
            _validatedKeyDiscs = discs;
            SummaryText.Text = heading + "Not signed in to GOG, so this product could not be checked.\n" +
                "This app signs in separately from GOG Galaxy and the GOG website — use \"Sign in to GOG\" above,\n" +
                "then validate again before burning, because some store SKUs carry no installable build.";
            StatusText.Text = "Key Media identity is valid, but unverified";
            return true;
        }

        StatusText.Text = "Checking what GOG can deliver for this product…";
        GogKeyAvailability availability;
        try { availability = await GogKeyAvailability.ProbeAsync(product, _cancellation?.Token ?? CancellationToken.None); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { ResetScan(); ShowError(ex.Message); return false; }

        foreach (var disc in discs)
        {
            disc.SupportsDirectDownload = availability.DirectDownload;
            disc.AvailableExtras = availability.Extras;
        }
        _validatedKeyDiscs = discs;

        if (!availability.AnyInstallPath)
        {
            ResetScan();
            ShowError($"GOG has no installable build and no offline installer for \"{product.Title}\" " +
                      $"(product {product.ProductId}).\n\nThis is usually a bundle or edition SKU rather than the game " +
                      "itself. Search again and pick the entry that installs, then validate before burning.");
            return false;
        }

        SummaryText.Text = heading +
            $"Direct install: {(availability.DirectDownload ? "available" : "NOT available — the launcher will use the offline installer")}\n" +
            $"Offline installer: {(availability.OfflineBackup ? $"{availability.InstallerFiles} file(s)" : "not available")}\n" +
            $"Extras: {availability.Extras}\n\n" +
            string.Join("\n", discs.Select((disc, index) =>
                $"Disc {index + 1}: {disc.Title}" +
                (disc.DiscRole == KeyDiscRole.Dlc ? "  (installs into the Disc 1 game folder)" : ""))) +
            "\n\nPayload: launcher and durable product identity only.";
        StatusText.Text = availability.DirectDownload ? "Key Media verified against GOG" : "Verified — offline installer only";
        return true;
    }

    /// <summary>One disc carrying every selected add-on, or a numbered set with the base game first.</summary>
    private static List<GogKeyProduct> BuildDiscProducts(GogKeyProduct baseProduct, List<DlcOption> selected, bool split)
    {
        if (!split)
        {
            baseProduct.IncludedDlcs = selected.Select(dlc => dlc.ProductId).ToList();
            return [baseProduct];
        }
        baseProduct.IncludedDlcs = [];
        var discs = new List<GogKeyProduct> { baseProduct };
        discs.AddRange(selected.Select(dlc => new GogKeyProduct
        {
            ProductId = dlc.ProductId,
            Slug = baseProduct.Slug,
            Title = dlc.Title,
            Platform = baseProduct.Platform,
            Language = baseProduct.Language,
            DiscRole = KeyDiscRole.Dlc,
            BaseProductId = baseProduct.ProductId,
            BaseTitle = baseProduct.Title,
            IncludedDlcs = [dlc.ProductId]
        }));
        return discs;
    }

    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        if (!await ScanPackageAsync()) return;
        if (string.IsNullOrWhiteSpace(TitleBox.Text) || (!IsKeyMedia && string.IsNullOrWhiteSpace(VersionBox.Text)))
        {
            ShowError("Enter a title and version before building.");
            return;
        }

        SetBusy(true);
        ActivityPanel.Visibility = Visibility.Visible;
        _cancellation = new CancellationTokenSource();
        string? temporaryIcon = null;
        try
        {
            var preparedIcon = IconPreparation.Prepare(EmptyToNull(IconBox.Text), out temporaryIcon);
            if (IsKeyMedia)
            {
                var discProducts = _validatedKeyDiscs ?? [GetKeyProduct()];
                var artByProductId = _dlcs.ToDictionary(dlc => dlc.ProductId);
                var discs = discProducts.Select(disc => disc.DiscRole == KeyDiscRole.Dlc && artByProductId.TryGetValue(disc.ProductId, out var art)
                    ? new KeyMediaDisc
                    {
                        Product = disc,
                        BackgroundImage = art.BackgroundImage,
                        CoverImage = art.CoverImage,
                        IconImage = IconPreparation.Prepare(art.IconImage, out _)
                    }
                    : new KeyMediaDisc
                    {
                        Product = disc,
                        BackgroundImage = EmptyToNull(BackgroundBox.Text),
                        CoverImage = EmptyToNull(CoverBox.Text),
                        IconImage = preparedIcon
                    }).ToList();
                var keyResult = await KeyMediaBuilder.BuildAsync(new KeyMediaBuildRequest
                {
                    Discs = discs,
                    SetTitle = discProducts[0].Title,
                    Version = string.IsNullOrWhiteSpace(VersionBox.Text) ? "Current GOG build" : VersionBox.Text,
                    OutputDirectory = OutputBox.Text,
                    LauncherExecutable = LauncherBox.Text
                }, _cancellation.Token);
                Log(discProducts[0].SupportsDirectDownload is null
                    ? "GOG was not signed in, so direct-install support and extras were not verified."
                    : $"Verified against GOG: direct install {(discProducts[0].SupportsDirectDownload!.Value ? "available" : "unavailable")}, " +
                      $"{discProducts[0].AvailableExtras ?? 0} extra(s).");
                Log($"Built {discs.Count} Key Media disc(s).");
                CompleteBuild(keyResult);
                return;
            }
            var progress = new Progress<PackagingProgress>(value =>
            {
                BuildProgress.Value = value.Percent;
                StatusText.Text = $"{value.Activity}: {value.CurrentFile}";
            });
            if (IsCollection)
            {
                var capacity = GetCapacity();
                var collectionResult = await CollectionBuilder.BuildAsync(new CollectionBuildRequest
                {
                    Title = TitleBox.Text,
                    Version = VersionBox.Text,
                    Collection = _collection!,
                    CapacityBytes = capacity,
                    ReserveBytes = GetReserve(),
                    MediaName = $"{capacity / 1_000_000_000d:0.###} GB media",
                    OutputDirectory = OutputBox.Text,
                    LauncherExecutable = LauncherBox.Text,
                    BackgroundImage = EmptyToNull(BackgroundBox.Text),
                    CoverImage = EmptyToNull(CoverBox.Text),
                    IconImage = preparedIcon
                }, progress, _cancellation.Token);
                CompleteBuild(collectionResult);
                return;
            }
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
            CompleteBuild(result);
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
        BuildButton.IsEnabled = !busy && (_plan is not null || _collection is not null || _validatedKeyDiscs is not null);
        CancelButton.IsEnabled = busy;
    }

    private void ResetScan()
    {
        _family = null;
        _plan = null;
        _collection = null;
        _validatedKeyDiscs = null;
        if (BuildButton is not null) BuildButton.IsEnabled = false;
    }

    private GogKeyProduct GetKeyProduct()
    {
        if (_resolvedGame is null) throw new InvalidOperationException("Find and select the GOG game before building Key Media.");
        return new GogKeyProduct
        {
            ProductId = _resolvedGame.ProductId,
            Slug = _resolvedGame.Slug,
            Title = _resolvedGame.Title,
            Platform = "windows",
            Language = string.IsNullOrWhiteSpace(LanguageBox.Text) ? "en" : LanguageBox.Text.Trim().ToLowerInvariant()
        };
    }

    private void CompleteBuild(PackageBuildResult result)
    {
        Log($"Complete: {result.PackageDirectory}");
        StatusText.Text = IsKeyMedia ? "Key Media folder built" : "Disc folders built and verified";
        MessageBox.Show(this, $"Physical-media folder is ready:\n\n{result.PackageDirectory}", "Build complete", MessageBoxButton.OK, MessageBoxImage.Information);
        if (OpenWhenCompleteBox.IsChecked == true)
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{result.PackageDirectory}\"") { UseShellExecute = true });
        if (ResetAfterBuildBox.IsChecked == true) ClearBackupInputs();
    }

    private void ClearBackupInputs()
    {
        SetupBox.Clear();
        GameLookupBox.Clear();
        ResolvedGameBox.ItemsSource = null;
        _resolvedGame = null;
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
        // The log earns its space only once there is something in it; until then the form gets the room.
        ActivityPanel.Visibility = Visibility.Visible;
        LogBox.AppendText($"[{DateTime.Now:T}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }


    private async void SignInGog_Click(object sender, RoutedEventArgs e)
    {
        // A fresh sign-in can reveal add-ons the unsigned form could not list.
        if (PromptForSignIn() && _resolvedGame is not null) await LoadOwnedDlcsAsync();
    }

    /// <summary>
    /// Shows the packager's own GOG sign-in. Returns true once credentials exist, whether this
    /// call created them or they were already there.
    /// </summary>
    private bool PromptForSignIn()
    {
        if (GogAuthentication.HasCredentials()) { UpdateAccountStatus(); return true; }
        var dialog = new GogSignInWindow { Owner = this };
        var signedIn = dialog.ShowDialog() == true;
        UpdateAccountStatus();
        if (signedIn) Log("Signed in to GOG.");
        return signedIn;
    }

    private void UpdateAccountStatus()
    {
        if (AccountStatusText is null) return;
        var signedIn = GogAuthentication.HasCredentials();
        AccountStatusText.Text = signedIn
            ? "Signed in. Key Media validation can check what GOG will deliver for this product."
            : "Not signed in. This app signs in separately from GOG Galaxy and the GOG website.";
        AccountSignInButton.Content = signedIn ? "Sign in again…" : "Sign in to GOG…";
    }
    private void ShowError(string message)
    {
        Log("ERROR: " + message);
        StatusText.Text = "Error";
        MessageBox.Show(this, message, "GOG Disc Packager", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
