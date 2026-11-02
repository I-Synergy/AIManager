namespace AiWorkspaceManager.Core.Workspace;

public sealed record AdoptPayload(bool Created, string Path, int Selected);
