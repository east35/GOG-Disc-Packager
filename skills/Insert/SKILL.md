---
name: gog-case-insert
description: Pulls case-insert metadata (GOG ID, QR code, recommended PC specs, copyright line, ESRB rating, publisher address, publisher/developer logos) for a GOG.com game page, for building personal backup-disc case inserts. Trigger on a request for "case insert info" / "disc metadata" / publisher address / QR badge for a GOG title, or a bare gog.com/en/game/... URL where the user wants metadata rather than finished artwork. If they want the printable pamphlet panels (AF/AB/BF/BB) built, use gog-pamphlet instead — it covers the whole flow and reuses this skill's QR script.
---

# GOG case insert metadata

Purpose: the user is designing case-insert artwork (Figma/SVG) for personal
backup Blu-ray/DVD-Rs of games they own on GOG.com. This is cosmetic —
it makes the disc look like a legitimate retail product sitting on a shelf.
**Nobody reads the fine print.** Don't over-hedge, don't multi-paragraph
every caveat — pick the most sensible answer and move on. A one-line flag
is fine when something is genuinely ambiguous (e.g. publisher has multiple
regional legal entities); anything more is wasted effort here.

When given a `https://www.gog.com/en/game/<slug>` URL (or asked to redo
this for a title already discussed), do step 0 first, then produce all
seven items below unless the user asks for a subset.

## 0. Create the game folder

Before pulling any metadata, create the per-title folder structure:

```
0.1_Games in Progress/<Game Name>/Assets/
0.1_Games in Progress/<Game Name>/For Print/
```

- `<Game Name>` is the game's display title (from step 1's `cp.title`,
  cleaned up for a folder name — trim trailing whitespace, drop/replace
  characters that don't work in a path).
- `Assets/` holds everything gathered/generated for this title — the QR
  code, downloaded logos specific to this title, reference screenshots,
  etc.
- `For Print/` is empty at this point — it's where the finished case
  insert artwork goes later, once the user has built it in Figma.
- If the game already has a folder elsewhere in the pipeline (`0.2_Ready
  for Print`, `0.4_Printed`, `0.5_Archive`), don't create a duplicate in
  `0.1_Games in Progress` — say so and ask where the user wants this
  round of work to go instead.

## 1. Game ID, title, publisher, developer

Open the URL in a browser tool and read `window.productcardData` off the
page — it's already embedded, no scraping/parsing needed:

```js
var cp = window.productcardData.cardProduct;
JSON.stringify({
  id: window.productcardData.cardProductId,
  slug: window.productcardData.cardProductSlug,
  title: cp.title,
  publishers: cp.publishers,
  developers: cp.developers,
  globalReleaseDate: cp.globalReleaseDate,
  copyrights: cp.copyrights,
  os: cp.supportedOperatingSystems
})
```

Report the numeric `id` (this is the "GOG game ID") and the slug.

## 2. GOG ID + QR badge SVG

The deliverable is a single combined badge — a black header bar with the
GOG game ID, and a bordered white box below it holding the QR code — not
a bare QR code. This matches the user's reference asset
(`~/Desktop/QR.svg`): black header ~28% of total height, GOG ID centered
in it (auto-sized/margined, not touching the edges), 1px black frame
around a white QR box below.

Run the bundled script against a Python environment that has the
`qrcode` package (create a disposable venv if the host doesn't have one
globally — `pip install` may be externally managed):

```bash
python3 scripts/generate_qr_badge.py "<canonical page url>" "<GOG ID>" \
  "0.1_Games in Progress/<Game Name>/Assets/<Game Name>_qr.svg" \
  31 45
```

- Strip stray trailing punctuation from the URL the user pastes (e.g. a
  trailing `&`) before encoding it — it isn't part of a real query string.
- **Output path**: save into the `Assets/` subfolder created in step 0,
  named `<Game Name>_qr.svg` — not the shared `0.3_Assets/` folder (that's
  for reusable, cross-title assets like logos and templates, not per-title
  QR codes).
- **Size**: 31×45 is the reference default; pass a different width/height
  if the user asks for a different footprint, but keep the ~28% header
  ratio and 1px frame — the script derives both from the height passed.
