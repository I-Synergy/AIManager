"use strict";

// The host API uses PascalCase; sidecar handlers use camelCase. These tests pin the translation
// so a parameter rename cannot silently make install/sync return invalidParams.

const test = require("node:test");
const assert = require("node:assert/strict");

const { coreParams } = require("../out-test/AiWorkspaceManager.VSCode.Host/core-params.js");

test("getBrowse uses camelCase refresh", () => {
  assert.deepEqual(coreParams.getBrowse(undefined), { refresh: false });
  assert.deepEqual(coreParams.getBrowse({ Refresh: true }), { refresh: true });
});

test("describeProject, upgrade and addPreset map their fields", () => {
  assert.deepEqual(
    coreParams.describeProject({ Name: "Payments", Intention: "Track invoices" }),
    { name: "Payments", intention: "Track invoices" });
  assert.deepEqual(coreParams.upgrade({ Release: "v2" }), { release: "v2" });
  assert.deepEqual(coreParams.addPreset({ Name: "Everything" }), { name: "Everything" });
});

test("selection operations map ids and flags", () => {
  assert.deepEqual(coreParams.install({ Ids: ["alpha"] }), { ids: ["alpha"] });
  assert.deepEqual(coreParams.remove({ Ids: ["alpha"], Prune: true }), { ids: ["alpha"], prune: true });
  assert.deepEqual(coreParams.setLocked({ Ids: ["alpha"], Locked: true }), { ids: ["alpha"], locked: true });
  assert.deepEqual(coreParams.updateItems(undefined), { ids: [], force: false });
  assert.deepEqual(coreParams.updateItems({ Ids: ["alpha"], Force: true }), { ids: ["alpha"], force: true });
});

test("clients and vendoring map their fields", () => {
  assert.deepEqual(coreParams.setClients({ Clients: ["claude", "copilot"] }), { clients: ["claude", "copilot"] });
  assert.deepEqual(coreParams.vendorTooling(undefined), { overwrite: false });
  assert.deepEqual(coreParams.vendorTooling({ Overwrite: true }), { overwrite: true });
});
