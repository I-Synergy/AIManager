namespace AiWorkspaceManager.Core.Workspace;

/// <summary>Raw CLI outcome; the webview turns a non-zero code into a visible error.</summary>
public sealed record CliResult(int Code, string Stdout, string Stderr);
