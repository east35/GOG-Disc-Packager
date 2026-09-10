# Linux companion preview

Status: graphical Linux preview under development. Look Outside was confirmed by
its owner to work from physical disc insertion through installation, launch, and
play on the baseline machine. This is one tested game, not a general compatibility
claim. Physical multi-disc validation is deferred to another machine after the
original box crashed during the Witcher test; its cause remains unconfirmed.

## Using the preview

Build with `./publish-linux.sh`, then run `./install-linux.sh`. The publish script
uses a .NET 8 SDK or the SDK container through Podman, and produces a self-contained
Linux x64 directory plus `artifacts/gog-disc-companion-linux-x64-preview.tar.gz`
and its SHA-256 file. Extracted archives include `install.sh` for per-user
installation. Dependencies are umu-launcher, the graphical desktop libraries
required by Avalonia, Python 3 for desktop-entry installation, and `xdg-open` /
`xdg-user-dir` for folders and desktop shortcuts. Runtime provisioning may need
network access. The companion never needs root.

Open **GOG Disc Companion** from the application menu to see the library. A
separate `--watch` startup mode stays hidden until a supported disc is mounted.
Opening the app again asks the existing watcher to show its window. Closing the
window keeps the watcher alive; **Quit companion** stops it.

- Insert a disc or use **Choose disc folder**. The game icon and cover are cached
  for its window and shortcuts. The companion's menu entry uses the packager icon.
- **Install / resume** stages this disc, verifies saved installer data from all
  discs, and names any discs still needed. Setup starts only after verification
  and a confirmation explaining additional storage and possible downloads.
- After setup, confirm the game's executable. The chooser lists executables in
  its prefix and offers browsing; it does not assume the first result is correct.
  Existing installations from the earlier prototype can be registered by
  reinserting their disc and choosing the executable, without rerunning setup.
- **Play** uses the saved prefix and exact Proton directory. Missing runtimes
  produce an error instead of silently switching versions. **Choose Proton
  folder** is an explicit override. The first runtime is inferred from the
  prefix's version file after setup; unfamiliar runtime layouts require selection.
- Saving a game creates an application-menu shortcut with its cached icon.
  **Create shortcuts** additionally creates a desktop shortcut. The desktop may
  require marking that shortcut trusted. Shortcuts launch without inserted media.
- **Save disc extras** copies and verifies the current disc's extras to persistent
  storage; **Open saved extras** makes them accessible later.
- **Uninstall** runs a unique `unins*.exe` beside the chosen game executable in
  the same prefix/runtime. The record is cleared only after a successful exit and
  disappearance of the play target. Nonstandard uninstaller layouts need manual
  handling. Review the original uninstaller's choices concerning saved games.
- **Remove from library** removes the record and companion-created shortcuts.
  Prefix deletion is a separate unchecked option and deletes saves inside that
  prefix. Installer backups and extras are retained in either case.
- **Cancel operation** stops staging or the launched process tree. **Open log**
  shows timestamped media, staging, runtime, and failure details. A game operation
  lock prevents another companion process from using the same game concurrently.

Per-user files follow XDG data/cache/state locations (defaults shown):

| Location | Contents |
| --- | --- |
| `~/.local/share/gog-disc-companion/library/<id>` | Installation record, cached artwork, saved extras |
| `~/.local/share/gog-disc-companion/prefixes/<id>` | Windows prefix, installed game, potentially saved games |
| `~/.cache/gog-disc-companion/staging/<id>` | Verified installer files and partial staging |
| `~/.local/state/gog-disc-companion/logs/<id>.log` | Phase and runtime logs |

To update, quit the companion, build/extract the new preview, and rerun its
installer. Existing game records, prefixes, and staging are retained. To remove
only the companion, remove its `~/.local/lib/gog-disc-companion` installation,
`~/.local/bin/gog-disc-companion` link, and the `gog-disc-companion.desktop` entries
from the XDG applications and autostart directories. Game data is separate.

Current limits: installed-size estimates are unavailable; only staging space is
preflighted. Offline runtime provisioning, new library/shortcut/uninstall UI
behavior on the physical desktop, and launching after reboot still need hands-on
validation. Key Media and DLC installation remain unsupported. Moving or deleting
the published executable used by a shortcut breaks that shortcut; recreate it
from the installed companion. No automatic runtime upgrades or save migration.

## Automated checks

