using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// Per-item status: the single place the UI's health states are decided. With source == target the
/// catalog and the installed set are the same files, so the state is always <c>ok</c>.
/// </summary>
public static class StatusEvaluator
{
    public static bool IsAttention(string status) => status is
        ItemStatus.Missing or ItemStatus.LocalModified or ItemStatus.UpstreamChanged
        or ItemStatus.Both or ItemStatus.RemovedUpstream;

    /// <summary>True when the catalog content differs from what the lock recorded.</summary>
    public static bool UpstreamChanged(CatalogItem? item, LockEntry? entry) =>
        !string.IsNullOrEmpty(item?.Sha256) && !string.Equals(item.Sha256, entry?.Sha256, StringComparison.Ordinal);

    public static string Evaluate(
        string kind,
        CatalogItem? item,
        string target,
        LockEntry? entry,
        bool sameDirectory)
    {
        if (item is null)
        {
            return ItemStatus.RemovedUpstream;
        }

        if (sameDirectory)
        {
            return ItemStatus.Ok;
        }

        var exists = PathOps.ExistsOrLink(target);
        if (!exists)
        {
            return entry is null ? ItemStatus.NotInstalled : ItemStatus.Missing;
        }

        if (entry is null)
        {
            return ItemStatus.NotInstalled;
        }

        var current = ContentHash.InstalledHash(item, target);
        var local = !string.Equals(current, entry.Sha256, StringComparison.Ordinal);
        var upstream = UpstreamChanged(item, entry);
        if (local && upstream)
        {
            return ItemStatus.Both;
        }

        if (local)
        {
            return ItemStatus.LocalModified;
        }

        return upstream ? ItemStatus.UpstreamChanged : ItemStatus.Ok;
    }
}
