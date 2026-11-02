import { LitElement, css, html } from "lit";
import type { CatalogItem } from "../../AiWorkspaceManager.VSCode.Common/types";
import {
  filterInstalled,
  groupBySource,
  groupInstalled,
  isAttention,
  pickState,
  pluralise,
  sourceLabel,
  toggleAll,
} from "../logic";
import { store } from "../store";
import { sharedStyles } from "../styles/shared.css";
import { kindIcon, lockIcon } from "./parts";

/**
 * Installed: what this repository actually has, grouped by state.
 *
 * Rows can be ticked for a bulk remove, which edits `.ai/ai.json` only — the
 * files stay until Prune, matching the resolver's rule that nothing the lock
 * does not record is ever deleted.
 */
export class InstalledView extends LitElement {
  private readonly onChange = (): void => this.requestUpdate();

  override connectedCallback(): void {
    super.connectedCallback();
    store.addEventListener("changed", this.onChange);
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
      }
      .summary {
        display: flex;
        align-items: center;
        gap: 10px;
        padding: 10px 14px;
        border-bottom: 1px solid var(--vscode-panel-border);
        flex: 0 0 auto;
        flex-wrap: wrap;
      }
      .summary input[type="checkbox"] {
        width: 14px;
        height: 14px;
        margin: 0;
        accent-color: var(--vscode-focusBorder);
      }
      .summary .grow {
        flex: 1;
      }
      .summary button {
        flex: 0 0 auto;
        white-space: nowrap;
      }
      .mode {
        color: var(--vscode-descriptionForeground);
        font-size: 12px;
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
      .scroll {
        flex: 1;
        min-height: 0;
        overflow: auto;
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
      .row {
        display: grid;
        grid-template-columns: 20px 16px minmax(120px, 240px) 1fr auto auto auto;
        gap: 10px;
        align-items: center;
        padding: 6px 14px;
        border-bottom: 1px solid color-mix(in srgb, var(--vscode-panel-border) 45%, transparent);
      }
      .row:hover {
        background: var(--vscode-list-hoverBackground);
      }
      .row input[type="checkbox"] {
        width: 14px;
        height: 14px;
        margin: 0;
        accent-color: var(--vscode-focusBorder);
        cursor: pointer;
      }
      .name {
        font-weight: 600;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .path {
        color: var(--vscode-descriptionForeground);
        font-size: 11.5px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .row button {
        opacity: 0;
      }
      .row:hover button {
        opacity: 1;
      }
      /* Lock state is information, not a hover-only action. */
      .row button.lock {
        opacity: 1;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        padding: 4px 6px;
        line-height: 0;
      }
    `,
  ];

  override render() {
    const browse = store.browse;
    if (!browse) {
      return html`<empty-state heading="No selection yet"
        hint="Open Presets to install a curated set, or Browse to pick items one by one."></empty-state>`;
    }

    const installed = browse.items.filter((item) => item.selected);
    if (installed.length === 0) {
      return html`
        <empty-state heading="Nothing installed in this repository"
          hint="Use Presets to install a curated set in one click, or Browse and tick what you need.">
          <button class="primary" @click=${() => store.selectTab("presets")}>Choose a preset</button>
        </empty-state>
      `;
    }

    const filtered = filterInstalled(browse.items, {
      query: store.installedQuery,
      kind: store.installedKind,
      state: store.installedState,
      source: store.installedSource,
    });
    const groups = groupInstalled(filtered);
    const attention = filtered.filter((item) => isAttention(item.status)).length;
    const pending = filtered.filter((item) => item.status === "not-installed").length;
    let health = "all in sync";
    if (attention > 0) {
      health = `${attention} need attention`;
    } else if (pending > 0) {
      health = `${pending} not materialised`;
    }
    const deselectedOnDisk = browse.items.filter(
      (item) => !item.selected && item.installed).length;

    const visibleIds = filtered.map((item) => item.id);
    const headerState = pickState(visibleIds, store.picked);
    const pickedCount = store.actionablePicked(true).length;
    const count = filtered.length === installed.length
      ? pluralise(installed.length, "item")
      : `${filtered.length} of ${pluralise(installed.length, "item")}`;
    const pruneButton = deselectedOnDisk > 0
      ? html`<button class="danger ghost" ?disabled=${store.loading}
          title="Deletes files for items no longer selected"
          @click=${() => void store.prune()}>Prune ${deselectedOnDisk}</button>`
      : "";

    return html`
      ${this.renderFilters(filtered.length)}
      <div class="summary">
        <input
          type="checkbox"
          title="Select every installed item shown"
          .checked=${headerState === "all"}
          .indeterminate=${headerState === "some"}
          @change=${(event: Event) =>
            store.setPicked(toggleAll(visibleIds, store.picked, (event.target as HTMLInputElement).checked))}
        />
        <strong>${count}</strong>
        <span class="muted">${health}</span>
        <span class="grow"></span>
        ${store.picked.length > 0
          ? html`
              <span class="mode">${pluralise(store.picked.length, "item")} ticked</span>
              <button class="secondary" ?disabled=${store.loading || pickedCount === 0}
                title="Removes them from .ai/ai.json; files stay on disk"
                @click=${() => void store.removePickedAsync(false)}>
                Remove ${pickedCount || ""} ticked
              </button>
              <button class="ghost" @click=${() => store.clearPicked()}>Clear</button>
            `
          : html`
              <span class="mode">materialize: ${browse.materialize}</span>
              <button class="secondary" ?disabled=${store.loading} @click=${() => void store.sync()}>Sync</button>
              ${pruneButton}
            `}
      </div>
      ${filtered.length === 0
        ? html`
            <empty-state heading="No installed items match these filters"
              hint="Clear a filter or search for something else.">
              <button class="secondary" @click=${() => store.clearInstalledFilters()}>Clear filters</button>
            </empty-state>
          `
        : html`
            <div class="scroll">
              ${groups.map(
                (group) => html`
                  <h3>${group.label} (${group.items.length})</h3>
                  ${groupBySource(group.items, this.sourceOrder()).map(
                    (source) => html`
                      <h4>${source.label} (${source.items.length})</h4>
                      ${source.items.map((item) => this.renderRow(item))}
                    `,
                  )}
                `,
              )}
            </div>
          `}
    `;
  }

  /** The same filter set Browse offers, with installed-specific state options. */
  private renderFilters(count: number) {
    const items = store.browse?.items ?? [];
    const sources = ["all", ...new Set(
      items.filter((item) => item.selected).map((item) => item.source))];
    const filtering = store.installedQuery !== "" || store.installedKind !== "all"
      || store.installedState !== "all" || store.installedSource !== "all";

    return html`
      <div class="toolbar">
        <input
          type="search"
          placeholder="Search installed items…"
          .value=${store.installedQuery}
          @input=${(event: Event) => {
            store.installedQuery = (event.target as HTMLInputElement).value;
            this.requestUpdate();
          }}
        />
        <select
          .value=${store.installedKind}
          @change=${(event: Event) => {
            store.installedKind = (event.target as HTMLSelectElement).value as never;
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
          .value=${store.installedState}
          @change=${(event: Event) => {
            store.installedState = (event.target as HTMLSelectElement).value as never;
            this.requestUpdate();
          }}
        >
          <option value="all">Any state</option>
          <option value="in-sync">In sync</option>
          <option value="needs-attention">Needs attention</option>
          <option value="locked">Locked</option>
          <option value="pending">Not materialised</option>
        </select>
        <select
          .value=${store.installedSource}
          @change=${(event: Event) => {
            store.installedSource = (event.target as HTMLSelectElement).value;
            this.requestUpdate();
          }}
        >
          ${sources.map((source) =>
            html`<option value=${source}>${source === "all" ? "All sources" : sourceLabel(source)}</option>`)}
        </select>
        <span class="mode">${pluralise(count, "item")}</span>
        ${filtering
          ? html`<button class="ghost" @click=${() => store.clearInstalledFilters()}>Clear</button>`
          : ""}
      </div>
    `;
  }

  /** The manifest's source order, so headings do not reshuffle with the data. */
  private sourceOrder(): string[] {
    return (store.browse?.sources ?? []).map((source) => source.id);
  }

  private renderRow(item: CatalogItem) {
    const ticked = store.picked.includes(item.id);
    return html`
      <div class="row">
        <input
          type="checkbox"
          .checked=${ticked}
          title="Tick for a bulk action"
          @change=${() => store.togglePick(item.id)}
        />
        <span class="muted" title=${item.kind}>${kindIcon(item.kind)}</span>
        <span class="name mono">${item.displayId}</span>
        <span class="path">${item.path}</span>
        ${item.status === "ok" ? html`<span></span>` : html`
          <state-pill .status=${item.status} .selected=${true}></state-pill>`}
        <button class="ghost lock" ?disabled=${store.loading}
          title=${item.locked
            ? "Locked — click to unlock and follow the catalog again"
            : "Lock this item so updates and sync never overwrite your edits"}
          aria-label=${item.locked ? "Unlock item" : "Lock item"}
          @click=${() => void store.setLocked([item.id], !item.locked)}>
          ${lockIcon(item.locked)}
        </button>
        <button class="ghost" ?disabled=${store.loading}
          @click=${() => void store.remove([item.id], true)}>Remove</button>
      </div>
    `;
  }
}
customElements.define("installed-view", InstalledView);
