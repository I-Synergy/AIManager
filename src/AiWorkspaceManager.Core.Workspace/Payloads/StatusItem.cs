namespace AiWorkspaceManager.Core.Workspace;

public sealed record StatusItem(
    string Id,
    string Kind,
    string Path,
    string Status,
    string Upstream,
    bool Selected,
    string Materialize);
