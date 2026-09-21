# Decision: static hosting and external artwork discovery

Date: 2026-09-11  
Status: Accepted; hosting migration and artwork links are not yet implemented.

## Context

The website and template generator are a free service maintained as part of GOG Disc Packager. They currently use personal-site hosting. The generator already runs in the browser, and keeping operating costs and maintenance low matters more than eliminating a manual artwork download/upload step.

## Decision

- Use GitHub Pages as the target host for the website and template generator. Build the Vite website with GitHub Actions and publish its static output. Support the repository subpath in navigation and asset URLs.
- Keep editing, artwork cropping, live previews, project save/open, and future PDF/PNG generation in the browser. This decision does not make the current preview print-ready.
- Use a **Find artwork on SteamGridDB** link that opens the game's gallery in a new tab. For example, Doom's cover gallery is <https://www.steamgriddb.com/game/2460/grids>.
- Let users choose and download art on SteamGridDB, then upload the file into the generator and approve its crop. Selection on SteamGridDB does not automatically transfer artwork into the generator.
- Initially accept an optional user-supplied SteamGridDB game/gallery URL and save it with the project. SteamGridDB IDs are separate from GOG IDs; do not derive one from the other or promise automatic title matching.
- Preserve an optional per-artwork source-page URL and attribution with the uploaded asset. Keep uploaded image bytes in the saved project so it can be reopened independently of external image URLs.
- Do not implement a SteamGridDB API integration, secret API key, proxy, scraping workflow, or server-side artwork search for this scope.

## Consequences

The complete generator can remain statically hosted without a runtime backend or API credentials to maintain. Users handle artwork discovery and the download/upload step themselves. Automatic game matching and one-click artwork import are intentionally outside this scope. Reconsider a backend only if a future requirement justifies its maintenance cost.

This supersedes the original schema's server-side SteamGridDB candidate-fetching step. See [the generator schema](TEMPLATE-GENERATOR-SCHEMA.md) for the updated workflow.
