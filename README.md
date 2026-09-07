# GOG Disc Packager

GOG Disc Packager creates two kinds of physical GOG release: traditional offline media containing an original GOG installer backup, and compact **GOG Key Media** that stores durable product identity and retrieves an owned game from GOG at installation time.

[Download the latest GOG Disc Packager release for Windows](https://github.com/east35/GOG-Disc-Packager/releases/latest)

> This is an independent preservation tool. It is not affiliated with or endorsed by GOG, CD Projekt, or any game publisher. You must supply your own legally obtained GOG offline installers and artwork.

## What it does

- Finds the complete file family belonging to a selected `setup_*.exe`, including its numbered `.bin` files.
- Splits large files safely when one file cannot fit on a disc and reassembles them during installation.
- Supports CD-R 650 MB, CD-R 700 MB, DVD-5, DVD-9, BD-25, BD-50, BDXL-100, BDXL-128, and custom capacities.
- Supports mixed media inventories such as `BD50 x1, BD25 x10` and recommends the lowest-capacity combination that fits. Ties use fewer discs.
- Packages optional extras such as manuals, soundtracks, art books, and wallpapers.
- Adds custom background art, cover art, and a disc icon.
- Creates one burn-ready directory per disc with `autorun.inf`, the launcher, metadata, artwork, payload files, and integrity hashes.
- Provides a themed launcher that stays open while requesting each disc, displays copy progress, and starts the untouched GOG installer when staging is complete.
- Detects an existing installation and changes the launcher to offer **Play**, **Uninstall**, and **Extras** where available.
- Creates payload-free GOG Key Media for CD, DVD, Blu-ray, USB, or another filesystem-based medium.
- Key Media installs the current Galaxy build directly by default, avoiding a second full-size installer copy.
- Optionally downloads and keeps the current offline `.exe`/`.bin` backup, then reuses the original-installer workflow.
- Authenticates in the user's browser; credentials, tokens, and expiring download URLs are never written to physical media.

## Screenshots

### Package creator

![GOG Disc Packager application](img/Image%203.png)

### Multi-disc installation

![Cyberpunk 2077 multi-disc pre-install screen](img/Image%201.png)

### Installed game

![Installed-game launcher screen](img/Image%202.png)

## Requirements

The published application supports 64-bit Windows and is self-contained; the target computer does not need .NET installed.

Building from source requires:

- Windows 10 or Windows 11
- .NET 8 SDK
- PowerShell

## Create a disc package

1. Download and extract the latest release, then run `GOG Disc Packager.exe`.
2. Select one original GOG `setup_*.exe`. Matching numbered `.bin` files are detected automatically.
3. Confirm the game title, version, and whether the package is a base game or DLC.
4. Choose a media type:
   - A standard CD, DVD, or Blu-ray preset
   - A custom capacity in decimal GB
   - **Mixed media - optimize inventory**
5. For mixed media, enter what you have available, for example `BD50 x1, BD25 x10`, and scan the package. The summary shows the recommended combination and the media required for every disc.
6. Optionally choose an Extras folder, background image, portrait cover image, and PNG or ICO disc icon.
7. Select an output folder and choose **Scan package**.
8. Review the detected files, excluded patches, required disc count, and disc layout.
9. Choose **Build disc folders**. The completed package directory opens automatically unless that option is disabled.

For **GOG Key Media**, choose that deployment type, enter a game title or paste its GOG store URL, then select the matched product. The internal product ID and store slug are resolved automatically. No local setup file or media-capacity planning is required. The build is written to `<Title> GOG (Key Media)`; burn or copy the contents of the `Key Media` folder inside it to any filesystem-based physical medium.

## GOG Key Media installation

The default path downloads the current Windows Galaxy build directly into the final game folder through the independently updated `gogdl` runtime. Enable **Keep an offline backup** to download the account's current offline installers into a chosen folder and run the normal GOG setup program from them instead. Those installers are kept, so the same build can be reinstalled later without GOG; uninstalling the game does not remove them. The launcher validates free space for the backup and the installed game. If a complete backup is already present it asks whether to reuse it or download the current build again. Bonus content is never mixed into the backup; it is retrieved separately through **Extras**.

GOG sign-in uses GOG's browser authorization page. The runtime stores refresh/access credentials under `%LocalAppData%\GOG Disc Tool\GOG Runtime`; the physical media contains only the product identity. GOG Galaxy is detected for user context but is not required and its private state is not automated.

The packager reserves space on each disc for filesystem overhead and package support files. Capacity values therefore do not represent payload space byte-for-byte.

## Package layout

A completed package resembles:

```text
Game Title Version/
|-- BURNING-INSTRUCTIONS.txt
|-- Disc 01 of 03/
|   |-- Launch.exe
|   |-- autorun.inf
|   |-- game.ico
|   |-- package.json
|   |-- disc.json
|   |-- background.png
|   |-- cover.png
|   `-- Payload/
|-- Disc 02 of 03/
|   `-- ...
`-- Disc 03 of 03/
    |-- Payload/
    `-- Extras/
```

The exact media assignment for every directory is recorded in both the scan summary and `BURNING-INSTRUCTIONS.txt`.

## Burn the discs or make ISOs

Create one disc or ISO from the **contents** of each `Disc NN of NN` folder. Do not place that folder itself inside the image.

Recommended settings:

- Filesystem: UDF 2.50 or later
- One ISO or physical disc per generated disc folder
- A distinct volume label ending in the disc number
- Verify-after-write enabled

When ImgBurn asks whether it should add only the contents of the selected `Disc NN of NN` folder, choose **Yes**. Mount and test every ISO before burning permanent media.

Modern Windows versions frequently ignore `autorun.inf` on removable media. If the launcher does not start automatically, open the disc and run `Launch.exe` manually.

## Installation behavior

### Single-disc packages

The launcher runs the original GOG installer directly from the disc. No temporary backup location is needed.

### Multi-disc packages

The launcher copies and verifies the installation files from each disc into temporary storage, prompting for the next numbered disc without closing. Once all required files are present, it launches the original GOG setup program.

Peak free-space demand is approximately twice the installation-file size: one copy in temporary staging and one installed copy. The launcher shows this estimate before installation. Verified staging survives cancellation or a read failure so copying can resume, and the owned temporary backup is removed after installation is successfully confirmed.

The default local data location is:

```text
%LocalAppData%\GOG Disc Tool
```

It contains runtime cache files, installation state, logs, and resumable staging data. Staging cleanup is restricted to directories containing the matching package marker.

## Extras and patches

Extras are optional and may share remaining room on the final installer disc or continue onto additional discs. The launcher exposes them through an **Extras** button and requests the relevant disc when necessary.

Incremental `patch_*.exe` files are intentionally excluded from automatic installation. They may optionally be preserved as non-running archival extras. Base games and DLC should be packaged separately.

## Integrity and safety

- The stock GOG setup executable and data files are never modified.
- Every packaged payload fragment has a recorded SHA-256 hash and is verified while staging.
- Package and disc identifiers prevent a disc from another package being accepted accidentally.
- Output is built in a temporary directory and moved into place only after completion.
- Partial output is removed when packaging fails or is cancelled.
- Relative paths are validated to reject traversal outside controlled directories.

These checks are designed to catch damaged, incomplete, or incorrect media. They are not a cryptographic signature or a substitute for obtaining installers from a trusted source.

## Build from source

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\publish.ps1
```

The script restores dependencies and creates a timestamped self-contained release under `artifacts/`. The directory contains:

```text
GOG Disc Packager.exe
LauncherPayload\Launch.exe
README.md
```

Versioned tags matching `v*.*.*` are built, tested, packaged, checksummed, and published on the [GitHub Releases page](https://github.com/east35/GOG-Disc-Packager/releases). See [Windows code signing and reputation](docs/CODE-SIGNING.md) for Authenticode setup and false-positive guidance.

## Tests

Run the dependency-free self-test suite:

```powershell
dotnet run --project .\tests\GogDisc.SelfTests\GogDisc.SelfTests.csproj -c Release
```

Inspect a real GOG setup family without copying it:

```powershell
dotnet run --project .\tests\GogDisc.SelfTests\GogDisc.SelfTests.csproj -c Release -- --inspect "C:\path\to\setup_game.exe" "C:\path\to\Extras"
```

The tests cover setup-family detection, natural sorting, disc allocation, mixed-media optimization, multipart file staging, integrity metadata, path safety, read-only runtime caching, and rejection of incomplete installer families.

## Source layout

```text
src/GogDisc.Core       Packaging, manifests, media planning, hashing, staging, and install discovery
src/GogDisc.Packager   WPF package-creation application
src/GogDisc.Launcher   WPF launcher copied onto every generated disc
tests/GogDisc.SelfTests
assets                 Application icon
img                    README screenshots
```

## License

GOG Disc Packager is licensed under the [GNU General Public License v3.0](LICENSE).
