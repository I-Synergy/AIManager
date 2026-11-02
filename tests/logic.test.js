"use strict";

// Pure-logic tests: no DOM, no Lit, no RPC.
const test = require("node:test");
const assert = require("node:assert/strict");

const logic = require("../out-test/AiWorkspaceManager.VSCode.Webview/logic.js");

function item(overrides) {
  return {
    id: "template:alpha",
    displayId: "alpha",
    kind: "skill",
    name: "alpha",
    path: ".ai/skills/alpha",
    description: "alpha skill",
    source: "template",
    ref: "v1",
    license: "MIT",
    upstream: "https://example/alpha",
    sha256: "a".repeat(64),
    files: 2,
    selected: false,
    status: "available",
    installed: false,
    localModified: false,
    upstreamChanged: false,
    ...overrides,
  };
}

const filters = { query: "", kind: "all", state: "all", source: "all" };

test("filterItems matches id, kind, path and description", () => {
  const items = [
    item({}),
    item({ id: "template:arch", displayId: "arch", kind: "agent", name: "arch", path: ".ai/agents/arch.md", description: "an architecture agent" }),
  ];
  assert.deepEqual(logic.filterItems(items, { ...filters, query: "alpha" }).map((i) => i.displayId), ["alpha"]);
  assert.deepEqual(logic.filterItems(items, { ...filters, query: "AGENT" }).map((i) => i.displayId), ["arch"]);
  assert.equal(logic.filterItems(items, { ...filters, query: "   " }).length, 2);
});

test("filterItems honours kind, source and state", () => {
  const items = [
    item({}),
    item({ id: "acme:zeta", displayId: "zeta", source: "acme", selected: true, installed: true, status: "ok" }),
    item({ id: "template:beta", displayId: "beta", status: "local-modified", selected: true, installed: true }),
  ];
  assert.deepEqual(logic.filterItems(items, { ...filters, kind: "agent" }), []);
  assert.deepEqual(logic.filterItems(items, { ...filters, source: "acme" }).map((i) => i.displayId), ["zeta"]);
  assert.deepEqual(logic.filterItems(items, { ...filters, state: "installed" }).map((i) => i.displayId), ["zeta", "beta"]);
  assert.deepEqual(logic.filterItems(items, { ...filters, state: "available" }).map((i) => i.displayId), ["alpha"]);
  assert.deepEqual(logic.filterItems(items, { ...filters, state: "needs-attention" }).map((i) => i.displayId), ["beta"]);
});

test("idsToInstall is exactly the unselected items", () => {
  const items = [item({}), item({ selected: true, status: "ok" }), item({ id: "template:b", displayId: "b" })];
  assert.deepEqual(logic.idsToInstall(items), ["template:alpha", "template:b"]);
});

test("groupInstalled splits attention, pending, locked and in-sync", () => {
  const items = [
    item({ selected: true, status: "ok" }),
    item({ id: "template:b", displayId: "b", selected: true, status: "missing" }),
    item({ id: "template:c", displayId: "c", selected: true, status: "not-installed" }),
    item({ id: "template:d", displayId: "d", selected: true, status: "locked" }),
    item({ id: "template:e", displayId: "e", selected: false }),
  ];
  const groups = logic.groupInstalled(items);
  assert.deepEqual(groups.map((g) => g.key), ["attention", "pending", "locked", "synced"]);
  assert.deepEqual(groups[0].items.map((i) => i.displayId), ["b"]);
  assert.deepEqual(groups[2].items.map((i) => i.displayId), ["d"]);
  assert.deepEqual(groups[3].items.map((i) => i.displayId), ["alpha"]);
});

test("groupInstalled omits empty groups", () => {
  const groups = logic.groupInstalled([item({ selected: true, status: "ok" })]);
  assert.deepEqual(groups.map((g) => g.key), ["synced"]);
});

test("status labels separate deselected from gone upstream", () => {
  assert.equal(logic.statusLabel("ok"), "in sync");
  assert.equal(logic.statusLabel("removed-upstream", false), "deselected");
  assert.equal(logic.statusLabel("removed-upstream", true), "gone upstream");
  assert.equal(logic.statusLabel("upstream-changed"), "update");
  assert.equal(logic.statusLabel("locked"), "locked");
});

test("status classes drive the pill colours", () => {
  assert.equal(logic.statusClass("ok"), "ok");
  assert.equal(logic.statusClass("both"), "danger");
  assert.equal(logic.statusClass("local-modified"), "warn");
  assert.equal(logic.statusClass("upstream-changed"), "update");
  assert.equal(logic.statusClass("locked"), "available");
});

