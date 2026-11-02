using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

public sealed record ReleasePayload(
    bool Offline,
    IReadOnlyList<string> Newer,
    IReadOnlyList<ReleaseRow> Sources);
