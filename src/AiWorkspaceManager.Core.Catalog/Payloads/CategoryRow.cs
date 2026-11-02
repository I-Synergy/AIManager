using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>A category bucket in the declared vocabulary order, with a live count.</summary>
public sealed record CategoryRow(
    string Id,
    string Label,
    string Description,
    int Count);
