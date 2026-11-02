/**
 * The contract between the extension host and the webview.
 *
 * Both sides import this file; the RPC layer is typed by `HostAPI`, so a
 * renamed field breaks the build rather than silently rendering `undefined`.
 */

export type Result<T> = { ok: true; value: T } | { ok: false; error: string };

export function ok<T>(value: T): Result<T> {
  return { ok: true, value };
}

export function fail<T = never>(error: string): Result<T> {
  return { ok: false, error };
}

export type ItemKind = "skill" | "agent" | "instruction" | "prompt" | "plugin" | "hook";

/** AI clients whose platform folders are generated. */
export type ClientName = "claude" | "copilot" | "reasonix";

export interface Bootstrap {
  WorkspaceName: string;
  HasManifest: boolean;
  HasScript: boolean;
  Materialize: "link" | "copy";
  Version: string;
}

export interface SourceRow {
  id: string;
  repo: string;
  ref: string;
  resolve: "local" | "remote";
  license: string;
  allowHooks: boolean;
}

export interface PresetCard {
  name: string;
  description: string;
  skills: number;
  agents: number;
  /** Resolved ids, dependencies included, so the UI can say what is new. */
  ids?: string[];
  /** The preset the UI leads with; `Everything` stays available but not promoted. */
  recommended?: boolean;
}

/** One row of the sidecar's `getBrowse` payload. */
export interface CatalogItem {
  id: string;
  displayId: string;
  kind: ItemKind;
  name: string;
  path: string;
  description: string;
  source: string;
  ref: string;
  license: string;
  upstream: string;
  sha256: string;
  files: number;
  /** Ids this item depends on (an agent's skills, or a skill's `requires:`). */
  requires: string[];
  /** Ids that depend on this one, so the UI can explain why it is installed. */
  requiredBy: string[];
  /** Category id from catalog/categories.json; 'uncategorised' is the fallback. */
  category: string;
  /** Extra cross-cutting tags declared in frontmatter. */
  tags: string[];
  selected: boolean;
  status: string;
  installed: boolean;
  /** Frozen by the user: update and sync will not overwrite it. */
  locked: boolean;
  localModified: boolean;
  upstreamChanged: boolean;
}

export interface BrowseSummary {
  available: number;
  selected: number;
  installed: number;
  drift: number;
  updates: number;
  conflicts: number;
}

export interface CategoryRow {
  id: string;
  label: string;
  description: string;
  count: number;
}

export interface BrowsePayload {
  source: { id: string; root: string; ref: string; sameDir: boolean; remote: boolean };
  materialize: "link" | "copy";
  /** Clients selected in .ai/ai.json; absent means every legacy client. */
  clients?: ClientName[];
  summary: BrowseSummary;
  sources: SourceRow[];
  presets: PresetCard[];
  categories: CategoryRow[];
  items: CatalogItem[];
}

export interface UpdateRow {
  id: string;
  displayId: string;
  kind: ItemKind;
  status: string;
  /** Frozen by the user: reported here, never applied. */
  locked?: boolean;
  path: string;
  source: string;
}

export interface UpdatePayload {
  dryRun: boolean;
  ready: UpdateRow[];
  blocked: UpdateRow[];
  /** Locked rows that would otherwise be ready or in conflict. */
  locked?: UpdateRow[];
  remote: boolean;
  /** Whether any *selected* item resolves remotely — a local selection has no
      upstream even when another, unselected source is remote. */
  remoteSelected: boolean;
}

export interface PresetsPayload {
  /** First active preset; kept for resolvers that predate the additive list. */
  current: string | null;
  /** Every preset in the selection, in manifest order. */
  active?: string[];
  presets: PresetCard[];
}

/** One source's release state from the sidecar's `getReleases` payload. */
export interface ReleaseRow {
  id: string;
  repo: string;
  ref: string;
  /** False when `ref` is a branch or sha: there is nothing to compare. */
  pinnedTag: boolean;
  current: string | null;
  latest: string | null;
  latestIsPrerelease: boolean;
  newer: boolean;
  /** Newest semantic-version tags, capped; `tagCount` is the real total. */
  tags: string[];
  tagCount: number;
  checked: boolean;
  error: string | null;
}

export interface ReleasePayload {
  offline: boolean;
  /** Ids of sources whose pinned tag is older than their newest release. */
  newer: string[];
  sources: ReleaseRow[];
}

/** Outcome of "update to this release": pin the source, then rewrite the files. */
export interface UpgradeResult {
  /** The user dismissed the confirmation; nothing was written. */
  Cancelled: boolean;
  Code: number;
  Stdout: string;
  Stderr: string;
}

/** Result of persisting the implicit manifest (mirrors the sidecar's `adopt` payload). */
export interface AdoptPayload {
  created: boolean;
  path: string;
  selected: number;
}

