using System.Text.Json.Serialization;

using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>A resolved source the index walks: a local tree, with its provenance.</summary>
public sealed record CatalogSource(
    string Name,
    string Root,
    string Repo,
    string Ref,
    string License,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Paths = null);
