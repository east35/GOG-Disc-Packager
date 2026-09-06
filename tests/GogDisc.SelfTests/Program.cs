using GogDisc.Core;

if (args.Length >= 2 && args[0].Equals("--inspect", StringComparison.OrdinalIgnoreCase))
{
    var extras = args.Length >= 3 && Directory.Exists(args[2]) ? args[2] : null;
    var capacity = args.Length >= 4 ? long.Parse(args[3]) : DiscPlanner.Bd50CapacityBytes;
    var family = SetupFamilyScanner.Scan(args[1], extras);
    var reserve = capacity is DiscPlanner.Cd650CapacityBytes or DiscPlanner.CdCapacityBytes
        ? DiscPlanner.CdReserveBytes : DiscPlanner.DefaultReserveBytes;
    var plan = DiscPlanner.Create(family, capacity, reserve);
    Console.WriteLine($"Setup family: {family.FamilyName}");
    Console.WriteLine($"Required files: {family.InstallerFiles.Count} ({family.InstallerBytes / 1024d / 1024d / 1024d:N2} GiB)");
    Console.WriteLine($"Extras: {family.Extras.Count} ({family.ExtrasBytes / 1024d / 1024d / 1024d:N2} GiB)");
    Console.WriteLine($"Excluded patches: {family.ExcludedPatches.Count}");
    Console.WriteLine($"Required discs: {plan.RequiredDiscCount}; total discs: {plan.Discs.Count}");
    foreach (var disc in plan.Discs)
        Console.WriteLine($"  Disc {disc.Number}: {disc.Role}, {disc.Files.Count} files, {disc.UsedBytes / 1024d / 1024d / 1024d:N2} GiB");
    return 0;
}

if (args.Length >= 2 && args[0].Equals("--launcher-smoke", StringComparison.OrdinalIgnoreCase))
{
    using var fixture = new TempFixture();
    var setup = fixture.File("setup_smoke_1.0.exe", 256);
    var family = SetupFamilyScanner.Scan(setup);
    var plan = DiscPlanner.Create(family, 4096, 256);
    var result = await PackageBuilder.BuildAsync(new PackageBuildRequest
    {
        Title = "Launcher Smoke Test",
        Version = "1.0",
        ProductType = PackageProductType.BaseGame,
        SetupFamily = family,
        Plan = plan,
        OutputDirectory = fixture.Directory("output"),
        LauncherExecutable = args[1]
    });
    var disc = Path.Combine(result.PackageDirectory, "Disc 01 of 01");
    var start = new System.Diagnostics.ProcessStartInfo(args[1]) { UseShellExecute = false };
    start.ArgumentList.Add("--self-test");
    start.ArgumentList.Add("--disc-root");
    start.ArgumentList.Add(disc);
    using var process = System.Diagnostics.Process.Start(start) ?? throw new Exception("Launcher did not start.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new Exception($"Launcher smoke test exited with code {process.ExitCode}.");
    Console.WriteLine("Published launcher smoke test passed.");
    return 0;
}

var failures = new List<string>();
await Run("Natural sorting", TestNaturalSorting);
await Run("Setup family scanning", TestSetupScanning);
await Run("Disc allocation", TestDiscAllocation);
await Run("Mixed media economy", TestMixedMediaEconomy);
await Run("Package build and staging", TestBuildAndStage);
await Run("Path traversal rejection", TestPathSafety);
await Run("Read-only runtime cache", TestReadOnlyCache);
await Run("Incomplete and gapped families rejected", TestIncompleteFamilies);
await Run("GOG Key Media package", TestKeyMediaPackage);
await Run("GOG string size metadata", TestGogStringSizes);
await Run("GOG localized download estimate", TestGogLocalizedEstimate);
await Run("Owned Key Media uninstall", TestOwnedKeyUninstall);

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} test(s) failed:");
    failures.ForEach(Console.Error.WriteLine);
    return 1;
}

Console.WriteLine("All self-tests passed.");
return 0;

async Task Run(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL {name}: {ex.Message}");
    }
}

Task TestNaturalSorting()
{
    var values = new[] { "part-10.bin", "part-2.bin", "part-1.bin" };
    Array.Sort(values, NaturalStringComparer.Instance);
    Equal("part-1.bin,part-2.bin,part-10.bin", string.Join(',', values));
    return Task.CompletedTask;
}

