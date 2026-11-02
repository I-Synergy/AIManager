namespace AiWorkspaceManager.Core.Workspace;

public sealed record PresetMutationPayload(
    string Action,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Presets,
    string Manifest);
