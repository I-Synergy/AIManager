namespace AiWorkspaceManager.Core.Workspace;

public sealed record BootstrapToolingSummary(
    IReadOnlyList<string> Copied,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Protected);
