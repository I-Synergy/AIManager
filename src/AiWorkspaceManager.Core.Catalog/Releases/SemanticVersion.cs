using System.Globalization;
using System.Text.RegularExpressions;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>
/// A semantic version tag. A prerelease sorts below its release (<c>1.6.0-rc1 &lt; 1.6.0</c>);
/// the comparison is the distinction the updates banner needs.
/// </summary>
public sealed partial record SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    bool IsStable,
    string Prerelease) : IComparable<SemanticVersion>
{
    [GeneratedRegex(
        "^v?(\\d+)\\.(\\d+)\\.(\\d+)(?:-([0-9A-Za-z.-]+))?(?:\\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var compared = Major.CompareTo(other.Major);
        if (compared != 0)
        {
            return compared;
        }

        compared = Minor.CompareTo(other.Minor);
        if (compared != 0)
        {
            return compared;
        }

        compared = Patch.CompareTo(other.Patch);
        if (compared != 0)
        {
            return compared;
        }

        compared = IsStable.CompareTo(other.IsStable);
        return compared != 0 ? compared : string.CompareOrdinal(Prerelease, other.Prerelease);
    }

    public static bool operator <(SemanticVersion? left, SemanticVersion? right) =>
        Compare(left, right) < 0;

    public static bool operator <=(SemanticVersion? left, SemanticVersion? right) =>
        Compare(left, right) <= 0;

    public static bool operator >(SemanticVersion? left, SemanticVersion? right) =>
        Compare(left, right) > 0;

    public static bool operator >=(SemanticVersion? left, SemanticVersion? right) =>
        Compare(left, right) >= 0;

    private static int Compare(SemanticVersion? left, SemanticVersion? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);

    /// <summary><c>v1.6.0</c> → a version; <c>main</c>, <c>latest</c> and <c>v1.6</c> → null.</summary>
    public static SemanticVersion? TryParse(string? tag)
    {
        var match = VersionPattern().Match((tag ?? string.Empty).Trim());
        if (!match.Success)
        {
            return null;
        }

        var prerelease = match.Groups[4].Value;
        return new SemanticVersion(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
            prerelease.Length == 0,
            prerelease);
    }

    /// <summary>The semantic-version tags among <paramref name="tags"/>, newest first.</summary>
    public static IReadOnlyList<string> ReleaseTags(IEnumerable<string>? tags)
    {
        var parsed = new List<(SemanticVersion Version, string Tag)>();
        foreach (var tag in tags ?? [])
        {
            var version = TryParse(tag);
            if (version is not null)
            {
                parsed.Add((version, tag));
            }
        }

        parsed.Sort((left, right) => right.Version.CompareTo(left.Version));
        return parsed.Select(pair => pair.Tag).ToList();
    }
}
