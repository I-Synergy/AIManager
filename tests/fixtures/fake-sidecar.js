"use strict";

// A fake NDJSON sidecar. Modes:
//   normal — ready, echo, slow (2 s) and cancel
//   crash  — ready, then exit on the first request
//   silent — never announce readiness (handshake timeout)

const readline = require("node:readline");

const mode = process.argv[2] || "normal";
const send = (message) => process.stdout.write(JSON.stringify(message) + "\n");

if (mode !== "silent") {
  send({ ready: true, protocol: 1, version: "fake" });
}

const lines = readline.createInterface({ input: process.stdin });
lines.on("line", (line) => {
  let request;
  try {
    request = JSON.parse(line);
  } catch {
    return;
  }

  if (request.method === "cancel") {
    send({ id: request.id, ok: true, value: { cancelled: request.params.target } });
    return;
  }

  if (mode === "crash") {
    process.exit(7);
  }

  if (request.method === "slow") {
    setTimeout(() => send({ id: request.id, ok: true, value: "slow" }), 2000);
    return;
  }

  send({ id: request.id, ok: true, value: { method: request.method, params: request.params } });
});
