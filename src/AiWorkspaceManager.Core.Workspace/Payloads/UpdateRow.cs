namespace AiWorkspaceManager.Core.Workspace;

/// <summary>One selected item's update state.</summary>
public sealed record UpdateRow(
    string Id,
    string DisplayId,
    string Kind,
    string Status,
    bool Locked,
    string Path,
    string Source);
