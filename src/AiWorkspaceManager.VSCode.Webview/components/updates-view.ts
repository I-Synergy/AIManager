import { LitElement, css, html } from "lit";
import type { UpdatePayload, UpdateRow } from "../../AiWorkspaceManager.VSCode.Common/types";
import { emptyUpdateNotice, filterUpdates, groupBySource, pluralise, releaseNotice, releaseSource, sourceLabel } from "../logic";
import { store } from "../store";
import { sharedStyles } from "../styles/shared.css";
import { kindIcon, lockIcon } from "./parts";

/**
 * Updates: real only when a source resolves remotely. When it does not, the
 * view says so and shows the exact manifest edit that would enable it, instead
 * of pretending there is nothing to update.
 */
export class UpdatesView extends LitElement {
  private readonly onChange = (): void => this.requestUpdate();

  override connectedCallback(): void {
    super.connectedCallback();
    store.addEventListener("changed", this.onChange);
    if (!store.updates) {
      void store.refreshUpdatesAsync();
    }
    // A release tag is a different question from a moved source, and it costs
    // one `git ls-remote` — asked for only when this view is opened.
    void store.ensureReleasesAsync();
  }

  override disconnectedCallback(): void {
    store.removeEventListener("changed", this.onChange);
    super.disconnectedCallback();
  }

  static override readonly styles = [
    sharedStyles,
    css`
      :host {
        display: flex;
        flex-direction: column;
        flex: 1;
        min-height: 0;
        overflow: auto;
      }
      .summary {
        display: flex;
        gap: 10px;
        align-items: center;
        padding: 10px 14px;
        border-bottom: 1px solid var(--vscode-panel-border);
      }
      .grow {
        flex: 1;
      }
      .toolbar {
        display: flex;
        gap: 8px;
        align-items: center;
        padding: 10px 14px;
        border-bottom: 1px solid var(--vscode-panel-border);
        flex-wrap: wrap;
        flex: 0 0 auto;
      }
      .toolbar input[type="search"] {
        flex: 1 1 220px;
        min-width: 140px;
      }
      .mode {
        color: var(--vscode-descriptionForeground);
        font-size: 12px;
      }
      .row {
        display: grid;
        grid-template-columns: 16px minmax(120px, 240px) 1fr auto auto;
        gap: 10px;
        align-items: center;
        padding: 6px 14px;
        border-bottom: 1px solid color-mix(in srgb, var(--vscode-panel-border) 45%, transparent);
      }
      .row:hover {
        background: var(--vscode-list-hoverBackground);
      }
      .name {
        font-weight: 600;
      }
      h3 {
        margin: 16px 14px 6px;
        font-size: 11px;
        letter-spacing: 0.06em;
        text-transform: uppercase;
        color: var(--vscode-descriptionForeground);
      }
      h4 {
        margin: 10px 14px 4px 24px;
        font-size: 10.5px;
        letter-spacing: 0.05em;
        color: var(--vscode-descriptionForeground);
        opacity: 0.85;
      }
      code {
        background: var(--vscode-textCodeBlock-background);
        padding: 1px 5px;
        border-radius: 3px;
        font-family: var(--vscode-editor-font-family, monospace);
      }
      .banner {
        display: flex;
        gap: 10px;
        align-items: flex-start;
        margin: 10px 14px 0;
        padding: 9px 12px;
        border: 1px solid var(--vscode-inputValidation-infoBorder, var(--vscode-panel-border));
        border-left-width: 3px;
        border-radius: 4px;
        background: var(--vscode-inputValidation-infoBackground, transparent);
      }
      .banner .text {
        flex: 1;
        min-width: 0;
        line-height: 1.5;
      }
      .banner strong {
        font-weight: 600;
      }
      .row button.lock {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        padding: 4px 6px;
        line-height: 0;
      }
      .badge {
        font-size: 10px;
        letter-spacing: 0.06em;
        text-transform: uppercase;
        color: var(--vscode-descriptionForeground);
        padding-top: 2px;
      }
    `,
  ];

  override render() {
    return html`${this.renderRelease()}${this.renderBody()}`;
  }

