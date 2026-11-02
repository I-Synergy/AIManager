namespace AiWorkspaceManager.Core.Workspace;

public sealed record BootstrapPayload(
    bool Created,
    string Path,
    int Selected,
    IReadOnlyList<string> SourcesAdded,
    BootstrapToolingSummary Tooling,
    BootstrapScaffoldSummary Scaffold,
    IReadOnlyList<string> LegacyRemoved,
    string Gitignore,
    string Readme,
    IReadOnlyDictionary<string, string> Instructions);