test("presetDelta only counts what adding the preset would change", () => {
  const card = {
    name: "p", description: "", skills: 3, agents: 1,
    ids: ["template:a", "template:b", "template:c", "template:lead"],
  };
  assert.deepEqual(logic.presetDelta(card, new Set(["template:a"])),
    { add: 3, label: "3 to add" });
  // Extra selected items are kept: presets compose, so nothing is "to remove".
  assert.equal(logic.presetDelta(card, new Set([...card.ids, "template:extra"])).label,
    "already selected");
  // A swapped set has the same count but still adds the missing id.
  assert.equal(logic.presetDelta(card, new Set(["template:a", "template:b", "template:c", "template:other"])).add, 1);
});

test("presetDelta falls back to counts for older resolvers", () => {
  assert.equal(logic.presetDelta(
    { name: "p", description: "", skills: 9, agents: 1 },
    new Set(),
    { skills: 0, agents: 0 },
  ).add, 10);
  assert.equal(logic.presetDelta(
    { name: "p", description: "", skills: 2, agents: 2 },
    new Set(["template:a"]),
    { skills: 2, agents: 2 },
  ).label, "already selected");
});

test("isAttention ignores healthy and merely-available items", () => {
  assert.equal(logic.isAttention("ok"), false);
  assert.equal(logic.isAttention("available"), false);
  assert.equal(logic.isAttention("not-installed"), false);
  assert.equal(logic.isAttention("locked"), false);
  for (const status of ["missing", "local-modified", "upstream-changed", "both", "removed-upstream"]) {
    assert.equal(logic.isAttention(status), true, status);
  }
});

test("pluralise is grammatical", () => {
  assert.equal(logic.pluralise(1, "item"), "1 item");
  assert.equal(logic.pluralise(0, "item"), "0 items");
  assert.equal(logic.pluralise(2, "skill"), "2 skills");
});

test("displayId strips only the template namespace", () => {
  assert.equal(logic.displayId("template:council"), "council");
  assert.equal(logic.displayId("acme:zeta"), "acme:zeta");
  assert.equal(logic.displayId("council"), "council");
});

test("itemKey keeps an agent and a skill with the same name apart", () => {
  const agent = { kind: "agent", id: "template:architect" };
  const skill = { kind: "skill", id: "template:architect" };
  assert.notEqual(logic.itemKey(agent), logic.itemKey(skill));
  assert.equal(logic.itemKey(skill), "skill:template:architect");
});

test("groupByCategory follows the vocabulary order and drops empty groups", () => {
  const categories = [
    { id: "architecture", label: "Architecture & design" },
    { id: "testing", label: "Testing" },
    { id: "mobile", label: "Mobile & MAUI" },
  ];
  const items = [
    item({ id: "template:t1", displayId: "t1", category: "testing" }),
    item({ id: "template:a1", displayId: "a1", category: "architecture" }),
    item({ id: "template:t2", displayId: "t2", category: "testing" }),
  ];

  const groups = logic.groupByCategory(items, categories);

  assert.deepEqual(groups.map((group) => group.id), ["architecture", "testing"]);
  assert.deepEqual(groups[1].items.map((entry) => entry.displayId), ["t1", "t2"]);
  assert.equal(groups[1].label, "Testing");
});

test("groupByCategory appends categories the vocabulary does not know", () => {
  const groups = logic.groupByCategory(
    [item({ category: "mobile" }), item({ id: "x", displayId: "x", category: "zzz-unknown" })],
    [{ id: "mobile", label: "Mobile & MAUI" }],
  );
  assert.deepEqual(groups.map((group) => group.id), ["mobile", "zzz-unknown"]);
  assert.equal(groups[1].label, "zzz-unknown");
});

test("groupBySource follows the manifest order and labels the template source", () => {
  const items = [
    item({ id: "awesome:a", displayId: "a", source: "awesome-copilot" }),
    item({}),
    item({ id: "awesome:b", displayId: "b", source: "awesome-copilot" }),
  ];

  const groups = logic.groupBySource(items, ["template", "awesome-copilot"]);

  assert.deepEqual(groups.map((group) => group.id), ["template", "awesome-copilot"]);
  assert.equal(groups[0].label, "I-Synergy");
  assert.equal(groups[1].label, "awesome-copilot");
  assert.deepEqual(groups[1].items.map((entry) => entry.displayId), ["a", "b"]);
});

