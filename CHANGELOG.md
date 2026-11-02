# Changelog

## Unreleased

- **The Updates tab only refreshes the sources it needs.** It first reads the cached index to find the selected items, then refreshes just those sources. A refresh now runs `git fetch --depth 1` in the existing checkout instead of deleting it and cloning again, so a moving branch pays for changed objects only. Progress is logged at info level (`core → getUpdates`, `refreshing <source>`, `catalog index: …`, `update plan: …`), which makes a slow remote visible in the **AI Workspace Manager** output channel.
- **Layout follows the project names.** `src/AiWorkspaceManager.VSCode.{Host,Common,Webview}` replaces `src/{host,common,web}`, the Node test moved into `tests/`, `out-test/` is removed, and the interfaces live in `src/AiWorkspaceManager.Common/Abstractions/`. The runtime `vendorTooling` command now runs in the C# sidecar; `scripts/sync-tooling.mjs` remains a build-only Node step.
- **The resolver is now a .NET sidecar.** `catalog.py`, `sync-platforms.py` and the pytest resolver suite are gone; the extension host is a thin TypeScript adapter over an NDJSON JSON-RPC sidecar published to `dist/core`. Browse, Installed, Updates, Presets, Settings, sync/update/lock/prune, bootstrap, project info, clients, notice and index all run in C#. The webview contract is unchanged, the manifest/lock/index writers are byte-identical to the Python resolver on the shared fixtures, and `npm run test:core` replaces `npm run test:resolver`.
- The `aiManager.pythonPath` setting, the interpreter field in Settings and the `setPythonPath` RPC are removed; Settings now reports resolver/core status instead.
- `vscode:prepublish` publishes the framework-dependent core (`npm run build:core`), the CI job installs .NET and runs `dotnet test`, and the VSIX size guard moves from 2 MB to 16 MB to fit the ~4.5 MB core.
- **A bare id in `.ai/ai.json` now resolves to the item when only one source ships that name.** Ids from the template source are written bare (`council`), so a bare name used to always mean the template: a manifest that listed an item from another source without its prefix failed every sync with `unknown id`, hiding the rest of the run. Selection, `exclude` and `locked` entries now heal against the catalog when the template does not offer the name and exactly one other source does; an ambiguous name still fails, so a typo is not guessed. This is what the identity repository hit for its MAUI and MAUI-tooling skills.
- **An item with a symlinked directory no longer re-installs on every sync.** `aspire` (and three of its siblings) link `evals/fixtures` to the repository's shared fixtures; a copy install dereferences that link, but hashing skipped it, so the installed copy could never match the catalog and `sync` reported the same four updates forever. The hash now walks symlinked directories the way the copy does, guarded against a link cycle, and `sync --check` settles.

- **Installed and Updates are grouped by source as well as by state.** Each section (Needs attention / Selected, not materialised / Locked / In sync, and Ready to update / Conflicts / Locked) now lists its items under the source they came from, in the manifest's source order, so provenance is visible without opening a row. The source filter labels the `template` bucket as **I-Synergy** consistently across Browse, Installed and Updates.
- **Releases use the same automatic versioning as the AI repository.** A push to `main` derives a UTC build number (`YYYY.1MMdd.1HHmm`) and the matching `VersionInformational`/`VersionPackage` values (`-preview` on pull requests), uses `VersionPackage` as the extension version, publishes to the Marketplace, tags the build number and creates a GitHub Release with generated notes. Version-bump commits, the `Release` workflow and `tools/release.mjs` are gone; pull requests build and test without publishing.
- The README publishing section documents the automatic flow, and `RELEASE_NOTES.md` is no longer required.
- **Fixed the webview hanging on "Reading the catalog…".** The RPC client dropped every host response because it compared `event.source` with `window.parent`, and the VS Code webview preload rewrites `window.parent` to point at the webview itself. Responses are now accepted from the webview's own origin only; the UI no longer waits out the 10-minute RPC timeout.
- **Diagnostics now show up in VS Code.** A timestamped **AI Workspace Manager** output channel records activation, template setup, every `catalog.py` invocation with its duration and exit status, RPC failures, and webview warnings/errors (console and uncaught exceptions) forwarded from the panel. A timed-out RPC call is logged too, instead of only surfacing after ten minutes in the webview.
- The SonarCloud project is keyed `I-Synergy_AIWorkspaceManager` (it was `I-Synergy_AIManager`), matching the repository name; the README badge links follow the new key.

## 0.13.2 (2026-10-30)

- README now shows the project status at a glance: CI, GitHub release, VS Marketplace version/installs/downloads/rating, SonarCloud security, maintainability, reliability and duplication ratings, and the MIT licence.

