using Avalonia;
using GogDisc.Companion;
using GogDisc.Core;

var root = Path.Combine(Path.GetTempPath(), "companion-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
foreach (var variable in new[] { "XDG_DATA_HOME", "XDG_CACHE_HOME", "XDG_STATE_HOME" })
    Environment.SetEnvironmentVariable(variable, Path.Combine(root, variable));
try
{
    if (args.Length == 2 && args[0] == "--ui-smoke")
    {
        SmokeApp.Output = args[1];
        Environment.ExitCode = AppBuilder.Configure<SmokeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        return;
    }
    var game = new LibraryGame { Package = new PackageManifest { PackageId = "fixture", Title = "A game % \"quoted\"\nTitle", Version = "1" } };
    LibraryStore.Save(game);
    Check(LibraryStore.Load("fixture")!.Package.Title == game.Package.Title, "Library survives reload");
    Check(LibraryStore.List().Count == 1, "Library discovery");
    using (OperationLease.Acquire("fixture"))
    {
        try { using var second = OperationLease.Acquire("fixture"); throw new Exception("Duplicate operation accepted"); }
        catch (IOException) { Console.WriteLine("PASS: Concurrent operation rejected"); }
    }
    Reject(() => CompanionPaths.PackageRoot("../escape"), "Package identity traversal");
    var prefix = CompanionPaths.PrefixRoot("fixture");
    var gameRoot = Path.Combine(prefix, "drive_c", "GOG Games", "Game é");
    Directory.CreateDirectory(gameRoot);
    var target = Path.Combine(gameRoot, "Game.exe"); File.WriteAllText(target, "fixture");
    File.WriteAllText(Path.Combine(gameRoot, "unins000.exe"), "fixture");
    Check(LibraryStore.Executables(game).Single() == target, "Discovery excludes uninstaller and supports Unicode/spaces");
    Check(LibraryStore.ValidateTarget(game, target) == target, "Valid target accepted");
    var outside = Path.Combine(root, "outside.exe"); File.WriteAllText(outside, "outside");
    Reject(() => LibraryStore.ValidateTarget(game, outside), "Outside-prefix target");
    if (OperatingSystem.IsLinux())
    {
        var link = Path.Combine(gameRoot, "linked.exe"); File.CreateSymbolicLink(link, outside);
        Reject(() => LibraryStore.ValidateTarget(game, link), "Symlink target rejected");
        Check(LibraryStore.Executables(game).Count() == 1, "Discovery skips symlinks");
    }
    game.PlayTarget = target; game.ProtonPath = Path.Combine(root, "Proton 1");
    LibraryStore.Save(game);
    var loaded = LibraryStore.Load("fixture")!;
    Check(loaded.PlayTarget == target && loaded.ProtonPath == game.ProtonPath, "Target/runtime persisted together");
    var shortcut = DesktopIntegration.ShortcutText(game, "/tmp/space % $ ` \"/companion");
    Check(shortcut.Contains("Name=A game % \\\"quoted\\\"", StringComparison.Ordinal) == false, "Name does not use Exec quoting");
    Check(shortcut.Contains("\\nTitle\nExec="), "Desktop value prevents newline injection");
    Check(shortcut.Contains("%%"), "Exec field codes escaped");
    Check(!shortcut.Contains("\nTitle\n"), "No injected desktop line");
    await TestStagingRecovery(root);
    await TestRuntime(root, game, target);
    var extras = Path.Combine(CompanionPaths.PackageRoot("fixture"), "extras"); Directory.CreateDirectory(extras);
    File.WriteAllText(Path.Combine(extras, "soundtrack"), "fixture");
    LibraryStore.Remove(game, false);
    Check(LibraryStore.Load("fixture") is null && File.Exists(target), "Removing entry retains prefix by default");
    LibraryStore.Save(game);
    LibraryStore.Remove(game, true);
    Check(!Directory.Exists(prefix) && File.Exists(outside), "Explicit prefix deletion does not follow symlinks");
    Check(File.Exists(Path.Combine(extras, "soundtrack")), "Removing game retains saved extras");
    Console.WriteLine("All companion tests passed.");
}
finally { Directory.Delete(root, true); }

static async Task TestRuntime(string root, LibraryGame game, string target)
{
    if (!OperatingSystem.IsLinux()) return;
    var bin = Path.Combine(root, "bin"); Directory.CreateDirectory(bin);
    var runtime = game.ProtonPath!; Directory.CreateDirectory(runtime); File.WriteAllText(Path.Combine(runtime, "proton"), "fixture");
    var runner = Path.Combine(bin, "umu-run");
    await File.WriteAllTextAsync(runner, "#!/bin/sh\nprintf '%s\\n' \"$WINEPREFIX\" \"$PROTONPATH\" \"$1\"\nprintf 'stderr line\\n' >&2\nexit 7\n");
    File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    var previous = Environment.GetEnvironmentVariable("PATH");
    Environment.SetEnvironmentVariable("PATH", bin + ":" + previous);
    try
    {
        var code = await UmuInstaller.RunAsync(game, target, "Play", CancellationToken.None);
        Check(code == 7, "Runtime exit propagated");
        var log = File.ReadAllText(CompanionPaths.LogPath(game.Package.PackageId));
        Check(log.Contains(runtime) && log.Contains(target) && log.Contains("stderr line"), "Runtime pin, target, stdout and stderr logged");
        await File.WriteAllTextAsync(runner, "#!/bin/sh\nsleep 30\n");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try { await UmuInstaller.RunAsync(game, target, "Play", cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Console.WriteLine("PASS: Runtime cancellation"); }
    }
    finally { Environment.SetEnvironmentVariable("PATH", previous); }
}

static async Task TestStagingRecovery(string root)
{
    var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
    await File.WriteAllBytesAsync(Path.Combine(source, "part1"), [1, 2, 3]);
    await File.WriteAllBytesAsync(Path.Combine(source, "part2"), [4, 5, 6]);
    var first = new PackageFileEntry { RelativePath = "game.bin", DiscPath = "part1", DiscNumber = 1, Size = 3, SourceSize = 6, PartCount = 2, PartIndex = 1, Sha256 = await Hashing.Sha256FileAsync(Path.Combine(source, "part1")) };
    var second = new PackageFileEntry { RelativePath = "game.bin", DiscPath = "part2", DiscNumber = 2, Size = 3, SourceSize = 6, SourceOffset = 3, PartCount = 2, PartIndex = 2, Sha256 = await Hashing.Sha256FileAsync(Path.Combine(source, "part2")) };
    var package = new PackageManifest { Files = [first, second] };
    var staging = Path.Combine(root, "staging"); var state = new StagingState();
    var disc1 = new LoadedDisc(source, package, new DiscManifest { Files = [first] });
    var disc2 = new LoadedDisc(source, package, new DiscManifest { Files = [second] });
    await StagingCopier.CopyDiscAsync(disc1, staging, state, null, CancellationToken.None);
    Check((await StagingCopier.RevalidateAsync(package, staging, state, CancellationToken.None)).SequenceEqual([2]), "Resume requests missing disc");
    await StagingCopier.CopyDiscAsync(disc2, staging, state, null, CancellationToken.None);
    await StagingCopier.CopyDiscAsync(disc1, staging, state, null, CancellationToken.None);
    Check((await StagingCopier.RevalidateAsync(package, staging, state, CancellationToken.None)).Count == 0, "Completed split file can be revisited");
    var assembled = Path.Combine(staging, "game.bin");
    await File.WriteAllBytesAsync(assembled, [9, 2, 3, 4, 5, 6]);
    Check((await StagingCopier.RevalidateAsync(package, staging, state, CancellationToken.None)).SequenceEqual([1]), "Tampered earlier disc detected before setup");
    await StagingCopier.CopyDiscAsync(disc1, staging, state, null, CancellationToken.None);
    Check(File.ReadAllBytes(assembled).SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6 }), "Repair one split segment preserves other segments");
}

static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
static void Reject(Action action, string name)
{
    try { action(); } catch (InvalidDataException) { Console.WriteLine("PASS: " + name); return; }
    throw new Exception(name + " was accepted");
}
