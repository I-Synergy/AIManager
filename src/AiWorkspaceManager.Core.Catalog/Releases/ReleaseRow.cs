using System.Globalization;
using System.Text.RegularExpressions;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>One source's release state, for the updates banner.</summary>
public sealed record ReleaseRow(
    string Id,
    string Repo,
    string Ref,
    bool PinnedTag,
    string? Current,
    string? Latest,
    bool LatestIsPrerelease,
    bool Newer,
    IReadOnlyList<string> Tags,
    int TagCount,
    bool Checked,
    string? Error);