Task TestSetupScanning()
{
    using var fixture = new TempFixture();
    var setup = fixture.File("setup_example_1.0_(1).exe", 10);
    fixture.File("setup_example_1.0_(1)-2.bin", 20);
    fixture.File("setup_example_1.0_(1)-1.bin", 20);
    fixture.File("patch_example_0.9_to_1.0.exe", 5);
    var extras = fixture.Directory("extras");
    File.WriteAllText(Path.Combine(extras, "manual.txt"), "manual");
    var family = SetupFamilyScanner.Scan(setup, fixture.Root, true);
    Equal(3, family.InstallerFiles.Count);
    Equal("setup_example_1.0_(1)-1.bin", family.InstallerFiles[1].RelativePath);
    Equal(1, family.ExcludedPatches.Count);
    Equal(2, family.Extras.Count);
    return Task.CompletedTask;
}

Task TestDiscAllocation()
{
    Equal(700_000_000L, DiscPlanner.CdCapacityBytes);
    Equal(96L * 1024 * 1024, DiscPlanner.CdReserveBytes);
    Equal(4_700_000_000L, DiscPlanner.Dvd5CapacityBytes);
    Equal(8_500_000_000L, DiscPlanner.Dvd9CapacityBytes);
    Equal(100_000_000_000L, DiscPlanner.Bd100CapacityBytes);
    Equal(128_000_000_000L, DiscPlanner.Bd128CapacityBytes);
    var family = new SetupFamily
    {
        SetupExecutable = "setup.exe",
        FamilyName = "setup",
        InstallerFiles =
        [
            new("a", "setup.exe", 10, PackageFileKind.Installer, 0),
            new("b", "setup-1.bin", 55, PackageFileKind.Installer, 1),
            new("c", "setup-2.bin", 55, PackageFileKind.Installer, 2)
        ],
        Extras = [new("d", "manual.pdf", 20, PackageFileKind.Extra, 0)],
        ExcludedPatches = []
    };
    var plan = DiscPlanner.Create(family, 100, 10);
    Equal(2, plan.RequiredDiscCount);
    Equal(2, plan.Discs.Count);
    Equal(PackageFileKind.Extra, plan.Discs[1].Files[^1].Kind);

    var cdSizedFamily = new SetupFamily
    {
        SetupExecutable = "setup_breath_of_fire_iv.exe",
        FamilyName = "setup_breath_of_fire_iv",
        InstallerFiles =
        [
            new("game", "setup_breath_of_fire_iv.exe", 558_467_216, PackageFileKind.Installer, 0)
        ],
        Extras = [new("manual", "Manual.pdf", 9_538_566, PackageFileKind.Extra, 0)],
        ExcludedPatches = []
    };
    var cdPlan = DiscPlanner.Create(
        cdSizedFamily,
        DiscPlanner.CdCapacityBytes,
        DiscPlanner.CdReserveBytes);
    Equal(1, cdPlan.RequiredDiscCount);
    Equal(1, cdPlan.Discs.Count);
    return Task.CompletedTask;
}

Task TestMixedMediaEconomy()
{
    var inventory = MediaCatalog.ParseInventory("BD50 x1, BD25 x10");
    var suggestion = MediaCatalog.Suggest(53_000_000_000L, inventory);
    Equal(2, suggestion.Discs.Count);
    Equal("BD50", suggestion.Discs[0].Id);
    Equal("BD25", suggestion.Discs[1].Id);

    var family = new SetupFamily
    {
        SetupExecutable = "setup.exe",
        FamilyName = "setup",
        InstallerFiles = [new("a", "setup.exe", 80, PackageFileKind.Installer, 0)],
        Extras = [],
        ExcludedPatches = []
    };
    var media = new[]
    {
        new OpticalMediaType("BIG", "Big disc", 70, 10),
        new OpticalMediaType("SMALL", "Small disc", 40, 10)
    };
    var plan = DiscPlanner.CreateMixed(family, media);
    Equal(2, plan.Discs.Count);
    Equal("Big disc", plan.Discs[0].MediaName);
    Equal("Small disc", plan.Discs[1].MediaName);
    True(plan.Discs.SelectMany(disc => disc.Files).All(file => file.PartCount == 2), "Mixed-media file was not split across both discs.");
    return Task.CompletedTask;
}

