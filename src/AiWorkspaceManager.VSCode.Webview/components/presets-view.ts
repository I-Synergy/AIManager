import { LitElement, css, html } from "lit";
import { presetDelta, pluralise } from "../logic";
import { store } from "../store";
import { sharedStyles } from "../styles/shared.css";
import "./parts";

/**
 * Presets: the new-repository story. Recommended preset first-class, `Everything`
 * one click away and labelled with its real size — the resolver exists because
 * installing everything is expensive, so the UI must not hide that fact.
 */
export class PresetsView extends LitElement {
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
        display: block;
        flex: 1;
        min-height: 0;
        overflow: auto;
        padding: 14px;
      }
      .intro {
        margin: 0 0 16px;
        max-width: 720px;
        line-height: 1.55;
        color: var(--vscode-descriptionForeground);
      }
      .grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
        gap: 12px;
      }
      .card {
        border: 1px solid var(--vscode-panel-border);
        border-radius: 5px;
        padding: 14px;
        display: flex;
        flex-direction: column;
        gap: 8px;
        background: var(--vscode-editorWidget-background, transparent);
      }
      .card.applied {
        border-color: var(--vscode-focusBorder);
        box-shadow: inset 0 0 0 1px var(--vscode-focusBorder);
      }
      .card h2 {
        margin: 0;
        font-size: 14px;
        display: flex;
        align-items: center;
        gap: 8px;
      }
      .card p {
        margin: 0;
        flex: 1;
        line-height: 1.5;
        color: var(--vscode-descriptionForeground);
        font-size: 12px;
      }
      .counts {
        display: flex;
        gap: 10px;
        font-size: 12px;
        align-items: center;
      }
      .delta {
        color: var(--vscode-charts-blue, #3794ff);
        font-weight: 600;
      }
      .footer {
        display: flex;
        gap: 8px;
        align-items: center;
      }
    `,
  ];

  override render() {
    // The payload order is the curated order in presets.json; only `Everything`
    // moves, because it is the escape hatch rather than a starting point.
    const presets = [...(store.presets?.presets ?? store.browse?.presets ?? [])]
      .sort((a, b) => Number(a.name === "Everything") - Number(b.name === "Everything"));
    const active = new Set(
      store.presets?.active
        ?? (store.presets?.current ? [store.presets.current] : []));
    const items = store.browse?.items ?? [];
    const selectedIds = new Set(items.filter((item) => item.selected).map((item) => item.id));
    const installedSkills = items.filter((item) => item.selected && item.kind === "skill").length;
    const installedAgents = items.filter((item) => item.selected && item.kind === "agent").length;

    return html`
      <p class="intro">
        A preset is a named selection — the fastest way to give a new repository a working set.
        Presets combine: adding one keeps every other preset and anything you picked in Browse.
        Nothing is deleted from disk; deselected items stay until you prune them.
      </p>
      <div class="grid">
        ${presets.map((preset) => {
          const delta = presetDelta(preset, selectedIds, {
            skills: installedSkills,
            agents: installedAgents,
          });
          const isEverything = preset.name === "Everything";
          const isRecommended = preset.recommended === true;
          const isActive = active.has(preset.name);
          return html`
            <div class="card ${isActive ? "applied" : ""}">
              <h2>
                ${preset.name}
                ${isActive ? html`<span class="pill ok">applied</span>` : ""}
                ${isRecommended && !isActive ? html`<span class="pill ok">recommended</span>` : ""}
                ${isEverything ? html`<span class="pill warn">everything</span>` : ""}
              </h2>
              <p>${preset.description || "(no description)"}</p>
              <div class="counts">
                <span>${pluralise(preset.skills, "skill")}</span>
                <span class="muted">·</span>
                <span>${pluralise(preset.agents, "agent")}</span>
                <span class="spacer" style="flex:1"></span>
                <span class="delta" title="Items this preset would add to your selection">${delta.label}</span>
              </div>
              <div class="footer">
                <button
                  class="${isActive || isRecommended ? "primary" : "secondary"}"
                  ?disabled=${store.loading || isActive}
                  @click=${() => void store.addPreset(preset.name)}
                >
                  ${isActive ? "Applied" : "Add to selection"}
                </button>
              </div>
            </div>
          `;
        })}
      </div>
    `;
  }
}
customElements.define("presets-view", PresetsView);
