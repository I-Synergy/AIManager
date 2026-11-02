using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>Link-aware path checks. A junction is a reparse point; <c>Directory.Exists</c> follows it.</summary>
public static class PathOps
{
    public static bool IsLink(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }

            return info.LinkTarget is not null
                || (HostPlatform.IsWindows && (info.Attributes & FileAttributes.ReparsePoint) != 0);
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

    public static bool ExistsOrLink(string path) =>
        File.Exists(path) || Directory.Exists(path) || IsLink(path);
}
