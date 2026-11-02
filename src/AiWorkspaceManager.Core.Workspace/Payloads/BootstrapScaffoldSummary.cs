namespace AiWorkspaceManager.Core.Workspace;

public sealed record BootstrapScaffoldSummary(
    IReadOnlyList<string> Copied,
    IReadOnlyList<string> Skipped);
