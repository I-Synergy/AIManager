using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>Every path the resolver derives from the project root.</summary>
public sealed class WorkspacePaths
{
    public WorkspacePaths(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string AiDirectory => Path.Combine(Root, ".ai");

    public string ManifestPath => Path.Combine(AiDirectory, "ai.json");

    public string LockPath => Path.Combine(AiDirectory, "ai.lock.json");

    public string InstalledDirectory => Path.Combine(AiDirectory, "installed");

    public string IndexPath => Path.Combine(AiDirectory, "tmp", "catalog-index.json");

    public string ProjectInfoPath => Path.Combine(AiDirectory, "project", "project.json");

    public string ToolingRecordPath => Path.Combine(AiDirectory, "tooling.json");

    public string CacheDirectory => Path.Combine(AiDirectory, "tmp", "catalog-cache");

    /// <summary>
    /// The vocabulary's home: <c>.ai/catalog</c> in a project, <c>catalog/</c> in the template.
    /// Reads prefer whichever exists, so both layouts work.
    /// </summary>
    public string CatalogDirectory
    {
        get
        {
            var aiCatalog = Path.Combine(AiDirectory, "catalog");
            if (Directory.Exists(aiCatalog))
            {
                return aiCatalog;
            }

            var rootCatalog = Path.Combine(Root, "catalog");
            return Directory.Exists(rootCatalog) ? rootCatalog : aiCatalog;
        }
    }

    public string KindDirectory(string kind) =>
        Path.Combine(AiDirectory, ResolverDefaults.KindDirectories[kind]);

    public string TargetPath(string kind, string name) =>
        Path.Combine(KindDirectory(kind), name + ResolverDefaults.KindSuffixes[kind]);

    /// <summary>True when both paths resolve to the same object on disk.</summary>
    public static bool SamePath(string left, string right)
    {
        var comparison = HostPlatform.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            if ((Directory.Exists(left) || File.Exists(left))
                && (Directory.Exists(right) || File.Exists(right)))
            {
                var leftInfo = Directory.Exists(left)
                    ? (FileSystemInfo)new DirectoryInfo(left)
                    : new FileInfo(left);
                var rightInfo = Directory.Exists(right)
                    ? (FileSystemInfo)new DirectoryInfo(right)
                    : new FileInfo(right);
                var leftTarget = leftInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? leftInfo.FullName;
                var rightTarget = rightInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? rightInfo.FullName;
                if (string.Equals(
                    leftTarget.TrimEnd(Path.DirectorySeparatorChar),
                    rightTarget.TrimEnd(Path.DirectorySeparatorChar),
                    comparison))
                {
                    return true;
                }
            }

            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                comparison);
        }
        catch (IOException)
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
        catch (UnauthorizedAccessException)
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
    }
}