## 0.13.1 (2026-10-30)

- **Security hardening from the SonarCloud gate.** CI installs with `npm ci --ignore-scripts` and `npx --yes --ignore-scripts`, Python test dependencies are pinned with SHA-256 hashes (`--require-hashes`), and the GitHub release action is pinned to a full commit SHA.
- `scripts/install-tooling.mjs` validates every destination against the target repository root, and `tools/dev-server.mjs` rejects path traversal outside `dist/`, `fixtures/` and `tools/`.
- `tools/release.mjs` no longer shells out to `npm`; it updates `package.json` and `package-lock.json` in-process with `execFileSync` and no shell, removing the command-injection surface.
- The webview RPC client accepts responses only from the embedding host: it checks both `event.source` and `event.origin`.
- Cleared the remaining maintainability findings: nested ternaries, duplicate imports, unused `async`, non-readonly Lit statics, redundant `void` and `String.fromCharCode`.
- Shared the resolver test loader/CLI/fixtures in `conftest.py` and `resolver_helpers.py`, removing all duplicated test blocks (SonarCloud duplication 1.5% → 0%).
- SonarCloud result: **79 open issues → 0**, **17 vulnerabilities → 0**, **0 duplicated lines**.

## 0.13.0 (2026-10-30)

- Presets now compose instead of replacing each other. `.ai/ai.json` gains a `presets` array (the old `preset` string is still read and is folded into the array on the next write); `init --preset` keeps its replace-the-selection bootstrap behaviour, while the new `catalog.py preset add|remove` edits the list additively. Explicit `skills`/`agents` picks and `exclude` entries survive both operations.
- The Presets tab uses the additive command: each card shows an **applied** badge, an **Add to selection** button, and an exact "N to add" count based on the preset's resolved ids instead of a count-only comparison that could report items "to remove".
- Removing an item that a preset contributes now records it in `exclude` (that is what the field was for) instead of failing with "not declared"; dependency-only items still ask you to remove the dependent, and orphan checks run for exclusions too.
- `catalog.py presets --json` adds `active: string[]` alongside `current`, and each preset card carries the resolved `ids`.
- Items can be **locked** so an edit you made on purpose is never overwritten. `.ai/ai.json` gains a `locked` array; `catalog.py lock|unlock <id...>` edits it, Installed rows offer Lock/Unlock, and Updates lists frozen rows separately. `update` (even with `--force`) and `sync` leave an installed locked item untouched and keep its pristine hash, so unlocking still reports the edit as drift. A missing locked item is still materialised, as on a fresh clone.
- **Install markers are gone.** `.ai/ai.lock.json` already records an item's id, ref, hash, path and materialize mode, so the extra `.ai-installed.json` / `.ai/installed/<id>.<kind>.json` files were redundant. They are no longer written, and `sync` reports and removes any left by an earlier version (including the now-empty `.ai/installed/` directory).
- **Supporting `.ai/` folders are created for a new project, never pruned.** A repository with no manifest gets plans, patterns, reference, checklists and `session-context.md` during setup (`bootstrap`, `init`, or the first `sync`). Once they exist, nothing overwrites or removes them; `catalog.py scaffold` / **Add template folders** only copies missing files. Existing projects are not re-seeded by ordinary sync.
- **The generated catalog index is local-only.** It now lives at `.ai/tmp/catalog-index.json` (gitignored) instead of `.ai/catalog/index.json`; a legacy index at the old path is removed on the next sync. Presets, categories, schemas and the agent configuration are unchanged.
- Upgrading from an earlier version cleans up only generated state: legacy markers and the old index disappear on the next activation or sync (bootstrap runs the same cleanup). Existing scaffold files are left exactly as they are.
- Settings → Maintenance adds **Add template folders**, reports legacy cleanup in the setup toast, and no longer implies that setup or sync re-seeds every existing project.
- The generated `.github/copilot-instructions.md` rewrites shared `AGENTS.md` platform paths from `.claude/` to `.github/` (`.claude/settings.json` stays as written), so a valid project no longer fails the template's stale-reference check.
- The extension now bundles the corrected `.ai/tests/validate-release.py`. Activation/bootstrap refreshes an unmodified old copy, so implementing repositories stop failing the template-only release-layout assertion even when their pinned template predates the fix.
- **AI clients are selectable.** `.ai/ai.json` gains a `clients` array (`claude`, `copilot`, `reasonix`); new projects default to Claude only, while a missing field keeps every client for existing projects. Settings → Clients and `catalog.py clients --set ...` update it. `sync-platforms.py` creates or removes only the selected clients' junctions; disabling a client never deletes a real directory or `.github/workflows`, and removes `.claude`/`.github`/`.reasonix` only when they are empty. Generated Copilot instructions are removed when Copilot is disabled, and Claude settings/Copilot instructions are not re-created for disabled clients.

