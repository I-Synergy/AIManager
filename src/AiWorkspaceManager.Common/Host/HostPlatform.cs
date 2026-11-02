using System.Runtime.InteropServices;

namespace AiWorkspaceManager.Common;

/// <summary>Platform branch, centralised so tests can exercise the link/junction paths.</summary>
public static class HostPlatform
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}
