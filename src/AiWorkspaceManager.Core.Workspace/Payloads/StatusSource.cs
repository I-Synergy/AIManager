namespace AiWorkspaceManager.Core.Workspace;

public sealed record StatusSource(
    string Id,
    string Root,
    string Ref,
    bool SameDir);
