import { LitElement, css, html } from "lit";
import type { BrowseSummary } from "../../AiWorkspaceManager.VSCode.Common/types";
import { pluralise } from "../logic";
import { router } from "../registrations";
import { store } from "../store";
import { sharedStyles } from "../styles/shared.css";
import "./browse-view";
import "./installed-view";
import "./presets-view";
import "./settings-view";
import "./updates-view";

const TABS = [
  { id: "browse", label: "Browse" },
  { id: "installed", label: "Installed" },
  { id: "updates", label: "Updates" },
  { id: "presets", label: "Presets" },
] as const;

/** The shell: header, tabs, route switching, and the one toast host. */
export class AiManagerApp extends LitElement {
  private readonly onChange = (): void => this.requestUpdate();
  private readonly onHostMessage = (event: MessageEvent): void => {
    const message = event.data as { type?: string; command?: string; tab?: string };
    if (message?.type === "command" && message.command === "navigate-tab" && message.tab) {
      store.selectTab(message.tab as "browse" | "installed" | "updates" | "presets");
    }
  };

  override connectedCallback(): void {
    super.connectedCallback();
    store.addEventListener("changed", this.onChange);
    router.addEventListener("route-changed", this.onChange);
    window.addEventListener("message", this.onHostMessage);

    // The initial tab arrives in the script URL (see panel.ts), so the first
    // paint is already correct.
    const requested = new URLSearchParams(window.location.search).get("tab");
    if (requested) {
      store.selectTab(requested as "browse" | "installed" | "updates" | "presets");
    }
    void store.loadAsync();
  }

  override disconnectedCallback(): void {
    store.removeEventListener("changed", this.onChange);
    router.removeEventListener("route-changed", this.onChange);
    window.removeEventListener("message", this.onHostMessage);
    super.disconnectedCallback();
  }

  static override readonly styles = [
    sharedStyles,
    css`
      :host {
        display: block;
        height: 100%;
        background: var(--vscode-editor-background);
      }
      /* The shell is an inner flex column, not :host. Styles in the outer
         document (web.css) outrank :host rules in the cascade, so a
         \`display: block\` there silently beat a :host flex column and let the
         content grow past the viewport instead of scrolling inside it. */
      .shell {
        display: flex;
        flex-direction: column;
        height: 100%;
        min-height: 0;
      }
      header {
        border-bottom: 1px solid var(--vscode-panel-border);
      }
      .title {
        display: flex;
        align-items: center;
        gap: 10px;
        padding: 10px 14px 8px;
      }
      .title h1 {
        font-size: 14px;
        margin: 0;
        font-weight: 600;
        white-space: nowrap;
      }
      .repo {
        color: var(--vscode-descriptionForeground);
        font-size: 12px;
      }
      .stat + .stat::before {
        content: "·";
        margin-right: 8px;
        opacity: 0.5;
      }
      .grow {
        flex: 1;
      }
      .stat {
        display: inline-flex;
        gap: 6px;
        align-items: baseline;
        font-size: 12px;
        color: var(--vscode-descriptionForeground);
      }
      .stat b {
        color: var(--vscode-foreground);
        font-size: 13px;
      }
      .stat.warn b {
        color: var(--vscode-charts-orange, #d18616);
      }
      .stat.info b {
        color: var(--vscode-charts-blue, #3794ff);
      }
      nav {
        display: flex;
        gap: 2px;
        padding: 0 10px;
      }
      nav button {
        background: transparent;
        border: none;
        border-bottom: 1px solid transparent;
        border-radius: 0;
        color: var(--vscode-descriptionForeground);
        padding: 7px 12px;
        font-size: 12px;
      }
      nav button:hover {
        color: var(--vscode-foreground);
        background: var(--vscode-list-hoverBackground);
      }
      nav button.active {
        color: var(--vscode-panelTitle-activeForeground, var(--vscode-foreground));
        border-bottom-color: var(--vscode-panelTitle-activeBorder, var(--vscode-focusBorder));
      }
      nav .badge {
        margin-left: 6px;
        background: var(--vscode-badge-background);
        color: var(--vscode-badge-foreground);
        border-radius: 8px;
        padding: 0 6px;
        font-size: 10px;
      }
      main {
        flex: 1;
        display: flex;
        min-height: 0;
        overflow: hidden;
      }
      .bar {
        height: 2px;
        background: transparent;
      }
      .bar.busy {
        background: linear-gradient(
          90deg,
          transparent,
          var(--vscode-progressBar-background),
          transparent
        );
        animation: sweep 1.1s ease-in-out infinite;
      }
      @keyframes sweep {
        0% { opacity: 0.2; }
        50% { opacity: 1; }
        100% { opacity: 0.2; }
      }
      .error {
        margin: 10px 14px;
        padding: 10px 12px;
        border-left: 3px solid var(--vscode-errorForeground, #f14c4c);
        background: color-mix(in srgb, var(--vscode-errorForeground, #f14c4c) 10%, transparent);
        font-size: 12px;
        white-space: pre-wrap;
      }
      .toast {
        position: fixed;
        right: 16px;
        bottom: 16px;
        max-width: 420px;
        padding: 9px 12px;
        border-radius: 4px;
        font-size: 12px;
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
        background: var(--vscode-notifications-background, var(--vscode-editorWidget-background));
        border: 1px solid var(--vscode-notifications-border, var(--vscode-panel-border));
        display: flex;
        gap: 10px;
        align-items: flex-start;
        cursor: pointer;
      }
      .toast.error {
        border-left: 3px solid var(--vscode-errorForeground, #f14c4c);
      }
      .toast.info {
        border-left: 3px solid var(--vscode-charts-green, #89d185);
      }
    `,
  ];

