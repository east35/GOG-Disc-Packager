import { test } from "node:test";
import assert from "node:assert/strict";
import {
  newProject,
  parseProject,
  syncLabels,
  projectChecks,
} from "./model.js";
test("project round trip preserves embedded art and focal point", () => {
  const p = newProject();
  p.game.artwork.front = {
    source: "upload",
    sourceId: "test.png",
    imageUrl: "data:image/png;base64,AAAA",
    focalPoint: { x: 0.2, y: 0.8 },
    approved: true,
  };
  assert.deepEqual(parseProject(JSON.stringify(p)), p);
});
test("malformed project input cannot replace the current project", () => {
  for (const bad of [
    {},
    { ...newProject(), media: { type: "cd", discCount: 100, labels: [] } },
    {
      ...newProject(),
      game: { ...newProject().game, rating: { system: "bad" } },
    },
  ])
    assert.throws(() => parseProject(JSON.stringify(bad)));
  const p = newProject();
  p.game.artwork.front = { imageUrl: "test", focalPoint: { x: 2, y: 0.5 } };
  assert.throws(() => parseProject(JSON.stringify(p)));
});
test("disc count maintains numbered labels and detects incompatible cases", () => {
  const p = newProject();
  p.media.labels[0].title = "Install";
  p.media.discCount = 3;
  syncLabels(p);
  assert.deepEqual(
    p.media.labels.map((l) => l.number),
    [1, 2, 3],
  );
  assert.equal(p.media.labels[0].title, "Install");
  assert.ok(projectChecks(p, null).some((s) => s.includes("two discs")));
  p.media.discCount = 1;
  syncLabels(p);
  assert.equal(p.media.labels.length, 1);
  assert.ok(!projectChecks(p, null).some((s) => s.includes("two discs")));
});
test("low resolution and missing artwork approval are reported", () => {
  const p = newProject();
  p.game.artwork.front = { approved: false };
  const issues = projectChecks(p, { width: 300, height: 400 });
  assert.ok(issues.some((s) => s.includes("DPI")));
  assert.ok(issues.some((s) => s.includes("Approve")));
});
