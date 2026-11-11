// File: tests/AiWorkspaceManager.Core.Tests/Protocol/NdjsonServerTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Pins the transport the TypeScript host depends on: handshake first, one JSON object per line,
// structured errors for every request, and cancel by id. The reference implementation came from
// the vscode-ts-to-csharp-migration skill; these tests are the repository's own.

using System.Text.Json;
using AiWorkspaceManager.Core.Protocol;

namespace AiWorkspaceManager.Core.Tests.Protocol;

[TestClass]
public sealed class NdjsonServerTests
{
    private static async Task<string[]> RunAsync(
        string input,
        IReadOnlyDictionary<string, RpcHandler>? handlers = null)
    {
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var server = new NdjsonServer(reader, output, error, handlers);

        var exit = await server.RunAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(0, exit);
        return output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static JsonElement Parse(string line) => JsonElement.Parse(line);

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Run_FirstLine_IsTheReadyHandshake()
    {
        var lines = await RunAsync(string.Empty).ConfigureAwait(false);

        Assert.HasCount(1, lines);
        var ready = Parse(lines[0]);
        Assert.IsTrue(ready.GetProperty("ready").GetBoolean());
        Assert.AreEqual(1, ready.GetProperty("protocol").GetInt32());
        Assert.IsFalse(string.IsNullOrWhiteSpace(ready.GetProperty("version").GetString()));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Ping_AnswersWithPongAndProtocol()
    {
        var lines = await RunAsync("{\"id\":7,\"method\":\"ping\",\"params\":{}}\n").ConfigureAwait(false);

        var response = Parse(lines[1]);
        Assert.AreEqual(7, response.GetProperty("id").GetInt64());
        Assert.IsTrue(response.GetProperty("ok").GetBoolean());
        Assert.IsTrue(response.GetProperty("value").GetProperty("pong").GetBoolean());
        Assert.AreEqual(1, response.GetProperty("value").GetProperty("protocol").GetInt32());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task UnknownMethod_ReturnsStructuredError()
    {
        var lines = await RunAsync("{\"id\":3,\"method\":\"nope\",\"params\":{}}\n").ConfigureAwait(false);

        var response = Parse(lines[1]);
        Assert.IsFalse(response.GetProperty("ok").GetBoolean());
        Assert.AreEqual("unknownMethod", response.GetProperty("error").GetProperty("code").GetString());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task InvalidJson_ReturnsInvalidRequestInsteadOfDroppingIt()
    {
        var lines = await RunAsync("{not json}\n").ConfigureAwait(false);

        var response = Parse(lines[1]);
        Assert.IsFalse(response.GetProperty("ok").GetBoolean());
        Assert.AreEqual("invalidRequest", response.GetProperty("error").GetProperty("code").GetString());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task HandlerException_IsReportedAsInternalErrorAndLoggedToStderr()
    {
        var handlers = new Dictionary<string, RpcHandler>(StringComparer.Ordinal)
        {
            ["boom"] = (_, _) => throw new InvalidOperationException("kaboom"),
        };

        using var reader = new StringReader("{\"id\":1,\"method\":\"boom\",\"params\":{}}\n");
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var server = new NdjsonServer(reader, output, error, handlers);

        await server.RunAsync(CancellationToken.None).ConfigureAwait(false);

        var response = Parse(output.ToString().Split('\n')[1]);
        Assert.AreEqual("internalError", response.GetProperty("error").GetProperty("code").GetString());
        StringAssert.Contains(error.ToString(), "kaboom");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RpcException_CarriesItsOwnCode()
    {
        var handlers = new Dictionary<string, RpcHandler>(StringComparer.Ordinal)
        {
            ["domain"] = (_, _) => throw new RpcException("resolutionFailed", "no manifest"),
        };

        var lines = await RunAsync("{\"id\":1,\"method\":\"domain\",\"params\":{}}\n", handlers)
            .ConfigureAwait(false);

        var error = Parse(lines[1]).GetProperty("error");
        Assert.AreEqual("resolutionFailed", error.GetProperty("code").GetString());
        Assert.AreEqual("no manifest", error.GetProperty("message").GetString());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Cancel_ForAnUnknownId_IsASuccessNotAnError()
    {
        var lines = await RunAsync("{\"id\":9,\"method\":\"cancel\",\"params\":{\"target\":404}}\n")
            .ConfigureAwait(false);

        var response = Parse(lines[1]);
        Assert.IsTrue(response.GetProperty("ok").GetBoolean());
        Assert.AreEqual(404, response.GetProperty("value").GetProperty("cancelled").GetInt64());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RequestsRunConcurrently_AndAreMatchedById()
    {
        var handlers = new Dictionary<string, RpcHandler>(StringComparer.Ordinal)
        {
            ["slow"] = async (_, token) =>
            {
                await Task.Delay(40, token).ConfigureAwait(false);
                return "slow";
            },
            ["fast"] = (_, _) => Task.FromResult<object?>("fast"),
        };

        var lines = await RunAsync(
            "{\"id\":1,\"method\":\"slow\",\"params\":{}}\n{\"id\":2,\"method\":\"fast\",\"params\":{}}\n",
            handlers).ConfigureAwait(false);

        Assert.AreEqual(3, lines.Length);
        var byId = lines.Skip(1).ToDictionary(
            line => Parse(line).GetProperty("id").GetInt64(),
            line => Parse(line).GetProperty("value").GetString());
        Assert.AreEqual("slow", byId[1]);
        Assert.AreEqual("fast", byId[2]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Cancel_AnInFlightRequest_AbortsItAndAnswersTheCancel()
    {
        var handlers = new Dictionary<string, RpcHandler>(StringComparer.Ordinal)
        {
            ["slow"] = async (_, token) =>
            {
                await Task.Delay(500, token).ConfigureAwait(false);
                return "slow";
            },
        };

        var lines = await RunAsync(
            "{\"id\":1,\"method\":\"slow\",\"params\":{}}\n"
            + "{\"id\":2,\"method\":\"cancel\",\"params\":{\"target\":1}}\n",
            handlers).ConfigureAwait(false);

        var responses = lines.Skip(1).Select(Parse).ToList();
        Assert.IsTrue(responses.Any(response =>
            response.GetProperty("id").GetInt64() == 1
            && !response.GetProperty("ok").GetBoolean()
            && response.GetProperty("error").GetProperty("code").GetString() == "cancelled"));
        Assert.IsTrue(responses.Any(response =>
            response.GetProperty("id").GetInt64() == 2
            && response.GetProperty("ok").GetBoolean()
            && response.GetProperty("value").GetProperty("cancelled").GetInt64() == 1));
    }
}
