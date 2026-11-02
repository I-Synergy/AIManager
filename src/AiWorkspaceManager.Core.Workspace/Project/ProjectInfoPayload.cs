using System.Globalization;
using System.Text.Json.Nodes;

namespace AiWorkspaceManager.Core.Workspace;

public sealed record ProjectInfoPayload(
    string Name,
    string Intention,
    string DetectedName,
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Skipped);