async Task TestBuildAndStage()
{
    using var fixture = new TempFixture();
    var setup = fixture.File("setup_tiny_1.0_(1).exe", 512);
    fixture.File("setup_tiny_1.0_(1)-1.bin", 2048);
    fixture.File("setup_tiny_1.0_(1)-2.bin", 2048);
    var launcher = fixture.File("Launch.exe", 128);
    var background = fixture.File("background.jpg", 96);
    var cover = fixture.File("cover.png", 96);
    var family = SetupFamilyScanner.Scan(setup);
    var plan = DiscPlanner.Create(family, 3500, 256);
    Equal(2, plan.RequiredDiscCount);
    var result = await PackageBuilder.BuildAsync(new PackageBuildRequest
    {
        Title = "Tiny Game",
        Version = "1.0",
        ProductType = PackageProductType.BaseGame,
        SetupFamily = family,
        Plan = plan,
        OutputDirectory = fixture.Directory("output"),
        LauncherExecutable = launcher,
        BackgroundImage = background,
        CoverImage = cover
    });
    Equal(2, result.Manifest.RequiredDiscCount);
    Equal(2, result.Manifest.DiscLayout.Count);
    var discOne = Path.Combine(result.PackageDirectory, "Disc 01 of 02");
    var media = DiscMedia.Load(discOne);
    var staging = fixture.Directory("staging");
    var state = StagingStateStore.LoadOrCreate(staging, result.Manifest);
    await StagingCopier.CopyDiscAsync(media, staging, state, null, CancellationToken.None);
    True(File.Exists(Path.Combine(staging, "setup_tiny_1.0_(1).exe")), "Setup was not staged.");
    var discTwo = Path.Combine(result.PackageDirectory, "Disc 02 of 02");
    await StagingCopier.CopyDiscAsync(DiscMedia.Load(discTwo), staging, state, null, CancellationToken.None);
    Equal(2048L, new FileInfo(Path.Combine(staging, "setup_tiny_1.0_(1)-2.bin")).Length);
    True(result.Manifest.Files.Any(file => file.PartCount > 1), "Oversized file was not split across discs.");
    True(File.ReadAllText(Path.Combine(discOne, "autorun.inf")).Contains("open=Launch.exe"), "Autorun was not generated.");
    True(File.ReadAllText(Path.Combine(result.PackageDirectory, "BURNING-INSTRUCTIONS.txt")).Contains("Disc 01: 0 GB media"),
        "Burning instructions did not identify the required media.");
    Equal(3500L, media.Disc.CapacityBytes);
    Equal("background.jpg", result.Manifest.BackgroundFile);
    Equal("cover.png", result.Manifest.CoverFile);
    True(File.Exists(Path.Combine(discOne, "cover.png")), "Cover art was not packaged.");
}

Task TestPathSafety()
{
    using var fixture = new TempFixture();
    var rejected = false;
    try { SafePaths.ResolveUnderRoot(fixture.Root, "..\\outside.exe"); }
    catch (InvalidDataException) { rejected = true; }
    True(rejected, "Traversal path was accepted.");
    return Task.CompletedTask;
}

Task TestReadOnlyCache()
{
    using var fixture = new TempFixture();
    var source = fixture.File("disc-package.json", 128);
    var destination = Path.Combine(fixture.Directory("runtime"), "package.json");
    File.SetAttributes(source, File.GetAttributes(source) | FileAttributes.ReadOnly);
    CacheFiles.Copy(source, destination);
    True((File.GetAttributes(destination) & FileAttributes.ReadOnly) == 0, "Cached file inherited the disc's read-only attribute.");
    File.SetAttributes(destination, File.GetAttributes(destination) | FileAttributes.ReadOnly);
    CacheFiles.Copy(source, destination);
    True((File.GetAttributes(destination) & FileAttributes.ReadOnly) == 0, "Existing read-only cache file was not repaired.");
    File.SetAttributes(source, File.GetAttributes(source) & ~FileAttributes.ReadOnly);
    return Task.CompletedTask;
}