  private renderSummary(summary: BrowseSummary | undefined) {
    if (!summary) {
      return "";
    }
    const driftClass = summary.drift ? "warn" : "";
    const updatesClass = summary.updates ? "info" : "";
    return html`
      <span class="stat"><b>${summary.selected}</b> selected</span>
      <span class="stat ${driftClass}"><b>${summary.drift}</b> need attention</span>
      <span class="stat ${updatesClass}"><b>${summary.updates}</b> updates</span>
    `;
  }

  override render() {
    const browse = store.browse;
    const summary = browse?.summary;
    const route = router.CurrentRoute;
    const tab = router.CurrentTab;

    return html`
      <div class="shell">
        <header>
        <div class="title">
          <h1>AI Workspace Manager</h1>
          ${store.bootstrap?.WorkspaceName
            ? html`<span class="repo">· ${store.bootstrap.WorkspaceName}</span>`
            : ""}
          <span class="grow"></span>
          ${this.renderSummary(summary)}
          <button class="ghost" title="Refresh" ?disabled=${store.loading}
            @click=${() => void store.refreshAsync(false)}>Refresh</button>
          <button class="secondary" ?disabled=${store.loading} @click=${() => void store.sync()}>Sync</button>
        </div>
        <nav>
          ${TABS.map((entry) => html`
            <button
              class=${route === "CATALOG" && tab === entry.id ? "active" : ""}
              @click=${() => store.selectTab(entry.id)}
            >
              ${entry.label}
              ${entry.id === "updates" && summary?.updates
                ? html`<span class="badge">${summary.updates}</span>`
                : ""}
            </button>
          `)}
          <span class="grow"></span>
          <button
            class=${route === "SETTINGS" ? "active" : ""}
            @click=${() => router.Navigate(route === "SETTINGS" ? "CATALOG" : "SETTINGS")}
          >Settings</button>
        </nav>
        <div class="bar ${store.loading ? "busy" : ""}"></div>
      </header>

      ${store.error
        ? html`<div class="error">${store.error}</div>`
        : ""}

      <main>
        ${this.renderContent(route, tab)}
      </main>

      ${store.toast
        ? html`<div class="toast ${store.toast.kind}" @click=${() => store.dismissToast()}>
            ${store.toast.text}
          </div>`
        : ""}
      </div>
    `;
  }

  private renderContent(route: string, tab: string) {
    if (route === "SETTINGS") {
      return html`<settings-view></settings-view>`;
    }
    switch (tab) {
      case "installed":
        return html`<installed-view></installed-view>`;
      case "updates":
        return html`<updates-view></updates-view>`;
      case "presets":
        return html`<presets-view></presets-view>`;
      default:
        return html`<browse-view></browse-view>`;
    }
  }

  /** Small affordance: reflect the selection size in the document title. */
  protected override updated(): void {
    const summary = store.browse?.summary;
    document.title = summary
      ? `AI Workspace Manager — ${pluralise(summary.selected, "item")} selected`
      : "AI Workspace Manager";
  }
}
customElements.define("ai-manager-app", AiManagerApp);
