# Plan: convert AI Workspace Manager to a thin Node host + .NET sidecar

**Status:** COMPLETE
**Date:** 2026-11-02
**Skills:** `D:\Projects\Github\AI\.ai\skills\vscode-ts-to-csharp-migration`,
`D:\Projects\Github\AI\.ai\skills\vscode-csharp-sidecar-benchmark`

## What this can and cannot be

VS Code loads a Node module; there is no .NET binding for the extension API. The host therefore
stays TypeScript and keeps activation, commands, TreeView, webview/`postMessage`, settings,
workspace trust and `context.subscriptions`. Everything else moves behind one NDJSON JSON-RPC
sidecar launched as `dotnet AiWorkspaceManager.Core.dll`.

The domain logic of this extension is **not** in TypeScript. `src/AiWorkspaceManager.VSCode.Host/host-api.ts` is already a
thin adapter; the engine is `tooling/.ai/scripts/catalog.py` (4,515 lines) plus
`tooling/.ai/scripts/sync-platforms.py` (235 lines), with 2,645 lines of pytest. The port replaces
that engine. Decision (user, 2026-11-02): **replace it fully** — delete `catalog.py`, the sync
helpers and the pytest resolver suite; ship the C# core in the VSIX.

## Inventory

`scripts/inventory.mjs src/`:

- 22 TS modules / 4,567 lines; 16 movable modules / 3,614 lines; 3 modules import `vscode`.
- The movable TS surface is almost entirely the Lit webview (3,273 lines) — it stays TypeScript,
  it is UI. The only movable host module is `host/cli.ts` (74 lines of spawn plumbing).
- The real port surface is the Python resolver: **4,750 lines** across `catalog.py` and
  `sync-platforms.py`, plus **2,645 lines** of tests.

## Boundary rules

1. **One RPC per user-level operation** — `getBrowse`, `getUpdates`, `sync`, `install`,
   `bootstrap`, `vendorTooling` — never `classify`, `compareVersions` or `itemHash`.
2. **The webview contract does not change.** `src/AiWorkspaceManager.VSCode.Common/types.ts` stays the contract; the sidecar
   emits those shapes in camelCase. `Bootstrap`/`VendorSummary` keep their existing PascalCase
   fields because the host maps them.
3. **The host keeps what only the host can do**: activation, `vscode.workspace`/settings, output
   channel, webview, commands, TreeView, workspace trust, child-process spawning not owned by the
   sidecar. The sidecar never imports `vscode`.
4. **No dual implementation.** The Python engine and its pytest suite are deleted in the cutover
   PR; the sidecar is the only resolver.
5. **Primitives move with their consumers.** The JSON writers, manifest/lock semantics and
   selection rules move too — they are the domain, not host primitives.
6. **Long operations stay request/response** where they already are; install/update/sync are
   bounded by the existing 10-minute host timeout. Cancellation is passed through `AbortSignal`.

## Target layout

```
AIWorkspaceManager.slnx
Directory.Build.props / Directory.Packages.props / .editorconfig (root policy)
src/AiWorkspaceManager.Common/          kind/source/status constants, ids, JSON helpers
src/AiWorkspaceManager.Core.Catalog/    frontmatter, hashing, index, classification, presets,
                                        categories, semver, release rows
src/AiWorkspaceManager.Core.Workspace/  paths, manifest, lock, selection, closure, status,
                                        materialization, sync/update, tooling, scaffold,
                                        project info, instruction docs, platform mirrors, git
src/AiWorkspaceManager.Core/            Program.cs, Protocol/, Handlers/
tests/AiWorkspaceManager.Core.Tests/
tests/AiWorkspaceManager.Core.Catalog.Tests/
tests/AiWorkspaceManager.Core.Workspace.Tests/
tests/AiWorkspaceManager.TestUtilities/
```

`net8.0`, framework-dependent, `UseAppHost=false`, `RollForward=LatestMajor`; MSTest only.
Published to `dist/core` by `npm run build:core` and included in `vscode:prepublish`.

## Waves

