import {
  FEATURES,
  newProject,
  syncLabels,
  parseProject,
  projectChecks,
} from "./model.js";
import {
  ART_SLOTS,
  backCopyLines,
  assetRef,
  setAssetRef,
  createRenderer,
} from "./template.js";
import manifest from "./figma-assets.json";
const $ = (s) => document.querySelector(s),
  form = $("#controls"),
  words = (s) => s.replaceAll("-", " "),
  staticAssetUrl = (file) =>
    location.protocol === "file:"
      ? `../public/${file}`
      : `${import.meta.env.BASE_URL}${file}`,
  templateAssetUrl = (file) =>
    location.protocol === "file:"
      ? `../public/assets/template/${file}`
      : `${import.meta.env.BASE_URL}assets/template/${file}`;
for (const el of document.querySelectorAll("[data-static-asset]"))
  el.href = el.src = staticAssetUrl(el.dataset.staticAsset);
const SCALABLE_ART_SLOTS = new Set([
  "front",
  "back",
  "fullWrap",
  "spineLogo",
  "disc",
]);
let project = newProject(),
  images = {},
  activeSlot = "front",
  templateAssets = {},
  renderer,
  assetErrors = [],
  discNumber = 1,
  view = "wrap",
  token = 0,
  renderFrame;
for (const f of FEATURES) {
  const l = document.createElement("label");
  l.className = "check";
  const i = document.createElement("input");
  i.type = "checkbox";
  i.value = f;
  i.dataset.feature = "";
  l.append(i, document.createTextNode(words(f)));
  $("#features").append(l);
}
for (const k of ["os", "cpu", "ram", "gpu", "storage"]) {
  const l = document.createElement("label");
  l.textContent = k.toUpperCase();
  const i = document.createElement("input");
  i.name = `game.requirements.${k}`;
  l.append(i);
  $("#requirements").append(l);
}
const get = (path) => path.split(".").reduce((o, k) => o?.[k], project);
function set(path, value) {
  const keys = path.split(".");
  let node = project;
  for (const k of keys.slice(0, -1)) node = node[k] ??= {};
  node[keys.at(-1)] = value;
}
function backHighlightInputs() {
  return [...form.querySelectorAll("[data-back-item]")];
}
function refreshBackCopyFields() {
  const lines = backCopyLines(project.game.includedContent);
  backHighlightInputs().forEach((input, index) => {
    input.value = lines[index] || "";
  });
  updateBackCopyCounters();
}
function syncBackHighlights() {
  project.game.includedContent = backHighlightInputs()
    .map((input) => input.value.trim())
    .filter(Boolean)
    .join("\n");
}
function updateBackCopyCounters() {
  for (const counter of form.querySelectorAll("[data-count-for]")) {
    const source = document.getElementById(counter.dataset.countFor);
    if (!source) continue;
    const length = source.value.length,
      max = source.maxLength;
    counter.textContent = String(length);
    source.closest("label")?.classList.toggle(
      "over-budget",
      max > 0 && length > max,
    );
  }
  const used = backHighlightInputs().filter((input) => input.value.trim()).length;
  $("#back-highlight-count").textContent = String(used);
}
function populate() {
  for (const el of form.elements) {
    if (!el.name) continue;
    const activeAsset = assetRef(project, activeSlot);
    el.value = el.name.startsWith("focal.")
      ? (activeAsset?.focalPoint?.[el.name.at(-1)] ?? 0.5)
      : el.name === "asset.scale"
        ? (activeAsset?.scale ?? 1)
      : el.name === "descriptors"
        ? (project.game.rating.descriptors || []).join(", ")
        : (get(el.name) ?? "");
  }
  for (const el of form.querySelectorAll("[data-feature]"))
    el.checked = project.game.features.includes(el.value);
  refreshBackCopyFields();
  $("#approved").checked = !!assetRef(project, activeSlot)?.approved;
  $("#includes-title").checked = !!project.game.artwork.front?.includesTitle;
  labels();
  render();
}
function labels() {
  $("#disc-fields").replaceChildren();
  for (const d of project.media.labels) {
    const row = document.createElement("div");
    row.className = "pair disc-row";
    const l = document.createElement("label");
    l.textContent = `Disc ${d.number} title (optional)`;
    const input = document.createElement("input");
    input.value = d.title || "";
    input.maxLength = 120;
    input.oninput = () => {
      d.title = input.value;
      render();
    };
    l.append(input);
    const purpose = document.createElement("label");
    purpose.textContent = "Purpose";
    const select = document.createElement("select");
    for (const v of ["", "install", "play", "extras", "key-media"]) {
      const o = document.createElement("option");
      o.value = v;
      o.textContent = v ? words(v) : "Automatic";
      select.append(o);
    }
    select.value = d.purpose || "";
    select.onchange = () => {
      d.purpose = select.value || undefined;
      render();
    };
    purpose.append(select);
    row.append(l, purpose);
    $("#disc-fields").append(row);
  }
}
form.onsubmit = (e) => e.preventDefault();
form.oninput = (e) => {
  const el = e.target;
  if (el.dataset.feature !== undefined)
    project.game.features = [
      ...form.querySelectorAll("[data-feature]:checked"),
    ].map((i) => i.value);
  else if (el.id === "approved") {
    if (assetRef(project, activeSlot))
      assetRef(project, activeSlot).approved = el.checked;
  } else if (el.id === "includes-title") {
    if (!project.game.artwork.front)
      project.game.artwork.front = { includesTitle: el.checked };
    else project.game.artwork.front.includesTitle = el.checked;
  } else if (el.name?.startsWith("focal.")) {
    const a = assetRef(project, activeSlot);
    if (a) {
      a.focalPoint ??= { x: 0.5, y: 0.5 };
      a.focalPoint[el.name.at(-1)] = Number(el.value);
    }
  } else if (el.name === "asset.scale") {
    const a = assetRef(project, activeSlot);
    if (a) a.scale = Number(el.value);
    $("#art-scale-value").textContent =
      Math.round(Number(el.value) * 100) + "%";
  } else if (el.name === "descriptors")
    project.game.rating.descriptors = el.value
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);
  else if (el.dataset.backItem !== undefined) syncBackHighlights();
  else if (el.name) {
    if (el.type === "number" && !el.validity.valid) return;
    set(el.name, el.type === "number" ? Number(el.value) : el.value);
    if (el.name === "media.discCount") {
      syncLabels(project);
      labels();
      discNumber = Math.min(discNumber, project.media.discCount);
    }
  }
  updateBackCopyCounters();
  render();
};
const read = (file) =>
  new Promise((resolve, reject) => {
    const r = new FileReader();
    r.onload = () => resolve(r.result);
    r.onerror = () => reject(Error("Could not read file."));
    r.readAsDataURL(file);
  });
