namespace AiWorkspaceManager.Core.Workspace;

/// <summary>One row of the catalog, joined with the project's installed state.</summary>
public sealed record BrowseItem(
    string Id,
    string DisplayId,
    string Kind,
    string Name,
    string Path,
    string Description,
    string Category,
    IReadOnlyList<string> Tags,
    string Source,
    string Ref,
    string License,
    string Upstream,
    string Sha256,
    int Files,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> RequiredBy,
    bool Selected,
    string Status,
    bool Installed,
    bool Locked,
    bool LocalModified,
    bool UpstreamChanged);
