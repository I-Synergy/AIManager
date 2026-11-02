namespace AiWorkspaceManager.Common;

/// <summary>
/// A configuration error the CLI and the RPC boundary report as a structured failure. The
/// Python resolver called this <c>_die</c> and exited with code 2.
/// </summary>
public sealed class ResolverException : Exception
{
    public ResolverException(string message)
        : base(message)
    {
    }

    public ResolverException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
