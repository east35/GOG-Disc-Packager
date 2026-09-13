# Per-title JSON reference

Three files in `<title folder>/Assets/`. Card 3 is only read for preserved titles.

- `<Title>_card1.json` → panel AF
- `<Title>_card2.json` → panel AB, and BF when preserved
- `<Title>_card3.json` → panel BB (preserved only)

`<Title>` is the file stem you pass as `--title`. Keep it filesystem-safe: no colons.
The display name lives in the JSON `title` field, so a stem of
`Sleeping Dogs Definitive Edition` can print as `Sleeping Dogs: Definitive Edition`.

---

## card1.json — AF (title, rating, copyright, publisher)

| Field | Required | Notes |
|---|---|---|
| `title` | yes | Display name. Splits on the first colon into title + edition subtitle. |
| `gog_id` | yes | Numeric product id. |
| `archived` | yes | ISO date the disc was written. |
| `rating_board` | no | e.g. `"ESRB"`. Omit when no board actually rated it — the caption then reads "Content rating" instead of claiming a board. |
| `rating_asset` | no | e.g. `"Ratings/M.svg"`, relative to `0.3_Assets`. |
| `rating_category` | no | e.g. `"Mature 17+"`. Absent ⇒ the panel prints "NOT RATED". |
| `descriptors` | no | List of content descriptors. |
| `rating_note` | no | Used instead of descriptors when a rating has none. |
| `unrated_note` | no | Shown when there is no rating at all. |
| `copyright_lines` | yes | List of paragraphs. Prefer GOG's `copyrights` verbatim — it usually carries the exact legal phrasing, studio codes like `(s15)`, and live-service disclaimers worth printing. |
| `publisher_entity` | yes | Present-day legal entity. |
| `publisher_address` | yes | Registered or business address. |
| `developer` | yes | Studio, optionally with location. |
| `logos` | no | List of `"path"` strings or `{"path", "h"}` objects, relative to `0.3_Assets`. SVG or PNG/JPEG. `h` is a preference in mm — the row auto-scales to clear the QR badge. Wordmarks want a smaller height than shields: Square Enix's mark is 9.6:1. |

## card2.json — AB always, BF when preserved

| Field | Required | Notes |
|---|---|---|
| `title` | yes | Display name. |
| `specs_heading` | no | Overrides the AB masthead when the full title is too long. |
| `preserved` | no | `true`/`false`. Defaults to whether `changelog` exists. Gates card B. |
| `archived` | when preserved | The certificate reads this from card2, **not** card1. Card B crashes without it. |
| `seal_asset` | when preserved | `"Seals/GOG_Preserved_Good_Old_Game_seal.png"`. |
| `disc_format` | when preserved | e.g. `"BD-R 25 GB"`. Ask — it's a fact about the user's media. |
| `original_release` | when preserved | The game's *first* release, not the re-release. |
| `programme` | when preserved | `"GOG Preservation Program"`. |
| `certificate_text` | when preserved | One or two sentences. |
| `changelog` | when preserved | `{"date": "...", "items": [...]}`. `date` may be omitted. |
| `contents` | when preserved | `[{"name", "on_disc"}]` — filled dot on disc, hollow means GOG Galaxy required. |
| `requirements` | yes | `{"minimum": {...}, "recommended": {...}}` keyed by `system`, `processor`, `memory`, `graphics`, `directx`, `storage`. Omit `recommended` entirely for minimum-only titles — the table then collapses to one wide column. Rows absent from both tiers are skipped. |
| `requirements_note` | no | One line under the table (Linux/macOS availability, 64-bit requirement). |
| `hltb` | no | `[{"value", "label"}]`. Omit the key when the page has no figures. |
| `hltb_source` | no | Attribution line. |
| `genre` | no | Middot-separated. |
| `tags` | no | List. |
| `languages` | yes | `[{"name", "audio", "text"}]`. Draws up to three columns, only as many as there are languages. |
| `dlc_heading` | no | The block's heading — "Included downloadable content", "Included extras", "Digital Deluxe content", "What these discs unlock". |
| `dlc_items` | no | List → a ruled list, one row each. Over 8 items runs two-up at smaller size. |
| `dlc_note` | no | Paragraph. Used alone for long grouped lists, or beneath `dlc_items`. |
| `notes_heading` | no | Defaults to "Notes". |

The Notes area takes whatever vertical space is left and is dropped entirely below two
lines. That is deliberate: bundled content the user paid for outranks blank ruled lines.

## card3.json — BB (production history)

```json
{
  "title": "<display name>",
  "side_a": {
    "module": "history",
    "heading": "Production history",
    "body": ["paragraph", "paragraph", "paragraph"],
    "timeline_heading": "Timeline",
    "timeline": [{"when": "7 Jul 2000", "what": "Released in Japan for PlayStation."}],
    "reception": "Scores and sales, one line.",
    "credit_line": "Director … · Producer … · Music …",
    "source_note": "Compiled from public release records. Dates are first-release dates unless noted."
  }
}
```

`build_card3.py` also implements a `catalogue` module ("also available"), kept for
reference but not used in the current set — that content moved to the stock print.

History copy must be self-contained. Never point at another panel by card number,
because card B is conditional and the DLC list lives on card A. Say "included on this
disc" rather than "see card 2".

---

## Worked example: a card-A-only title

```json
// Vampyr_card2.json (abridged)
{
  "title": "Vampyr",
  "preserved": false,
  "requirements": {
    "minimum":     {"system": "64-bit Windows 7, 8 or 10", "memory": "8 GB RAM", "storage": "20 GB available"},
    "recommended": {"system": "64-bit Windows 7, 8 or 10", "memory": "16 GB RAM", "storage": "20 GB available"}
  },
  "hltb": [{"value": "16.5 h", "label": "Main"}, {"value": "29.5 h", "label": "Main + sides"}],
  "genre": "Action · Horror · Role-playing",
  "tags": ["Story Rich", "Atmospheric", "Dark"],
  "languages": [{"name": "English", "audio": true, "text": true},
                {"name": "French", "audio": false, "text": true}],
  "dlc_heading": "Downloadable content",
  "dlc_items": ["The Hunters Heirlooms"],
  "dlc_note": "Listed on GOG.com as a separate product, not part of the base installer.",
  "notes_heading": "Notes"
}
```

## Scripts

All in `0.3_Assets/Templates/scripts/`:

| File | Role |
|---|---|
| `build_set.py` | Entry point. Builds the set, writes `SVG/` and `PNG/`. |
| `pamphlet_common.py` | Geometry, type, wrapping, asset placement, the safe-area guard. Layout is authored in millimetres and mapped through the viewBox, so the file is exactly 483 × 629 while every internal measurement stays physical. |
| `build_card1.py` | AF renderer. |
| `build_card2.py` | AB and BF renderers. |
| `build_card3.py` | BB renderers, keyed by `module`. |
| `impose.py` | Superseded — imposed panels 2-up on US Letter with duplex and proof modes. The user does their own print layout now. Don't offer it unprompted. |

When editing a renderer, replace code by matching on unique text rather than slicing
between function names — the functions are not in the order you expect, and a bad
slice once duplicated a definition so the stale version silently won.
