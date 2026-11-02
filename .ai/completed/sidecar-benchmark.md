# Resolver port: Python host vs .NET sidecar — time and memory

Status: DONE
Date: 2026-11-02
Plan: `.ai/plans/vscode-ts-to-csharp-migration.md`

## Question

The TypeScript extension was already a thin adapter over `tooling/.ai/scripts/catalog.py`. This
migration moved that resolver into `AiWorkspaceManager.Core` (NDJSON JSON-RPC over stdio) and
deleted the Python engine. Does the C# sidecar make catalog resolution faster, and does it use
less memory?

The baseline resolver already ran as a **separate process per call**; the sidecar is a
**persistent** process. The comparison is therefore "where the work runs and how long the process
lives", not "one process vs two".

## Environment

- Windows 11 (10.0.26300) arm64 (win-arm64)
- Node.js 26.12.1
- .NET SDK 11.0.100-rc.3.26522.148; target `net8.0` with `RollForward=LatestMajor` (installed
  runtimes include 8.0.33, 9.0.22, 10.0.x and 11.0.0-rc.3; LatestMajor selects 11.0.0-rc.3)
- Python 3.14.7 with PyYAML 6.0.3 (the baseline resolver's parser)
- Build: Release for both; C# published framework-dependent to `dist/core`, Python run from the
  pre-cutover tree

## Revisions

- Baseline: `73f40b5` ("Merge pull request #13 from I-Synergy/feat/group-by-source"), extracted
  with `git show HEAD:tooling/.ai/scripts/catalog.py` (181,961 bytes).
- Current: the working tree (uncommitted migration) with `AiWorkspaceManager.Core` and `dist/core`
  built from it.
- The run below is the **third** run: after the post-review layout/interface refactor and after
  synchronous domain handlers were moved onto the thread pool (`Task.Run`) so they no longer block
  the protocol read loop. The resolver logic and workloads are unchanged.

## Workloads

- **Synthetic:** 1,000 skills, 20 agents, 10 instructions and 10 prompts = **1,040 items**;
  140,945 bytes across 1,040 source files. Frontmatter carries tags and a `requires:` chain on
  every seventh skill. A local `template` source and a minimal `Everything` preset.
- **Real fixture:** the repository's parity fixture (10 items across `template`, `labs` and
  `awesome-copilot`, including a shadowed duplicate and a nested `paths` source). The full
  I-Synergy/AI tree was not used: it has ~31k files / 2.8 GB of references, which measures file
  hashing rather than catalog resolution.

## Parity

Both implementations produce **identical** `browse` payloads on both workloads (canonical JSON
after removing the CLI-only `command` field and normalising the source root): **2/2 equal**,
1,040 items synthetic and 10 items real, re-checked for this run. The manifest/lock writers are
also byte-identical in the C# test suite (`sync.lock.json`, `add.manifest.json`,
`pin.manifest.json`).

## Time

Medians of 3 interleaved paired rounds; Python and C# alternate in each round. Python: 5 warmups,
10 measured iterations on the synthetic workload / 30 on the real fixture. C#: 5 warmups, same
iteration count. The Python time excludes `tracemalloc` (measured in a second pass).

| Workload / phase | Python in-process | C# in-process | C# end-to-end (sidecar) |
|---|---|---|---|
| Synthetic index (`build_index`) | **565.8 ms** | **313.1 ms** | — |
| Synthetic browse (full payload) | **1,134.6 ms** | **526.2 ms** | **532.0 ms steady** |
| Real fixture index | **4.09 ms** | **1.29 ms** | — |
| Real fixture browse | **10.37 ms** | **2.45 ms** | **2.56 ms steady** |

- In-process speedups: index 1.8× (synthetic) / 3.2× (real); browse 2.2× (synthetic) / 4.2×
  (real).
- End-to-end includes spawn-free steady state over the real NDJSON transport (last 20 of 50
  calls). Cold sidecar start: **78–80 ms** on this run.
- The `Task.Run` handler dispatch did **not** add measurable latency: synthetic sidecar steady
  state was 532.0 ms with the async dispatch, 530.7 ms before it; the real fixture was 2.56 ms
  vs 2.91 ms.
