namespace AiWorkspaceManager.Core.Workspace;

public sealed record StatusPayload(
    StatusSource Source,
    IReadOnlyList<StatusItem> Items);
