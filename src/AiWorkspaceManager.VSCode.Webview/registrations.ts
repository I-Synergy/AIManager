import { createRpcClient } from "../AiWorkspaceManager.VSCode.Common/rpc/rpc-client";
import { RPC_TIMEOUT_MS, type HostAPI, type WebviewLog } from "../AiWorkspaceManager.VSCode.Common/types";
import Router from "./router";

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };

const vscode = acquireVsCodeApi();

/**
 * Mirror console warnings/errors and uncaught failures into the extension's
 * output channel. The webview console is only reachable through Developer
 * Tools, which made a stuck UI impossible to diagnose from VS Code itself.
 */
function describe(value: unknown): string {
  if (value instanceof Error) {
    return value.stack ?? `${value.name}: ${value.message}`;
  }
  if (typeof value === "string") {
    return value;
  }
  try {
    return JSON.stringify(value) ?? String(value);
  } catch {
    return String(value);
  }
}

function report(level: WebviewLog["level"], ...args: unknown[]): void {
  try {
    vscode.postMessage({
      type: "webview-log",
      level,
      message: args.map(describe).join(" "),
    } satisfies WebviewLog);
  } catch {
    // The panel is gone (a disposed webview); the console still has the line.
  }
}

for (const level of ["warn", "error"] as const) {
  const original = console[level].bind(console);
  console[level] = (...args: unknown[]): void => {
    original(...args);
    report(level, ...args);
  };
}

window.addEventListener("error", (event) => {
  report("error", `Uncaught error: ${event.message} (${event.filename}:${event.lineno}:${event.colno})`);
});
window.addEventListener("unhandledrejection", (event) => {
  report("error", "Unhandled rejection:", event.reason);
});

/** The one place the webview talks to the extension host. */
export const hostApi: HostAPI = createRpcClient((msg) => vscode.postMessage(msg), RPC_TIMEOUT_MS);

export const router = new Router();
