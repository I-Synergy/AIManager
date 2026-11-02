using System.Text.Json;

namespace AiWorkspaceManager.Core.Protocol;

/// <summary>Handles one request. Throwing <see cref="RpcException"/> yields a structured error.</summary>
public delegate Task<object?> RpcHandler(JsonElement parameters, CancellationToken cancellationToken);
