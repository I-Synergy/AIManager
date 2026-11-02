namespace AiWorkspaceManager.Core.Workspace;

public sealed record UpdatePayload(
    bool DryRun,
    IReadOnlyList<UpdateRow> Ready,
    IReadOnlyList<UpdateRow> Blocked,
    IReadOnlyList<UpdateRow> Locked,
    bool Remote,
    bool RemoteSelected);