```sh
dotnet run --project tests/GogDisc.SelfTests -c Release
dotnet run --project tests/GogDisc.CompanionTests -c Release
```

Companion fixtures cover library persistence, executable containment, symlink
rejection, desktop-entry escaping, split-file revisit/repair, cross-disc tampering,
runtime configuration, logging, and cancellation using a fake runtime. They do
not substitute for physical-disc or real-game testing.

## First validation baseline

The initial M01 media read was performed on Bazzite 43 Kinoite, KDE Plasma on
Wayland, x86-64 AMD BC-250 hardware with the `amdgpu` driver, and a USB-connected
LG WH16NS60 optical drive. A physical UDF 1.02 single-disc schema-1 package was
automatically mounted read-only. The companion read both manifests, staged its
884,031,608-byte installer, and matched the payload SHA-256 recorded in the
package manifest.

The implementation lives in `src/GogDisc.Companion` and uses Avalonia with a
cross-platform .NET 8 shared core. On September 10, 2026, the user confirmed that
Look Outside had worked from disc insertion through actual gameplay. The retained
September 9 runtime log records UMU-Proton-10.0-4, Steam Linux Runtime
sniper_platform_3.0.20260805.254768, and setup exiting with code 0 at 18:26 CDT.

The Witcher Enhanced Edition staging record retains the setup executable, first
`.bin`, and first segment of the second `.bin`. The previous boot's journal ends
at 18:42:59 CDT without a recorded cause or clean shutdown. Optical read errors
occurred earlier at 18:27–18:30, before the logged disc-one mount. Do not label
this interrupted physical multi-disc run as passed or attribute its crash to the
companion without further evidence.

## Direction

Build a Linux companion that installs games from existing Windows offline
installer discs, using the same disc format and untouched GOG installers.
Keep it in this repository and share portable core logic. Do not fork the
format or require users to rebuild or reburn existing media.

The initial target is the x86-64 Bazzite configuration recorded above.
Support for other distributions and Steam Deck must follow actual testing.

Disc compatibility and game compatibility are separate promises: reading and
verifying a supported package should be deterministic; successfully running a
particular Windows game depends on its runtime, dependencies, and hardware.

## First-release scope

- Read existing offline package/disc manifests and artwork from mounted optical
  discs, mounted ISOs, USB media, and copied disc folders.
- Stage, verify, resume, and reconstruct installers across a multi-disc set.
- Prepare an isolated Wine/Proton prefix (Windows environment) for each game.
- Run the original GOG setup in that prefix, then record and launch the game.
- Offer Play, Uninstall, and access to extras, with useful progress and logs.
- Work without a GOG account for offline installer media.

Defer Linux disc creation, Linux-only media, native Linux game downloads, GOG
Key Media sign-in/downloads, automatic Steam library integration, a comprehensive
game-profile database, and bundled offline runtime media. Initially identify Key
Media clearly and explain that its Linux installation flow is not supported.
DLC installation into an existing base-game prefix is a later milestone; the
first prototype must not silently install DLC into a separate prefix.

## Intended experience

1. Install the companion once on the Linux computer. Its per-user media watcher
   starts with the desktop session; normal operation requires no terminal.
2. Insert or mount an existing disc. After the desktop mounts it, the companion
   recognizes the package and automatically opens its game/install window.
   Selecting a mounted folder remains a manual fallback.
3. Review the title/artwork, destination, space requirements, and any runtime
   download required before starting.
4. Stage the installers with hash verification and numbered disc-swap prompts.
5. Prepare the game's prefix and run GOG setup through the selected backend.
6. Confirm the installed executable, save installation state, and create a
   desktop/application shortcut. Allow manual executable selection when needed.
7. Play without reinserting the disc once installation is complete.

## Architecture and portability work

The Windows launcher remains WPF on `net8.0-windows`. The shared core targets
`net8.0`; the Linux interface uses Avalonia. The preview is distributed as a
self-contained tar archive with a per-user installer.

Reuse candidates include manifest models, JSON serialization, hashing, planning,
and staging/reassembly. Audit them before changing target frameworks. Existing
manifest paths are written with forward slashes. Existing Windows registry
discovery, process launching, optical eject, shell actions, and application-data
locations need platform-specific implementations.

Proposed boundaries:

