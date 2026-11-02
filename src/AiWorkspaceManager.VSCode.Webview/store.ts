import type {
  Bootstrap, BrowsePayload, ClientName, CliResult, ItemKind, PresetsPayload, ProjectInfo,
  ReleasePayload, Result, UpdatePayload,
} from "../AiWorkspaceManager.VSCode.Common/types";
import { hostApi, router } from "./registrations";
import { itemKey, pickedIds } from "./logic";
import type { InstalledState, UpdateState } from "./logic";
import type { Tab } from "./router";

export interface Toast {
  kind: "info" | "error";
  text: string;
}

function describeFailure(result: Result<CliResult>): string {
  if (!result.ok) {
    return result.error;
  }
  const detail = (result.value.stderr || result.value.stdout).trim().split(/\r?\n/)[0];
  return detail || `resolver exited ${result.value.code}`;
}

/**
 * Single source of truth for the webview. Views never call the RPC client
 * directly: they read this store and call its actions, which keeps the
 * loading/error/toast handling in exactly one place.
 */
class Store extends EventTarget {
  bootstrap: Bootstrap | undefined;
  browse: BrowsePayload | undefined;
  presets: PresetsPayload | undefined;
  updates: UpdatePayload | undefined;
  releases: ReleasePayload | undefined;
  /** Why the release check failed, kept apart from `error` so one hint cannot
      blank the whole view. */
  releaseError: string | undefined;
  private releasesLoading = false;
  loading = false;
  error: string | undefined;
  toast: Toast | undefined;
  /** Browse filter state, shared so the tab switch does not reset it. */
  query = "";
  kindFilter: ItemKind | "all" = "all";
  stateFilter: "all" | "installed" | "available" | "needs-attention" = "all";
  sourceFilter = "all";
  /** Installed keeps its own filters: the two views answer different questions. */
  installedQuery = "";
  installedKind: ItemKind | "all" = "all";
  installedState: InstalledState = "all";
  installedSource = "all";
  /** Updates filters, also independent. */
  updatesQuery = "";
  updatesKind: ItemKind | "all" = "all";
  updatesState: UpdateState = "all";
  updatesSource = "all";
  /** Project name/intention, loaded when Settings opens. */
  project: ProjectInfo | undefined;
  projectName = "";
  projectIntention = "";
  private projectLoading = false;
  /** Row identity: kind + id, because an agent and a skill can share a name. */
  selectedKey: string | undefined;
  /** Ids ticked for a bulk action, independent of whether they are installed. */
  picked: string[] = [];

  constructor() {
    super();
    router.addEventListener("route-changed", () => this.fire());
  }

  private fire(): void {
    this.dispatchEvent(new Event("changed"));
  }

  get tab(): Tab {
    return router.CurrentTab;
  }

  selectTab(tab: Tab): void {
    router.SelectTab(tab);
  }

  notify(kind: Toast["kind"], text: string): void {
    this.toast = { kind, text };
    this.fire();
    setTimeout(() => {
      if (this.toast?.text === text) {
        this.toast = undefined;
        this.fire();
      }
    }, 6000);
  }

  dismissToast(): void {
    this.toast = undefined;
    this.fire();
  }

  async loadAsync(): Promise<void> {
    this.loading = true;
    this.error = undefined;
    this.fire();
    try {
      const bootstrap = await hostApi.getBootstrap({});
      this.bootstrap = bootstrap.ok ? bootstrap.value : undefined;
      if (!bootstrap.ok) {
        this.error = bootstrap.error;
        return;
      }
      const browse = await hostApi.getBrowse({});
      if (!browse.ok) {
        this.error = browse.error;
        return;
      }
      this.browse = browse.value;
      const presets = await hostApi.getPresets({});
      this.presets = presets.ok ? presets.value : undefined;
      if (!this.selectedKey && browse.value.items.length > 0) {
        this.selectedKey = itemKey(browse.value.items[0]);
      }
    } finally {
      this.loading = false;
      this.fire();
    }
  }

  async refreshAsync(refreshRemote = false): Promise<void> {
    this.loading = true;
    this.fire();
    try {
      const browse = await hostApi.getBrowse({ Refresh: refreshRemote });
      if (browse.ok) {
        this.browse = browse.value;
        this.error = undefined;
      } else {
        this.error = browse.error;
      }
    } finally {
      this.loading = false;
      this.fire();
    }
  }

  async refreshUpdatesAsync(): Promise<void> {
    this.loading = true;
    this.fire();
    try {
      const updates = await hostApi.getUpdates({});
      if (updates.ok) {
        this.updates = updates.value;
        this.error = undefined;
      } else {
        this.error = updates.error;
      }
    } finally {
      this.loading = false;
      this.fire();
    }
  }

