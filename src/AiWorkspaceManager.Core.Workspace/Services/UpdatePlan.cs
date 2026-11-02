using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>The plan behind an update check or apply, kept so the caller does not rebuild it.</summary>
public sealed record UpdatePlan(
    ManifestDocument Manifest,
    CatalogIndex Index,
    IReadOnlyDictionary<(string Kind, string Id), CatalogItem> IndexByKey,
    IReadOnlyList<(string Kind, string Id)> Selected,
    LockDocument Lock,
    string Mode,
    HashSet<string> Frozen,
    IReadOnlyList<UpdateRow> Rows,
    IReadOnlyList<UpdateRow> Ready,
    IReadOnlyList<UpdateRow> Blocked,
    IReadOnlyList<UpdateRow> Held,
    IReadOnlyList<UpdateRow> Chosen,
    bool RemoteSelected,
    bool Remote);
