import type {
  HostAPI, LogSink, Result, RpcCancel, RpcRequest, RpcResponse, WebviewLog,
} from "../types";
import { fail } from "../types";

/** Minimal surface of a VS Code webview we depend on. */
export interface WebviewLike {
  postMessage(message: unknown): Thenable<boolean> | boolean;
  onDidReceiveMessage(listener: (message: unknown) => void): { dispose(): void };
}

/**
 * The host half of the RPC layer: dispatches `rpc-request` messages to the
 * `HostAPI` implementation and posts back `rpc-response`. Errors are caught
 * here so a handler throwing can never take the webview down.
 */
export class RpcHost {
  private readonly inFlight = new Map<number, AbortController>();
  private readonly subscription: { dispose(): void };

  constructor(
    private readonly webview: WebviewLike,
    private readonly api: HostAPI,
    private readonly log?: LogSink,
  ) {
    this.subscription = webview.onDidReceiveMessage((message) => {
      void this.handleMessageAsync(message);
    });
  }

  private async handleMessageAsync(message: unknown): Promise<void> {
    if (this.isWebviewLog(message)) {
      const line = `webview ${message.level}: ${message.message}`;
      if (message.level === "error") {
        this.log?.error(line);
      } else {
        this.log?.warn(line);
      }
      return;
    }
    if (this.isCancel(message)) {
      this.inFlight.get(message.id)?.abort();
      this.inFlight.delete(message.id);
      return;
    }
    if (!this.isRequest(message)) {
      return;
    }

    const { id, method, params } = message;
    const controller = new AbortController();
    this.inFlight.set(id, controller);
    const started = Date.now();
    this.log?.debug(`→ ${method} #${id}`);

    let result: Result<unknown>;
    try {
      const handler = (this.api as unknown as Record<string, unknown>)[method];
      if (typeof handler !== "function") {
        this.log?.warn(`Unknown RPC method: ${method}`);
        result = fail(`Unknown RPC method: ${method}`);
      } else {
        result = (await (handler as (p: unknown, s: AbortSignal) => Promise<Result<unknown>>)
          .call(this.api, params, controller.signal));
      }
    } catch (error) {
      const message_ = error instanceof Error ? error.message : String(error);
      this.log?.error(`Internal error in ${method}: ${message_}`);
      result = fail(`Internal error in ${method}: ${message_}`);
    } finally {
      this.inFlight.delete(id);
    }

    const elapsed = Date.now() - started;
    if (controller.signal.aborted) {
      this.log?.info(`← ${method} #${id} cancelled after ${elapsed} ms`);
      return;
    }
    if (result.ok) {
      this.log?.debug(`← ${method} #${id} ok in ${elapsed} ms`);
    } else {
      this.log?.warn(`← ${method} #${id} failed after ${elapsed} ms: ${result.error}`);
    }
    this.webview.postMessage({ type: "rpc-response", id, result } satisfies RpcResponse);
  }

  private isWebviewLog(message: unknown): message is WebviewLog {
    return (
      typeof message === "object" && message !== null &&
      (message as WebviewLog).type === "webview-log" &&
      ((message as WebviewLog).level === "warn" || (message as WebviewLog).level === "error") &&
      typeof (message as WebviewLog).message === "string"
    );
  }

  private isCancel(message: unknown): message is RpcCancel {
    return (
      typeof message === "object" && message !== null &&
      (message as RpcCancel).type === "rpc-cancel" &&
      typeof (message as RpcCancel).id === "number"
    );
  }

  private isRequest(message: unknown): message is RpcRequest {
    return (
      typeof message === "object" && message !== null &&
      (message as RpcRequest).type === "rpc-request" &&
      typeof (message as RpcRequest).id === "number" &&
      typeof (message as RpcRequest).method === "string"
    );
  }

  dispose(): void {
    for (const controller of this.inFlight.values()) {
      controller.abort();
    }
    this.inFlight.clear();
    this.subscription.dispose();
  }
}
