namespace AiWorkspaceManager.Core.Workspace;

public sealed record UpdateApplyPayload(
    bool Applied,
    bool DryRun,
    IReadOnlyList<UpdateAppliedItem> AppliedItems,
    IReadOnlyList<UpdateRow> Blocked,
    IReadOnlyList<UpdateRow> Locked,
    bool RemoteSelected);
