---
name: gog-pamphlet
description: Builds the printable pamphlet panels that go inside a GOG backup disc case — scrapes the store page, generates the QR badge, writes the per-title JSON and runs build_set.py to emit 483x629 SVG and PNG panels. Use this whenever the user gives a gog.com/en/game/... URL and wants a pamphlet, insert panels, or "the full flow" for a title; when they ask to rebuild, fix or restyle an existing title's panels; or when they mention AF/AB/BF/BB panels, the preservation certificate, or the production-history card. Also use it for batches of several GOG URLs at once. For case-insert artwork metadata alone (the disc case itself rather than the pamphlet), use gog-case-insert instead.
---

# GOG backup pamphlet

The user prints a small pamphlet for each personal backup disc, styled as a modern
reissue line. This skill turns a GOG store URL into finished print panels.

**This is cosmetic dressing** meant to make a shelf of burned discs look like a real
product line. Don't agonise over near-equivalent legal entities or hedge every
uncertainty — pick the most defensible option and say so in one line. But facts that
get *printed* should be true: dates, ratings, DLC and company names are worth a
minute of checking, because a wrong one is permanent once it's in the case.

Related: **gog-case-insert** covers the case artwork metadata. This skill reuses its
QR script. Don't duplicate its job.

## What gets built

Two cards, four panels, each **483 × 629** (127.79 × 166.42 mm at 96 dpi):

| Panel | Content | When |
|---|---|---|
| `<Title>_AF` | Title, rating, copyright, publisher/developer, QR, logos | always |
| `<Title>_AB` | Requirements, time to beat, genre/tags, languages, DLC, Notes | always |
| `<Title>_BF` | Preservation certificate, GOG changelog, edition contents | preserved titles only |
| `<Title>_BB` | Production history, timeline, reception, credits | preserved titles only |

Health & safety, warranty and "also available" are **not** per-title — the user has a
separate stock print for those. Never add them here.

## The flow

### 1. Locate the folder — never duplicate

Search the pipeline before creating anything:

```bash
find . -ipath '*<title>*' -not -path '*/.*' | sort
```

Titles live in `0.1_Games in Progress/`, `0.2_Ready for Print/`, `0.4_Printed/` or
`0.5_Archive/`. If one already exists anywhere, **work in it** — many of these discs
are already printed and the case insert is done. Only create
`0.1_Games in Progress/<Title>/{Assets,For Print}` for a genuinely new title.

Output goes to `<title folder>/Pamphlet/`, and `build_set.py` makes `SVG/` and `PNG/`
inside it.

### 2. Scrape the store page

Open the URL in the browser, then **wait and scroll before reading page text** —
the preservation badge, changelog and HowLongToBeat figures lazy-load and read as
absent if you query straight after navigating. This has caused a wrong answer before.

```
navigate → wait 3s → scroll down 14 → wait 2-3s → javascript_exec
```

From `window.productcardData.cardProduct` take: id, title, publishers, developers,
globalReleaseDate, gogReleaseDate, copyrights, esrbRating, pegiRating, features,
size, localizations, supportedOperatingSystems, bonuses, and `cardProductDlcs`.

From `document.body.innerText` take: the preservation marker, "What improvements we
made" changelog, "Time to beat", "Genre:/Tags:", and critic/user scores.

Keep each extraction under ~2,500 characters or it truncates mid-object. For a batch,
chain several `navigate → wait → scroll → js` groups in one browser_batch call.

### 3. Decide preserved or not — this gates card B

A title is in the **GOG Preservation Program** when the page text contains
*"We made this game up to today's standards as part of GOG Preservation Program"*
(usually alongside "This game is 2026-ready" and "Windows 11 verified").

Test `/GOG Preservation Program/i` against the rendered text.

**The "Good Old Game" tag is not the signal.** Mad Max carries it; FFIX, Breath of
Fire IV and both Witchers are all in the programme and none of them do. Going by the
tag would have silently dropped card B from four titles.

Set `"preserved": true|false` in card2.json. A non-preserved build emits card A only
and deletes any stale BF/BB from a previous run.

### 4. QR badge

Reuse the case-insert script. It needs the `qrcode` package, so make a venv in the
scratchpad if there isn't one:

