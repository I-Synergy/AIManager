namespace AiWorkspaceManager.Core.Workspace;

public sealed record RemovePayload(
    IReadOnlyList<RemovedItem> Removed,
    IReadOnlyList<PrunedItem> Pruned);
