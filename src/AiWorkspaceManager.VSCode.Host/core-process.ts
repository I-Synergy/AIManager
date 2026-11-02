/**
 * The singleton .NET sidecar of the extension host.
 *
 * The published core lives at `<extensionPath>/dist/core/AiWorkspaceManager.Core.dll`. It is
 * started lazily on the first call and disposed with the extension's lifetime. A missing dll or
 * `dotnet` surfaces as a CoreError("unavailable") — never as a crash at activation.
 */

import { existsSync } from "node:fs";
import * as nodePath from "node:path";
import type { LogSink } from "../AiWorkspaceManager.VSCode.Common/types";
import { CoreClient, CoreError } from "./core-client";
import { RPC_TIMEOUT_MS } from "../AiWorkspaceManager.VSCode.Common/types";

let client: CoreClient | undefined;
let extensionPath: string | undefined;
let log: LogSink | undefined;

export function Configure(extensionDir: string, logger: LogSink): void {
  extensionPath = extensionDir;
  log = logger;
}

export function corePath(): string {
  if (!extensionPath) {
    throw new CoreError("unavailable", "The .NET core was not configured during activation.");
  }
  return nodePath.join(extensionPath, "dist", "core", "AiWorkspaceManager.Core.dll");
}

export function isAvailable(): boolean {
  try {
    return existsSync(corePath());
  } catch {
    return false;
  }
}

/** Sends one request to the sidecar; the workspace root is supplied by the caller. */
export async function CallAsync<T>(
  method: string,
  params: unknown,
  signal?: AbortSignal,
): Promise<T> {
  const dll = corePath();
  if (!existsSync(dll)) {
    throw new CoreError(
      "unavailable",
      `The .NET core is missing at ${dll}. Run 'npm run build:core' before packaging the extension.`,
    );
  }

  client ??= new CoreClient({
    executable: "dotnet",
    arguments: [dll],
    env: {
      ...process.env,
      CATALOG_TOOLING: nodePath.join(extensionPath!, "tooling"),
    },
    requestTimeoutMs: RPC_TIMEOUT_MS,
    onStderr: (line) => log?.info(`[core] ${line}`),
  });

  return client.CallAsync<T>(method, params, signal);
}

export function IsRunning(): boolean {
  return client?.IsRunning ?? false;
}

export function Dispose(): void {
  client?.Dispose();
  client = undefined;
}