const decode = (url) =>
  new Promise((resolve, reject) => {
    const i = new Image();
    i.onload = () => resolve(i);
    i.onerror = () => reject(Error("Could not open image."));
    i.src = url;
  });
for (const [slot, label] of Object.entries(ART_SLOTS)) {
  const option = new Option(label, slot);
  $("#art-slot").append(option);
}
$("#art-slot").onchange = (e) => {
  activeSlot = e.target.value;
  refreshArtwork();
};
function refreshArtwork() {
  const a = assetRef(project, activeSlot);
  const logo = activeSlot === "spineLogo",
    scalable = SCALABLE_ART_SLOTS.has(activeSlot),
    scaleInput = form.elements.namedItem("asset.scale"),
    scale = a?.scale ?? 1;
  $("#art-upload-label").textContent = logo
    ? "Choose game logo"
    : "Choose artwork";
  $("#art-upload-help").textContent = logo
    ? "Transparent PNG · up to 15 MB"
    : "PNG, JPG or WebP · up to 15 MB";
  $("#art").accept = logo
    ? "image/png,.png"
    : "image/png,image/jpeg,image/webp";
  $("#art-x-label").textContent = logo
    ? "Front logo position · horizontal"
    : "Focal point · horizontal";
  $("#art-y-label").textContent = logo
    ? "Front logo position · vertical"
    : "Vertical";
  $("#art-scale-control").hidden = !scalable;
  $("#art-scale-label").textContent =
    {
      front: "Front cover size",
      back: "Back cover size",
      fullWrap: "Full-wrap size",
      spineLogo: "Front logo size",
      disc: "Disc artwork size",
    }[activeSlot] || "Image size";
  scaleInput.min = logo ? "0.4" : "1";
  scaleInput.max = logo ? "1.6" : "2";
  scaleInput.value = scale;
  $("#art-scale-value").textContent = Math.round(scale * 100) + "%";
  $("#approved").checked = !!a?.approved;
  for (const k of ["x", "y"])
    form.elements.namedItem("focal." + k).value = a?.focalPoint?.[k] ?? 0.5;
  const img = images[activeSlot];
  $("#art-info").textContent = img
    ? `${ART_SLOTS[activeSlot]} · ${img.width} × ${img.height} px`
    : `No ${ART_SLOTS[activeSlot].toLowerCase()} selected.`;
  $("#remove-art").disabled = !a;
}
$("#remove-art").onclick = () => {
  ++token;
  setAssetRef(project, activeSlot, null);
  delete images[activeSlot];
  refreshArtwork();
  render();
};
$("#art").onchange = async (e) => {
  const file = e.target.files[0];
  if (!file) return;
  const t = ++token,
    slot = activeSlot;
  try {
    if (
      (slot === "spineLogo"
        ? file.type !== "image/png"
        : !["image/png", "image/jpeg", "image/webp"].includes(file.type)) ||
      file.size > 15 * 1024 * 1024
    )
      throw Error(
        slot === "spineLogo"
          ? "Choose a PNG game logo smaller than 15 MB."
          : "Choose a PNG, JPG or WebP smaller than 15 MB.",
      );
    const url = await read(file),
      img = await decode(url);
    if (t !== token) return;
    images[slot] = img;
    setAssetRef(project, slot, {
      source: "upload",
      sourceId: file.name,
      imageUrl: url,
      focalPoint: { x: 0.5, y: 0.5 },
      ...(SCALABLE_ART_SLOTS.has(slot) ? { scale: 1 } : {}),
      attribution: file.name,
      approved: false,
      ...(slot === "front"
        ? { includesTitle: $("#includes-title").checked }
        : {}),
    });
    refreshArtwork();
    render();
    $("#status").textContent =
      slot === "spineLogo"
        ? "Game logo loaded. It will be reused on the front, spine, and disc labels."
        : ART_SLOTS[slot] +
          " loaded. Review the crop and approve your selection.";
  } catch (err) {
    $("#status").textContent = err.message;
  }
  e.target.value = "";
};
$("#open-button").onclick = () => $("#open").click();
$("#open").onchange = async (e) => {
  const file = e.target.files[0];
  if (!file) return;
  const t = ++token;
  try {
    if (file.size > 150 * 1024 * 1024)
      throw Error("Project files must be smaller than 150 MB.");
    const next = parseProject(await file.text()),
      loaded = {},
      failed = [];
    await Promise.all(
      Object.keys(ART_SLOTS).map(async (slot) => {
        const url = assetRef(next, slot)?.imageUrl;
        if (!url) return;
        if (!/^data:image\/(png|jpeg|webp);base64,/.test(url)) {
          failed.push(ART_SLOTS[slot]);
          return;
        }
        try {
          loaded[slot] = await decode(url);
        } catch {
          failed.push(ART_SLOTS[slot]);
        }
      }),
    );
    if (t !== token) return;
    project = next;
    images = loaded;
    discNumber = 1;
    populate();
    refreshArtwork();
    $("#status").textContent = failed.length
      ? "Project opened. Re-upload: " + failed.join(", ") + "."
      : "Project opened with artwork and template settings.";
  } catch (err) {
    $("#status").textContent =
      err instanceof SyntaxError
        ? "This file contains invalid JSON."
        : err.message;
  }
  e.target.value = "";
};
$("#save").onclick = () => {
  const url = URL.createObjectURL(
      new Blob([JSON.stringify(project, null, 2)], {
        type: "application/json",
      }),
    ),
    a = document.createElement("a");
  a.href = url;
  a.download = `${project.game.title.replace(/[^a-z0-9]+/gi, "-") || "untitled"}-print-project.json`;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
  $("#status").textContent =
    "Project saved with uploaded artwork. Reopen it to continue editing.";
};
for (const b of document.querySelectorAll("[data-view]"))
  b.onclick = () => {
    view = b.dataset.view;
    for (const other of document.querySelectorAll("[data-view]"))
      other.setAttribute("aria-pressed", String(b === other));
    render();
  };
