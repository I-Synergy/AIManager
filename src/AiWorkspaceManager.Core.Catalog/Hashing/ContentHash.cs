using System.Security.Cryptography;
using System.Text;

using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>
/// Content identity for an item. A copy install and a link install must hash the same way, so
/// symlinked directories are followed (the copy dereferences them) and each real directory is
/// walked once to make a link cycle terminate.
/// </summary>
public static class ContentHash
{
    public const string MarkerFile = ".ai-installed.json";

    /// <summary>Every hashable file of an item as (relative path, full path), sorted by path.</summary>
    public static IReadOnlyList<(string Relative, string Full)> ItemFiles(string path)
    {
        if (File.Exists(path))
        {
            return [(Path.GetFileName(path), path)];
        }

        if (!Directory.Exists(path))
        {
            return [];
        }

        var found = new List<(string Relative, string Full)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(path, path, found, visited);
        found.Sort((left, right) => string.CompareOrdinal(left.Relative, right.Relative));
        return found;
    }

    /// <summary>sha256 over sorted <c>&lt;relpath&gt;\0&lt;content-sha256&gt;\n</c> lines.</summary>
    public static (string Hash, int Files) ItemHash(string path, string? nameOverride = null)
    {
        IEnumerable<(string Relative, string Full)> files = ItemFiles(path);
        if (!string.IsNullOrEmpty(nameOverride) && File.Exists(path))
        {
            files = [(nameOverride, path)];
        }

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = 0;
        foreach (var (relative, full) in files)
        {
            digest.AppendData(Encoding.UTF8.GetBytes(relative));
            digest.AppendData([0]);
            digest.AppendData(Encoding.ASCII.GetBytes(FileSha(full)));
            digest.AppendData("\n"u8);
            count++;
        }

        return (Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(), count);
    }

    /// <summary>
    /// The lock-basis hash of an installed item. A file installed under a different name
    /// (<c>X.md</c> from <c>X.agent.md</c>) hashes under the source's name, so an untouched
    /// install equals its lock entry.
    /// </summary>
    public static string InstalledHash(IItemEntry item, string target)
    {
        var name = (string?)null;
        if (File.Exists(target))
        {
            var sourcePath = !string.IsNullOrEmpty(item.SourcePath) ? item.SourcePath : item.Path;
            name = Path.GetFileName(string.IsNullOrEmpty(sourcePath) ? target : sourcePath);
        }

        return ItemHash(target, name).Hash;
    }

    public static string FileSha(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void Walk(
        string root,
        string directory,
        List<(string Relative, string Full)> found,
        HashSet<string> visited)
    {
        var real = RealPath(directory);
        if (!visited.Add(real))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var info = new FileInfo(file);
            if (info.LinkTarget is not null && !File.Exists(info.FullName))
            {
                continue; // a broken link is not content
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var segments = relative.Split('/');
            if (segments[^1] == MarkerFile
                || segments.Contains("__pycache__", StringComparer.Ordinal)
                || relative.EndsWith(".pyc", StringComparison.Ordinal)
                || relative.EndsWith(".pyo", StringComparison.Ordinal))
            {
                continue;
            }

            found.Add((relative, file));
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            Walk(root, child, found, visited);
        }
    }

    private static string RealPath(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return (target?.FullName ?? info.FullName).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (IOException)
        {
            return path;
        }
    }
}
