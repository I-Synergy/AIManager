namespace AiWorkspaceManager.Core.Workspace;

/// <summary>Counters for the launcher badge and the Browse header.</summary>
public sealed record BrowseSummary(
    int Available,
    int Selected,
    int Installed,
    int Drift,
    int Updates,
    int Conflicts);