/** Result of the full setup run (the sidecar's `bootstrap` payload). */
export interface BootstrapPayload {
  created: boolean;
  path: string;
  selected: number;
  tooling: {
    copied: string[];
    skipped: string[];
    unchanged: string[];
    missing: string[];
    protected: string[];
  };
  scaffold: { copied: string[]; skipped: string[] };
  /** Legacy markers / old index removed during upgrade cleanup. */
  legacyRemoved?: string[];
}

/** The project name/intention and what writing them touched. */
export interface ProjectInfo {
  name: string;
  intention: string;
  detectedName: string;
  written: string[];
  skipped: string[];
}

/** Raw CLI outcome; the web layer turns a non-zero code into a visible error. */
export interface VendorSummary {
  Copied: number;
  Skipped: number;
  Manifest: string;
}

export interface CliResult {
  code: number;
  stdout: string;
  stderr: string;
}

export interface HostAPI {
  getBootstrap(params: Record<string, never>): Promise<Result<Bootstrap>>;
  getBrowse(params: { Refresh?: boolean }): Promise<Result<BrowsePayload>>;
  /** `update` always re-fetches remote sources; there is no refresh flag. */
  getUpdates(params: Record<string, never>): Promise<Result<UpdatePayload>>;
  getPresets(params: Record<string, never>): Promise<Result<PresetsPayload>>;
  /** Newer release tags for the declared sources; read-only, no clone. */
  getReleases(params: Record<string, never>): Promise<Result<ReleasePayload>>;
  /** Pin a source to a release and rewrite the installed items from it. */
  upgrade(params: { Id: string; Release: string }): Promise<Result<UpgradeResult>>;
  /** Write .ai/ai.json from what the repository already has; no files change. */
  adopt(params: Record<string, never>): Promise<Result<AdoptPayload>>;
  /** Set the repository up from the template: manifest, resolver, scaffold. */
  bootstrap(params: Record<string, never>): Promise<Result<BootstrapPayload>>;
  /** The stored project name/intention, with the detected default. */
  getProject(params: Record<string, never>): Promise<Result<ProjectInfo>>;
  /** Store name/intention and fill the template documents and scaffold. */
  describeProject(params: { Name: string; Intention: string }): Promise<Result<ProjectInfo>>;
  addPreset(params: { Name: string }): Promise<Result<CliResult>>;
  /** Freeze or thaw items so update/sync leave their content alone. */
  setLocked(params: { Ids: string[]; Locked: boolean }): Promise<Result<CliResult>>;
  install(params: { Ids: string[] }): Promise<Result<CliResult>>;
  remove(params: { Ids: string[]; Prune: boolean }): Promise<Result<CliResult>>;
  sync(params: Record<string, never>): Promise<Result<CliResult>>;
  updateItems(params: { Ids?: string[]; Force?: boolean }): Promise<Result<CliResult>>;
  pruneDeselected(params: Record<string, never>): Promise<Result<CliResult>>;
  writeNotice(params: Record<string, never>): Promise<Result<CliResult>>;
  rebuildIndex(params: Record<string, never>): Promise<Result<CliResult>>;
  /** Copy any missing template `.ai/` folders and files (never overwrites). */
  addScaffold(params: Record<string, never>): Promise<Result<CliResult>>;
  /** Select the AI clients and reconcile their platform folders. */
  setClients(params: { Clients: ClientName[] }): Promise<Result<CliResult>>;
  openFile(params: { Path: string }): Promise<Result<null>>;
  openUpstream(params: { Url: string }): Promise<Result<null>>;
  /** Copy the resolver into the workspace so CI/agents can run it there. */
  vendorTooling(params: { Overwrite?: boolean }): Promise<Result<VendorSummary>>;
}

/**
 * A resolver call may clone two repositories and rewrite ~90 items; 120 s was
 * short enough that a real update timed out client-side while the host kept
 * running, inviting a second click and leaving a half-applied lock.
 */
export const RPC_TIMEOUT_MS = 600_000;

export interface RpcRequest {
  type: "rpc-request";
  id: number;
  method: string;
  params: unknown;
}

export interface RpcResponse {
  type: "rpc-response";
  id: number;
  result: Result<unknown>;
}

export interface RpcCancel {
  type: "rpc-cancel";
  id: number;
}

/**
 * A line the webview wants in the extension's output channel. The webview's own
 * console is only reachable through Developer Tools, so failures there would
 * otherwise be invisible; this bridge puts them next to the host's log.
 */
export interface WebviewLog {
  type: "webview-log";
  level: "warn" | "error";
  message: string;
}

/** The subset of `vscode.LogOutputChannel` the host uses for diagnostics. */
export interface LogSink {
  info(message: string): void;
  warn(message: string): void;
  error(message: string): void;
  debug(message: string): void;
}
