namespace AiWorkspaceManager.Core.Workspace;

public sealed record ScaffoldPayload(IReadOnlyList<string> Copied, IReadOnlyList<string> Skipped);
