/**
 * Wire parameters for the sidecar. The host's `HostAPI` uses PascalCase (it is the webview
 * contract), the sidecar handlers read camelCase; this module is the single translation point.
 *
 * It is deliberately dependency-free so the Node test suite can pin every key without loading
 * `vscode`.
 */

export const coreParams = {
  getBrowse: (params: { Refresh?: boolean } | undefined) => ({
    refresh: params?.Refresh ?? false,
  }),

  describeProject: (params: { Name: string; Intention: string }) => ({
    name: params.Name,
    intention: params.Intention,
  }),

  upgrade: (params: { Release: string }) => ({ release: params.Release }),

  addPreset: (params: { Name: string }) => ({ name: params.Name }),

  setLocked: (params: { Ids: string[]; Locked: boolean }) => ({
    ids: params.Ids,
    locked: params.Locked,
  }),

  install: (params: { Ids: string[] }) => ({ ids: params.Ids }),

  remove: (params: { Ids: string[]; Prune: boolean }) => ({
    ids: params.Ids,
    prune: params.Prune,
  }),

  updateItems: (params: { Ids?: string[]; Force?: boolean } | undefined) => ({
    ids: params?.Ids ?? [],
    force: params?.Force ?? false,
  }),

  setClients: (params: { Clients: string[] }) => ({ clients: params.Clients }),

  vendorTooling: (params: { Overwrite?: boolean } | undefined) => ({
    overwrite: params?.Overwrite ?? false,
  }),
};
