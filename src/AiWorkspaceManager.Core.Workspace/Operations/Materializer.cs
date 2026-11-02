using System.Diagnostics;
using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// Places items on disk and removes them safely. A link is removed as a link so a junction never
/// takes its target with it; a copy dereferences symlinked directories, matching the Python
/// resolver and the hash.
/// </summary>
public static class Materializer
{
    /// <summary>Per-item junction (Windows) or relative symlink (POSIX).</summary>
    public static bool CreateLink(string target, string source)
    {
        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (HostPlatform.IsWindows)
        {
            if (Directory.Exists(source))
            {
                return RunCmd($"/c mklink /J \"{target}\" \"{source}\"") == 0;
            }

            if (RunCmd($"/c mklink /H \"{target}\" \"{source}\"") == 0)
            {
                return true;
            }

            if (RunCmd($"/c mklink \"{target}\" \"{source}\"") == 0)
            {
                return true;
            }

            File.Copy(source, target);
            return true;
        }

        try
        {
            var relative = Path.GetRelativePath(Path.GetDirectoryName(target)!, source);
            if (Directory.Exists(source))
            {
                Directory.CreateSymbolicLink(target, relative);
            }
            else
            {
                File.CreateSymbolicLink(target, relative);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Place one item at its target path.</summary>
    public static bool MaterializeItem(CatalogItem item, string source, string target, string mode)
    {
        if (PathOps.ExistsOrLink(target))
        {
            RemovePath(target);
        }

        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        try
        {
            if (mode == "link")
            {
                if (CreateLink(target, source))
                {
                    return true;
                }

                Console.Error.WriteLine($"ERROR: could not link {target} -> {source}");
                return false;
            }

            if (Directory.Exists(source))
            {
                CopyDirectory(source, target, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                File.Copy(source, target);
            }

            return true;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"ERROR: could not materialize {target}: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"ERROR: could not materialize {target}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Remove a directory, file, symlink or junction without following links.</summary>
    public static void RemovePath(string path)
    {
        if (PathOps.IsLink(path))
        {
            if (HostPlatform.IsWindows && Directory.Exists(path) && !new DirectoryInfo(path).LinkTarget!.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                // A junction: rmdir removes the link itself, never the target.
                _ = RunCmd($"/c rmdir \"{path}\"");
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else
                {
                    Directory.Delete(path, recursive: false);
                }

                return;
            }
            catch (IOException)
            {
                // fall through to the generic removal
            }
            catch (UnauthorizedAccessException)
            {
                // fall through to the generic removal
            }
        }

        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return;
        }

        if (Directory.Exists(path))
        {
            DeleteTree(path);
        }
    }

    /// <summary>Delete a lock-recorded path; only paths under the install roots are touched.</summary>
    public static (string? Deleted, int Markers) PruneEntry(WorkspacePaths paths, LockEntry entry)
    {
        var relative = entry.Path.Replace('\\', '/');
        if (!relative.StartsWith(".ai/skills/", StringComparison.Ordinal)
            && !relative.StartsWith(".ai/agents/", StringComparison.Ordinal))
        {
            return (null, 0);
        }

        var target = Path.Combine(paths.Root, entry.Path);
        string? deleted = null;
        if (PathOps.ExistsOrLink(target))
        {
            RemovePath(target);
            deleted = target;
        }

        var markers = RemoveMarkers(paths, entry.Id);
        var copyMarker = Path.Combine(target, ContentHash.MarkerFile);
        if (File.Exists(copyMarker))
        {
            File.Delete(copyMarker);
            markers++;
        }

        return (deleted, markers);
    }

    public static int RemoveMarkers(WorkspacePaths paths, string itemId)
    {
        var directory = paths.InstalledDirectory;
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var pattern = Slug(itemId) + ".*.json";
        var removed = 0;
        foreach (var candidate in Directory.EnumerateFiles(directory, pattern))
        {
            File.Delete(candidate);
            removed++;
        }

        return removed;
    }

    /// <summary>Marker files left by older versions; the lock already records this state.</summary>
    public static List<string> LegacyMarkers(WorkspacePaths paths, IEnumerable<string> targets)
    {
        var found = new List<string>();
        if (Directory.Exists(paths.InstalledDirectory))
        {
            found.AddRange(Directory.EnumerateFiles(paths.InstalledDirectory, "*.json").OrderBy(path => path, StringComparer.Ordinal));
        }

        foreach (var target in targets)
        {
            if (PathOps.IsLink(target))
            {
                continue;
            }

            var marker = Path.Combine(target, ContentHash.MarkerFile);
            if (File.Exists(marker))
            {
                found.Add(marker);
            }
        }

        return found;
    }

    public static string? LegacyIndex(WorkspacePaths paths)
    {
        var path = Path.Combine(paths.AiDirectory, "catalog", "index.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var data = JsonFiles.ReadObject(path);
        if (data is not null
            && data["indexVersion"] is JsonValue version
            && version.TryGetValue<int>(out var value)
            && value == 1
            && data.ContainsKey("items"))
        {
            return path;
        }

        return null;
    }

    /// <summary>Delete state files older versions wrote (markers and the old index).</summary>
    public static Dictionary<string, List<string>> RemoveLegacyArtifacts(
        WorkspacePaths paths,
        IEnumerable<string> targets)
    {
        var removed = new List<string>();
        var candidates = LegacyMarkers(paths, targets);
        var oldIndex = LegacyIndex(paths);
        if (oldIndex is not null)
        {
            candidates.Add(oldIndex);
        }

        foreach (var path in candidates)
        {
            try
            {
                File.Delete(path);
                removed.Add(Path.GetRelativePath(paths.Root, path).Replace('\\', '/'));
            }
            catch (IOException)
            {
                // best effort
            }
        }

        try
        {
            if (Directory.Exists(paths.InstalledDirectory)
                && !Directory.EnumerateFileSystemEntries(paths.InstalledDirectory).Any())
            {
                Directory.Delete(paths.InstalledDirectory);
            }
        }
        catch (IOException)
        {
            // best effort
        }

        return new Dictionary<string, List<string>>(StringComparer.Ordinal) { ["removed"] = removed };
    }

    private static void CopyDirectory(string source, string target, HashSet<string> visited)
    {
        var real = RealPath(source);
        if (!visited.Add(real))
        {
            return;
        }

        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)), visited);
        }
    }

    private static string RealPath(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return (info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName)
                .TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (IOException)
        {
            return path;
        }
    }

    private static void DeleteTree(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static string Slug(string itemId)
    {
        var text = itemId.Replace(":", "__", StringComparison.Ordinal);
        return string.Concat(text.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '_'));
    }

    private static int RunCmd(string arguments)
    {
        var start = new ProcessStartInfo("cmd")
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return 1;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return 1;
        }
    }
}