```bash
python3 -m venv "$SP/venv" && "$SP/venv/bin/pip" -q install qrcode
"$SP/venv/bin/python" .claude/skills/gog-case-insert/scripts/generate_qr_badge.py \
  "<canonical url>" "<gog id>" "<Title folder>/Assets/<Title>_qr.svg" 31 45
```

Check for an existing QR first. Some titles carry an older bare-QR style with no ID
header — regenerate as a badge and keep the old one as `*_qr_old_bareQR.svg`.

### 5. Logos

Check `0.3_Assets/Logos/Publishers-Developers/` before anything else — most major
studios are covered. **Ask before downloading any logo**, naming the file, source and
format. Say plainly when a defunct studio has no findable mark rather than inventing
one. SVG and PNG/JPEG both work.

### 6. Publisher address

GOG has no address field. Look up the current registered or business address of the
entity that actually holds the rights, and cite where it came from. Companies House,
the French company registry, and a publisher's own imprint/legal-notice page are all
good sources.

### 7. Production history — preserved titles only

Card B's back needs a real research pass: studio, engine, development story, release
dates by region, what changed in later editions, reception, credits. Verify against
Wikipedia or primary sources rather than recall — a plausible-sounding date that turns
out wrong is worse than omitting it. Write 3 paragraphs, a dated timeline, a reception
line and a credits line.

### 8. Write the JSON and build

Per-title JSON lives in `<title folder>/Assets/`:
`<Title>_card1.json`, `_card2.json`, and `_card3.json` for preserved titles.
Field reference: `references/per-title-json.md`.

```bash
python3 "0.3_Assets/Templates/scripts/build_set.py" \
  --title "<Title>" \
  --data   "<title folder>/Assets" \
  --assets "0.3_Assets" \
  --out    "<title folder>/Pamphlet"
```

`--png-scale` (default 4 → 1932×2516) and `--no-png` are available.

### 9. Check the output

The builder prints the lowest element on each panel and **warns when anything drops
below the 157.4 mm safe line**. Treat that warning as a failure — fix it before
showing the user. Then look at the rendered PNGs; the guard measures vertical position
only, so it cannot see two elements colliding side by side.

## Traps that have actually bitten

**`cardProductDlcs: []` does not mean no DLC.** It's empty whenever the add-ons are
folded into the base product. Sleeping Dogs returned `[]` and ships 24 extensions; Mad
Max returned `[]` and ships ten. Check the store description, then ask the user.

**A null `esrbRating` does not mean unrated.** Septerra Core returns null for both
boards but is ESRB Teen. Check esrb.org, and ask the user — they own the game. If the
ESRB genuinely has no record (Look Outside), print the rating the user states but
caption the block "Content rating" rather than claiming an ESRB rating.

**Companies rename.** GOG's publisher field is current; the copyright string is
historical. Keep the copyright verbatim and use the present-day entity for the
imprint, naming the original publisher in the copyright line. Anachronox, Vampyr and
Septerra all needed this.

**Key discs.** Some releases hold a licence rather than an installer — inserting the
disc calls the download from GOG's servers, like a Switch 2 Game-Key Card. Nothing in
GOG's data reveals this, so **ask** when a title looks like a key release, and
describe it in the DLC block.

**Original vs re-release dates.** `globalReleaseDate` is often the re-release. The
certificate's "original release" should be the game's first release; check the
copyright string, which frequently spans both years.

## House style

Two typefaces and nothing else: **Barlow Condensed** for every title and label,
**Noto Sans** for all running text. Black on white; the only colour comes from logos
and the GOG seal.

**Title bars carry the title and nothing else.** A title with a colon splits — the
main name at full size, the edition on a second line.

One consistent style across the whole collection — these are a modern reissue line,
**not period reproductions**. Never propose 1990s pastiche, big-box or jewel-case
treatments; that was considered and rejected.

Cards adapt to thin data: a minimum-only requirements table collapses to one column,
a single language draws one column, missing HowLongToBeat figures drop the block, and
the ruled Notes area shrinks to whatever space is left — disappearing entirely when
bundled content needs the room. Bundled extras the user paid for outrank Notes.
