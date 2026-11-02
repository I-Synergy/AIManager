/**
 * The extension host's half of the .NET sidecar protocol.
 *
 * Responsibilities: lazy single-flight start, handshake, id matching, a per-request deadline that
 * DETACHES (never cancels work), explicit cancellation through AbortSignal, crash detection with
 * bounded restarts, stderr forwarding, and a synchronous dispose on deactivate.
 */

import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";

export type CoreErrorCode =
  | "unavailable"
  | "crashed"
  | "timeout"
  | "cancelled"
  | "protocol"
  | "internalError"
  | "invalidRequest"
  | "unknownMethod"
  | "invalidParams"
  | "resolutionFailed"
  | (string & {});

export class CoreError extends Error {
  constructor(
    public readonly code: CoreErrorCode,
    message: string,
  ) {
    super(message);
    this.name = "CoreError";
  }
}

type PendingCall = {
  resolve: (value: unknown) => void;
  reject: (error: Error) => void;
  timer: NodeJS.Timeout;
  signal: AbortSignal | undefined;
  onAbort: (() => void) | undefined;
};

export type CoreClientOptions = {
  executable: string;
  arguments: string[];
  env?: NodeJS.ProcessEnv;
  requestTimeoutMs?: number;
  handshakeTimeoutMs?: number;
  maxRestarts?: number;
  onStderr?: (line: string) => void;
};

const DEFAULT_REQUEST_TIMEOUT_MS = 600_000;
const DEFAULT_HANDSHAKE_TIMEOUT_MS = 10_000;
const DEFAULT_MAX_RESTARTS = 3;

const isAborted = (signal: AbortSignal | undefined): boolean => signal?.aborted === true;

export class CoreClient {
  private child: ChildProcessWithoutNullStreams | undefined;
  private starting: Promise<ChildProcessWithoutNullStreams> | undefined;
  private buffer = "";
  private nextId = 1;
  private restarts = 0;
  private disposed = false;
  private readonly pending = new Map<number, PendingCall>();
  private onReady: (() => void) | undefined;
  private onStartFailure: ((error: Error) => void) | undefined;

  public constructor(private readonly options: CoreClientOptions) {}

  public get IsRunning(): boolean {
    return this.child !== undefined;
  }

  /** Sends a request and resolves with its `value`; rejects with CoreError. */
  public async CallAsync<T>(method: string, params: unknown, signal?: AbortSignal): Promise<T> {
    if (this.disposed) {
      throw new CoreError("unavailable", "The .NET sidecar client has been disposed.");
    }
    if (isAborted(signal)) {
      throw new CoreError("cancelled", "The request was cancelled.");
    }

    const child = await this.EnsureStartedAsync();
    if (isAborted(signal)) {
      // The caller cancelled while the sidecar was starting; do not send the request.
      throw new CoreError("cancelled", "The request was cancelled.");
    }

    const id = this.nextId++;

    const response = new Promise<T>((resolve, reject) => {
      this.RegisterPending(id, signal, resolve as (value: unknown) => void, reject);
    });

    child.stdin.write(`${JSON.stringify({ id, method, params })}\n`);
    return response;
  }

  /** Fire-and-forget; failures are logged, never thrown. */
  public Notify(method: string, params: unknown): void {
    void this.CallAsync<unknown>(method, params).catch((error: unknown) => {
      this.options.onStderr?.(`notify ${method} failed: ${String(error)}`);
    });
  }

  public Dispose(): void {
    if (this.disposed) {
      return;
    }
    this.disposed = true;
    this.FailPending(new CoreError("unavailable", "The .NET sidecar client has been disposed."));

    const child = this.child;
    this.child = undefined;
    if (child) {
      child.stdin.end();
      child.kill();
    }
  }

  private RegisterPending(
    id: number,
    signal: AbortSignal | undefined,
    resolve: (value: unknown) => void,
    reject: (error: Error) => void,
  ): void {
    if (isAborted(signal)) {
      reject(new CoreError("cancelled", "The request was cancelled."));
      return;
    }

    const timer = setTimeout(() => {
      // Detach: the caller stops waiting, the sidecar keeps working.
      this.RemovePending(id);
      reject(new CoreError("timeout", `The .NET sidecar did not answer request ${id} in time.`));
    }, this.options.requestTimeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS);

    const onAbort = (): void => {
      // Explicit cancellation is the only thing that stops work in the sidecar.
      this.RemovePending(id);
      this.Notify("cancel", { target: id });
      reject(new CoreError("cancelled", "The request was cancelled."));
    };

    signal?.addEventListener("abort", onAbort, { once: true });
    this.pending.set(id, { resolve, reject, timer, signal, onAbort: signal ? onAbort : undefined });
  }

