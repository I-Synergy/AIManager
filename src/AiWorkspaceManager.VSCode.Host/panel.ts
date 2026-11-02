import * as vscode from "vscode";
import { createNonce } from "../AiWorkspaceManager.VSCode.Common/nonce";
import { RpcHost } from "../AiWorkspaceManager.VSCode.Common/rpc/rpc-host";
import type { HostAPI, LogSink } from "../AiWorkspaceManager.VSCode.Common/types";

export const PANEL_ID = "aiManager.panel";

export type PanelTab = "browse" | "installed" | "updates" | "presets";

/**
 * Owns the single full-page editor tab. Revealed rather than duplicated, and
 * restored after a window reload through the registered serializer.
 */
export class AiManagerPanel implements vscode.Disposable {
  private panel: vscode.WebviewPanel | undefined;
  private rpc: RpcHost | undefined;
  private requestedTab: PanelTab | undefined;

  constructor(
    private readonly extensionUri: vscode.Uri,
    private readonly api: HostAPI,
    private readonly log: LogSink,
  ) {}

  show(tab?: PanelTab): void {
    this.requestedTab = tab;
    const where = tab ? ` on ${tab}` : "";
    this.log.debug(`Show panel${where}.`);
    if (this.panel) {
      this.panel.reveal(undefined, false);
      // The webview is already loaded: nudge it to the requested view.
      if (tab) {
        void this.panel.webview.postMessage({ type: "command", command: "navigate-tab", tab });
      }
      return;
    }
    const panel = vscode.window.createWebviewPanel(
      PANEL_ID,
      "AI Workspace Manager",
      { viewColumn: vscode.ViewColumn.Active, preserveFocus: false },
      {
        enableScripts: true,
        retainContextWhenHidden: true,
        localResourceRoots: [vscode.Uri.joinPath(this.extensionUri, "dist")],
      },
    );
    this.attach(panel);
  }

  restore(panel: vscode.WebviewPanel): void {
    this.attach(panel);
  }

  private attach(panel: vscode.WebviewPanel): void {
    this.panel = panel;
    this.rpc?.dispose();
    this.rpc = new RpcHost(panel.webview, this.api, this.log);
    panel.webview.html = this.html(panel.webview);
    this.log.info("Webview attached.");

    panel.onDidDispose(() => {
      this.log.info("Webview disposed.");
      this.rpc?.dispose();
      this.rpc = undefined;
      this.panel = undefined;
    });
  }

  private html(webview: vscode.Webview): string {
    const nonce = createNonce();
    const scriptUri = webview.asWebviewUri(
      vscode.Uri.joinPath(this.extensionUri, "dist", "web.js"));
    const styleUri = webview.asWebviewUri(
      vscode.Uri.joinPath(this.extensionUri, "dist", "web.css"));
    // The initial view travels in the script URL, so the first paint is already
    // on the right tab instead of flashing Browse and then switching.
    const suffix = this.requestedTab ? `?tab=${this.requestedTab}` : "";

    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource} data:; style-src ${webview.cspSource} 'unsafe-inline'; font-src ${webview.cspSource} data:; script-src 'nonce-${nonce}';">
  <link rel="stylesheet" href="${styleUri}">
  <title>AI Workspace Manager</title>
</head>
<body>
  <ai-manager-app></ai-manager-app>
  <script type="module" nonce="${nonce}" src="${scriptUri}${suffix}"></script>
</body>
</html>`;
  }

  dispose(): void {
    this.rpc?.dispose();
    this.panel?.dispose();
    this.panel = undefined;
  }
}
