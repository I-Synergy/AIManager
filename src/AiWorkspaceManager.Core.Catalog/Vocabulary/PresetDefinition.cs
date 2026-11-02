using System.Text.Json;

using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>One curated preset from <c>catalog/presets.json</c>.</summary>
public sealed class PresetDefinition
{
    public string Description { get; init; } = string.Empty;

    public bool Recommended { get; init; }

    /// <summary>Keyed by manifest key (<c>skills</c>, <c>agents</c>, …), in file order.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PresetEntry>> Entries { get; init; } =
        new Dictionary<string, IReadOnlyList<PresetEntry>>(StringComparer.Ordinal);
}