| Wave | Domain | Replaces | Exit criterion |
|---|---|---|---|
| A | transport + shared wire types | — | handshake/ping/cancel/error tests; audit clean |
| B | catalog index | `frontmatter`, `item_files`/`item_hash`, `build_index`, `classify`, categories/presets, semver/release rows | golden index over fixtures identical to Python; unit tests |
| C | selection semantics | manifest/lock readers+writers, `_selection`, `_closure`, `evaluate_status`, browse/update payloads | parity harness green on frozen fixtures |
| D | remote sources | `fetch_source`, `remote_tags`, `release_row`, `_source_root_for` | file:// git fixtures cover clone/fetch/tags/offline |
| E | materialization + mutations | link/copy, prune, sync, update, lock/unlock, add/remove, init | byte-identical manifest/lock and tree against Python on fixtures |
| F | setup + maintenance | tooling install, scaffold, agent config, project info, clients, notice, index, platform mirrors | pytest cases ported to MSTest |
| G | cutover | host client, host-api delegation, packaging, delete Python | `npm run quality`, VSIX contains `dist/core` |

Sequencing per wave: implement in C# → MSTest + parity fixture → wire behind the host adapter →
run the gate → delete the Python slice. Because the Python engine is one file, the intermediate
waves keep it only as the parity oracle (run from `$TMPDIR`, not shipped); the cutover deletes it.

## Deliberate remainders

- **Webview (3,214 lines)** — Lit UI; `vscode`/DOM concerns, not domain logic.
- **`extension.ts` / `panel.ts` / `rpc-host.ts` / `rpc-client.ts`** — activation, webview lifecycle,
  CSP/nonce, `postMessage` RPC. A sidecar cannot reach any of it.
- **`vendorTooling` child process** — the host still spawns the installer for the C# core bundle;
  the sidecar returns the summary. Spawning and trust stay on the host.
- **`git` executable** — invoked by the sidecar (as Python did); on `PATH`, no credentials or
  proxy handling exists in this extension.

## Parity strategy

1. Freeze the Python resolver at the pre-cutover revision (git `HEAD`) and run it from
   `$TMPDIR` to generate golden JSON for: `browse`, `presets`, `update`, `release`, `status`,
   `list`, `index`, `project`, `bootstrap`, `scaffold`, `sync --check`, `notice`.
2. Fixtures are real: a local template checkout, a nested `paths` source, a `file://` git release
   repo, manifests with presets/locks/excludes, symlinked skill content.
3. C# tests compare byte-for-byte where the output is a file (manifest, lock, index, NOTICE,
   AGENTS.md), and field-for-field where it is a payload.
4. Write operations compare the resulting tree and the manifest/lock bytes; the TypeScript host
   tests stub `CallAsync` and assert `(method, params, ordering)`.

## Benchmark

After cutover, per `vscode-csharp-sidecar-benchmark`:

- **M1** `build_index` + `browse` in Python (in-process, and through the CLR-free CLI) vs C# core
  vs end-to-end over the real NDJSON transport.
- **M2** allocation per operation (`tracemalloc` / `GC.GetTotalAllocatedBytes`).
- **M3** retained after 30 cycles (post-GC).
- **M4** sidecar RSS idle/50 calls/200-call burst; Python process RSS for comparison.
- Synthetic catalog (1,000 items, ~130 KiB of markdown) + the real `tooling/` catalog fixture.
- Interleaved rounds, 5 warmups + 30 iterations, medians; report both directions of the trade.

## Post-review layout

The sidecar is split one top-level type per file and organised by responsibility:

- `src/AiWorkspaceManager.Common/{Configuration,Identifiers,Errors,Host}`
- `src/AiWorkspaceManager.Core.Catalog/{Models,Parsing,Hashing,Indexing,Vocabulary,Releases,Payloads}`
- `src/AiWorkspaceManager.Core.Workspace/{Abstractions,Paths,Json,Manifest,Selection,Payloads,Sources,Operations,Setup,Project,Instructions,Platforms,Services,Maintenance}`
- `src/AiWorkspaceManager.Core/{Protocol,Handlers}` + `Program.cs`

The guardrail audit is clean (0 failures, 0 warnings). Namespaces stay project-scoped
(`AiWorkspaceManager.Core.Workspace`, …); folders group by responsibility, and the TypeScript side
has no namespaces.

`scripts/sync-tooling.mjs` deliberately stays Node: it is an npm build step that snapshots the
vocabulary before esbuild runs, not resolver logic. The runtime vendoring path moved into the
sidecar (`vendorTooling`), so `scripts/install-tooling.mjs` is gone.
`out-test/` (the stale compiled test tree) and the pytest resolver suite are removed.

## Non-goals

- No webview rewrite (Blazor/React) in this change.
- No new UI features; no change to `HostAPI` shapes.
- No per-RID/self-contained packaging; no trimming/R2R (measured as losses).
