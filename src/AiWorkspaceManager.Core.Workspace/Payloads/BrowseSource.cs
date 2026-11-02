namespace AiWorkspaceManager.Core.Workspace;

/// <summary>The source descriptor the browse payload carries.</summary>
public sealed record BrowseSource(
    string Id,
    string Root,
    string Ref,
    bool SameDir,
    bool Remote);
