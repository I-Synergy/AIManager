import * as vscode from "vscode";
import type { CatalogItem, CategoryRow } from "../AiWorkspaceManager.VSCode.Common/types";
import { Configure, Dispose, corePath, isAvailable } from "./core-process";
import { createHostAPI } from "./host-api";
import { AiManagerPanel, PANEL_ID } from "./panel";

const VIEW_ID = "aiManager.launcher";
const HAS_MANIFEST = "aiManager.hasManifest";

type Node =
  | { kind: "open" }
  | { kind: "category"; row: CategoryRow }
  | { kind: "item"; item: CatalogItem };

let panel: AiManagerPanel | undefined;
let tree: CatalogTreeProvider | undefined;
let treeView: vscode.TreeView<Node> | undefined;
let badgeTimer: NodeJS.Timeout | undefined;

export function activate(context: vscode.ExtensionContext): void {
  const workspaceRoot = (): string | undefined =>
    vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
  const version = String(context.extension.packageJSON.version ?? "0.0.0");

  // Output → "AI Workspace Manager". `log: true` gives a timestamped channel
  // with per-level filtering, so host and webview diagnostics are visible
  // without opening Developer Tools.
  const log = vscode.window.createOutputChannel("AI Workspace Manager", { log: true });
  context.subscriptions.push(log);
  Configure(context.extensionUri.fsPath, log);
  context.subscriptions.push({ dispose: () => Dispose() });
  log.info(`Activating AI Workspace Manager ${version}; workspace ${
    workspaceRoot() ?? "(none)"}; core ${corePath()} (${
    isAvailable() ? "available" : "missing — run npm run build:core"}).`);

  let markReady: () => void = () => {};
  const ready = new Promise<void>((resolve) => {
    markReady = resolve;
  });

  const api = createHostAPI({
    workspaceRoot,
    extensionVersion: version,
    extensionPath: context.extensionUri.fsPath,
    ready,
    log,
  });

  panel = new AiManagerPanel(context.extensionUri, api, log);
  context.subscriptions.push(panel);

  tree = new CatalogTreeProvider(api, workspaceRoot);
  treeView = vscode.window.createTreeView(VIEW_ID, {
    treeDataProvider: tree,
    showCollapseAll: true,
  });
  context.subscriptions.push(
    treeView,
    treeView.onDidChangeVisibility((event) => {
      if (event.visible) {
        void tree?.refresh();
      }
    }),
  );

  const setBadge = (drift: number): void => {
    if (!treeView) {
      return;
    }
    treeView.badge = drift > 0
      ? { value: drift, tooltip: `${drift} skill/agent item(s) need attention` }
      : undefined;
  };

  const refreshAll = async (): Promise<void> => {
    await tree?.refresh();
    const result = await api.getBrowse({});
    if (result.ok) {
      setBadge(result.value.summary.drift);
    }
  };

  const startBadgeTimer = (): void => {
    if (badgeTimer) {
      return;
    }
    setTimeout(() => void refreshAll(), 1200);
    badgeTimer = setInterval(() => void refreshAll(), 5 * 60 * 1000);
  };

  const stopBadgeTimer = (): void => {
    if (badgeTimer) {
      clearInterval(badgeTimer);
      badgeTimer = undefined;
    }
    setBadge(0);
  };
  context.subscriptions.push({ dispose: stopBadgeTimer });

  const refreshContextKey = async (): Promise<void> => {
    const root = workspaceRoot();
    const found = root
      ? await vscode.workspace.fs.stat(vscode.Uri.file(`${root}/.ai/ai.json`))
        .then(() => true, () => false)
      : false;
    await vscode.commands.executeCommand("setContext", HAS_MANIFEST, found);
    if (found) {
      startBadgeTimer();
    } else {
      stopBadgeTimer();
    }
  };

  /**
   * A new folder gets the resolver and vocabulary under `.ai/`, the agent
   * configuration, the platform mirrors and a manifest — plus the supporting
   * folders (plans, patterns, reference, checklists, project, …) on first
   * setup. Existing projects keep their own files, and legacy state files are
   * cleaned up here so an upgrade is pruned even without a sync. Nothing
   * project-owned is overwritten.
   */
  const ensureSetup = async (): Promise<void> => {
    if (!workspaceRoot()) {
      return;
    }
    const started = Date.now();
    log.info(`Template setup started for ${workspaceRoot()}.`);
    const result = await api.bootstrap({});
    if (!result.ok) {
      log.error(`Template setup failed after ${Date.now() - started} ms: ${result.error}`);
      return;
    }
    const copied = result.value.tooling.copied.length + result.value.scaffold.copied.length;
    const cleaned = result.value.legacyRemoved?.length ?? 0;
    log.info(`Template setup finished in ${Date.now() - started} ms: `
      + `${result.value.path} (${result.value.selected} selected, ${copied} file(s) copied, `
      + `${cleaned} old state file(s) cleaned, created=${result.value.created}).`);
  };

  const runItemAction = async (
    node: Node | undefined,
    action: "install" | "remove",
  ): Promise<void> => {
    if (node?.kind !== "item") {
      return;
    }
    const item = node.item;
    const result = action === "install"
      ? await api.install({ Ids: [item.id] })
      : await api.remove({ Ids: [item.id], Prune: false });
    if (!result.ok || result.value.code !== 0) {
      const detail = result.ok ? result.value.stderr.trim() : result.error;
      const failure = detail || `${action} failed`;
      void vscode.window.showErrorMessage(`AI Workspace Manager: ${failure}`);
    }
    await refreshAll();
  };

  context.subscriptions.push(
    vscode.commands.registerCommand("aiManager.open", () => panel?.show()),
    vscode.commands.registerCommand("aiManager.setup", () => panel?.show("presets")),
    vscode.commands.registerCommand("aiManager.updates", () => panel?.show("updates")),
    vscode.commands.registerCommand("aiManager.refresh", () => void refreshAll()),
    vscode.commands.registerCommand("aiManager.vendorTooling", async () => {
      const result = await api.vendorTooling({});
      if (!result.ok) {
        void vscode.window.showErrorMessage(`AI Workspace Manager: ${result.error}`);
        return;
      }
      const { Copied, Skipped, Manifest } = result.value;
      void vscode.window.showInformationMessage(
        `AI Workspace Manager: wrote ${Copied} file(s), left ${Skipped} alone, manifest ${Manifest}.`);
      await refreshAll();
    }),
    vscode.commands.registerCommand("aiManager.installItem",
      (node: Node) => runItemAction(node, "install")),
    vscode.commands.registerCommand("aiManager.removeItem",
      (node: Node) => runItemAction(node, "remove")),
    vscode.commands.registerCommand("aiManager.sync", async () => {
      const result = await api.sync({});
      if (!result.ok) {
        void vscode.window.showErrorMessage(`AI Workspace Manager: ${result.error}`);
        return;
      }
      await refreshAll();
      void vscode.window.showInformationMessage("AI Workspace Manager: selection synced.");
    }),
    vscode.commands.registerCommand("aiManager.openManifest", async () => {
      const root = workspaceRoot();
      if (!root) {
        return;
      }
      const document = await vscode.workspace.openTextDocument(
        vscode.Uri.file(`${root}/.ai/ai.json`));
      await vscode.window.showTextDocument(document, { preview: false });
    }),
    vscode.window.registerWebviewPanelSerializer(PANEL_ID, {
      deserializeWebviewPanel: (restored: vscode.WebviewPanel) => {
        panel?.restore(restored);
        return Promise.resolve();
      },
    }),
    vscode.workspace.onDidChangeWorkspaceFolders(() => void refreshContextKey()),
  );

  const manifestWatcher = vscode.workspace.createFileSystemWatcher("**/.ai/ai.json");
  const onManifestChange = (): void => {
    void refreshContextKey();
    void refreshAll();
  };
  manifestWatcher.onDidCreate(onManifestChange);
  manifestWatcher.onDidDelete(onManifestChange);
  manifestWatcher.onDidChange(onManifestChange);
  context.subscriptions.push(manifestWatcher);

  void ensureSetup().finally(() => {
    markReady();
    void refreshContextKey();
  });
}

