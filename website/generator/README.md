# Generator interface

Open `/generator/` using the website's existing `npm run dev` command. The Vite build includes both the landing page and generator.

This is an interface and live layout prototype for `docs/TEMPLATE-GENERATOR-SCHEMA.md`, independent of disc packaging. It supports game metadata, case/media choices, per-disc titles and purposes, cover artwork uploads, transparent PNG game logos, focal points, artwork approval, ratings, features, requirements, and archive data. Front, back, full-wrap, game-logo, and disc artwork have position and size controls. A game logo is reused on the disc labels and can be rotated onto the spine; the spine may instead use title text. The back uses a fixed editorial blueprint—hook, synopsis, four highlight lines, and an optional screenshot strip—so users provide content without having to solve typography or placement. Blank highlight lines fall back to selected game features. Back legal copy and its warning/media marks follow the fixed Figma treatment. JSON save/open embeds uploaded artwork; nothing is uploaded to a server. Unsaved edits last only for the current page session.

Preview geometry is provisional: 129 × 184 mm panels, 14/17 mm spine, 120 mm disc with a 15 mm spindle opening. The renderer supports separate front, back, full-wrap, disc, logo, and screenshot assets. It is not a reproduction of the Figma production template. Remote or cached asset references require re-uploading artwork for this local preview.

Checks cover required content, case capacity, approximate cropped front-image DPI, approval, back-cover copy budgets, and fixed-line text overflow. Guides show illustrative safe areas; bleed, crop marks, exact production geometry, and print-ready PDF/PNG export remain future work. Draft projects may be saved with validation warnings. The interactive preview caps projects at 99 discs.

The accepted [architecture decision](../../docs/TEMPLATE-GENERATOR-ARCHITECTURE.md) targets GitHub Pages and external SteamGridDB gallery links followed by local artwork upload. Hosting migration, the gallery-link control, and source-page fields remain to be implemented. A server-side SteamGridDB integration is intentionally outside scope.

Validation: `node --test generator/model.test.js` and `npm run build`. Browser checks cover title/count updates, numbered disc previews, capacity warnings, and desktop/mobile layout.
