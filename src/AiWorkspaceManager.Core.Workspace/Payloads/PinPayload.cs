namespace AiWorkspaceManager.Core.Workspace;

public sealed record PinPayload(
    string Source,
    string Repo,
    string Ref,
    int Selected,
    string Manifest);
