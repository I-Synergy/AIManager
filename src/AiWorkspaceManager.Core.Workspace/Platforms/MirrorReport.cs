using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>What a platform-mirror reconciliation did, for the host log and tests.</summary>
public sealed record MirrorReport(
    int Changed,
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> RemovedDirectories);
