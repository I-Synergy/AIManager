/**
 * Development harness: a fake extension host.
 *
 * Runs before the webview bundle and answers the same `rpc-request` messages a
 * real host would, from fixtures on disk. This is what lets the UI be opened in
 * a plain browser (and screenshotted) without VS Code.
 *
 * Scenario is chosen with `?scenario=empty|remote` in the page URL.
 */
(function () {
  const scenario = new URLSearchParams(location.search).get("scenario") || "default";
  const state = {
    browse: undefined,
    presets: undefined,
    updates: undefined,
    adopted: false,
  };

  async function loadJson(name) {
    const response = await fetch(`/fixtures/${name}`);
    if (!response.ok) {
      throw new Error(`fixture ${name} missing (${response.status})`);
    }
    return response.json();
  }

  function clone(value) {
    return JSON.parse(JSON.stringify(value));
  }

  function recomputeSummary(browse) {
    const items = browse.items;
    browse.summary = {
      available: items.length,
      selected: items.filter((i) => i.selected).length,
      installed: items.filter((i) => i.selected && i.status === "ok").length,
      drift: items.filter((i) => i.selected && i.status !== "ok" && i.status !== "available").length,
      updates: items.filter((i) => i.status === "upstream-changed").length,
      conflicts: items.filter((i) => i.status === "both").length,
    };
    return browse;
  }

  async function browsePayload() {
    return clone(await liveBrowse());
  }

  /**
   * The live object, for mutating handlers. `browsePayload()` clones for the
   * wire; mutating a clone and expecting it to stick silently loses the change
   * (which is exactly what happened the first time this was written).
   */
  async function liveBrowse() {
    if (!state.browse) {
      state.browse = await loadJson("browse.json");
      if (scenario === "empty") {
        state.browse.items = state.browse.items.map((item) => ({
          ...item, selected: false, status: "available", installed: false,
        }));
      }
      recomputeSummary(state.browse);
    }
    return state.browse;
  }

  const handlers = {
    async getBootstrap() {
      const browse = await liveBrowse();
      return {
        WorkspaceName: scenario === "empty" ? "new-project" : "AI",
        HasManifest: scenario !== "empty" || state.adopted,
        HasScript: true,
        Materialize: browse.materialize,
        Version: "0.10.0",
      };
    },

    getBrowse() {
      return browsePayload();
    },

    async getUpdates() {
      if (!state.updates) {
        state.updates = await loadJson("updates.json");
        // Keep the header summary honest: the fixture's rows really are the
        // items the header counts.
        const browse = await liveBrowse();
        for (const row of [...state.updates.ready, ...state.updates.blocked]) {
          const item = browse.items.find((entry) => entry.id === row.id);
          if (item) {
            item.status = row.status;
            item.selected = true;
            item.upstreamChanged = row.status === "upstream-changed";
          }
        }
        recomputeSummary(browse);
      }
      return clone(state.updates);
    },

    async getPresets() {
      if (!state.presets) {
        state.presets = await loadJson("presets.json");
        if (scenario === "empty") {
          state.presets.current = null;
        }
      }
      return clone(state.presets);
    },

    async getReleases() {
      if (scenario === "empty") {
        // No manifest means no pin to compare: the real CLI refuses, and the
        // view hides the release hint entirely.
        return { command: "release", offline: false, newer: [], sources: [] };
      }
      if (!state.releases) {
        state.releases = await loadJson("releases.json");
      }
      return clone(state.releases);
    },

    async bootstrap() {
      const browse = await liveBrowse();
      state.adopted = true;
      return {
        created: false, path: ".ai/ai.json", selected: browse.summary.selected,
        tooling: { copied: [], skipped: [], unchanged: [], missing: [], protected: [] },
        scaffold: { copied: [], skipped: [] },
      };
    },

    async getProject() {
      return {
        name: "AI", intention: "", detectedName: "AI",
        written: [], skipped: [],
      };
    },

    async describeProject(params) {
      return {
        name: params.Name, intention: params.Intention, detectedName: "AI",
        written: [".ai/session-context.md", ".ai/project/architecture.md"],
        skipped: [],
      };
    },

    async adopt() {
      const browse = await liveBrowse();
      state.adopted = true;
      return {
        created: true, path: ".ai/ai.json",
        selected: browse.summary.selected,
      };
    },

    async upgrade(params) {
      const browse = await liveBrowse();      for (const source of browse.sources ?? []) {
        if (source.id === params.Id) {
          source.ref = params.Release;
          source.resolve = "remote";
        }
      }
      if (!state.releases) {
        state.releases = await loadJson("releases.json");
      }
      for (const row of state.releases.sources) {
        if (row.id === params.Id) {
          row.ref = params.Release;
          row.pinnedTag = true;
          row.current = params.Release;
          row.latest = params.Release;
          row.newer = false;
        }
      }
      state.releases.newer = [];
      return { Cancelled: false, Code: 0, Stdout: "pinned", Stderr: "" };
    },

    async applyPreset(params) {
      const browse = await liveBrowse();
      const preset = (await handlers.getPresets()).presets.find((p) => p.name === params.Name);
      const wanted = preset ? preset.skills : 0;
      let taken = 0;
      for (const item of browse.items) {
        if (item.kind !== "skill") {
          continue;
        }
        item.selected = taken < wanted;
        item.status = item.selected ? "ok" : "available";
        item.installed = item.selected;
        taken += 1;
      }
      recomputeSummary(browse);
      return { code: 0, stdout: `preset ${params.Name} applied`, stderr: "" };
    },

    async install(params) {
      const browse = await liveBrowse();
      for (const item of browse.items) {
        if (params.Ids.includes(item.id)) {
          item.selected = true;
          item.installed = true;
          item.status = "ok";
        }
      }
      recomputeSummary(browse);
      return { code: 0, stdout: `added ${params.Ids.length}`, stderr: "" };
    },

    async remove(params) {
      const browse = await liveBrowse();
      for (const item of browse.items) {
        if (params.Ids.includes(item.id)) {
          item.selected = false;
          item.installed = false;
          item.status = "available";
        }
      }
      recomputeSummary(browse);
      return { code: 0, stdout: `removed ${params.Ids.length}`, stderr: "" };
    },

    async sync() {
      return { code: 0, stdout: "in sync", stderr: "" };
    },

    async updateItems(params) {
      const updates = await handlers.getUpdates();
      const browse = await liveBrowse();
      const ids = params.Ids && params.Ids.length
        ? params.Ids
        : updates.ready.map((row) => row.id);
      state.updates = {
        ...updates,
        ready: updates.ready.filter((row) => !ids.includes(row.id)),
        blocked: params.Force ? [] : updates.blocked,
      };
      for (const item of browse.items) {
        if (ids.includes(item.id)) {
          item.status = "ok";
          item.upstreamChanged = false;
        }
      }
      recomputeSummary(browse);
      return { code: 0, stdout: `updated ${ids.length}`, stderr: "" };
    },

    async pruneDeselected() {
      return { code: 0, stdout: "0 item(s) changed", stderr: "" };
    },

    async writeNotice() {
      return { code: 0, stdout: "wrote: NOTICE.md (12 item(s))", stderr: "" };
    },

    async rebuildIndex() {
      return { code: 0, stdout: "wrote: catalog/index.json (89 item(s))", stderr: "" };
    },

    async openFile() {
      return null;
    },

    async openUpstream(params) {
      console.log("[mock] open upstream", params.Url);
      return null;
    },

  };

  // The web layer creates the client before any request is made, so the fake
  // host only has to answer requests it sees on the window.
  acquireVsCodeApi = function () {
    return {
      postMessage(message) {
        if (!message || message.type !== "rpc-request") {
          return;
        }
        const handler = handlers[message.method];
        const respond = (result) =>
          window.postMessage({ type: "rpc-response", id: message.id, result }, "*");
        if (!handler) {
          respond({ ok: false, error: `Unknown RPC method: ${message.method}` });
          return;
        }
        Promise.resolve(handler(message.params || {}))
          .then((value) => respond({ ok: true, value }))
          .catch((error) => respond({ ok: false, error: String(error) }));
      },
      setState() {},
      getState() {},
    };
  };
})();
