using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>One payload that paints the whole UI: catalog + selection + state.</summary>
public sealed record BrowsePayload(
    BrowseSource Source,
    string Materialize,
    IReadOnlyList<string> Clients,
    BrowseSummary Summary,
    IReadOnlyList<SourceRow> Sources,
    IReadOnlyList<PresetCard> Presets,
    IReadOnlyList<CategoryRow> Categories,
    IReadOnlyList<BrowseItem> Items);