## 0.12.0 (2026-10-29)

- `microsoft/aspire-skills` is now a built-in source (`aspire-skills`) with its seven official Aspire skills and a new `Aspire` preset. It is indexed before `awesome-copilot` because both ship an `aspire` skill; the official one keeps the name.
- Existing projects pick up newly offered built-in sources automatically: `bootstrap` (run on activation) adds any missing source entry to `.ai/ai.json` without touching the selection. Built-in source priority now follows the resolver's declaration order, not the order a manifest happened to be written in.
- The `template` bucket is labelled **I-Synergy** instead of "Template".
- Presets: `.Net Maui` also selects the useful `dotnet-maui` reviewer agent, and `Devops` includes the Aspire deployment skill; the preset copy no longer says "both sources".
- Added **prompts** as a first-class kind next to instructions: prompts are read from `prompts/` (`*.prompt.md` or plain `*.md`), installed under `.ai/prompts/`, shown in the kind filter, and written to `.ai/ai.json` as `prompts: [...]`.
- Renamed the template's `sync-skills.py` to **`sync-platforms.py`**, since it now mirrors skills, agents, instructions and prompts. `sync-skills.py` remains as a deprecated alias, so existing hooks and project instructions keep working. The Claude `SessionStart`/`PostToolUse` hooks now also fire on `.ai/prompts/` and `.ai/instructions/` changes.
- `sync --prune` now preserves lock entries whose kind the running resolver does not know instead of treating them as deselected. A newer client can therefore add a kind (like `prompt`) without an older client's prune deleting it. Resolvers shipped before this change still have the old behaviour, so upgrade before pruning a project that has prompts.
- Renamed the `update-skills` skill to **`sync-platforms`**; it now documents all four junction sets (skills, agents, prompts, instructions) and the new script.
- Instructions and prompts now mirror to `.github/instructions/` and `.github/prompts/` alongside the skill/agent junctions. The AI template's own `.ai/instructions/` and `.ai/prompts/` are selectable without further configuration.
- Presets are ordered alphabetically (case-insensitive, leading dot ignored). **Tools** is 13th, **Minimal** 14th and **Everything** last. Added a **Minimal** preset with a small template baseline, and renamed the `full` preset to `Everything` (the old `full` value in an existing manifest still resolves through an alias).
- `dotnet/maui-labs` is now offered as two built-in third-party sources, `dotnet-maui` and `dotnet-maui-tooling`, instead of copied content. Each source reads its own plugin's skills; `dotnet-maui` also contributes the repository's agents and instructions. Sources can declare per-kind `paths`, which is how the nested `plugins/<plugin>/skills` layout is read (and an empty list disables a kind).
- The `.NET MAUI` and `Design` presets select those skills from the `dotnet-maui:` and `dotnet-maui-tooling:` namespaces; nothing is vendored into the template catalog.

## 0.11.0 (2026-10-28)

- Generated-file markers moved to the `ai-workspace-manager:` namespace (`generated-from-agents`, `copilot-reference:start`/`:end`, `installed:start`/`:end`). Files written with the old markers are still recognized and are rewritten with the new namespace on the next regeneration — no duplicated or lost reference block.
- README now documents where the catalog comes from: the `I-Synergy/AI` template and `github/awesome-copilot`, with pinned refs, licences and provenance in `.ai/ai.lock.json`.
- Marketplace categories now include **AI** and **Machine Learning**.

## 0.10.1 (2026-10-28)

- Renamed to **AI Workspace Manager for VS Code** with the Marketplace name `aiworkspacemanager`; the repository moved to `https://github.com/I-Synergy/AIWorkspaceManager`.
- Refreshed the UI copy, screenshots, animated demo and social preview.

## 0.10.0 (2026-10-28)

- First Marketplace release of AI Workspace Manager for VS Code.
- Browse, Installed, Updates, Presets and Settings surfaces for the per-repository skill/agent selection.
- Newer-template-release detection against the source's git tags, with a one-click upgrade to the newest release.
- Self-contained resolver: the extension ships the `.ai/scripts` tooling and vocabulary, and sets a repository up on first activation.
- Project form (name/intention) fills `session-context.md`, the project documents and `{ApplicationName}` across the scaffold.
- Generated agent instructions: `AGENTS.md` is the source of truth and `.github/copilot-instructions.md` is derived from it, listing only what is installed.