test("groupBySource appends unknown sources and keeps first-seen order without a list", () => {
  const ordered = logic.groupBySource(
    [
      item({ id: "zeta:z", displayId: "z", source: "zeta" }),
      item({}),
      item({ id: "acme:a", displayId: "a", source: "acme" }),
    ],
    ["acme"],
  );
  assert.deepEqual(ordered.map((group) => group.id), ["acme", "template", "zeta"]);

  const asSeen = logic.groupBySource([
    item({ id: "zeta:z", displayId: "z", source: "zeta" }),
    item({}),
    item({ id: "acme:a", displayId: "a", source: "acme" }),
  ]);
  assert.deepEqual(asSeen.map((group) => group.id), ["zeta", "template", "acme"]);
});

/* ── multi-select ─────────────────────────────────────────────────────────── */

test("pickState reports none, some and all", () => {
  assert.equal(logic.pickState([], []), "none");
  assert.equal(logic.pickState(["a", "b"], []), "none");
  assert.equal(logic.pickState(["a", "b"], ["a"]), "some");
  assert.equal(logic.pickState(["a", "b"], ["a", "b"]), "all");
  // picks outside the visible set must not make a filtered list look complete
  assert.equal(logic.pickState(["a", "b"], ["a", "b", "z"]), "all");
  assert.equal(logic.pickState(["a", "b"], ["z"]), "none");
});

test("toggleAll selects and clears only the visible rows", () => {
  assert.deepEqual(logic.toggleAll(["a", "b"], [], true), ["a", "b"]);
  assert.deepEqual(logic.toggleAll(["a", "b"], ["z"], true), ["z", "a", "b"]);
  assert.deepEqual(logic.toggleAll(["a", "b"], ["a", "b", "z"], false), ["z"]);
  assert.deepEqual(logic.toggleAll([], ["z"], true), ["z"]);
});

test("pickedIds keeps only ids the action applies to", () => {
  const items = [
    item({ id: "template:alpha", displayId: "alpha", selected: false }),
    item({ id: "template:beta", displayId: "beta", selected: true }),
  ];
  assert.deepEqual(logic.pickedIds(items, ["template:alpha", "template:beta"], false), ["template:alpha"]);
  assert.deepEqual(logic.pickedIds(items, ["template:alpha", "template:beta"], true), ["template:beta"]);
  assert.deepEqual(logic.pickedIds(items, ["template:gone"], false), []);
});

/* ── releases ─────────────────────────────────────────────────────────────── */

function release(overrides) {
  return {
    id: "template",
    repo: "I-Synergy/AI",
    ref: "v1.6.0",
    pinnedTag: true,
    current: "v1.6.0",
    latest: "v1.7.0",
    latestIsPrerelease: false,
    newer: true,
    tags: ["v1.7.0", "v1.6.0"],
    tagCount: 2,
    checked: true,
    error: null,
    ...overrides,
  };
}

test("releaseSource prefers a source that is behind, then any published release", () => {
  assert.equal(logic.releaseSource([]), undefined);
  const behind = release({ id: "other" });
  assert.equal(logic.releaseSource([release({ newer: false }), behind]), behind);
  assert.equal(logic.releaseSource([release({ checked: false })]), undefined);
  // An unpinned source still has a published release worth reporting.
  const unpinned = release({ pinnedTag: false, current: null, newer: false });
  assert.equal(logic.releaseSource([unpinned]), unpinned);
  assert.equal(logic.releaseSource([release({ checked: true, latest: null })]), undefined);
});

test("releaseSummary never makes unknown look up to date", () => {
  assert.equal(logic.releaseSummary(undefined), "—");
  assert.equal(logic.releaseSummary(release({ checked: false, error: "offline" })), "offline");
  assert.equal(logic.releaseSummary(release({ checked: false, error: "boom" })), "unknown");
  assert.equal(logic.releaseSummary(release({ checked: true, latest: null })), "no tags");
  assert.equal(logic.releaseSummary(release({ pinnedTag: false, current: null })), "upstream v1.7.0");
  assert.equal(logic.releaseSummary(release()), "v1.6.0 → v1.7.0");
  assert.equal(logic.releaseSummary(release({ newer: false })), "v1.7.0 (current)");
});

test("sourceRefLabel spells the local sentinel out", () => {
  assert.equal(logic.sourceRefLabel("local"), "working tree");
  assert.equal(logic.sourceRefLabel("main"), "main");
  assert.equal(logic.sourceRefLabel("2026.11027.12350"), "2026.11027.12350");
});

