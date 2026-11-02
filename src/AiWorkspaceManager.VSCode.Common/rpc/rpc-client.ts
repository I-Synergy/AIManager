import type { HostAPI, Result, RpcCancel, RpcRequest, RpcResponse } from "../types";

type PendingCall = {
  resolve: (result: Result<unknown>) => void;
  timer: ReturnType<typeof setTimeout>;
};

/**
 * The webview half of the RPC layer: a Proxy typed as `HostAPI` where every
 * property becomes a request. Matches the reference extension's protocol, so
 * the host side (`RpcHost`) is a straight port too.
 */
export function createRpcClient(
  postMessage: (msg: unknown) => void,
  timeoutMs: number,
): HostAPI {
  let nextId = 1;
  const pending = new Map<number, PendingCall>();

  window.addEventListener("message", (event: MessageEvent) => {
    // Only the embedding VS Code host may answer RPC calls. VS Code relays host
    // messages from the webview's own origin, but it also rewrites
    // `window.parent` to point at the webview itself (see the webview preload's
    // `window.parent = window`), so `event.source` is *not* `window.parent` and
    // checking it here drops every response — the UI then hangs until timeout.
    if (event.origin !== window.location.origin) {
      return;
    }
    const message = event.data as RpcResponse;
    if (message?.type !== "rpc-response") {
      return;
    }
    const call = pending.get(message.id);
    if (!call) {
      return;
    }
    clearTimeout(call.timer);
    pending.delete(message.id);
    call.resolve(message.result);
  });

  function call(method: string, params: unknown): Promise<Result<unknown>> {
    return new Promise<Result<unknown>>((resolve) => {
      const id = nextId++;
      const timer = setTimeout(() => {
        pending.delete(id);
        postMessage({ type: "rpc-cancel", id } satisfies RpcCancel);
        // The host log gets this through the webview-log bridge, so a silent
        // host (a dropped response, a hung resolver) is visible in Output.
        console.warn(`RPC call ${method} timed out after ${timeoutMs} ms`);
        resolve({ ok: false, error: `Timed out after ${timeoutMs} ms calling ${method}` });
      }, timeoutMs);

      pending.set(id, { resolve, timer });
      postMessage({ type: "rpc-request", id, method, params } satisfies RpcRequest);
    });
  }

  return new Proxy({} as HostAPI, {
    get(_target, property: string) {
      return (params: unknown) => call(property, params ?? {});
    },
  });
}