$("#guides").onchange = render;
$("#disc-preview").onchange = (e) => {
  discNumber = Number(e.target.value);
  render();
};
$("#zoom").onchange = () => render();
function render() {
  cancelAnimationFrame(renderFrame);
  renderFrame = requestAnimationFrame(draw);
}
function draw() {
  if (!renderer) return;
  const result = renderer.render(project, images, {
    view,
    guides: $("#guides").checked,
    discNumber,
  });
  const host = $("#canvases");
  host.replaceChildren(result.canvas);
  host.className =
    view === "disc"
      ? "discs"
      : view === "wrap"
        ? ""
        : view === "spine"
          ? "spine"
          : "single";
  host.style.width = Number($("#zoom").value) * 100 + "%";
  $("#disc-preview-label").hidden = view !== "disc";
  $("#disc-preview").replaceChildren(
    ...project.media.labels.map(
      (l) =>
        new Option(
          `Disc ${l.number} of ${project.media.discCount}`,
          String(l.number),
        ),
    ),
  );
  discNumber = Math.min(discNumber, project.media.discCount);
  $("#disc-preview").value = String(discNumber);
  $("#dimensions").textContent =
    view === "disc"
      ? "120 mm disc · 118 mm outer / " +
        (project.media.type === "cd" ? "40" : "23") +
        " mm inner print guide"
      : `${project.case.spine} case · ${project.case.content==='full-game'?'full game':'game-key media'}`;
  const problems = [
    ...projectChecks(project, images.front || images.fullWrap),
    ...result.issues,
    ...assetErrors,
  ];
  if (!images.front && images.fullWrap) {
    const idx = problems.indexOf("Approve the selected artwork.");
    if (idx >= 0 && project.game.artwork.fullWrap?.approved)
      problems.splice(idx, 1);
  }
  for (const slot of Object.keys(ART_SLOTS)) {
    const ref = assetRef(project, slot);
    if (ref?.imageUrl && !images[slot])
      problems.push(`${ART_SLOTS[slot]} could not load. Re-upload it.`);
    else if (
      images[slot] &&
      !ref?.approved &&
      !["front", "fullWrap"].includes(slot)
    )
      problems.push(`Approve ${ART_SLOTS[slot].toLowerCase()}.`);
  }
  $("#checks").replaceChildren();
  for (const message of problems.length
    ? [...new Set(problems)]
    : [
        "Content checks passed. Print calibration and export are still pending.",
      ]) {
    const li = document.createElement("li");
    li.textContent = (problems.length ? "△ " : "✓ ") + message;
    if (!problems.length) li.className = "ok";
    $("#checks").append(li);
  }
  refreshArtwork();
}
populate();
Promise.all(
  manifest.map(async (a) => {
    try {
      templateAssets[a.name] = await decode(
        templateAssetUrl(a.file),
      );
    } catch {
      assetErrors.push(
        `Template asset could not load: ${a.name}. Refresh to retry.`,
      );
    }
  }),
).then(() => {
  renderer = createRenderer(templateAssets);
  render();
});
document.fonts.ready.then(render);
