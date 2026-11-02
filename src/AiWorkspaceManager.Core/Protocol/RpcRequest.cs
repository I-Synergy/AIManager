using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiWorkspaceManager.Core.Protocol;

/// <summary>One request line: <c>{"id":1,"method":"getBrowse","params":{...}}</c>.</summary>
public sealed record RpcRequest(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement Params);
