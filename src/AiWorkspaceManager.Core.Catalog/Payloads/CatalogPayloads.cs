using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>Payload pieces that are pure functions of the index and the vocabulary.</summary>
public static class CatalogPayloads
{
    /// <summary>
    /// The vocabulary in declared order, plus any strays. Buckets that hold at least one item
    /// from our own (template) source come first: the imported catalog is the guest here.
    /// </summary>
    public static IReadOnlyList<CategoryRow> CategoryRows(
        IReadOnlyList<CatalogItem> items,
        CategoryVocabulary vocabulary)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            counts[item.Category] = counts.GetValueOrDefault(item.Category) + 1;
        }

        var rows = new List<CategoryRow>();
        foreach (var entry in vocabulary.Categories)
        {
            var id = entry.Id;
            rows.Add(new CategoryRow(
                id,
                string.IsNullOrEmpty(entry.Label) ? id : entry.Label,
                entry.Description ?? string.Empty,
                counts.Remove(id, out var count) ? count : 0));
        }

        foreach (var (stray, count) in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            rows.Add(new CategoryRow(stray, stray, string.Empty, count));
        }

        var own = items
            .Where(item => item.Source == ResolverDefaults.TemplateSource)
            .Select(item => item.Category)
            .ToHashSet(StringComparer.Ordinal);

        return rows.Where(row => own.Contains(row.Id))
            .Concat(rows.Where(row => !own.Contains(row.Id)))
            .ToList();
    }
}
