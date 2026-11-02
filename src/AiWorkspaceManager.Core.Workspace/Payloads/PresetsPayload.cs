namespace AiWorkspaceManager.Core.Workspace;

public sealed record PresetsPayload(
    string? Current,
    IReadOnlyList<string> Active,
    IReadOnlyList<PresetCard> Presets);
