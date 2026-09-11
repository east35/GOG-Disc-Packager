# GOG Disc Packager

Turn games you own on GOG into custom physical editions for CD, DVD, Blu-ray,
or USB.

GOG Disc Packager can either put the original DRM-free offline installers on
the media or create lightweight **GOG Key Media** that downloads your owned
game when you install it.

**[Download for Windows](https://github.com/east35/GOG-Disc-Packager/releases/latest)** ·
**[Watch the video](https://youtu.be/HWElimyy0rs?si=kwxalTj4uyCF9lfO)** ·
**[Figma artwork template](https://www.figma.com/design/CBR9nICUoNR4Km00Dc8Nui/GOG-Disc-Packager?node-id=0-1&t=9PPg85pr21azP88F-1)**

![GOG Disc Packager application](img/Image%203.png)

> GOG Disc Packager is an independent preservation tool. It is not affiliated
> with or endorsed by GOG, CD Projekt, or any publisher. You must own the games
> you package and provide your own installers and artwork.

## Choose a package type

| | Offline installer discs | GOG Key Media |
|---|---|---|
| Stores the game on the media | Yes | No |
| Installs without internet | Yes | No |
| Requires a GOG account | No | Yes |
| Best for | A durable offline copy | A small physical token for a digital game |

Key Media can optionally download and keep GOG's current offline installers,
turning the installation into a reusable offline backup.

## Make offline installer discs

1. Download and extract the [latest release](https://github.com/east35/GOG-Disc-Packager/releases/latest), then run `GOG Disc Packager.exe`.
2. Choose **Offline installer media** and select the game's original GOG `setup_*.exe`. Matching `.bin` files are found automatically.
3. Choose a disc type, a custom capacity, or **Mixed media** if you want the app to optimize a supply such as `BD50 x1, BD25 x10`.
4. Optionally add extras and custom background, cover, and icon artwork.
5. Choose an output folder, select **Scan package**, review the proposed layout, then select **Build disc folders**.
6. Burn the **contents** of each generated `Disc NN of NN` folder—not the folder itself—to its own disc or ISO.

Use UDF 2.50 or later, give each disc a distinct label ending in its disc
number, and enable verify-after-write. Mount and test ISOs before burning them.

Windows may ignore `autorun.inf`. If nothing opens automatically, run
`Launch.exe` from the disc.

### Put multiple games on one disc (nightly)

Choose **Offline collection**, then select a parent folder containing one
subfolder per game. Each game folder must contain exactly one stock
`setup_*.exe` family and may contain an `Extras` folder. The collection launcher
lets you choose a game and then opens its normal install, play, extras, and
uninstall screen.

The initial nightly implementation builds one collection disc only. It rejects
a collection that does not fit the selected medium; multi-disc collections and
per-game artwork are not yet supported.

## Make GOG Key Media

1. Choose **GOG Key Media**.
2. Enter a game title or paste its GOG store URL and select the matching product.
3. Build the package.
4. Burn or copy the **contents** of the generated `Key Media` folder to any filesystem-based CD, DVD, Blu-ray, or USB drive.

During installation, the launcher opens GOG's browser sign-in and confirms that
the account owns the game. Credentials, tokens, installers, and temporary
download links are never stored on the physical media.

By default, Key Media downloads the current Windows Galaxy build directly into
the game folder. Enable **Keep an offline backup** to save the current GOG
`.exe` and `.bin` installers and install from those instead. GOG Galaxy is not
required.

## What the packager handles

- CD-R, DVD, Blu-ray, BDXL, custom capacities, and mixed-media inventories
- Installer families containing a setup executable and numbered `.bin` files
- Safe splitting and reassembly of files that are larger than one disc
- Optional manuals, soundtracks, art books, wallpapers, and other extras
- SHA-256 verification and protection against inserting a disc from the wrong set
- Resumable multi-disc staging followed by the untouched GOG installer
- Play, Uninstall, and Extras actions after an installed game is detected

Incremental `patch_*.exe` files are excluded from automatic installation but can
be kept as archival extras. Package base games and DLC separately.

![Multi-disc installation screen](img/Image%201.png)

![Installed-game launcher screen](img/Image%202.png)

## Requirements and troubleshooting

The release is a self-contained 64-bit Windows application. The target computer
does not need .NET installed.

Multi-disc installation temporarily needs roughly twice the installer size:
one copy for staging and one for the installed game. Verified staging can resume
after cancellation or a read error. Runtime data and logs are stored in:

```text
%LocalAppData%\GOG Disc Tool
```

For implementation details about Key Media, see
[GOG Key Media architecture](docs/GOG-KEY-MEDIA.md). For Windows trust warnings
and release signing, see [Code signing and reputation](docs/CODE-SIGNING.md).

## Build from source

Building requires Windows 10 or 11, the .NET 8 SDK, and PowerShell.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\publish.ps1
```

Run the self-tests with:

```powershell
dotnet run --project .\tests\GogDisc.SelfTests\GogDisc.SelfTests.csproj -c Release
```

## Nightly test builds

Work that needs real-disc testing before a stable release goes on the `nightly`
branch. Every push to that branch, as well as the daily scheduled run, replaces
the rolling **Nightly** GitHub prerelease with a signed Windows ZIP and checksum.
These builds may be unstable and should not be presented as the normal download.

Start new test work by bringing `nightly` up to date with `main`, commit and push
changes to `nightly`, then download the result from the Nightly prerelease. Once
the changes have passed testing, merge `nightly` into `main`; a stable release is
still created only by pushing a semantic version tag such as `v1.2.3`.

## License

GOG Disc Packager is licensed under the [GNU GPLv3](LICENSE).