test("releaseNotice reports newer and unpinned releases honestly", () => {
  assert.equal(logic.releaseNotice(undefined), null);
  assert.equal(logic.releaseNotice(release({ current: "v1.7.0", newer: false })), null);
  assert.equal(logic.releaseNotice(release({ checked: false, error: "offline" })), null);

  const notice = logic.releaseNotice(release());
  assert.equal(notice.kind, "newer");
  assert.match(notice.label, /A newer template release exists: v1\.7\.0/);
  assert.match(notice.detail, /pins v1\.6\.0/);
  assert.match(notice.detail, /v1\.7\.0/);

  assert.match(
    logic.releaseNotice(release({ latestIsPrerelease: true })).label,
    /\(prerelease\)$/
  );

  // Unpinned: we know what upstream publishes, not what this tree matches.
  const unpinned = logic.releaseNotice(
    release({ pinnedTag: false, current: null, ref: "local", newer: false })
  );
  assert.equal(unpinned.kind, "unpinned");
  assert.match(unpinned.label, /I-Synergy release v1\.7\.0 is published/);
  assert.match(unpinned.detail, /own tree/);
});

test("emptyUpdateNotice will not claim up to date for a local selection", () => {
  assert.equal(logic.emptyUpdateNotice(false).heading, "Nothing to compare");
  assert.match(logic.emptyUpdateNotice(false).hint, /own tree/);
  assert.equal(logic.emptyUpdateNotice(true).heading, "Everything is up to date");
});

/* ── installed filters ────────────────────────────────────────────────────── */

function owned(overrides) {
  return item({ selected: true, installed: true, status: "ok", ...overrides });
}

test("filterInstalled only ever returns selected items", () => {
  const items = [owned({}), item({ id: "template:beta", displayId: "beta" })];
  assert.deepEqual(logic.filterInstalled(items, filters).map((i) => i.displayId), ["alpha"]);
});

test("filterInstalled filters by kind, source, query and health", () => {
  const items = [
    owned({}),
    owned({ id: "template:arch", displayId: "arch", kind: "agent", name: "arch",
      path: ".ai/agents/arch.md", description: "arch agent", status: "not-installed" }),
    owned({ id: "awesome-copilot:zeta", displayId: "zeta", name: "zeta",
      path: "skills/zeta", description: "zeta", source: "awesome-copilot",
      status: "upstream-changed" }),
    owned({ id: "template:frozen", displayId: "frozen", name: "frozen",
      path: ".ai/skills/frozen", description: "frozen", status: "locked" }),
  ];
  const f = (over) => logic.filterInstalled(items, { ...filters, ...over }).map((i) => i.displayId);
  assert.deepEqual(f({ kind: "agent" }), ["arch"]);
  assert.deepEqual(f({ source: "awesome-copilot" }), ["zeta"]);
  assert.deepEqual(f({ query: "ZETA" }), ["zeta"]);
  assert.deepEqual(f({ state: "in-sync" }), ["alpha"]);
  assert.deepEqual(f({ state: "needs-attention" }), ["zeta"]);
  assert.deepEqual(f({ state: "locked" }), ["frozen"]);
  assert.deepEqual(f({ state: "pending" }), ["arch"]);
});

test("filterUpdates keeps the ready/conflict split and honours filters", () => {
  const ready = [
    { id: "template:alpha", displayId: "alpha", kind: "skill", status: "upstream-changed", path: "a", source: "template" },
    { id: "awesome-copilot:zeta", displayId: "zeta", kind: "skill", status: "not-installed", path: "z", source: "awesome-copilot" },
  ];
  const blocked = [
    { id: "template:arch", displayId: "arch", kind: "agent", status: "both", path: "b", source: "template" },
  ];
  const f = (over) => logic.filterUpdates(ready, blocked, { ...filters, ...over });
  assert.deepEqual(f({}).ready.map((r) => r.displayId), ["alpha", "zeta"]);
  assert.deepEqual(f({}).blocked.map((r) => r.displayId), ["arch"]);
  assert.deepEqual(f({ state: "ready" }).blocked, []);
  assert.deepEqual(f({ state: "conflict" }).ready, []);
  assert.deepEqual(f({ kind: "agent" }).ready, []);
  assert.deepEqual(f({ source: "awesome-copilot" }).blocked, []);
  assert.deepEqual(f({ query: "B" }).blocked.map((r) => r.displayId), ["arch"]);
});
