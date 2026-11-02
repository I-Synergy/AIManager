namespace AiWorkspaceManager.Core.Workspace;

/// <summary>What the vendor command copied into a repository.</summary>
public sealed record VendorToolingSummary(
    IReadOnlyList<string> Copied,
    IReadOnlyList<string> Skipped,
    string Manifest);
