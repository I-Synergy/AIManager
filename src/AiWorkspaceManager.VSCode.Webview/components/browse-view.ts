import { LitElement, css, html } from "lit";
import type { CatalogItem } from "../../AiWorkspaceManager.VSCode.Common/types";
import { displayId, filterItems, groupByCategory, idsToInstall, itemKey, pickState, pluralise, sourceLabel, toggleAll } from "../logic";
import { hostApi } from "../registrations";
import { store } from "../store";
import { sharedStyles } from "../styles/shared.css";
import { kindIcon } from "./parts";

/**
 * Browse: the catalog. Filters in the toolbar, a select-all header above the
 * list, a detail pane on the right.
 *
 * Two ways to install in bulk, both deliberate: tick individual rows (works
 * across filters, since filtering never drops a pick) or install everything the
 * current filter matches — the latter is the "install everything" path.
 */
export class BrowseView extends LitElement {
  private readonly onChange = (): void => this.requestUpdate();
  /** Categories the user has collapsed; the rest render open. */
  private readonly collapsed = new Set<string>();

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
        min-height: 0;
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
        flex: 1 1 240px;
        min-width: 160px;
      }
      .spacer {
        flex: 1;
      }
      /* select-all row: aligned with the checkbox column below */
      .listhead {
        display: flex;
        gap: 10px;
        align-items: center;
        padding: 5px 14px;
        border-bottom: 1px solid var(--vscode-panel-border);
        background: var(--vscode-sideBarSectionHeader-background, transparent);
        flex: 0 0 auto;
      }
      .listhead input[type="checkbox"] {
        width: 14px;
        height: 14px;
        margin: 0 6px 0 1px;
        accent-color: var(--vscode-focusBorder);
      }
      .listhead button {
        padding: 3px 10px;
        flex: 0 0 auto;
        white-space: nowrap;
      }
      .split {
        display: flex;
        min-height: 0;
        flex: 1;
      }
      .list {
        flex: 1;
        overflow: auto;
        margin: 0;
        padding: 0;
        list-style: none;
        min-width: 0;
      }
      .row {
        display: grid;
        grid-template-columns: 20px 16px minmax(150px, 250px) 1fr 118px;
        gap: 10px;
        align-items: center;
        padding: 6px 14px;
        border-bottom: 1px solid color-mix(in srgb, var(--vscode-panel-border) 45%, transparent);
        cursor: pointer;
      }
      .row:hover {
        background: var(--vscode-list-hoverBackground);
      }
      .row.selected {
        background: var(--vscode-list-activeSelectionBackground);
        color: var(--vscode-list-activeSelectionForeground);
      }
      .row.selected .desc,
      .row.selected .mono {
        color: inherit;
        opacity: 0.85;
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
      .desc {
        color: var(--vscode-descriptionForeground);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .detail {
        width: 340px;
        flex: 0 0 340px;
        border-left: 1px solid var(--vscode-panel-border);
        padding: 14px 16px;
        overflow: auto;
      }
      .detail h2 {
        margin: 0 0 2px;
        font-size: 14px;
      }
      .detail .path {
        color: var(--vscode-descriptionForeground);
        font-size: 11.5px;
        margin-bottom: 12px;
        word-break: break-all;
      }
      .detail p.description {
        line-height: 1.55;
        margin: 0 0 14px;
      }
      dl {
        display: grid;
        grid-template-columns: 74px 1fr;
        gap: 4px 10px;
        margin: 0 0 14px;
        font-size: 12px;
      }
      dt {
        color: var(--vscode-descriptionForeground);
      }
      dd {
        margin: 0;
        word-break: break-word;
      }
      .actions {
        display: flex;
        gap: 8px;
        flex-wrap: wrap;
      }
      a {
        color: var(--vscode-textLink-foreground);
      }
      .count {
        color: var(--vscode-descriptionForeground);
        font-size: 12px;
      }
      .synced {
        color: var(--vscode-charts-green, #89d185);
        font-weight: 700;
      }
      .group-head {
        display: flex;
        gap: 8px;
        align-items: baseline;
        padding: 5px 14px;
        position: sticky;
        top: 0;
        z-index: 1;
        cursor: pointer;
        background: var(--vscode-sideBarSectionHeader-background, var(--vscode-editor-background));
        border-bottom: 1px solid color-mix(in srgb, var(--vscode-panel-border) 70%, transparent);
        font-size: 11px;
        letter-spacing: 0.05em;
        text-transform: uppercase;
        color: var(--vscode-descriptionForeground);
        user-select: none;
      }
      .group-head:hover {
        color: var(--vscode-foreground);
      }
      .group-label {
        font-weight: 600;
      }
      .chev {
        width: 10px;
        display: inline-block;
      }
    `,
  ];

  override render() {
    const browse = store.browse;
    if (!browse) {
      return html`<empty-state heading="Reading the catalog…"
        hint="Reading the catalog with the .NET resolver."></empty-state>`;
    }

    const filtered = filterItems(browse.items, {
      query: store.query,
      kind: store.kindFilter,
      state: store.stateFilter,
      source: store.sourceFilter,
    });
    const installable = idsToInstall(filtered);
    // Only rows that are not in the selection yet can be ticked to install.
    const selectableIds = filtered.filter((item) => !item.selected).map((item) => item.id);
    const headerState = pickState(selectableIds, store.picked);
    const pickedCount = store.actionablePicked(false).length;
    const installLabel = installable.length
      ? `Install all ${installable.length} shown`
      : "Everything shown is installed";
    const selected = browse.items.find((item) => itemKey(item) === store.selectedKey) ?? filtered[0];
    const sources = ["all", ...new Set(browse.items.map((item) => item.source))];

    return html`
      <div class="toolbar">
        <input
          type="search"
          placeholder="Search skills and agents…"
          .value=${store.query}
          @input=${(event: Event) => {
            store.query = (event.target as HTMLInputElement).value;
            this.requestUpdate();
          }}
        />
        <select
          .value=${store.kindFilter}
          @change=${(event: Event) => {
            store.kindFilter = (event.target as HTMLSelectElement).value as never;
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
          .value=${store.stateFilter}
          @change=${(event: Event) => {
            store.stateFilter = (event.target as HTMLSelectElement).value as never;
            this.requestUpdate();
          }}
        >
          <option value="all">Any state</option>
          <option value="installed">Installed</option>
          <option value="available">Not installed</option>
          <option value="needs-attention">Needs attention</option>
        </select>
        <select
          .value=${store.sourceFilter}
          @change=${(event: Event) => {
            store.sourceFilter = (event.target as HTMLSelectElement).value;
            this.requestUpdate();
          }}
        >
          ${sources.map((source) => html`<option value=${source}>${source === "all" ? "All sources" : sourceLabel(source)}</option>`)}
        </select>
        <span class="count">${pluralise(filtered.length, "item")}</span>
      </div>

      <div class="listhead">
        <input
          type="checkbox"
          title="Select every item shown"
          .checked=${headerState === "all"}
          .indeterminate=${headerState === "some"}
          @change=${(event: Event) =>
            store.setPicked(toggleAll(selectableIds, store.picked, (event.target as HTMLInputElement).checked))}
        />
        <span class="count">${pluralise(filtered.length, "item")} shown</span>
        <span class="spacer"></span>
        ${pickedCount > 0
          ? html`
              <span class="count">${pluralise(pickedCount, "item")} ticked</span>
              <button class="primary" ?disabled=${store.loading || pickedCount === 0}
                @click=${() => void store.installPickedAsync()}>
                Install ${pickedCount || ""} ticked
              </button>
              <button class="ghost" @click=${() => store.clearPicked()}>Clear</button>
            `
          : html`
              <button class="primary" ?disabled=${installable.length === 0 || store.loading}
                @click=${() => void store.install(installable)}>
                ${installLabel}
              </button>
            `}
      </div>

      <div class="split">
        <ul class="list">
          ${groupByCategory(filtered, browse.categories).map((group) => html`
            <li class="group-head" @click=${() => this.toggleGroup(group.id)}>
              <span class="chev">${this.collapsed.has(group.id) ? "\u25B8" : "\u25BE"}</span>
              <span class="group-label">${group.label}</span>
              <span class="count">${group.items.length}</span>
            </li>
            ${this.collapsed.has(group.id)
              ? ""
              : group.items.map((item) => this.renderRow(item))}
          `)}
        </ul>
        ${selected ? this.renderDetail(selected) : html`<empty-state heading="Nothing matches"
          hint="Clear the filters to see the catalog again."></empty-state>`}
      </div>
    `;
  }

  private toggleGroup(id: string): void {
    if (this.collapsed.has(id)) {
      this.collapsed.delete(id);
    } else {
      this.collapsed.add(id);
    }
    this.requestUpdate();
  }

  private renderRow(item: CatalogItem) {
    const ticked = store.picked.includes(item.id);
    return html`
      <li
        class="row ${itemKey(item) === store.selectedKey ? "selected" : ""}"
        @click=${() => {
          store.selectedKey = itemKey(item);
          this.requestUpdate();
        }}
      >
        <input
          type="checkbox"
          .checked=${ticked}
          ?disabled=${item.selected}
          title=${item.selected
            ? "Already in this repository's selection — manage it in Installed"
            : "Tick for a bulk action"}
          @click=${(event: Event) => event.stopPropagation()}
          @change=${(event: Event) => {
            event.stopPropagation();
            store.togglePick(item.id);
          }}
        />
        <span class="muted" title=${item.kind}>${kindIcon(item.kind)}</span>
        <span class="name mono">${item.displayId}</span>
        <span class="desc">${item.description}</span>
        ${this.renderState(item)}
      </li>
    `;
  }

  /**
   * One state column, quiet by default: a check for healthy items, a pill for
   * anything that needs a decision, nothing for plain catalog entries.
   */
  private renderState(item: CatalogItem) {
    if ((item.selected || item.installed) && item.status === "ok") {
      const why = item.requiredBy?.length
        ? ` (required by ${item.requiredBy.map((id) => displayId(id)).join(", ")})`
        : "";
      return html`<span class="synced" title="Installed and in sync${why}">&#10003;</span>`;
    }
    if (!item.selected && !item.installed) {
      return html`<span></span>`;
    }
    return html`<state-pill .status=${item.status} .selected=${item.selected}></state-pill>`;
  }

  private renderDetail(item: CatalogItem) {
    return html`
      <aside class="detail">
        <h2 class="mono">${item.displayId}</h2>
        <div class="path">${item.path}</div>
        <p class="description">${item.description || "(no description)"}</p>
        <dl>
          <dt>Kind</dt>
          <dd>${item.kind}</dd>
          <dt>Source</dt>
          <dd>${item.source} @ ${item.ref}</dd>
          <dt>License</dt>
          <dd>${item.license}</dd>
          <dt>Files</dt>
          <dd>${item.files}</dd>
          <dt>Hash</dt>
          <dd class="mono">${item.sha256.slice(0, 12)}</dd>
          <dt>Upstream</dt>
          <dd><a href=${item.upstream} @click=${this.openUpstream}>repository path</a></dd>
          ${item.requires?.length
            ? html`<dt>Requires</dt>
                <dd>${item.requires.map((id) => displayId(id)).join(", ")}</dd>`
            : ""}
          ${item.requiredBy?.length
            ? html`<dt>Required by</dt>
                <dd>${item.requiredBy.map((id) => displayId(id)).join(", ")}</dd>`
            : ""}
        </dl>
        <div class="actions">
          ${item.selected
            ? html`<button class="secondary" ?disabled=${store.loading}
                @click=${() => void store.remove([item.id])}>Remove from project</button>`
            : html`<button class="primary" ?disabled=${store.loading}
                @click=${() => void store.install([item.id])}>Install</button>`}
          <button class="ghost" @click=${() => void this.openFile(item)}>Open</button>
        </div>
      </aside>
    `;
  }

  private openUpstream(event: Event): void {
    event.preventDefault();
    const href = (event.currentTarget as HTMLAnchorElement).href;
    void hostApi.openUpstream({ Url: href });
  }

  private async openFile(item: CatalogItem): Promise<void> {
    const path = item.kind === "skill" ? `${item.path}/SKILL.md` : item.path;
    await hostApi.openFile({ Path: path });
  }
}
customElements.define("browse-view", BrowseView);
