namespace AiWorkspaceManager.Core.Workspace;

public sealed record ListPayload(
    IReadOnlyList<ListItem> Items,
    IReadOnlyList<string> Available,
    IReadOnlyList<string> Installed);
