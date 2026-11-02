using System.Text;
using System.Text.RegularExpressions;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>
/// The frontmatter block of a SKILL.md / agent.md file.
///
/// The resolver only reads a handful of keys (name, description, category, tags, requires,
/// skills), so this is a deliberately small YAML reader: scalars, quoted values, folded/literal
/// blocks, inline lists and block lists. It refuses to guess at nested maps, which no catalog
/// item uses for the keys above.
/// </summary>
public sealed class Frontmatter
{
    private static readonly Regex KeyPattern = new(
        "^([A-Za-z0-9_-]+)\\s*:\\s*(.*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex InlineListPattern = new(
        "^\\[(?<body>.*)\\]$",
        RegexOptions.CultureInvariant);

    private readonly Dictionary<string, string> scalars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> lists = new(StringComparer.Ordinal);

    private Frontmatter()
    {
    }

    public static Frontmatter Empty { get; } = new();

    /// <summary>Parses the leading <c>---</c> block; a file without one has no frontmatter.</summary>
    public static Frontmatter Parse(string text)
    {
        var stripped = text.TrimStart('\uFEFF');
        if (!stripped.StartsWith("---", StringComparison.Ordinal))
        {
            return Empty;
        }

        var end = stripped.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return Empty;
        }

        return ParseBlock(stripped[3..end]);
    }

    /// <summary>One-line value for a key; lists stringify as their first entry.</summary>
    public string? GetString(string key)
    {
        if (scalars.TryGetValue(key, out var value))
        {
            return value;
        }

        return lists.TryGetValue(key, out var list) && list.Count > 0 ? list[0] : null;
    }

    /// <summary>
    /// A key that may be a YAML list or a comma/semicolon separated scalar — the shape
    /// <c>requires:</c> and <c>skills:</c> use in practice.
    /// </summary>
    public IReadOnlyList<string> GetNameList(string key)
    {
        if (lists.TryGetValue(key, out var list))
        {
            return list;
        }

        if (!scalars.TryGetValue(key, out var scalar) || string.IsNullOrWhiteSpace(scalar))
        {
            return [];
        }

        return scalar
            .Replace(';', ',')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0)
            .ToList();
    }

    /// <summary>Turns a scalar or list into the whitespace-collapsed one-line form the UI shows.</summary>
    public static string OneLine(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static Frontmatter ParseBlock(string block)
    {
        var result = new Frontmatter();
        var lines = block.Split('\n');
        var index = 0;
        while (index < lines.Length)
        {
            var line = lines[index].TrimEnd('\r');
            index++;
            var match = KeyPattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups[1].Value;
            var value = StripComment(match.Groups[2].Value).Trim();

            if (value is ">" or ">-" or "|" or "|-" || value is ">+" or "|+")
            {
                var collected = new List<string>();
                while (index < lines.Length && IsIndented(lines[index]))
                {
                    var content = lines[index].Trim();
                    if (content.Length > 0)
                    {
                        collected.Add(content);
                    }

                    index++;
                }

                var joiner = value.StartsWith('|') ? "\n" : " ";
                result.scalars[key] = string.Join(joiner, collected);
                continue;
            }

            var inline = InlineListPattern.Match(value);
            if (inline.Success)
            {
                result.lists[key] = SplitInlineList(inline.Groups["body"].Value);
                continue;
            }

            if (value.Length == 0)
            {
                // A block list is the only nested shape we read; any other nested map is
                // irrelevant to the catalog fields and is skipped.
                var items = new List<string>();
                while (index < lines.Length)
                {
                    var candidate = lines[index].TrimEnd('\r');
                    var trimmed = candidate.TrimStart();
                    if (trimmed.StartsWith("- ", StringComparison.Ordinal)
                        || string.Equals(trimmed, "-", StringComparison.Ordinal))
                    {
                        var entry = trimmed.Length > 1 ? trimmed[2..].Trim() : string.Empty;
                        items.Add(Unquote(StripComment(entry).Trim()));
                        index++;
                        continue;
                    }

                    if (trimmed.Length == 0)
                    {
                        index++;
                        continue;
                    }

                    break;
                }

                if (items.Count > 0)
                {
                    result.lists[key] = items.Where(item => item.Length > 0).ToList();
                }
                else
                {
                    result.scalars[key] = string.Empty;
                }

                continue;
            }

            result.scalars[key] = Unquote(value);
        }

        return result;
    }

    private static bool IsIndented(string line) =>
        line.StartsWith(' ') || line.StartsWith('\t');

    private static string StripComment(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var quote = '\0';
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (quote == '\0' && (character == '"' || character == '\''))
            {
                quote = character;
                continue;
            }

            if (quote != '\0' && character == quote)
            {
                quote = '\0';
                continue;
            }

            if (quote == '\0' && character == '#' && (index == 0 || char.IsWhiteSpace(value[index - 1])))
            {
                return value[..index].TrimEnd();
            }
        }

        return value;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == value[^1] && (value[0] == '"' || value[0] == '\''))
        {
            return value[1..^1];
        }

        return value;
    }

    private static List<string> SplitInlineList(string body)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        foreach (var character in body)
        {
            if (quote == '\0' && (character == '"' || character == '\''))
            {
                quote = character;
                continue;
            }

            if (quote != '\0' && character == quote)
            {
                quote = '\0';
                continue;
            }

            if (quote == '\0' && character == ',')
            {
                AddCurrent(result, current);
                continue;
            }

            current.Append(character);
        }

        AddCurrent(result, current);
        return result;
    }

    private static void AddCurrent(List<string> result, StringBuilder current)
    {
        var value = current.ToString().Trim();
        if (value.Length > 0)
        {
            result.Add(value);
        }

        current.Clear();
    }
}