  /**
   * "A newer template release exists" — the one thing a moved source cannot
   * tell you, because an unchanged ref never re-resolves to a new tag.
   */
  private renderRelease() {
    const row = releaseSource(store.releases?.sources ?? []);
    const notice = releaseNotice(row);
    const latest = row?.latest;
    if (notice && row && latest) {
      return html`
        <div class="banner">
          <span class="badge">release</span>
          <div class="text">
            <strong>${notice.label}</strong>
            <div class="muted">${notice.detail}</div>
          </div>
          <button class="primary" ?disabled=${store.loading}
            title="Pins the source to this release and replaces the installed files"
            @click=${() => void store.upgradeAsync(row.id, latest)}>Update to ${latest}</button>
          <button class="ghost" ?disabled=${store.loading}
            @click=${() => void store.refreshReleasesAsync()}>Re-check</button>
        </div>
      `;
    }
    if (store.releaseError) {
      return html`
        <div class="banner">
          <span class="badge">release</span>
          <div class="text muted">Could not check for newer releases: ${store.releaseError}</div>
          <button class="ghost" @click=${() => void store.refreshReleasesAsync()}>Try again</button>
        </div>
      `;
    }
    if (store.releases?.offline) {
      return html`
        <div class="banner">
          <span class="badge">release</span>
          <div class="text muted">Offline — newer releases were not checked.</div>
        </div>
      `;
    }
    return "";
  }

  private renderBody() {
    const updates = store.updates;
    if (!updates) {
      return html`<empty-state heading="Checking upstream…"
        hint="Re-reading the pinned source ref."></empty-state>`;
    }

    if (!updates.remote) {
      return html`
        <empty-state
          heading="This project has no remote source"
          hint="Updates compare the catalog you install from against what is on disk. Add a pinned remote source to .ai/ai.json to make this view meaningful.">
          <p class="mono muted">
            "sources": { "template": { "repo": "I-Synergy/AI", "ref": "v1.6.0", "resolve": "remote" } }
          </p>
          <button class="secondary" @click=${() => void store.refreshAsync(true)}>Re-check anyway</button>
        </empty-state>
      `;
    }

    const held = updates.locked ?? [];
    const total = updates.ready.length + updates.blocked.length + held.length;
    if (total === 0) {
      const notice = emptyUpdateNotice(updates.remoteSelected);
      return html`
        <empty-state heading=${notice.heading} hint=${notice.hint}>
          <button class="secondary" @click=${() => void store.refreshUpdatesAsync()}>Check again</button>
        </empty-state>
      `;
    }

    const filtered = filterUpdates(updates.ready, updates.blocked, {
      query: store.updatesQuery,
      kind: store.updatesKind,
      state: store.updatesState,
      source: store.updatesSource,
    });
    const ready = filtered.ready;
    const blocked = filtered.blocked;
    const filtering = store.updatesQuery !== "" || store.updatesKind !== "all"
      || store.updatesState !== "all" || store.updatesSource !== "all";
    const readySection = ready.length
      ? html`<h3>Ready to update (${ready.length})</h3>
          ${groupBySource(ready, this.sourceOrder()).map(
            (source) => html`
              <h4>${source.label} (${source.items.length})</h4>
              ${source.items.map((row) => this.renderRow(row, false))}
            `,
          )}`
      : "";
    const blockedSection = blocked.length
      ? html`<h3>Changed here and upstream (${blocked.length})</h3>
          <p class="muted" style="margin: 0 14px 8px;">
            These were edited locally. Updating would overwrite those edits.
          </p>
          ${groupBySource(blocked, this.sourceOrder()).map(
            (source) => html`
              <h4>${source.label} (${source.items.length})</h4>
              ${source.items.map((row) => this.renderRow(row, true))}
            `,
          )}`
      : "";
    const heldSection = held.length
      ? html`<h3>Locked (${held.length})</h3>
          <p class="muted" style="margin: 0 14px 8px;">
            You locked these, so nothing here — not even Update all safe —
            will overwrite them. Unlock one to let the catalog move it again.
          </p>
          ${groupBySource(held, this.sourceOrder()).map(
            (source) => html`
              <h4>${source.label} (${source.items.length})</h4>
              ${source.items.map((row) => this.renderLockedRow(row))}
            `,
          )}`
      : "";

    return html`
      ${this.renderFilters(ready.length + blocked.length + held.length, updates)}
      <div class="summary">
        <strong>${ready.length} update(s)</strong>
        ${blocked.length ? html`<span class="muted">${blocked.length} conflict(s)</span>` : ""}
        ${held.length ? html`<span class="muted">${held.length} locked</span>` : ""}
        <span class="grow"></span>
        <button class="ghost" ?disabled=${store.loading}
          @click=${() => void store.refreshUpdatesAsync()}>Refresh</button>
        <button class="primary" ?disabled=${store.loading || ready.length === 0}
          @click=${() => void store.updateRows(ready.map((row) => row.id))}>
          ${filtering ? `Update ${ready.length} shown` : "Update all safe"}
        </button>
      </div>
      ${ready.length === 0 && blocked.length === 0 && held.length === 0
        ? html`
            <empty-state heading="No updates match these filters"
              hint="Clear a filter or search for something else.">
              <button class="secondary" @click=${() => store.clearUpdateFilters()}>Clear filters</button>
            </empty-state>
          `
        : html`${readySection}${blockedSection}${heldSection}`}
    `;
  }

