import * as vscode from "vscode";
import * as fs from "node:fs/promises";
import * as nodePath from "node:path";
import { CallAsync, isAvailable } from "./core-process";
import { coreParams } from "./core-params";
import type {
  AdoptPayload, Bootstrap, BootstrapPayload, BrowsePayload, CliResult, HostAPI, LogSink, PresetsPayload,
  ProjectInfo, ReleasePayload, Result, UpdatePayload, UpgradeResult, VendorSummary,
} from "../AiWorkspaceManager.VSCode.Common/types";
import { fail, ok } from "../AiWorkspaceManager.VSCode.Common/types";

export interface HostApiContext {
  workspaceRoot: () => string | undefined;
  extensionVersion: string;
  extensionPath: string;
  /** Resolves once the activation-time setup has finished. Calls that need the
      repository's files wait for it; `bootstrap` itself does not. */
  ready?: Promise<void>;
  /** Output channel; every failure lands there so the UI is not the only trace. */
  log?: LogSink;
}

async function guarded<T>(work: () => Promise<T>, log?: LogSink): Promise<Result<T>> {
  try {
    return ok(await work());
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    log?.error(message);
    return fail(message);
  }
}

/**
 * Every method delegates to one .NET sidecar request. No resolution logic lives here: if the core
 * cannot answer a question, the UI cannot answer it either, and that is deliberate. The host keeps
 * only activation, editor state, trust, spawning and the webview.
 */
export function createHostAPI(context: HostApiContext): HostAPI {
  const guard = <T>(work: () => Promise<T>): Promise<Result<T>> => guarded(work, context.log);

  const root = (): string => {
    const workspaceRoot = context.workspaceRoot();
    if (!workspaceRoot) {
      throw new Error("Open a folder before using AI Workspace Manager.");
    }
    return workspaceRoot;
  };

  const core = async <T>(method: string, params: Record<string, unknown> = {}, wait = true): Promise<T> => {
    if (wait && context.ready) {
      await context.ready;
    }
    const started = Date.now();
    context.log?.info(`core → ${method}`);
    try {
      const value = await CallAsync<T>(method, { root: root(), ...params });
      context.log?.info(`core ← ${method} (${Date.now() - started} ms)`);
      return value;
    } catch (error) {
      context.log?.error(`core ✗ ${method} after ${Date.now() - started} ms: ${String(error)}`);
      throw error;
    }
  };

  const exists = (path: string): Promise<boolean> =>
    fs.stat(path).then(() => true, () => false);

  return {
    async getBootstrap() {
      return guard(async () => {
        if (context.ready) {
          await context.ready;
        }
        const workspaceRoot = context.workspaceRoot();
        const manifest = workspaceRoot
          ? nodePath.join(workspaceRoot, ".ai", "ai.json")
          : undefined;
        const hasManifest = manifest ? await exists(manifest) : false;
        let materialize: "link" | "copy" = "link";
        if (manifest && hasManifest) {
          try {
            const parsed = JSON.parse(await fs.readFile(manifest, "utf8")) as { materialize?: string };
            materialize = parsed.materialize === "copy" ? "copy" : "link";
          } catch {
            /* a malformed manifest is reported by browse, not here */
          }
        }
        const bootstrap: Bootstrap = {
          WorkspaceName: vscode.workspace.workspaceFolders?.[0]?.name ?? "(no folder)",
          HasManifest: hasManifest,
          HasScript: isAvailable(),
          Materialize: materialize,
          Version: context.extensionVersion,
        };
        return bootstrap;
      });
    },

    async getBrowse(params) {
      return guard(() => core<BrowsePayload>("getBrowse", coreParams.getBrowse(params)));
    },

    async getUpdates() {
      return guard(() => core<UpdatePayload>("getUpdates"));
    },

    async getPresets() {
      return guard(() => core<PresetsPayload>("getPresets"));
    },

    async getReleases() {
      return guard(() => core<ReleasePayload>("getReleases"));
    },

    async adopt() {
      return guard(() => core<AdoptPayload>("adopt"));
    },

    async bootstrap() {
      // Does not wait for `ready`: it *is* what activation waits for.
      return guard(() => core<BootstrapPayload>("bootstrap", {}, false));
    },

    async getProject() {
      return guard(() => core<ProjectInfo>("getProject"));
    },

    async describeProject(params) {
      return guard(() => core<ProjectInfo>("describeProject", coreParams.describeProject(params)));
    },

    async upgrade(params) {
      return guard(async (): Promise<UpgradeResult> => {
        const choice = await vscode.window.showWarningMessage(
          `Pin source "${params.Id}" to ${params.Release} and replace the installed `
            + "files and template scripts with that release? Local edits to those files are lost.",
          { modal: true },
          "Update",
        );
        if (choice !== "Update") {
          return { Cancelled: true, Code: 0, Stdout: "", Stderr: "" };
        }
        const result = await core<CliResult>("upgrade", coreParams.upgrade(params));
        return {
          Cancelled: false,
          Code: result.code,
          Stdout: result.stdout,
          Stderr: result.stderr,
        };
      });
    },

    async addPreset(params) {
      return guard(() => core<CliResult>("addPreset", coreParams.addPreset(params)));
    },

    async setLocked(params) {
      return guard(() => core<CliResult>("setLocked", coreParams.setLocked(params)));
    },

    async install(params) {
      return guard(() => core<CliResult>("install", coreParams.install(params)));
    },

    async remove(params) {
      return guard(() => core<CliResult>("remove", coreParams.remove(params)));
    },

    async sync() {
      return guard(() => core<CliResult>("sync"));
    },

    async updateItems(params) {
      return guard(() => core<CliResult>("updateItems", coreParams.updateItems(params)));
    },

    async pruneDeselected() {
      return guard(() => core<CliResult>("pruneDeselected"));
    },

    async writeNotice() {
      return guard(() => core<CliResult>("writeNotice"));
    },

    async rebuildIndex() {
      return guard(() => core<CliResult>("rebuildIndex"));
    },

    async addScaffold() {
      return guard(() => core<CliResult>("addScaffold"));
    },

    async setClients(params) {
      return guard(() => core<CliResult>("setClients", coreParams.setClients(params)));
    },

    async openFile(params) {
      return guard(async () => {
        const workspaceRoot = root();
        const document = await vscode.workspace.openTextDocument(
          vscode.Uri.file(`${workspaceRoot}/${params.Path}`));
        await vscode.window.showTextDocument(document, { preview: true });
        return null;
      });
    },

    async openUpstream(params) {
      return guard(async () => {
        await vscode.env.openExternal(vscode.Uri.parse(params.Url));
        return null;
      });
    },

    async vendorTooling(params) {
      return guard(async () => {
        const summary = await core<{
          copied: string[]; skipped: string[]; manifest: string;
        }>("vendorTooling", coreParams.vendorTooling(params));
        return {
          Copied: summary.copied.length,
          Skipped: summary.skipped.length,
          Manifest: summary.manifest,
        } satisfies VendorSummary;
      });
    },
  };
}
