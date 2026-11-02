namespace AiWorkspaceManager.Core.Workspace;

public sealed record ListItem(
    string Id,
    string Kind,
    string Name,
    string Path,
    string Description,
    string Sha256,
    string Ref,
    bool Installed,
    string State);
