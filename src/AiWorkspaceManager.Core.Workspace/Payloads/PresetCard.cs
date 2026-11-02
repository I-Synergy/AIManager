namespace AiWorkspaceManager.Core.Workspace;

/// <summary>A curated preset with the counts and ids it actually resolves to.</summary>
public sealed record PresetCard(
    string Name,
    string Description,
    int Skills,
    int Agents,
    IReadOnlyList<string> Ids,
    bool Recommended);
