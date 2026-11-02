namespace AiWorkspaceManager.Core.Workspace;

public sealed record SyncOutcome(CliResult Result, SyncReport Report);
