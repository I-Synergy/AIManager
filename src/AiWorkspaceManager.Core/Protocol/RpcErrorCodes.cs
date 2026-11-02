namespace AiWorkspaceManager.Core.Protocol;

/// <summary>Failure codes the protocol and the resolver share.</summary>
public static class RpcErrorCodes
{
    public const string InvalidRequest = "invalidRequest";
    public const string UnknownMethod = "unknownMethod";
    public const string InvalidParams = "invalidParams";
    public const string Cancelled = "cancelled";
    public const string InternalError = "internalError";

    /// <summary>A configuration error (the Python resolver's exit code 2).</summary>
    public const string ResolutionFailed = "resolutionFailed";
}
