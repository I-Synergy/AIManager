namespace AiWorkspaceManager.Common;

/// <summary>Per-item status values shared by browse, installed and updates.</summary>
public static class ItemStatus
{
    public const string Ok = "ok";
    public const string NotInstalled = "not-installed";
    public const string Missing = "missing";
    public const string LocalModified = "local-modified";
    public const string UpstreamChanged = "upstream-changed";
    public const string Both = "both";
    public const string RemovedUpstream = "removed-upstream";
    public const string Locked = "locked";
    public const string Available = "available";

    public const string UpstreamUnchanged = "unchanged";
}
