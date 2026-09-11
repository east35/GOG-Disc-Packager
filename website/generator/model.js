import { BRANDS, ratingAsset } from "./template.js";
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
    artwork: { front: null },
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
    g.archive.url,
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
    g.brandLogos != null &&
    (!Array.isArray(g.brandLogos) ||
      g.brandLogos.length > 2 ||
      g.brandLogos.some((b) => !Object.hasOwn(BRANDS, b)))
  )
    fail();
  if (
    g.artwork.screenshots != null &&
    (!Array.isArray(g.artwork.screenshots) || g.artwork.screenshots.length > 3)
  )
    fail();
  for (const a of [
    g.artwork.front,
    g.artwork.back,
    g.artwork.fullWrap,
    g.artwork.spineLogo,
    g.artwork.disc,
    ...(g.artwork.screenshots || []),
  ]) {
    if (a == null) continue;
    if (
      typeof a !== "object" ||
      (a.imageUrl != null && typeof a.imageUrl !== "string")
    )
      fail();
    if (a.approved != null && typeof a.approved !== "boolean") fail();
    if (a.includesTitle != null && typeof a.includesTitle !== "boolean") fail();
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
  syncLabels(p);
  return p;
}
export function projectChecks(p, image) {
  const issues = [];
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
