namespace AiWorkspaceManager.Common;

/// <summary>Selection ids are <c>source:name</c>; a bare id belongs to the template source.</summary>
public static class ItemId
{
    /// <summary>Splits <c>source:name</c>; a bare id belongs to <c>template</c>.</summary>
    public static (string Source, string Name) Split(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        var separator = text.IndexOf(':', StringComparison.Ordinal);
        return separator < 0
            ? (ResolverDefaults.TemplateSource, text)
            : (text[..separator], text[(separator + 1)..]);
    }

    public static string Make(string source, string name) => $"{source}:{name}";

    /// <summary>The id without the template prefix — what the UI shows.</summary>
    public static string Display(string? itemId)
    {
        var text = itemId ?? string.Empty;
        var prefix = ResolverDefaults.TemplateSource + ":";
        return text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text;
    }

    /// <summary>Normalises to <c>source:name</c>; throws for an empty name.</summary>
    public static string Canonical(string raw)
    {
        var (source, name) = Split(raw);
        if (string.IsNullOrEmpty(name))
        {
            throw new ResolverException($"empty id after source prefix in '{raw}'");
        }

        return Make(source, name);
    }

    /// <summary>Normalises without throwing; <c>null</c> for an unusable value.</summary>
    public static string? SoftCanonical(string? raw)
    {
        var (source, name) = Split(raw);
        return string.IsNullOrEmpty(name) ? null : Make(source, name);
    }
}
