import { css } from "lit";

/**
 * Design language copied from the NuGet manager: only `--vscode-*` variables,
 * so the UI is native in every theme (light, dark, high contrast) without a
 * single hard-coded colour.
 */
export const sharedStyles = css`
  :host {
    display: block;
    color: var(--vscode-foreground);
    font-family: var(--vscode-font-family, "Segoe UI", sans-serif);
    font-size: 13px;
  }

  * {
    box-sizing: border-box;
  }

  button {
    font-family: inherit;
    font-size: 12px;
    border-radius: 3px;
    border: 1px solid transparent;
    padding: 5px 12px;
    cursor: pointer;
  }

  button.primary {
    background: var(--vscode-button-background);
    color: var(--vscode-button-foreground);
  }
  button.primary:hover:not(:disabled) {
    background: var(--vscode-button-hoverBackground);
  }
  button.secondary {
    background: var(--vscode-button-secondaryBackground);
    color: var(--vscode-button-secondaryForeground);
  }
  button.secondary:hover:not(:disabled) {
    background: var(--vscode-button-secondaryHoverBackground, var(--vscode-button-secondaryBackground));
  }
  button.ghost {
    background: transparent;
    color: var(--vscode-foreground);
    border-color: var(--vscode-panel-border);
  }
  button.ghost:hover:not(:disabled) {
    background: var(--vscode-list-hoverBackground);
  }
  button:disabled {
    opacity: 0.5;
    cursor: default;
  }

  input[type="search"],
  input[type="text"],
  select {
    background: var(--vscode-input-background);
    color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border, transparent);
    border-radius: 3px;
    padding: 5px 8px;
    font-family: inherit;
    font-size: 12px;
    outline: none;
  }
  input:focus,
  select:focus {
    border-color: var(--vscode-focusBorder);
  }
  input::placeholder {
    color: var(--vscode-input-placeholderForeground);
  }

  /* --- state pills --- */
  .pill {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    border-radius: 9px;
    padding: 1px 8px;
    font-size: 10.5px;
    font-weight: 600;
    letter-spacing: 0.02em;
    text-transform: uppercase;
    white-space: nowrap;
    border: 1px solid transparent;
  }
  .pill.ok {
    color: var(--vscode-charts-green, #89d185);
    border-color: color-mix(in srgb, var(--vscode-charts-green, #89d185) 45%, transparent);
  }
  .pill.available {
    color: var(--vscode-descriptionForeground);
    border-color: var(--vscode-panel-border);
  }
  .pill.update,
  .pill.upstream-changed {
    color: var(--vscode-charts-blue, #3794ff);
    border-color: color-mix(in srgb, var(--vscode-charts-blue, #3794ff) 45%, transparent);
  }
  .pill.warn,
  .pill.local-modified,
  .pill.missing {
    color: var(--vscode-charts-orange, #d18616);
    border-color: color-mix(in srgb, var(--vscode-charts-orange, #d18616) 45%, transparent);
  }
  .pill.danger,
  .pill.both {
    color: var(--vscode-errorForeground, #f14c4c);
    border-color: color-mix(in srgb, var(--vscode-errorForeground, #f14c4c) 45%, transparent);
  }
  .pill.kind {
    color: var(--vscode-descriptionForeground);
    border-color: var(--vscode-panel-border);
    text-transform: none;
    letter-spacing: 0;
  }

  .muted {
    color: var(--vscode-descriptionForeground);
  }

  .mono {
    font-family: var(--vscode-editor-font-family, monospace);
    font-size: 12px;
  }

  /* --- scrollbars --- */
  ::-webkit-scrollbar {
    width: 10px;
    height: 10px;
  }
  ::-webkit-scrollbar-track {
    background: transparent;
  }
  ::-webkit-scrollbar-thumb {
    background: var(--vscode-scrollbarSlider-background);
  }
  ::-webkit-scrollbar-thumb:hover {
    background: var(--vscode-scrollbarSlider-hoverBackground);
  }

  .spinner {
    width: 14px;
    height: 14px;
    border: 2px solid var(--vscode-progressBar-background);
    border-top-color: transparent;
    border-radius: 50%;
    display: inline-block;
    animation: spin 0.9s linear infinite;
  }
  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }
`;
