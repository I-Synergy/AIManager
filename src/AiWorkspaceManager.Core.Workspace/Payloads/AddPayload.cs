namespace AiWorkspaceManager.Core.Workspace;

public sealed record AddPayload(
    IReadOnlyList<ItemRef> Added,
    string Manifest);
