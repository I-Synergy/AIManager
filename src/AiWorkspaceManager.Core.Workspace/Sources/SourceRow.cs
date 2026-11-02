using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>One row of the sources table the Settings view shows.</summary>
public sealed record SourceRow(
    string Id,
    string Repo,
    string Ref,
    string Resolve,
    string License,
    bool AllowHooks,
    string? Path);