  private RemovePending(id: number): PendingCall | undefined {
    const call = this.pending.get(id);
    if (!call) {
      return undefined;
    }
    clearTimeout(call.timer);
    if (call.signal && call.onAbort) {
      call.signal.removeEventListener("abort", call.onAbort);
    }
    this.pending.delete(id);
    return call;
  }

  private FailPending(error: Error): void {
    for (const id of this.pending.keys()) {
      this.RemovePending(id)?.reject(error);
    }
  }

  private async EnsureStartedAsync(): Promise<ChildProcessWithoutNullStreams> {
    if (this.child) {
      return this.child;
    }
    if (this.starting) {
      return this.starting;
    }

    if (this.restarts >= (this.options.maxRestarts ?? DEFAULT_MAX_RESTARTS)) {
      throw new CoreError(
        "unavailable",
        `The .NET sidecar exited ${this.restarts} times without answering; not restarting it again.`,
      );
    }

    this.starting = this.StartProcessAsync();
    try {
      return await this.starting;
    } finally {
      this.starting = undefined;
    }
  }

  private async StartProcessAsync(): Promise<ChildProcessWithoutNullStreams> {
    const child = spawn(this.options.executable, this.options.arguments, {
      windowsHide: true,
      stdio: ["pipe", "pipe", "pipe"],
      ...(this.options.env !== undefined && { env: this.options.env }),
    });

    this.child = child;
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdin.on("error", () => undefined);
    child.stdout.on("data", (chunk: string) => this.HandleStdout(chunk));
    child.stderr.on("data", (chunk: string) => this.HandleStderr(chunk));
    child.on("error", (error) =>
      this.HandleExit(child, new CoreError("unavailable", `Could not start the .NET sidecar: ${error.message}`)),
    );
    child.on("exit", (code, signal) =>
      this.HandleExit(child, new CoreError("crashed", `The .NET sidecar exited (${code ?? "null"}/${signal ?? "-"})`)),
    );

    const ready = new Promise<void>((resolve, reject) => {
      this.onReady = resolve;
      this.onStartFailure = reject;
    });

    const timer = setTimeout(
      () =>
        this.onStartFailure?.(
          new CoreError("unavailable", "The .NET sidecar did not announce readiness in time."),
        ),
      this.options.handshakeTimeoutMs ?? DEFAULT_HANDSHAKE_TIMEOUT_MS,
    );

    try {
      await ready;
      this.restarts = 0;
      return child;
    } catch (error) {
      child.kill();
      if (this.child === child) {
        this.child = undefined;
      }
      throw error;
    } finally {
      clearTimeout(timer);
      this.onReady = undefined;
      this.onStartFailure = undefined;
    }
  }

  private HandleStdout(chunk: string): void {
    this.buffer += chunk;
    let newline = this.buffer.indexOf("\n");
    while (newline >= 0) {
      const line = this.buffer.slice(0, newline).trim();
      this.buffer = this.buffer.slice(newline + 1);
      if (line.length > 0) {
        this.HandleLine(line);
      }
      newline = this.buffer.indexOf("\n");
    }
  }

  private HandleLine(line: string): void {
    let message: Record<string, unknown>;
    try {
      message = JSON.parse(line) as Record<string, unknown>;
    } catch {
      return;
    }

    if (message.ready === true) {
      this.onReady?.();
      this.onReady = undefined;
      return;
    }

    const id = message.id;
    if (typeof id !== "number") {
      return;
    }
    const call = this.RemovePending(id);
    if (!call) {
      return;
    }

    if (message.ok === true) {
      this.restarts = 0;
      call.resolve(message.value);
      return;
    }

    const detail = message.error as { code?: unknown; message?: unknown } | undefined;
    const code = typeof detail?.code === "string" ? detail.code : "protocol";
    const text = typeof detail?.message === "string" ? detail.message : "The sidecar reported a failure.";
    call.reject(new CoreError(code, text));
  }

  private HandleStderr(chunk: string): void {
    for (const line of chunk.split("\n")) {
      const trimmed = line.trim();
      if (trimmed.length > 0) {
        this.options.onStderr?.(trimmed);
      }
    }
  }

  private HandleExit(child: ChildProcessWithoutNullStreams, error: CoreError): void {
    if (this.child !== child) {
      return;
    }
    this.child = undefined;
    this.restarts += 1;
    this.onStartFailure?.(error);
    this.onStartFailure = undefined;
    this.onReady = undefined;
    this.FailPending(error);
  }
}
