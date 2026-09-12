# Template generator field model

This document defines the first generator contract for the existing Figma templates. The generator is a standalone tool for making printable case inserts and disc labels. It should ask for content and make layout decisions itself. Users choose a case, media, artwork, and optional overrides; they do not place or style individual elements.

## Template choices

| Field | Values | Default |
| --- | --- | --- |
| `case.spine` | `14mm`, `17mm` | `14mm` |
| `case.content` | `full-game`, `key-media` | `full-game` |
| `media.type` | `blu-ray`, `dvd`, `cd` | `blu-ray` |
| `media.discCount` | Positive integer | `1` |

The 14 mm case supports one or two discs. The wider case is intended for three discs; its exact manufactured spine width must be confirmed before print dimensions are locked.

## Required game data

```ts
type GameTemplateData = {
  title: string
  gameId: string
  releaseYear: number
  developer: string
  publisher: string
  rating: {
    system: "ESRB" | "PEGI" | "none"
    value?: string
    descriptors?: string[]
  }
  artwork: {
    front: AssetRef
    back?: AssetRef
    spineLogo?: AssetRef // transparent PNG game logo, reused on front/spine/discs
    spineTitleMode?: "logo" | "text"
    disc?: AssetRef
    screenshots?: AssetRef[]
  }
  features: Array<
    "single-player" | "multiplayer" | "cloud-saves" |
    "achievements" | "controller" | "offline-installer"
  >
  requirements?: {
    os?: string
    cpu?: string
    ram?: string
    gpu?: string
    storage?: string
  }
  archive: {
    date: string
    archivist?: string
  }
}
```

`AssetRef` should store the source, source ID, embedded uploaded image data, crop focal point, attribution, and an optional `sourcePageUrl`. Main case and disc artwork may store a `scale` from `1` to `2` for crop zoom. The game-logo asset uses `0.4` to `1.6`; its focal point positions the logo on the front panel. Users browse SteamGridDB in a separate tab, download their chosen artwork, then upload and approve it in the generator. External image URLs are not required to reopen a saved project.

The accepted [hosting and artwork architecture decision](TEMPLATE-GENERATOR-ARCHITECTURE.md) uses GitHub Pages and browser-side processing, without a SteamGridDB API key or backend integration.

## Project settings

The generator stores its own print project and does not depend on the disc-image packaging process:

```ts
type PrintProject = {
  game: GameTemplateData
  artworkDiscovery?: {
    steamGridDbUrl?: string // User-supplied game/gallery URL; not derived from the GOG ID
  }
  case: {
    spine: "14mm" | "17mm"
    content: "full-game" | "key-media"
  }
  media: {
    type: "blu-ray" | "dvd" | "cd"
    discCount: number
    labels: Array<{
      number: number
      title?: string
      purpose?: "install" | "play" | "extras" | "key-media"
    }>
  }
}
```

Case format, media type, and disc count are direct user choices. The generator creates one coherent label per disc and can suggest a compatible case, while leaving the final choice to the user.

## Figma layer contract

Generator-facing layers use stable semantic names:

- `Artwork / Full Wrap`
- `Panel / Back`, `Panel / Front`, `Panel / Spine`
- `Content / Back Metadata`, `Content / Disc Legal`
- `Metadata / Rating`, `Metadata / Disc Count`, `Metadata / Game ID`
- `Metadata / Rating and Media`
- `Branding / Header`, `Branding / Footer`
- `Guides / Crop Marks`, `Guides / Disc Safe Area`, `Guides / Spindle Hole`

Variant properties use `Spine=14mm|17mm`, `Full Game=Yes|No`, and `Medium=DVD|Blu-ray|CD`.

## Guardrails

- Crop artwork automatically from a saved focal point and preview every panel.
- Keep legal copy, rating marks, fixed template marks, crop marks, bleed, and safe areas locked to the template.
- Offer a small set of curated treatments instead of freeform typography and positioning.
- Compose the back cover from fixed editorial slots: a two-line hook, a short synopsis, up to four highlight lines, and a fixed screenshot strip. Fall back to selected game features when highlight copy is omitted.
- Enforce copy budgets before print: 68 characters for the hook, 420 for the synopsis, and 34 per highlight line.
- Flag low-resolution artwork and overflow before export.
- Export print-ready PDF plus optional 4x PNG assets; omit guides from final output.
- Save a JSON project beside exported files so a package can be regenerated later.

## First vertical slice

1. Enter a game title and its metadata.
2. Choose case format, media type, and disc count.
3. Optionally save a SteamGridDB game/gallery URL and open it in a new tab to find cover art.
4. Let the user upload their downloaded front art and optional transparent PNG game logo, adjust focal points, and approve each selection. Keep the back legal content on fixed template assets and copy.
5. Generate the 14 mm full-game cover and matching Blu-ray labels.
6. Validate bleed, safe areas, image resolution, and text overflow.
7. Export a print-ready PDF and project JSON.
