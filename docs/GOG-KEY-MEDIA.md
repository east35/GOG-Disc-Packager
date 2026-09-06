# GOG Key Media architecture

Key Media is an explicit deployment type, never a size fallback. A schema-2 `package.json` contains the existing package envelope plus `DeploymentType: GogKeyMedia` and a schema-1 `GogKeyProduct` identity (`ProductId`, `Slug`, `Title`, `Platform`, and `Language`). It contains no tokens, passwords, CDN links, installers, or account identity.

The launcher downloads the current Windows `heroic-gogdl` release into `%LocalAppData%\GOG Disc Tool\GOG Runtime`. Direct installation calls its `info` and `download` commands, so current manifests, chunks, resume behavior, and GOG authentication remain in an updateable PC-side component. `heroic-gogdl` is GPL-3.0 and is designed as an application backend; this repository is also GPL-3.0. It is downloaded as a separate executable rather than copied or vendored into each physical package.

Offline-backup and extras retrieval are isolated in `GogAccountDownloads`. They resolve authenticated account metadata and fresh downlinks at runtime. This boundary is intentionally replaceable because GOG's consumer account-download endpoints are not a documented stable public SDK. GOG's file listing exposes opaque ids rather than filenames, so `ResolveDestinationAsync` derives the real on-disk name from the resolved downlink before anything is written or compared.

An offline backup is kept, not staged: it is downloaded to a user-chosen folder and left there so the same build can be reinstalled without GOG, and uninstalling the game does not remove it. Because that folder persists across builds, the setup program to run is chosen from the current listing's own resolved files rather than by scanning the directory, so a superseded installer left in place is never executed. When every file in the current listing is already present at its expected size, the user chooses between reusing that backup and replacing it.

GOG Galaxy is detected but is not a dependency. The implementation does not automate Galaxy internals or copy its account database. If the independent token is absent or expired, the user signs in on GOG's browser page and pastes the returned authorization URL/code; the application never handles the password.

Failure boundaries include invalid manifests, missing ownership/authentication, unavailable builds, API/network failures, insufficient destination or backup space, resumable partial files, non-empty destinations, and failure to confirm an installed executable. Installation state is saved only after an executable exists.
