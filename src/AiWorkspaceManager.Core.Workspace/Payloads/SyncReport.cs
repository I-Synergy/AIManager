namespace AiWorkspaceManager.Core.Workspace;

public sealed record SyncReport(
    string Mode,
    bool Clean,
    bool SameDir,
    IReadOnlyList<SyncAction> Actions,
    IReadOnlyList<string> PendingPrune,
    IReadOnlyList<string> PreservedUnknown,
    IReadOnlyList<string> LegacyMarkers,
    string? LegacyIndex,
    bool LockChanged,
    IReadOnlyList<SyncPerformed> Performed,
    int MarkersRemoved,
    bool LegacyIndexRemoved,
    bool Pruned,
    IReadOnlyList<string> Conflicts);
