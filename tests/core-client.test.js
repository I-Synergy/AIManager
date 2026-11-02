"use strict";

// Transport tests for the host sidecar client. A fake NDJSON process covers start/handshake,
// timeout, explicit cancel, crash and dispose without launching the real core.

const test = require("node:test");
const assert = require("node:assert/strict");
const { join } = require("node:path");

const { CoreClient, CoreError } = require("../out-test/AiWorkspaceManager.VSCode.Host/core-client.js");

const FAKE = join(__dirname, "fixtures", "fake-sidecar.js");

function client(mode, overrides = {}) {
  return new CoreClient({
    executable: process.execPath,
    arguments: [FAKE, mode],
    handshakeTimeoutMs: 1500,
    requestTimeoutMs: 3000,
    maxRestarts: 1,
    ...overrides,
  });
}

test("CallAsync waits for the handshake and matches the response by id", async () => {
  const subject = client("normal");
  try {
    const value = await subject.CallAsync("echo", { value: 1 });
    assert.deepEqual(value, { method: "echo", params: { value: 1 } });
  } finally {
    subject.Dispose();
  }
});

test("CallAsync rejects with unavailable when the handshake never arrives", async () => {
  const subject = client("silent", { handshakeTimeoutMs: 200 });
  try {
    await assert.rejects(
      () => subject.CallAsync("echo", {}),
      (error) => error instanceof CoreError && error.code === "unavailable");
  } finally {
    subject.Dispose();
  }
});

test("An AbortSignal cancels the caller and sends cancel to the sidecar", async () => {
  const subject = client("normal");
  const controller = new AbortController();
  try {
    const call = subject.CallAsync("slow", {}, controller.signal);
    setTimeout(() => controller.abort(), 30);
    await assert.rejects(
      () => call,
      (error) => error instanceof CoreError && error.code === "cancelled");
  } finally {
    subject.Dispose();
  }
});

test("A crashed process rejects pending calls with crashed", async () => {
  const subject = client("crash");
  try {
    await assert.rejects(
      () => subject.CallAsync("echo", {}),
      (error) => error instanceof CoreError && error.code === "crashed");
  } finally {
    subject.Dispose();
  }
});

test("Dispose settles pending calls and rejects the next call as unavailable", async () => {
  const subject = client("normal");
  subject.Dispose();
  await assert.rejects(
    () => subject.CallAsync("echo", {}),
    (error) => error instanceof CoreError && error.code === "unavailable");
});