- Deliver the file to the user (send/attach it, don't just describe it).

## 3. Recommended PC system requirements

From `cp.supportedOperatingSystems`, find the Windows entry and report the
`"recommended"` tier's CPU / RAM / GPU / Storage / DirectX / OS fields.
If a field is missing from the Recommended tier (common — many GOG
listings only fill in what changed vs. Minimum), say so plainly rather
than backfilling from the Minimum tier silently. If GOG only lists a
Minimum tier at all (no Recommended), say that and report Minimum instead.

## 4. Copyright metadata line

Format: `[TITLE] ©[YEAR] [PUBLISHER] / [DEVELOPER]`

- Year comes from `globalReleaseDate` (use the original release year, not
  a later "Enhanced/Ultimate Edition" re-release year, when the two differ
  and the copyright notice below confirms the earlier year).
- Prefer reusing `cp.copyrights` verbatim when GOG already supplies exact
  legal phrasing — it's often more accurate than composing your own from
  the publisher/developer fields (e.g. it may name a parent holding
  company instead of the studio subsidiary listed as publisher).
- If publisher and developer are the same entity, collapse to a single
  `©[YEAR] [ENTITY]` rather than repeating the name twice.

## 5. ESRB rating + content descriptors

From `cp.esrbRating` (fetch it alongside the item-1 query if not already
pulled: `{esrbRating: cp.esrbRating, pegiRating: cp.pegiRating}`). Report
the category name (e.g. "Teen", "Mature 17+") and the content descriptor
list (e.g. "Violence", "Blood and Gore"). If `esrbRating` is `null` on the
GOG listing (common for older/indie titles), say it isn't rated by ESRB
rather than guessing — fall back to reporting `pegiRating` if that's
present instead, and note which board's rating you're giving.

## 6. Publisher legal entity + mailing address

GOG's product data has no street address field. Look up the publisher's
official registered or retail-box address (web search is fine — retail
packaging, investor/legal-notice pages, and corporate registries are good
sources). Give one clean address and move on; don't present multiple
regional subsidiaries as an unresolved dilemma unless the user's specific
disc/region genuinely depends on picking the right one.

## 7. Publisher + developer logos

The user already has a large personal logo library (Rebel Wolves, CD
Projekt Red, Bandai Namco, WB, GOG.com, Unreal Engine, Ubisoft, Eidos,
Squaresoft/Square Enix, Capcom, Bethesda, Devolver Digital, Activision,
Arkane, TotallyGames, etc.) — this step exists mainly for **older/obscure
studios whose logos aren't easy to find**, so check with the user first
if the publisher/developer looks likely to already be covered.

- **Storage**: shared library at `0.3_Assets/Logos/`, split into
  `Publishers-Developers/` (studios and publishers, e.g.
  `topware_interactive.png`) and `Engines/` (tech marks like Unreal
  Engine, REDengine). A multi-file press-kit bundle (multiple formats/
  color variants, like the existing CD Projekt Red folder) gets its own
  subfolder named after the company rather than being flattened. Before
  sourcing a new logo, check whether it already exists — reuse it rather
  than re-fetching, since many titles share a publisher (Square Enix, CD
  Projekt, etc.).
- **Sourcing**: prefer an official vector source — Wikimedia Commons
  (search `<company> logo svg site:commons.wikimedia.org`), the
  publisher's own press/media kit page, or brand-asset sites like
  Brands of the World. Fall back to a clean PNG (ideally on a transparent
  background) when no vector version exists, which is common for
  older/defunct studios (this is the expected case they're needed for).
- **Downloading**: this is a real download action, so ask the user first
  every time — name the exact file, the source URL, and format/size
  before fetching it. Don't batch-approve across a whole session; each
  new company gets its own quick confirmation.
- If no usable logo can be found publicly for a defunct/obscure studio,
  say so rather than fabricating a wordmark.

## Output format

Lead with GOG ID + title, then publisher/developer, then recommended
specs, then the copyright line, then the address — plain text/markdown,
no headers needed for a single title. Attach the QR SVG file alongside.
Skip the warning/epilepsy-notice boilerplate entirely — the user has said
they don't want it generated per-title (it's static legal text, not
metadata that varies per game).
