namespace AiWorkspaceManager.Core.Workspace;

public sealed record NoticePayload(bool Stale, string Path, int Items);