Task TestIncompleteFamilies()
{
    using (var incomplete = new TempFixture())
    {
        var setup = incomplete.File("setup_bad_1.0.exe", 10);
        incomplete.File("download.bin.part", 10);
        Throws<InvalidDataException>(() => SetupFamilyScanner.Scan(setup));
    }
    using (var gapped = new TempFixture())
    {
        var setup = gapped.File("setup_bad_1.0.exe", 10);
        gapped.File("setup_bad_1.0-1.bin", 10);
        gapped.File("setup_bad_1.0-3.bin", 10);
        Throws<InvalidDataException>(() => SetupFamilyScanner.Scan(setup));
    }
    return Task.CompletedTask;
}

async Task TestKeyMediaPackage()
{
    using var fixture = new TempFixture();
    var launcher = fixture.File("Launch.exe", 128);
    var result = await KeyMediaBuilder.BuildAsync(new KeyMediaBuildRequest
    {
        Product = new GogKeyProduct { ProductId = "1091507383", Slug = "test_game", Title = "Test Game", Language = "en", AvailableExtras = 0 },
        OutputDirectory = fixture.Directory("output"),
        LauncherExecutable = launcher
    });
    Equal(PackageDeploymentType.GogKeyMedia, result.Manifest.DeploymentType);
    Equal("1091507383", result.Manifest.GogKeyProduct!.ProductId);
    Equal(0, result.Manifest.GogKeyProduct.AvailableExtras);
    var mediaRoot = Path.Combine(result.PackageDirectory, "Key Media");
    var media = DiscMedia.Load(mediaRoot);
    Equal(0, media.Package.Files.Count);
    True(!Directory.Exists(Path.Combine(mediaRoot, "Payload")), "Key Media unexpectedly contains a payload folder.");
    True(File.ReadAllText(Path.Combine(mediaRoot, "package.json")).Contains("1091507383"), "Product identity was not written.");
}

Task TestGogStringSizes()
{
    using var plain = System.Text.Json.JsonDocument.Parse("\"105300000000\"");
    True(GogDlRuntime.TryReadBytes(plain.RootElement, out var plainBytes), "Numeric string was not accepted.");
    Equal(105_300_000_000L, plainBytes);
    using var units = System.Text.Json.JsonDocument.Parse("\"105.3 GB\"");
    True(GogDlRuntime.TryReadBytes(units.RootElement, out var unitBytes), "Human-readable size was not accepted.");
    Equal(105_300_000_000L, unitBytes);
    return Task.CompletedTask;
}

Task TestGogLocalizedEstimate()
{
    var json = "{\"size\":{\"*\":{\"download_size\":188,\"disk_size\":424},\"en-US\":{\"download_size\":451552662,\"disk_size\":585344616}}}";
    var estimate = GogDlRuntime.ParseDownloadEstimate(json, "en");
    Equal(451_552_850L, estimate.DownloadBytes);
    Equal(585_345_040L, estimate.InstalledBytes);
    return Task.CompletedTask;
}

Task TestOwnedKeyUninstall()
{
    using var fixture = new TempFixture();
    var install = fixture.Directory("key-install");
    File.WriteAllText(Path.Combine(install, "game.exe"), "game");
    var package = new PackageManifest
    {
        PackageId = "gog-12345", Title = "Test", DeploymentType = PackageDeploymentType.GogKeyMedia,
        GogKeyProduct = new GogKeyProduct { ProductId = "12345", Slug = "test", Title = "Test" }
    };
    KeyInstallOwnership.Mark(install, package);
    True(KeyInstallOwnership.IsOwned(install, package), "Key installation marker was not accepted.");
    KeyInstallOwnership.Remove(install, package);
    True(!Directory.Exists(install), "Owned Key Media installation was not removed.");
    return Task.CompletedTask;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}.");
}

static void True(bool value, string message)
{
    if (!value) throw new Exception(message);
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

sealed class TempFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "GogDiscSelfTests", Guid.NewGuid().ToString("N"));
    public TempFixture() => System.IO.Directory.CreateDirectory(Root);

    public string Directory(string name)
    {
        var path = Path.Combine(Root, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    public string File(string name, int size)
    {
        var path = Path.Combine(Root, name);
        System.IO.File.WriteAllBytes(path, Enumerable.Range(0, size).Select(index => (byte)(index % 251)).ToArray());
        return path;
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Root, true); } catch { }
    }
}
