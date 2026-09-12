// Figma uses its own print coordinate system. Keep these source coordinates;
// converting them to physical dimensions requires a calibrated print check.
export const TEMPLATE = {
  width: 820,
  height: 504,
  panelWidth: 366,
  panelHeight: 456,
  top: 24,
  disc: 338.4167,
};
export function geometry(spine) {
  const wide = spine === "17mm";
  return {
    ...TEMPLATE,
    backX: wide ? 19 : 24,
    spineX: wide ? 385 : 390,
    spineWidth: wide ? 50 : 40,
    frontX: wide ? 435 : 430,
    trimWidth: wide ? 782 : 772,
  };
}
export const ART_SLOTS = {
  front: "Front cover",
  back: "Back cover",
  fullWrap: "Full-wrap artwork",
  spineLogo: "Game logo",
  disc: "Disc artwork",
  screenshot0: "Screenshot 1",
  screenshot1: "Screenshot 2",
  screenshot2: "Screenshot 3",
};
export const BACK_CONTENT_LIMITS = Object.freeze({
  headline: 68,
  synopsis: 420,
  highlightCount: 4,
  highlight: 34,
  copyright: 180,
});
export function backCopyLines(value) {
  return String(value || "")
    .split(/\r?\n/)
    .map((line) =>
      line
        .replace(/^\s*(?:[-•*]|\d+[.)])\s*/, "")
        .trim(),
    )
    .filter(Boolean);
}
const BACK_FEATURE_LABELS = [
  ["single-player", "Single-player campaign"],
  ["multiplayer", "Online multiplayer"],
  ["cloud-saves", "Cloud saves"],
  ["achievements", "Achievements"],
  ["controller", "Controller support"],
  ["offline-installer", "DRM-free offline installer"],
];
export function backHighlights(g) {
  const custom = backCopyLines(g.includedContent);
  if (custom.length) return custom.slice(0, BACK_CONTENT_LIMITS.highlightCount);
  return BACK_FEATURE_LABELS.filter(([key]) => g.features?.includes(key))
    .map(([, label]) => label)
    .slice(0, BACK_CONTENT_LIMITS.highlightCount);
}
export function assetRef(project, slot) {
  return slot.startsWith("screenshot")
    ? project.game.artwork.screenshots?.[Number(slot.at(-1))]
    : project.game.artwork[slot];
}
export function setAssetRef(project, slot, value) {
  if (slot.startsWith("screenshot")) {
    project.game.artwork.screenshots ??= [];
    project.game.artwork.screenshots[Number(slot.at(-1))] = value;
  } else project.game.artwork[slot] = value;
}
export function ratingAsset(rating) {
  if (rating.system !== "ESRB") return null;
  return (
    {
      m: "esrb-m",
      mature: "esrb-m",
      "mature 17+": "esrb-m",
      t: "esrb-t",
      teen: "esrb-t",
      e: "esrb-e",
      everyone: "esrb-e",
    }[rating.value?.trim().toLowerCase()] || null
  );
}
export function cropRect(iw, ih, w, h, f = { x: 0.5, y: 0.5 }, zoom = 1) {
  const scale = Math.max(w / iw, h / ih) * zoom,
    sw = w / scale,
    sh = h / scale;
  return {
    x: Math.max(0, Math.min(iw - sw, iw * f.x - sw / 2)),
    y: Math.max(0, Math.min(ih - sh, ih * f.y - sh / 2)),
    width: sw,
    height: sh,
  };
}
export function shouldUseGameLogo(project, images, surface) {
  return (
    !!images.spineLogo &&
    (surface !== "spine" || project.game.artwork.spineTitleMode !== "text")
  );
}
export function frontLogoFrame(
  asset,
  frame = { x: 22, y: 76, width: 322, height: 102 },
  panel = { width: 366, height: 456 },
) {
  const scale = asset?.scale ?? 1,
    width = frame.width * scale,
    height = frame.height * scale,
    focal = asset?.focalPoint ?? { x: 0.5, y: 0.5 },
    margin = 8,
    baseX = frame.x + frame.width / 2,
    baseY = frame.y + frame.height / 2,
    xRange =
      width > panel.width - margin * 2
        ? [panel.width / 2, panel.width / 2]
        : [margin + width / 2, panel.width - margin - width / 2],
    yRange =
      height > panel.height - margin * 2
        ? [panel.height / 2, panel.height / 2]
        : [margin + height / 2, panel.height - margin - height / 2],
    [minX, maxX] = xRange,
    [minY, maxY] = yRange,
    centeredX = Math.max(minX, Math.min(maxX, baseX)),
    centeredY = Math.max(minY, Math.min(maxY, baseY)),
    position = (value, min, center, max) =>
      value <= 0.5
        ? min + (center - min) * value * 2
        : center + (max - center) * (value - 0.5) * 2,
    centerX = position(focal.x, minX, centeredX, maxX),
    centerY = position(focal.y, minY, centeredY, maxY);
  return {
    x: centerX - width / 2,
    y: centerY - height / 2,
    width,
    height,
  };
}
export const ARCHIVE_COPY =
  "Feature availability varies by title, storefront build, and system configuration. This disc is a personal archival backup of DRM-free software obtained from GOG.com and is not an official retail product. It is not licensed for sale, rental, public performance, broadcast, or redistribution. All trademarks, logos, cover artwork, and screenshots are the property of their respective owners and appear here solely to identify the archived title. The software written to this disc remains subject to the End User License Agreement and terms of service accepted at the time of purchase; nothing on this insert grants any additional rights. Online features, multiplayer services, and downloadable content, where offered, are not contained on this disc and may require an active internet connection and a separate account. Installers were captured on the archive date printed below — later patches, hotfixes, and add-on content must be downloaded from the original storefront. Contents were verified against the publisher's checksums at the time of writing and are supplied as-is, without warranty of any kind. Optical media degrades: store away from heat, humidity, and direct sunlight, and re-verify periodically.";