export function deactivate(): void {
  if (badgeTimer) {
    clearInterval(badgeTimer);
    badgeTimer = undefined;
  }
  panel?.dispose();
  panel = undefined;
  Dispose();
  tree = undefined;
  treeView = undefined;
}

/**
 * The side-bar tree: Category → item.
 *
 * The categories are the shared vocabulary from catalog/categories.json, in its
 * declared order, so the tree reads the same way for our own skills and for an
 * external catalog whose items declare no category at all.
 */
class CatalogTreeProvider implements vscode.TreeDataProvider<Node> {
  private categories: CategoryRow[] = [];
  private items: CatalogItem[] = [];
  private error: string | undefined;
  private readonly changed = new vscode.EventEmitter<Node | undefined>();
  readonly onDidChangeTreeData = this.changed.event;

  constructor(
    private readonly api: ReturnType<typeof createHostAPI>,
    private readonly workspaceRoot: () => string | undefined,
  ) {}

  async refresh(): Promise<void> {
    if (!this.workspaceRoot()) {
      this.categories = [];
      this.items = [];
      this.changed.fire(undefined);
      return;
    }
    const result = await this.api.getBrowse({});
    if (result.ok) {
      this.categories = result.value.categories.filter((row) => row.count > 0);
      this.items = result.value.items;
      this.error = undefined;
    } else {
      this.categories = [];
      this.items = [];
      this.error = result.error;
    }
    this.changed.fire(undefined);
  }