  /** Release info is lazy: painting the app must not touch the network. */
  async ensureReleasesAsync(): Promise<void> {
    if (this.releases || this.releasesLoading) {
      return;
    }
    await this.refreshReleasesAsync();
  }

  async refreshReleasesAsync(): Promise<void> {
    if (this.releasesLoading) {
      return;
    }
    this.releasesLoading = true;
    this.fire();
    try {
      const releases = await hostApi.getReleases({});
      if (releases.ok) {
        this.releases = releases.value;
        this.releaseError = undefined;
      } else {
        this.releaseError = releases.error;
      }
    } finally {
      this.releasesLoading = false;
      this.fire();
    }
  }

  /** Run the full template setup: manifest, resolver/vocabulary, scaffold. */
  async bootstrapAsync(): Promise<boolean> {
    this.loading = true;
    this.fire();
    try {
      const result = await hostApi.bootstrap({});
      if (!result.ok) {
        this.notify("error", `Set up: ${result.error}`);
        return false;
      }
      const { created, path, selected, tooling, scaffold, legacyRemoved } = result.value;
      const copied = tooling.copied.length + scaffold.copied.length;
      const kept = tooling.protected.length + scaffold.skipped.length;
      const cleaned = legacyRemoved?.length ?? 0;
      const cleanup = cleaned ? ` ${cleaned} old state file(s) cleaned.` : "";
      this.notify("info", (created
        ? `Set up ${path} (${selected} selected, ${copied} file(s) copied).`
        : `${copied} template file(s) added, ${kept} kept.`) + cleanup);
      await this.loadAsync();
      return true;
    } catch (error) {
      this.notify("error", `Set up: ${error instanceof Error ? error.message : String(error)}`);
      return false;
    } finally {
      this.loading = false;
      this.fire();
    }
  }

  /** Load the project description the first time Settings opens. */
  async ensureProjectAsync(): Promise<void> {
    if (this.project || this.projectLoading) {
      return;
    }
    this.projectLoading = true;
    try {
      const result = await hostApi.getProject({});
      if (result.ok) {
        this.project = result.value;
        this.projectName = result.value.name;
        this.projectIntention = result.value.intention;
        this.fire();
      }
    } finally {
      this.projectLoading = false;
    }
  }

  /** Store the name/intention and fill the still-template documents. */
  async saveProjectAsync(): Promise<boolean> {
    this.loading = true;
    this.fire();
    try {
      const result = await hostApi.describeProject({
        Name: this.projectName.trim(),
        Intention: this.projectIntention.trim(),
      });
      if (!result.ok) {
        this.notify("error", `Project: ${result.error}`);
        return false;
      }
      this.project = result.value;
      this.projectName = result.value.name;
      this.projectIntention = result.value.intention;
      const count = result.value.written.length;
      this.notify("info", count > 0
        ? `Project details saved — ${count} file(s) updated.`
        : "Project details saved.");
      return true;
    } catch (error) {
      this.notify("error", `Project: ${error instanceof Error ? error.message : String(error)}`);
      return false;
    } finally {
      this.loading = false;
      this.fire();
    }
  }

  /**
   * Move a source to a release and rewrite what it installs. The host asks for
   * confirmation first; cancelling leaves everything untouched.
   */
  async upgradeAsync(id: string, release: string): Promise<boolean> {
    this.loading = true;
    this.fire();
    let changed = false;
    try {
      const result = await hostApi.upgrade({ Id: id, Release: release });
      if (!result.ok) {
        // Installing over a running window replaces the files on disk but not
        // the loaded host code: the webview gets the new bundle, the RPC layer
        // stays old. Say what to do instead of a bare "Unknown RPC method".
        const staleHost = /Unknown RPC method/i.test(result.error);
        this.notify("error", staleHost
          ? "AI Workspace Manager was updated on disk. Reload the window (Developer: Reload Window), then try again."
          : `Update to ${release}: ${result.error}`);
        return false;
      }
      if (result.value.Cancelled) {
        return false;
      }
      if (result.value.Code !== 0) {
        const detail = (result.value.Stderr || result.value.Stdout)
          .trim().split(/\r?\n/)[0];
        this.notify("error", `Update to ${release}: `
          + (detail || `resolver exited ${result.value.Code}`));
        return false;
      }
      changed = true;
      this.notify("info", `Pinned ${id} to ${release}; installed items updated.`);
      return true;
    } catch (error) {
      this.notify("error", `Update to ${release}: `
        + (error instanceof Error ? error.message : String(error)));
      return false;
    } finally {
      this.loading = false;
      this.fire();
      if (changed) {
        await this.refreshAsync();
        await this.refreshReleasesAsync();
        if (this.tab === "updates") {
          await this.refreshUpdatesAsync();
        }
      }
    }
  }