const WARNING =
  "IF YOU HAVE A HISTORY OF EPILEPSY OR SEIZURES, CONSULT A DOCTOR BEFORE USE. CERTAIN PATTERNS MAY TRIGGER SEIZURES WITH NO PRIOR HISTORY. BEFORE USING AND FOR MORE DETAILS SEE INSTRUCTIONS FOR THIS PRODUCT.";
const HOLDER = "GOG sp. z o.o.";
const HOLDER_ADDRESS =
  "ul. Jagiellońska 74 03-301. Warszawa (Warsaw), Poland";

export function createRenderer(templateAssets) {
  const issues = new Set();
  const make = (w, h, scale = 2) => {
    const c = document.createElement("canvas");
    c.width = Math.ceil(w * scale);
    c.height = Math.ceil(h * scale);
    const ctx = c.getContext("2d");
    ctx.scale(scale, scale);
    return [c, ctx];
  };
  function rect(ctx, x, y, w, h, color) {
    ctx.fillStyle = color;
    ctx.fillRect(x, y, w, h);
  }
  function image(
    ctx,
    img,
    x,
    y,
    w,
    h,
    { contain = false, focal, flip = false, zoom = 1 } = {},
  ) {
    if (!img) return;
    ctx.save();
    if (flip) {
      ctx.translate(0, y * 2 + h);
      ctx.scale(1, -1);
    }
    if (contain) {
      const s = Math.min(w / img.width, h / img.height);
      ctx.drawImage(
        img,
        x + (w - img.width * s) / 2,
        y + (h - img.height * s) / 2,
        img.width * s,
        img.height * s,
      );
    } else {
      const r = cropRect(img.width, img.height, w, h, focal, zoom);
      ctx.drawImage(img, r.x, r.y, r.width, r.height, x, y, w, h);
    }
    ctx.restore();
  }
  function icon(ctx, name, x, y, w, h, flip = false) {
    image(ctx, templateAssets[name], x, y, w, h, { contain: true, flip });
  }
  function copy(
    ctx,
    value,
    x,
    y,
    w,
    h,
    size = 8,
    {
      color = "#111",
      weight = 400,
      align = "left",
      family = "Helvetica Neue, Arial, sans-serif",
      min = size,
      leading = 1.2,
      name = "Text",
    } = {},
  ) {
    const str = String(value || "");
    if (!str) return;
    const linesAt = (s) => {
      ctx.font = `${weight} ${s}px ${family}`;
      const lines = [];
      for (const para of str.split("\n")) {
        let line = "";
        for (const word of para.split(/\s+/)) {
          if (
            ctx.measureText(line ? line + " " + word : word).width > w &&
            line
          ) {
            lines.push(line);
            line = word;
          } else line = line ? line + " " + word : word;
        }
        lines.push(line);
      }
      return lines;
    };
    let font = size,
      lines = linesAt(font);
    while (
      font > min &&
      ((lines.length - 1) * font * leading + font > h ||
        lines.some((l) => ctx.measureText(l).width > w))
    ) {
      font = Math.max(min, font - 0.5);
      lines = linesAt(font);
    }
    if (
      (lines.length - 1) * font * leading + font > h + 0.1 ||
      lines.some((l) => ctx.measureText(l).width > w)
    )
      issues.add(`${name} exceeds its template area. Shorten the copy.`);
    ctx.save();
    ctx.beginPath();
    ctx.rect(x, y, w, h);
    ctx.clip();
    ctx.fillStyle = color;
    ctx.textAlign = align;
    ctx.textBaseline = "top";
    lines.forEach((l, i) =>
      ctx.fillText(
        l,
        x + (align === "center" ? w / 2 : align === "right" ? w : 0),
        y + i * font * leading,
      ),
    );
    ctx.restore();
  }
  function gradient(ctx, x, y, w, h) {
    const grad = ctx.createLinearGradient(x, y, x + w, y + h);
    grad.addColorStop(0.29427, "#ad34e0");
    grad.addColorStop(0.7354, "#601eca");
    rect(ctx, x, y, w, h, grad);
  }
  function rating(ctx, g, x, y, w, h, full = false) {
    const key = ratingAsset(g.rating);
    if (key) {
      if (full) {
        rect(ctx, x, y, w, h, "#fff");
        ctx.strokeStyle = "#000";
        ctx.lineWidth = 0.7;
        ctx.strokeRect(x, y, w, h);
        icon(ctx, key, x, y, 29, h);
        copy(
          ctx,
          (g.rating.descriptors || []).join("\n"),
          x + 31,
          y + 3,
          w - 33,
          h - 5,
          5,
          { weight: 600, min: 4, name: "Rating descriptors" },
        );
      } else icon(ctx, key, x, y, w, h);
    } else if (full && g.rating.system !== "none") {
      copy(
        ctx,
        `${g.rating.system} ${g.rating.value || ""}\n${(g.rating.descriptors || []).join(", ")}`,
        x + 2,
        y + 3,
        w - 4,
        h - 6,
        5,
        { name: "Rating details" },
      );
    }
  }
  const copyright = (g) =>
    g.copyright ||
    [g.title && `${g.title} ©${g.releaseYear}`, g.publisher, g.developer]
      .filter(Boolean)
      .join(" / ") + ".";
  const archive = (g) =>
    `GOG and the GOG logo are trademarks of GOG sp. z o.o. Archived by ${g.archive.archivist || "the archivist"}, ${g.archive.date}. Not for resale.`;
  function featureBadges(ctx, p) {
    const g = p.game;
    const items = [];
    if (g.features.includes("single-player"))
      items.push(["players", "Single\nPlayer", 29]);
    if (g.requirements?.storage)
      items.push(["storage", g.requirements.storage + "\nMinimum", 40]);
    if (g.features.includes("multiplayer"))
      items.push(["network", "Network\nFeatures", 33]);
    if (g.features.includes("achievements"))
      items.push(["achievements", "Achievements", 42]);
    if (g.features.includes("cloud-saves"))
      items.push(["cloud", "Cloud\nSaves", 28]);
    if (g.features.includes("controller"))
      items.push(["controller", "Controller\nSupport", 36]);
    if (
      p.case.content === "full-game" &&
      g.features.includes("offline-installer")
    )
      items.push(["offline", "DRM-Free\nOffline Installer", 44]);
    let x = 8;
    for (const [name, label, w] of items) {
      ctx.fillStyle = "#000";
      ctx.beginPath();
      ctx.roundRect(x, 8, w, 13, 6.5);
      ctx.fill();
      icon(ctx, "feature-" + name, x + 4, 10.5, 8, 8);
      copy(ctx, label, x + 14, 11, w - 16, 9, 3.5, {
        weight: 700,
        color: "#fff",
        leading: 1.05,
        name: "Feature badge",
      });
      x += w + 4;
    }
  }
  function rearLegal(ctx, p) {
    const g = p.game;
    rect(ctx, 0, 0, 366, 137, "#fff");
    featureBadges(ctx, p);
    icon(ctx, p.media.type, 315, 5, 43, 19, p.media.type === "cd");
    copy(ctx, ARCHIVE_COPY, 8, 32, 350, 37, 4, {
      leading: 1.15,
      name: "Archive notice",
    });
    copy(ctx, copyright(g) + " " + archive(g), 8, 73, 350, 9, 3.6, {
      name: "Copyright / archive",
    });
    rating(ctx, g, 8, 86, 85, 44, true);
    rect(ctx, 95, 86, 121, 13, "#000");
    copy(ctx, HOLDER + "\n" + HOLDER_ADDRESS, 97, 87, 117, 10, 3.2, {
      color: "#fff",
      align: "center",
      weight: 700,
      leading: 1.1,
      name: "GOG rights holder",
    });
    rect(ctx, 95, 101, 121, 8, "#e01b1b");
    icon(ctx, "warning-icon", 96, 102, 6, 6);
    icon(ctx, "warning-word", 104, 103, 29.77, 4.63);
    ctx.strokeStyle = "#e01b1b";
    ctx.lineWidth = 0.5;
    ctx.strokeRect(95, 101, 121, 29);
    copy(ctx, WARNING, 97, 111, 117, 17, 3.4, {
      color: "#c31a1a",
      leading: 1.15,
      name: "Template warning",
    });
    rect(ctx, 218, 86, 140, 12.5, "#000");
    copy(ctx, "RECOMMENDED PC SETTINGS", 220, 90, 136, 7, 4.4, {
      color: "#fff",
      weight: 700,
      align: "center",
    });
    ctx.strokeStyle = "#000";
    ctx.strokeRect(218, 98.5, 140, 31.5);
    const req = g.requirements || {};
    copy(
      ctx,
      ["os", "cpu", "ram", "gpu"]
        .map((k) => `${k.toUpperCase()}: ${req[k] || "—"}`)
        .join("\n"),
      220,
      101,
      136,
      27,
      4,
      { leading: 1.3, name: "PC requirements" },
    );
  }
  function keyBack(ctx) {
    rect(ctx, 0, 202, 366, 102, "#fff");
    copy(ctx, "INSTALL IT & KEEP IT", 8, 210, 137, 19, 14, {
      weight: 700,
      family: "Barlow Condensed, Arial Narrow, sans-serif",
    });
    copy(
      ctx,
      "Installing downloads the current build from your GOG account.",
      155,
      210,
      203,
      19,
      7,
    );
    for (const [x, name, title, body] of [
      [
        8,
        "key-install",
        "DIRECT INSTALL",
        "Installs the latest build straight from GOG.\nFastest, smallest footprint.",
      ],
      [
        187,
        "key-backup",
        "KEEP AN OFFLINE BACKUP",
        "Saves GOG’s standalone installer to your drive.\nReinstall later without internet or GOG.",
      ],
    ]) {
      rect(ctx, x, 232, 171, 1, "#8a38f5");
      icon(ctx, name, x, 239, 10, 11);
      copy(ctx, title, x + 14, 239, 157, 14, 11, {
        family: "Barlow Condensed, Arial Narrow, sans-serif",
        weight: 700,
      });
      copy(ctx, body, x, 257, 170, 23, 7, { name: "Key media instructions" });
    }
    rect(ctx, 8, 281, 350, 19, "#eee0fc");
    icon(ctx, "key-offline", 14, 286, 8.3, 10);
    copy(ctx, "DRM-FREE / OFFLINE INSTALLER SUPPORTED", 27, 286, 325, 11, 8, {
      weight: 700,
      color: "#601eca",
      family: "Barlow Condensed, Arial Narrow, sans-serif",
    });
  }
  function backContent(ctx, p, images) {
    const g = p.game,
      key = p.case.content === "key-media",
      height = key ? 202 : 304,
      screenshots = [0, 1, 2].filter((i) => images["screenshot" + i]),
      customHighlights = backCopyLines(g.includedContent),
      highlights = backHighlights(g),
      railHighlights = highlights.slice(0, key ? 2 : 4),
      hasRail = railHighlights.length > 0,
      headline = g.tagline?.trim() || g.title || "YOUR GAME TITLE",
      hasText = headline || g.description || highlights.length || screenshots.length;
    if (hasText) {
      const grad = ctx.createLinearGradient(0, 0, 0, height);
      grad.addColorStop(0, "#000b");
      grad.addColorStop(1, "#0003");
      rect(ctx, 0, 0, 366, height, grad);
    }
    copy(ctx, headline, 18, 18, 330, 27, 14, {
      color: "#fff",
      weight: 700,
      family: "Barlow Condensed, Arial Narrow, sans-serif",
      min: 12,
      name: "Back headline",
    });
    const textHeight = screenshots.length
      ? key
        ? 78
        : 160
      : height - 58;
    copy(
      ctx,
      g.description,
      18,
      50,
      hasRail ? 168 : 330,
      textHeight,
      8,
      { color: "#fff", min: 7.5, name: "Back description" },
    );
    if (hasRail) {
      const railX = 202,
        railY = 50,
        railW = 147,
        railH = screenshots.length ? (key ? 76 : 160) : height - 58;
      rect(ctx, railX, railY, railW, railH, "#000b");
      ctx.strokeStyle = "#ffffff55";
      ctx.lineWidth = 0.5;
      ctx.strokeRect(railX, railY, railW, railH);
      copy(
        ctx,
        customHighlights.length ? "INCLUDES" : "FEATURES",
        railX + 10,
        railY + 9,
        railW - 20,
        10,
        6.5,
        {
          color: "#fff",
          weight: 700,
          family: "Barlow Condensed, Arial Narrow, sans-serif",
          name: "Back highlights heading",
        },
      );
      const itemStep = key ? 25 : 29;
      railHighlights.forEach((item, index) => {
        const itemY = railY + 27 + index * itemStep;
        ctx.fillStyle = "#d3b5ff";
        ctx.beginPath();
        ctx.arc(railX + 12, itemY + 4, 2, 0, Math.PI * 2);
        ctx.fill();
        copy(ctx, item, railX + 19, itemY, railW - 27, 20, 6.7, {
          color: "#fff",
          min: 6,
          leading: 1.1,
          name: "Back feature callout",
        });
      });
    }
    screenshots.forEach((i, index) => {
      const w = (350 - (screenshots.length - 1) * 7) / screenshots.length,
        x = 8 + index * (w + 7),
        h = key ? 58 : 78,
        y = height - h - 8;
      image(ctx, images["screenshot" + i], x, y, w, h, {
        focal: assetRef(p, "screenshot" + i)?.focalPoint,
      });
      ctx.strokeStyle = "#fff";
      ctx.lineWidth = 1;
      ctx.strokeRect(x, y, w, h);
    });
    if (key) keyBack(ctx);
    ctx.save();
    ctx.translate(0, 319);
    rearLegal(ctx, p);
    ctx.restore();
  }
  function title(ctx, p, images, x, y, w, h, surface = "disc") {
    const front = surface === "front";
    if (front && p.game.artwork.front?.includesTitle) return;
    if (shouldUseGameLogo(p, images, surface)) {
      if (front) {
        const frame = frontLogoFrame(p.game.artwork.spineLogo, {
          x,
          y,
          width: w,
          height: h,
        });
        ctx.save();
        ctx.beginPath();
        ctx.rect(0, 0, 366, 456);
        ctx.clip();
        image(ctx, images.spineLogo, frame.x, frame.y, frame.width, frame.height, {
          contain: true,
        });
        ctx.restore();
      } else image(ctx, images.spineLogo, x, y, w, h, { contain: true });
      return;
    }
    copy(ctx, p.game.title || "YOUR GAME TITLE", x, y, w, h, front ? 36 : 20, {
      color: "#fff",
      weight: 700,
      align: "center",
      min: front ? 22 : 12,
      family: "Barlow Condensed, Arial Narrow, sans-serif",
      name: front ? "Front title" : "Title",
    });
  }
  function drawWrap(p, images, guides) {
    const geo = geometry(p.case.spine),
      [c, ctx] = make(820, 504);
    const g = p.game;
    rect(ctx, 0, 0, 820, 504, "#fff");
    rect(ctx, geo.backX, 24, geo.trimWidth, 456, "#242424");
    if (images.fullWrap)
      image(ctx, images.fullWrap, geo.backX, 24, geo.trimWidth, 456, {
        focal: g.artwork.fullWrap?.focalPoint,
        zoom: g.artwork.fullWrap?.scale,
      });
    for (const [slot, x] of [
      ["back", geo.backX],
      ["front", geo.frontX],
    ]) {
      if (images[slot])
        image(ctx, images[slot], x, 24, 366, 456, {
          focal: g.artwork[slot]?.focalPoint,
          zoom: g.artwork[slot]?.scale,
        });
      else if (!images.fullWrap && images.front)
        image(ctx, images.front, x, 24, 366, 456, {
          focal: g.artwork.front?.focalPoint,
          zoom: g.artwork.front?.scale,
        });
    }
    ctx.save();
    ctx.translate(geo.backX, 24);
    backContent(ctx, p, images);
    ctx.restore();
    ctx.save();
    ctx.translate(geo.frontX, 24);
    title(ctx, p, images, 22, 76, 322, 102, "front");
    rating(ctx, g, 8, 379, 46, 65);
    if (p.media.discCount > 1) {
      rect(ctx, 58, 422, 83, 18, "#fff");
      icon(ctx, "disc-count", 62, 426, 22.3, 11);
      copy(ctx, `${p.media.discCount} DISCS`, 88, 429, 51, 8, 6, {
        weight: 700,
      });
    }
    ctx.restore();
    if (!images.fullWrap)
      rect(ctx, geo.spineX, 24, geo.spineWidth, 456, "#181818");
    ctx.save();
    ctx.translate(geo.spineX + geo.spineWidth / 2, 24 + 246);
    ctx.rotate(Math.PI / 2);
    title(
      ctx,
      p,
      images,
      -171,
      -(geo.spineWidth - 8) / 2,
      342,
      geo.spineWidth - 8,
      "spine",
    );
    ctx.restore();
    copy(ctx, g.gameId, geo.spineX + 2, 471, geo.spineWidth - 4, 7, 5, {
      color: "#fff",
      weight: 600,
      align: "center",
      name: "Spine game ID",
    });
    gradient(ctx, geo.spineX, 24, geo.spineWidth + 57, 44);
    icon(ctx, "gog-white", geo.spineX + 6, 32, 28, 27);
    icon(ctx, "gog-white", geo.frontX + 6, 32, 28, 27);
    ctx.save();
    ctx.translate(geo.frontX + 50, 34);
    ctx.rotate(Math.PI / 2);
    copy(ctx, String(g.releaseYear), 0, 0, 28, 9, 8, {
      color: "#fff",
      weight: 700,
      align: "center",
      family: "Avenir Next, Arial, sans-serif",
    });
    ctx.restore();
    if (p.case.content === "key-media") {
      rect(ctx, geo.frontX + 57, 24, 309, 44, "#fff");
      icon(ctx, "game-key-disc", geo.frontX + 68, 36, 18.5, 20.5);
      copy(ctx, "GAME-KEY DISC", geo.frontX + 96, 32, 220, 16, 12, {
        weight: 800,
        family: "Barlow Condensed, Arial Narrow, sans-serif",
      });
      copy(
        ctx,
        "Internet and GOG account required. Install direct, or keep an offline installer.",
        geo.frontX + 96,
        49,
        254,
        13,
        6,
        { name: "Key media header" },
      );
      icon(ctx, "game-key", geo.spineX + geo.spineWidth / 2 - 9, 443, 18, 20);
    }
    if (guides) {
      icon(
        ctx,
        p.case.spine === "14mm" ? "crop-14" : "crop-wide",
        0,
        0,
        820,
        504,
      );
      ctx.save();
      ctx.strokeStyle = "#3ecbca";
      ctx.lineWidth = 0.65;
      ctx.setLineDash([3, 3]);
      for (const x of [geo.backX, geo.frontX])
        ctx.strokeRect(x + 8, 32, 350, 440);
      ctx.setLineDash([2, 3]);
      for (const x of [geo.spineX, geo.frontX]) {
        ctx.beginPath();
        ctx.moveTo(x, 24);
        ctx.lineTo(x, 480);
        ctx.stroke();
      }
      ctx.restore();
    }
    c.setAttribute("role", "img");
    c.setAttribute(
      "aria-label",
      `${g.title || "Untitled"} · Figma cover: back, spine and front`,
    );
    return c;
  }
  function drawDisc(p, l, images, guides) {
    const d = TEMPLATE.disc,
      [c, ctx] = make(d, d),
      g = p.game;
    ctx.save();
    ctx.beginPath();
    ctx.arc(d / 2, d / 2, d / 2, 0, Math.PI * 2);
    ctx.clip();
    rect(ctx, 0, 0, d, d, "#474747");
    const slot = images.disc ? "disc" : images.front ? "front" : "fullWrap";
    image(ctx, images[slot], 0, 0, d, d, {
      focal: g.artwork[slot]?.focalPoint,
      zoom: g.artwork[slot]?.scale,
    });
    title(ctx, p, images, 67, 29, 204, 60, "disc");
    rating(ctx, g, 33, 109, 46, 65);
    rect(ctx, 46, 179, 22, 22, "#ffffffdd");
    icon(ctx, "gog-black", 48, 181, 19, 18);
    rect(ctx, 257, 161, 50, 24, "#ffffffdd");
    icon(ctx, p.media.type, 259, 163, 46, 20, p.media.type === "cd");
    const cd = p.media.type === "cd";
    const legalY = cd ? 236 : 219,
      legalWidth = cd ? 236 : 168;
    rect(
      ctx,
      (d - legalWidth) / 2 - 3,
      legalY - 2,
      legalWidth + 6,
      cd ? 28 : 43,
      "#0009",
    );
    copy(
      ctx,
      copyright(g) + " " + archive(g),
      (d - legalWidth) / 2,
      legalY,
      legalWidth,
      cd ? 26 : 42,
      6,
      {
        color: "#fff",
        align: "center",
        leading: 1.33,
        name: "Disc legal copy",
      },
    );
    gradient(ctx, 0, 266, d, 72);
    const details =
      p.media.discCount > 1 ||
      l.title ||
      l.purpose ||
      p.case.content === "key-media";
    if (details) {
      rect(ctx, 0, 266, d, 13, "#0008");
      copy(
        ctx,
        `DISC ${l.number}: ${(l.title || l.purpose || (p.case.content === "key-media" ? "GAME-KEY DISC" : "INSTALL + PLAY")).replaceAll("-", " ").toUpperCase()}`,
        65,
        269,
        208,
        9,
        7,
        { color: "#fff", weight: 700, align: "center", name: "Disc label" },
      );
    }
    icon(ctx, "gog-white", 146, details ? 286 : 280, 46, 44);
    if (guides) {
      ctx.strokeStyle = "#3ecbca";
      ctx.lineWidth = 0.65;
      ctx.setLineDash([3, 3]);
      for (const r of [
        ((d / 2) * 118) / 120,
        ((d / 2) * (cd ? 40 : 23)) / 120,
      ]) {
        ctx.beginPath();
        ctx.arc(d / 2, d / 2, r, 0, Math.PI * 2);
        ctx.stroke();
      }
    }
    ctx.globalCompositeOperation = "destination-out";
    ctx.beginPath();
    ctx.arc(d / 2, d / 2, ((d / 2) * 15) / 120, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
    c.setAttribute("role", "img");
    c.setAttribute(
      "aria-label",
      `${g.title || "Untitled"} · Disc ${l.number} of ${p.media.discCount}`,
    );
    return c;
  }

  return {
    render(p, images, { view = "wrap", guides = false, discNumber = 1 } = {}) {
      issues.clear();
      const wrap = drawWrap(p, images, guides),
        geo = geometry(p.case.spine);
      let output = wrap;
      // Validate every disc's dynamic text, but release off-screen canvases promptly.
      for (const l of p.media.labels) {
        const disc = drawDisc(p, l, images, guides);
        if (view === "disc" && l.number === discNumber) output = disc;
      }
      if (["front", "back", "spine"].includes(view)) {
        const x =
            view === "front"
              ? geo.frontX
              : view === "back"
                ? geo.backX
                : geo.spineX,
          w = view === "spine" ? geo.spineWidth : 366;
        const [c, ctx] = make(w, 456);
        ctx.drawImage(wrap, x * 2, 48, w * 2, 912, 0, 0, w, 456);
        c.setAttribute("role", "img");
        c.setAttribute(
          "aria-label",
          `${p.game.title || "Untitled"} · ${view} panel`,
        );
        output = c;
      }
      return { canvas: output, issues: [...issues], geometry: geo };
    },
  };
}