| Component | Responsibility |
| --- | --- |
| Shared package core | Read/validate metadata, identify sets, verify payloads, stage and reconstruct installers |
| Linux media services | Discover mount roots, select folders, handle swaps/remounts and optional eject |
| Desktop activation service | Run in the user session, observe newly mounted packages, deduplicate events and open the companion window |
| Compatibility backend | Locate/provision a runtime, create prefixes, execute setup/game/uninstaller, collect results |
| Linux installation store | Track package identity, prefix, executable, arguments, working directory, runtime version and status |
| Linux companion UI | Artwork, progress, recovery, settings, extras and shortcuts |

Use umu-launcher for the first Proton execution proof. Valve describes
Proton as a Steam compatibility tool; umu provides infrastructure for non-Steam
use and supports an explicit prefix, executable, game identity, store, and Proton
version. Keep this behind a backend interface so it can be replaced if testing
uncovers a blocker. Heroic may be offered later as an optional library/import
integration, but the first install flow must not depend on Heroic being installed
or on modifying Heroic's private state.
Keep backend details out of the disc format so they can evolve independently.

References: [Valve Proton](https://github.com/ValveSoftware/Proton),
[umu-launcher](https://github.com/Open-Wine-Components/umu-launcher).

## Requirements

### Existing media and integrity

- Support the offline schema variants actually emitted by released packagers;
  collect representative historical fixtures before claiming backward compatibility.
- Never modify source media or rewrite its manifests. Preserve checks against
  the original manifest bytes and existing SHA-256 values.
- Reject wrong sets, invalid metadata, unsupported schemas, missing pieces,
  inconsistent offsets/sizes, and corrupt payloads before running setup.
- Audit path containment for Linux case sensitivity, separators, traversal,
  absolute paths, and symlinks. Package-controlled paths must stay under their
  intended roots. Pass process arguments without shell interpolation.
- Revalidate resumed data sufficiently to detect altered staged content; cached
  completion flags and matching file sizes alone are not proof of integrity.
- Prompt for the needed disc number and tolerate a changed mount path. Release
  open media handles before swapping; provide folder selection as a fallback.

### Installation and runtime

- Use a dedicated writable prefix per base game; normal operation must not
  require running the companion or installer as root.
- Run setup, game, and uninstaller with the same recorded prefix and runtime
  configuration. Handle spaces and non-ASCII names in all paths.
- Choose a tested default runtime and persist its exact version. Do not silently
  upgrade existing installations. Permit an explicit override and retain logs.
- Determine dependencies and game-specific configuration during testing. Do not
  promise that choosing a Proton version alone makes every game compatible.
- Older offline manifests lack a dedicated GOG product ID. Automatic matching
  to compatibility profiles must allow an unknown/manual choice rather than
  guessing from the title alone.
- Preflight space for staging, installed content, and runtime/prefix storage.
  Explain when installed size is an estimate and handle out-of-space failures.
- Distinguish setup cancellation, setup failure, and an unconfirmed executable.
  Mark installation complete only after verifying the selected target exists;
  separately record whether launching the game was successfully tested.
- Keep extras accessible. Remove only app-owned shortcuts/state on uninstall;
  make prefix/save deletion explicit and preserve installer backups by default.

### Offline behavior and recovery

- First-time runtime/dependency provisioning may require internet. Explain the
  download before starting and fail clearly if it is unavailable.
- With the required runtime and dependencies cached, demonstrate installation
  and play with networking disabled. Audit backend update checks and downloads.
- Preserve verified staging through cancellation, read errors, app restart, and
  disc swaps. Never execute partially assembled or failed-verification installers.
- Store Linux state/cache/logs in appropriate user-writable XDG locations;
  distinguish disposable staging from prefixes and saves.
- Report the failed phase, actionable retry options, and log location. A game
  launch failure must not force recopying verified media.

## Milestones and acceptance gates

1. **Disc-reader proof (in progress):** On the user's Linux box, read an existing single-disc
   package and an existing split multi-disc package. Reconstruct installers and
   compare their SHA-256 hashes against the original source installer files.
   No media changes. This establishes format compatibility, not game support.
2. **Execution proof:** Using the candidate backend, install and play both games
   in separate prefixes. Record exact runtime versions, setup behavior, launch
   targets, dependencies, and hardware. Confirm a second launch after reboot.
3. **Companion prototype:** Add the native flow, installation records, shortcuts,
   recovery, extras, and uninstall. Pass the scenarios below on the initial
   supported Linux configuration before describing it as a usable preview.
4. **Broader validation:** Add older/32-bit and newer 64-bit games, another Linux
   configuration, and DLC handling. Decide distribution format using results
   from runtime access, mount permissions, and desktop integration tests.
5. **Later expansion:** Revisit Key Media, Steam integration, game profiles,
   native installers, Linux authoring, and portable offline runtime provisioning.

## Test scenarios

The baseline single-disc read and Look Outside install/play have been observed.
The expanded scenarios below remain acceptance gates, with physical multi-disc
validation deferred. Use owned installers and disposable copies for
corruption tests; do not damage the user's original discs or backups.

| ID | Scenario | Expected result |
| --- | --- | --- |
| M01 | Existing single-disc package | Title/artwork load; original installer hash matches after staging; source unchanged |
| M02 | Existing multi-disc package with a split `.bin` | Correct swap prompts; reconstructed files match originals byte-for-byte |
| M03 | Multi-disc package without split files and optional extras disc | All required installers collected; extras handled separately without blocking setup |
| M04 | Physical optical disc, mounted ISO, USB and copied folder | Same package recognized through discovery or folder selection |
| M05 | Wrong set, wrong disc number, missing disc | Clear expected-disc prompt; no unrelated data accepted |
| M06 | Corrupt payload or manifest; incomplete split parts | Verification/validation fails; setup never starts |
| M07 | Cancel, restart, retry after read error; tamper with staged bytes | Valid work reused; altered/incomplete data detected and recopied |
| M08 | Eject/remount under another path; slow optical spin-up | Handles released; new root accepted; recoverable wait/retry |
| M09 | Historical released formats and unsupported future schema | Supported fixtures load; unsupported formats fail explicitly |
| M10 | Spaces, Unicode, case-sensitive paths, traversal and symlinks | Valid paths work; unsafe paths rejected without writes outside intended roots |
| I01 | Install and launch two games into separate prefixes | Independent working installs with recorded targets/runtime; no cross-contamination |
| I02 | Older 32-bit game and newer 64-bit game | Architecture/dependencies recorded; success or actionable compatibility failure |
| I03 | Setup cancelled, nonzero exit, missing/ambiguous play target | No false installed state; retry or manual selection available |
| I04 | Runtime download interrupted or unavailable | Clear provisioning failure; retry succeeds without recopying media |
| I05 | Network disabled with runtime/dependencies already cached | Offline setup and play succeed for the tested games |
| I06 | Low disk space before staging and during installation | Useful error; no false success; recoverable state |
| I07 | Relaunch companion and reboot Linux | Saved installation found; Play works with recorded prefix/runtime |
| I08 | Desktop shortcut; uninstall; extras | Shortcut launches correctly; uninstall stays within selected install; extras remain usable |
| I09 | Explicit runtime change | Change recorded; failure recoverable without deleting saves or staged installers |
| I10 | Key Media or DLC during initial prototype | Scope limitation explained; no misleading success or isolated DLC install |
| R01 | Existing Windows build and representative install flow | Shared-core changes preserve Windows packaging and installation behavior |

For each run record: date, companion commit/build, test ID, pass/fail, distro and
version, desktop/session, CPU architecture, GPU/driver, filesystem/media type,
package schema and creating packager version if known, game/installer version,
runtime/backend versions, network state, reproduction steps, and relevant logs.
Record physical-disc and ISO results separately: ISO success does not validate
optical timing or swap behavior. Keep account information out of shared logs.

## Decisions to resolve during the proof

- Repeat the Witcher multi-disc test on another Linux machine.
- umu provisioning, dependency installation, offline operation, and process exit
  behavior with the actual GOG installers.
- Validate the Avalonia preview and per-user archive installer on another desktop.
- How to discover the installed executable reliably within a prefix.
- Minimum supported Linux configuration and the game compatibility claims that
  the evidence actually supports.

## Development checkpoint — September 10, 2026

Work paused at the owner's request because this machine should not run unattended.
The shared-core regression suite and companion persistence/recovery/runtime tests
passed. A virtual-display smoke test passed for GUI startup and ICO-to-PNG
conversion. The last icon-cache change makes conversion happen only once per
package; its added cache-reuse smoke assertion still needs to be run. Final
installer/archive verification was interrupted and should be repeated on resume.
No new physical-disc or real-game test was attempted during this implementation.
The preview has not been installed over the user's existing companion, and no PR
or release is requested at this checkpoint.
