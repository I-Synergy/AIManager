using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>The supporting files a project installs (not items, not agent config).</summary>
public sealed record FileCopySummary(
    IReadOnlyList<string> Copied,
    IReadOnlyList<string> Skipped);