- Run-to-run variance: Python synthetic index measured 1,387 ms (run 1), 560 ms (run 2) and
  566 ms (run 3); C# measured 409/310/313 ms. Browse was stable at 1,135–1,153 ms (Python) and
  526–530 ms (C#). The index phase hashes 1,000 source trees, so it is sensitive to page-cache
  and machine load; report the range, not a single best case.

## Memory

| Metric | Python | C# in-process |
|---|---|---|
| Transient per op, synthetic index | 3.14 MB (tracemalloc proxy) | **22.08 MB** exact allocation |
| Transient per op, synthetic browse | 3.77 MB (proxy) | **23.82 MB** |
| Transient per op, real index | 217 KB (proxy) | 215 KB |
| Transient per op, real browse | 235 KB (proxy) | 315 KB |
| Retained after collection, synthetic | below tracemalloc noise floor (~160–219 B) | **~1.04–1.10 MB** |
| Retained after collection, real | 4.4–7.5 KB | 8.9–11.1 KB |
| Working set after the run, synthetic | 36.1–41.5 MB | 65.6–65.7 MB |
| Working set after the run, real | 41.1–41.3 MB | 42.9–44.3 MB |

The C# core allocates about **7× more** per synthetic operation than the Python proxy while being
~2× faster. The real fixture is at parity on allocation and retained bytes. The synthetic
allocation is recorded as a follow-up: `CatalogIndexBuilder.Classify` recomputes the vocabulary
set for every item, and `Browse` builds the full payload every call; neither was tuned inside this
measurement task.

## Sidecar footprint

| State | Synthetic (1,040 items) | Real (10 items) |
|---|---|---|
| Idle after handshake | 29.5 MB | 29.0 MB |
| After 50 browsed calls | 61.3 MB | 47.9 MB |
| Peak over a 200-call burst | 72.0 MB | 61.2 MB |
| Settled after the burst | 63.3 MB | 60.9 MB |
| Payload size | 647,473 bytes | 6,988 bytes |
| Host `JSON.parse`, median | 1.03 ms | 0.015 ms |
| Host heap delta while holding one payload | +1.59 MB | ~0 (below noise) |

RSS rises and then **settles** (peak 72.0 MB → 63.3 MB synthetic; 61.2 → 60.9 MB real); this is
GC heap utilisation, not a leak. A tiny 10-item payload still drives the process to ~61 MB in the
burst, so footprint is the runtime plus per-call garbage, not the document size.

## Verdict

- **Latency:** the C# core is faster everywhere measured: 1.8–3.4× in-process on the index,
  2.2× (large catalog) / 4.2× (small fixture) on browse, and 2.1× / 4.0× end-to-end after the
  ~78–80 ms cold start.
- **Memory:** it did not drop; it moved and grew. The host stops paying the parse (1.6 MB heap for
  a 647 KB payload), but the machine gains a persistent ~29 MB-idle / ~61–63 MB-loaded sidecar,
  and the C# core allocates ~7× more per operation on the 1,000-item catalog.
- **Net:** **faster but heavier** on large catalogs; at parity on the small real fixture.

## Follow-ups (not done here)

1. Reduce synthetic allocation: cache the category-id set instead of rebuilding it per item, and
   avoid rebuilding the full payload for callers that only need a summary.
2. The sidecar's ~61–63 MB settled RSS is independent of input size; if footprint matters more
   than latency, a pooled/managed cache trim or a smaller working set is the next lever.
3. Measure `sync`/`bootstrap`/`update` separately; they were not part of this run.
4. True mid-operation cancellation: the handler token is checked at dispatch boundaries, not
   inside the hashing/git loops, so a cancel prevents a successful response but does not interrupt
   in-flight work.

## What this does not cover

- VS Code renderer/webview memory; only the extension host and the sidecar are measured.
- Network-bound source fetching (`git clone`/`ls-remote`) and first-run activation.
- Realistic multi-source catalogs at the 1,000-item scale; the real fixture is deliberately small.
- A single machine; numbers are relative, not portable absolutes.

## Reproducing

The harness lives in the session scratch (`$TMPDIR/bench`), not in the repository, and is
disposable. It contains:

- `baseline/catalog.py` — extracted from `73f40b5`;
- `synth/` and the real fixture workspace;
- `cs-harness/` — a console app referencing `AiWorkspaceManager.Core.Workspace`;
- `py_bench.py` — in-process index/browse timing and memory;
- `sidecar-probe.mjs` — real NDJSON probe, RSS sampling and host-side `JSON.parse` measurement.

Commands used: `python py_bench.py <root> index|browse <n>`, `dotnet cs-harness ... bench <root>
<workload> <n>`, and `node --expose-gc sidecar-probe.mjs <dist/core dll> <root> <label>`. Delete
the scratch directory after recording these numbers.
