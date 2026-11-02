namespace AiWorkspaceManager.Core.Workspace;

/// <summary>The result of `install`, `remove`, `sync`, `update` and the maintenance commands.</summary>
public sealed record OperationOutcome(CliResult Result, object? Payload);
