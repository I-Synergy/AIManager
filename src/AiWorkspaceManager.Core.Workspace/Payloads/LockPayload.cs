namespace AiWorkspaceManager.Core.Workspace;

public sealed record LockPayload(
    string Command,
    IReadOnlyList<string> Locked,
    IReadOnlyList<string> Changed);
