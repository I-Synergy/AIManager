namespace AiWorkspaceManager.Core.Workspace;

public sealed record PrunedItem(
    string Id,
    string Kind,
    string Path,
    bool Deleted,
    int Markers);
