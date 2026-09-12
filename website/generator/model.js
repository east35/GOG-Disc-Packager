import {
  BACK_CONTENT_LIMITS,
  backCopyLines,
  ratingAsset,
} from "./template.js";
export const FEATURES = [
  "single-player",
  "multiplayer",
  "cloud-saves",
  "achievements",
  "controller",
  "offline-installer",
];
export const mediaName = (v) =>
  ({ "blu-ray": "Blu-ray", dvd: "DVD", cd: "CD" })[v];
export const newProject = () => ({
  game: {
    title: "",
    gameId: "",
    releaseYear: new Date().getFullYear(),
    developer: "",
    publisher: "",
    rating: { system: "none" },
    artwork: { front: null, spineTitleMode: "logo" },
    features: ["single-player", "offline-installer"],
    archive: { date: new Date().toISOString().slice(0, 10) },
  },
  case: { spine: "14mm", content: "full-game" },
  media: { type: "blu-ray", discCount: 1, labels: [{ number: 1 }] },
});
export function syncLabels(p) {
  p.media.labels = Array.from({ length: p.media.discCount }, (_, i) => ({
    ...p.media.labels.find((l) => l.number === i + 1),
    number: i + 1,
  }));
}
export function parseProject(raw) {
  const p = JSON.parse(raw),
    g = p?.game;
  const fail = () => {
    throw Error("This file is not a supported print project.");
  };
  if (
    !g ||
    !["14mm", "17mm"].includes(p.case?.spine) ||
    !["full-game", "key-media"].includes(p.case?.content) ||
    !["blu-ray", "dvd", "cd"].includes(p.media?.type) ||
    !Number.isInteger(p.media.discCount) ||
    p.media.discCount < 1 ||
    p.media.discCount > 99
  )
    fail();
  for (const k of ["title", "gameId", "developer", "publisher"])
    if (typeof g[k] !== "string") fail();
  if (
    !Number.isInteger(g.releaseYear) ||
    !["none", "ESRB", "PEGI"].includes(g.rating?.system) ||
    !Array.isArray(g.features) ||
    g.features.some((f) => !FEATURES.includes(f)) ||
    typeof g.archive?.date !== "string" ||
    !g.artwork ||
    !Array.isArray(p.media.labels)
  )
    fail();
  for (const v of [
    g.tagline,
    g.description,
    g.includedContent,
    g.copyright,
    g.rating.value,
    g.archive.archivist,
    ...Object.values(g.requirements || {}),
  ])
    if (v != null && typeof v !== "string") fail();
  if (
    g.rating.descriptors != null &&
    (!Array.isArray(g.rating.descriptors) ||
      g.rating.descriptors.some((v) => typeof v !== "string"))
  )
    fail();
  for (const l of p.media.labels)
    if (
      !Number.isInteger(l.number) ||
      (l.title != null && typeof l.title !== "string") ||
      (l.purpose != null &&
        !["install", "play", "extras", "key-media"].includes(l.purpose))
    )
      fail();
  if (
    g.artwork.screenshots != null &&
    (!Array.isArray(g.artwork.screenshots) || g.artwork.screenshots.length > 3)
  )
    fail();
  if (
    g.artwork.spineTitleMode != null &&
    !["logo", "text"].includes(g.artwork.spineTitleMode)
  )
    fail();
  for (const [slot, a] of [
    ["front", g.artwork.front],
    ["back", g.artwork.back],
    ["fullWrap", g.artwork.fullWrap],
    ["spineLogo", g.artwork.spineLogo],
    ["disc", g.artwork.disc],
    ...(g.artwork.screenshots || []).map((a, i) => [`screenshot${i}`, a]),
  ]) {
    if (a == null) continue;
    if (
      typeof a !== "object" ||
      (a.imageUrl != null && typeof a.imageUrl !== "string")
    )
      fail();
    if (a.approved != null && typeof a.approved !== "boolean") fail();
    if (a.includesTitle != null && typeof a.includesTitle !== "boolean") fail();
    if (a.scale != null) {
      const min = slot === "spineLogo" ? 0.4 : 1,
        max = slot === "spineLogo" ? 1.6 : 2;
      if (!Number.isFinite(a.scale) || a.scale < min || a.scale > max) fail();
    }
    if (
      a.focalPoint &&
      ["x", "y"].some(
        (k) =>
          !Number.isFinite(a.focalPoint[k]) ||
          a.focalPoint[k] < 0 ||
          a.focalPoint[k] > 1,
      )
    )
      fail();
  }
  g.artwork.spineTitleMode ??= "logo";
  // Migrate projects from the logo/QR version of the generator.
  delete g.brandLogos;
  delete g.archive.url;
  syncLabels(p);
  return p;
}
export function projectChecks(p, image) {
  const issues = [];
  const headline = p.game.tagline?.trim() || "";
  const synopsis = p.game.description?.trim() || "";
  const highlights = backCopyLines(p.game.includedContent);
  if (headline.length > BACK_CONTENT_LIMITS.headline)
    issues.push(
      `Back headline is too long (${BACK_CONTENT_LIMITS.headline} characters max).`,
    );
  if (synopsis.length > BACK_CONTENT_LIMITS.synopsis)
    issues.push(
      `Back synopsis is too long (${BACK_CONTENT_LIMITS.synopsis} characters max).`,
    );
  if (highlights.length > BACK_CONTENT_LIMITS.highlightCount)
    issues.push(
      `Back highlights are limited to ${BACK_CONTENT_LIMITS.highlightCount} lines.`,
    );
  if (highlights.some((line) => line.length > BACK_CONTENT_LIMITS.highlight))
    issues.push(
      `Back highlight lines are limited to ${BACK_CONTENT_LIMITS.highlight} characters each.`,
    );
  if (
    ["title", "gameId", "developer", "publisher"].some(
      (k) => !p.game[k].trim(),
    ) ||
    !Number.isInteger(p.game.releaseYear) ||
    p.game.releaseYear < 1970 ||
    p.game.releaseYear > 2100
  )
    issues.push("Complete the required game details.");
  if (p.case.spine === "14mm" && p.media.discCount > 2)
    issues.push("14 mm cases support up to two discs. Choose a wider case.");
  if (p.case.spine === "17mm")
    issues.push("Confirm the manufactured spine width before printing.");
  if (p.media.discCount > 3)
    issues.push("Confirm a case with enough trays for this disc count.");
  if (!image) issues.push("Choose front artwork.");
  else {
    const dpi = Math.min(
      image.width /
        ((p.game.artwork.front?.imageUrl
          ? 128.1
          : p.case.spine === "14mm"
            ? 270.2
            : 273.7) /
          25.4),
      image.height / (159.6 / 25.4),
    );
    if (dpi < 300)
      issues.push(
        `Artwork is approximately ${Math.round(dpi)} DPI after cropping; use 300 DPI or higher.`,
      );
    if (
      !(p.game.artwork.front?.imageUrl
        ? p.game.artwork.front.approved
        : p.game.artwork.fullWrap?.approved)
    )
      issues.push("Approve the selected artwork.");
  }
  if (
    p.game.rating.system !== "none" &&
    p.game.rating.value?.trim() &&
    !ratingAsset(p.game.rating)
  )
    issues.push(
      "This rating mark is not in the Figma library. Its text is shown on the back; a matching asset is needed for print.",
    );
  if (p.game.rating.system !== "none" && !p.game.rating.value?.trim())
    issues.push("Enter the rating value.");
  if (!/^\d{4}-\d{2}-\d{2}$/.test(p.game.archive.date))
    issues.push("Enter an archive date.");
  return issues;
}