  /** Run a mutating action, then reload so the UI reflects the new truth. */
  async runAsync(label: string, action: () => Promise<Result<CliResult>>): Promise<boolean> {
    this.loading = true;
    this.fire();
    try {
      const result = await action();
      if (!result.ok || result.value.code !== 0) {
        this.notify("error", `${label}: ${describeFailure(result)}`);
        return false;
      }
      this.notify("info", `${label} — done.`);
      return true;
    } catch (error) {
      this.notify("error", `${label}: ${error instanceof Error ? error.message : String(error)}`);
      return false;
    } finally {
      this.loading = false;
      this.fire();
      await this.refreshAsync();
      if (this.tab === "updates") {
        await this.refreshUpdatesAsync();
      }
    }
  }

  install(ids: string[]): Promise<boolean> {
    return this.runAsync(`Installing ${ids.length} item(s)`, () => hostApi.install({ Ids: ids }));
  }

  /* ── multi-select ───────────────────────────────────────────────────────── */

  clearInstalledFilters(): void {
    this.installedQuery = "";
    this.installedKind = "all";
    this.installedState = "all";
    this.installedSource = "all";
    this.fire();
  }

  clearUpdateFilters(): void {
    this.updatesQuery = "";
    this.updatesKind = "all";
    this.updatesState = "all";
    this.updatesSource = "all";
    this.fire();
  }

  togglePick(id: string): void {
    this.picked = this.picked.includes(id)
      ? this.picked.filter((entry) => entry !== id)
      : [...this.picked, id];
    this.fire();
  }

  setPicked(ids: string[]): void {
    this.picked = ids;
    this.fire();
  }

  clearPicked(): void {
    if (this.picked.length > 0) {
      this.picked = [];
      this.fire();
    }
  }

  /** Picked ids the given action can act on: installs skip what is installed. */
  actionablePicked(selected: boolean): string[] {
    return pickedIds(this.browse?.items ?? [], this.picked, selected);
  }

  async installPickedAsync(): Promise<boolean> {
    const ids = this.actionablePicked(false);
    if (ids.length === 0) {
      this.notify("info", "Nothing picked that is not already installed.");
      return false;
    }
    const ok = await this.install(ids);
    this.clearPicked();
    return ok;
  }

  async removePickedAsync(prune = false): Promise<boolean> {
    const ids = this.actionablePicked(true);
    if (ids.length === 0) {
      this.notify("info", "Nothing picked that is installed.");
      return false;
    }
    const ok = await this.remove(ids, prune);
    this.clearPicked();
    return ok;
  }

  remove(ids: string[], prune = false): Promise<boolean> {
    return this.runAsync(`Removing ${ids.length} item(s)`, () => hostApi.remove({ Ids: ids, Prune: prune }));
  }

  sync(): Promise<boolean> {
    return this.runAsync("Sync", () => hostApi.sync({}));
  }

  /** Update the rows the user can see, not everything the command would find. */
  updateRows(ids: string[]): Promise<boolean> {
    if (ids.length === 0) {
      this.notify("info", "Nothing to update.");
      return Promise.resolve(false);
    }
    return this.runAsync(`Update ${ids.length} item(s)`, () => hostApi.updateItems({ Ids: ids }));
  }

  updateOne(id: string, force = false): Promise<boolean> {
    return this.runAsync("Update", () => hostApi.updateItems({ Ids: [id], Force: force }));
  }

  addPreset(name: string): Promise<boolean> {
    return this.runAsync(`Adding preset ${name}`, () => hostApi.addPreset({ Name: name }));
  }

  setLocked(ids: string[], locked: boolean): Promise<boolean> {
    return this.runAsync(
      `${locked ? "Locking" : "Unlocking"} ${ids.length} item(s)`,
      () => hostApi.setLocked({ Ids: ids, Locked: locked }),
    );
  }

  prune(): Promise<boolean> {
    return this.runAsync("Prune", () => hostApi.pruneDeselected({}));
  }

  writeNotice(): Promise<boolean> {
    return this.runAsync("Write NOTICE.md", () => hostApi.writeNotice({}));
  }

  rebuildIndex(): Promise<boolean> {
    return this.runAsync("Rebuild index", () => hostApi.rebuildIndex({}));
  }

  addScaffold(): Promise<boolean> {
    return this.runAsync("Add template folders", () => hostApi.addScaffold({}));
  }

  setClients(clients: ClientName[]): Promise<boolean> {
    return this.runAsync(
      `Clients: ${clients.join(", ") || "none"}`,
      () => hostApi.setClients({ Clients: clients }),
    );
  }
}

export const store = new Store();
