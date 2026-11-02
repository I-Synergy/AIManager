using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

public sealed record ToolingSummary(
    IReadOnlyList<string> Copied,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Protected);