  /** The same filter set Browse offers, with update-specific state options. */
  private renderFilters(count: number, updates: UpdatePayload) {
    const rows = [...updates.ready, ...updates.blocked, ...(updates.locked ?? [])];
    const sources = ["all", ...new Set(rows.map((row) => row.source))];
    const filtering = store.updatesQuery !== "" || store.updatesKind !== "all"
      || store.updatesState !== "all" || store.updatesSource !== "all";

    return html`
      <div class="toolbar">
        <input
          type="search"
          placeholder="Search updates…"
          .value=${store.updatesQuery}
          @input=${(event: Event) => {
            store.updatesQuery = (event.target as HTMLInputElement).value;
            this.requestUpdate();
          }}
        />
        <select
          .value=${store.updatesKind}
          @change=${(event: Event) => {
            store.updatesKind = (event.target as HTMLSelectElement).value as never;
            this.requestUpdate();
          }}
        >
          <option value="all">All kinds</option>
          <option value="skill">Skills</option>
          <option value="agent">Agents</option>
          <option value="instruction">Instructions</option>
          <option value="prompt">Prompts</option>
          <option value="plugin">Plugins</option>
          <option value="hook">Hooks</option>
        </select>
        <select
          .value=${store.updatesState}
          @change=${(event: Event) => {
            store.updatesState = (event.target as HTMLSelectElement).value as never;
            this.requestUpdate();
          }}
        >
          <option value="all">Any state</option>
          <option value="ready">Ready to update</option>
          <option value="conflict">Conflicts</option>
        </select>
        <select
          .value=${store.updatesSource}
          @change=${(event: Event) => {
            store.updatesSource = (event.target as HTMLSelectElement).value;
            this.requestUpdate();
          }}
        >
          ${sources.map((source) =>
            html`<option value=${source}>${source === "all" ? "All sources" : sourceLabel(source)}</option>`)}
        </select>
        <span class="mode">${pluralise(count, "item")}</span>
        ${filtering
          ? html`<button class="ghost" @click=${() => store.clearUpdateFilters()}>Clear</button>`
          : ""}
      </div>
    `;
  }

  /** The manifest's source order, so headings do not reshuffle with the data. */
  private sourceOrder(): string[] {
    return (store.browse?.sources ?? []).map((source) => source.id);
  }

  private renderRow(
    row: UpdateRow,
    conflict: boolean,
  ) {
    return html`
      <div class="row">
        <span class="muted" title=${row.kind}>${kindIcon(row.kind)}</span>
        <span class="name mono">${row.displayId}</span>
        <span class="muted">${row.path}</span>
        <state-pill .status=${row.status}></state-pill>
        ${conflict
          ? html`<button class="ghost" ?disabled=${store.loading}
              @click=${() => void store.updateOne(row.id, true)}>Overwrite mine</button>`
          : html`<button class="secondary" ?disabled=${store.loading}
              @click=${() => void store.updateOne(row.id)}>Update</button>`}
      </div>
    `;
  }

  /** A frozen row: nothing updates it, so the only action is to unfreeze it. */
  private renderLockedRow(row: UpdateRow) {
    return html`
      <div class="row">
        <span class="muted" title=${row.kind}>${kindIcon(row.kind)}</span>
        <span class="name mono">${row.displayId}</span>
        <span class="muted">${row.path}</span>
        <span class="pill warn">locked</span>
        <button class="ghost lock" ?disabled=${store.loading}
          title="Locked — click to unlock and let updates touch this item again"
          aria-label="Unlock item"
          @click=${() => void store.setLocked([row.id], false)}>${lockIcon(true)}</button>
      </div>
    `;
  }
}
customElements.define("updates-view", UpdatesView);