  getChildren(element?: Node): Node[] {
    if (!element) {
      const nodes: Node[] = [{ kind: "open" }];
      if (this.error) {
        return nodes;
      }
      return nodes.concat(this.categories.map((row) => ({ kind: "category" as const, row })));
    }
    if (element.kind === "category") {
      return this.items
        .filter((item) => item.category === element.row.id)
        .map((item) => ({ kind: "item" as const, item }));
    }
    return [];
  }

  getTreeItem(element: Node): vscode.TreeItem {
    if (element.kind === "open") {
      const item = new vscode.TreeItem(
        this.error ? "Could not read the catalog" : "Open AI Workspace Manager",
        vscode.TreeItemCollapsibleState.None,
      );
      item.tooltip = this.error ?? "Open the full-page catalog manager";
      item.iconPath = new vscode.ThemeIcon(this.error ? "warning" : "library");
      item.command = { command: "aiManager.open", title: "Open AI Workspace Manager" };
      item.contextValue = "action";
      return item;
    }

    if (element.kind === "category") {
      const installed = this.items
        .filter((item) => item.category === element.row.id && item.selected).length;
      const node = new vscode.TreeItem(
        element.row.label,
        vscode.TreeItemCollapsibleState.Collapsed,
      );
      const installedText = installed ? ` · ${installed} installed` : "";
      node.description = `${element.row.count} item(s)${installedText}`;
      node.tooltip = element.row.description || element.row.label;
      node.iconPath = new vscode.ThemeIcon("folder-library");
      node.contextValue = "category";
      return node;
    }

    const { item } = element;
    const name = item.id.includes(":") ? item.id.slice(item.id.indexOf(":") + 1) : item.id;
    const node = new vscode.TreeItem(name, vscode.TreeItemCollapsibleState.None);
    node.description = item.kind === "agent" ? "agent" : "skill";
    node.tooltip = new vscode.MarkdownString(
      `**${name}** — ${item.kind}\n\n${item.description}\n\n${item.path}`,
    );
    node.iconPath = new vscode.ThemeIcon(
      item.selected ? "pass-filled" : "circle-outline",
      item.selected ? new vscode.ThemeColor("charts.green") : undefined,
    );
    node.contextValue = item.selected ? "installedItem" : "availableItem";
    node.command = { command: "aiManager.open", title: "Open AI Workspace Manager" };
    return node;
  }
}
