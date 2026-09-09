# Linux companion app plan

Status: planning only; no Linux implementation or compatibility claims yet.

## Direction

Build a Linux companion that installs games from existing Windows offline
installer discs, using the same disc format and untouched GOG installers.
Keep it in this repository and share portable core logic. Do not fork the
format or require users to rebuild or reburn existing media.

The user has a Linux computer available for hands-on testing. Its distribution,
CPU architecture, desktop, GPU/driver, and optical drive are still to be recorded.
Initial proposed target: one validated x86-64 Linux desktop configuration.
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

1. Install the companion once on the Linux computer.
2. Insert or mount an existing disc and open the companion. Select the mounted
   folder manually if discovery does not find it.
3. Review the title/artwork, destination, space requirements, and any runtime
   download required before starting.
4. Stage the installers with hash verification and numbered disc-swap prompts.
5. Prepare the game's prefix and run GOG setup through the selected backend.
6. Confirm the installed executable, save installation state, and create a
   desktop/application shortcut. Allow manual executable selection when needed.
7. Play without reinserting the disc once installation is complete.

## Architecture and portability work

The current launcher is WPF and targets `net8.0-windows`; the core also targets
Windows. A Linux interface and separation of operating-system services are
required. UI framework and Linux distribution format remain open decisions.

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
| Compatibility backend | Locate/provision a runtime, create prefixes, execute setup/game/uninstaller, collect results |
| Linux installation store | Track package identity, prefix, executable, arguments, working directory, runtime version and status |
| Linux companion UI | Artwork, progress, recovery, settings, extras and shortcuts |

Evaluate umu-launcher first as the Proton execution backend. Valve describes
Proton as a Steam compatibility tool; umu provides infrastructure for non-Steam
use. This is a candidate, not a settled dependency or a tested integration.
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

1. **Disc-reader proof:** On the user's Linux box, read an existing single-disc
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

All scenarios are pending. Use owned installers and disposable copies for
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

- Linux machine details and the two existing packages selected for the first run.
- umu provisioning, dependency installation, offline operation, and process exit
  behavior with the actual GOG installers.
- UI framework and distribution format after validating runtime integration.
- How to discover the installed executable reliably within a prefix.
- Minimum supported Linux configuration and the game compatibility claims that
  the evidence actually supports.
