namespace AiWorkspaceManager.Core.Workspace;

public sealed record IndexPayload(
    bool Stale,
    bool Written,
    string Path,
    int Items,
    string? Compact);
