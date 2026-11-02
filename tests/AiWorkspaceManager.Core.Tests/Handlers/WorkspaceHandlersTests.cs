// File: tests/AiWorkspaceManager.Core.Tests/Handlers/WorkspaceHandlersTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// The RPC surface is the host's only way into the resolver. These tests exercise every handler,
// the parameter contract and the session cache against a real temporary workspace.

using System.Text.Json;
using AiWorkspaceManager.Core.Handlers;
using AiWorkspaceManager.Core.Protocol;
using AiWorkspaceManager.Core.Workspace;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Tests.Handlers;

[TestClass]
public sealed class WorkspaceHandlersTests
{
    private static (TempDirectory Temp, WorkspaceServices Services, IReadOnlyDictionary<string, RpcHandler> Handlers, string Root)
        Scenario(bool withManifest = true)
    {
        var temp = new TempDirectory();
        temp.Write("template/.ai/skills/alpha/SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\n");
        temp.Write("template/.ai/agents/lead.agent.md", "---\nname: lead\ndescription: Lead\nskills:\n  - alpha\n---\n");
        temp.Write("project/.ai/catalog/presets.json", "{\"presets\":{\"Everything\":{\"skills\":[{\"all\":true}]}}}");
        temp.Write("project/.ai/catalog/categories.json", "{\"categories\":[{\"id\":\"uncategorised\",\"label\":\"I-Synergy\"}],\"sources\":{}}");
        if (withManifest)
        {
            temp.Write(
                "project/.ai/ai.json",
                """
                {
                  "version": 1,
                  "sources": { "template": { "repo": "I-Synergy/AI", "ref": "v1", "path": "../template" } },
                  "clients": ["claude"],
                  "skills": [],
                  "agents": [],
                  "materialize": "copy",
                  "exclude": []
                }
                """);
        }

        var root = Path.Combine(temp.Root, "project");
        var services = new WorkspaceServices(toolingRoot: null, offline: true);
        return (temp, services, new WorkspaceHandlers(services).Create(), root);
    }

    private static JsonElement Params(object value) => JsonSerializer.SerializeToElement(value);

    private static Task<object?> Call(
        IReadOnlyDictionary<string, RpcHandler> handlers,
        string method,
        object parameters) =>
        handlers[method](Params(parameters), CancellationToken.None);

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetHandlers_ReturnThePayloadsTheWebviewExpects()
    {
        var (temp, _, handlers, root) = Scenario();
        using var scope = temp;

        Assert.IsInstanceOfType<BrowsePayload>(await Call(handlers, "getBrowse", new { root }));
        Assert.IsInstanceOfType<PresetsPayload>(await Call(handlers, "getPresets", new { root }));
        Assert.IsInstanceOfType<UpdatePayload>(await Call(handlers, "getUpdates", new { root }));
        Assert.IsInstanceOfType<ReleasePayload>(await Call(handlers, "getReleases", new { root }));
        Assert.IsInstanceOfType<ProjectInfoPayload>(await Call(handlers, "getProject", new { root }));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MutationHandlers_WriteTheManifestAndReturnCliResults()
    {
        var (temp, services, handlers, root) = Scenario();
        using var scope = temp;

        var install = (CliResult)(await Call(handlers, "install", new { root, ids = new[] { "alpha" } }))!;
        Assert.AreEqual(0, install.Code);
        CollectionAssert.Contains(services.Session(root).Queries.LoadManifest()!.GetStringArray("skills").ToArray(), "alpha");

        var locked = (CliResult)(await Call(handlers, "setLocked", new { root, ids = new[] { "alpha" }, locked = true }))!;
        Assert.AreEqual(0, locked.Code);
        CollectionAssert.Contains(services.Session(root).Queries.LoadManifest()!.GetStringArray("locked").ToArray(), "alpha");

        Assert.AreEqual(0, ((CliResult)(await Call(handlers, "sync", new { root }))!).Code);
        Assert.AreEqual(0, ((CliResult)(await Call(handlers, "updateItems", new { root, ids = Array.Empty<string>(), force = false }))!).Code);
        Assert.AreEqual(0, ((CliResult)(await Call(handlers, "addPreset", new { root, name = "Everything" }))!).Code);
        Assert.AreEqual(0, ((CliResult)(await Call(handlers, "setClients", new { root, clients = new[] { "claude", "copilot" } }))!).Code);
        Assert.AreEqual(0, ((CliResult)(await Call(handlers, "writeNotice", new { root }))!).Code);
        Assert.AreEqual(0, ((CliResult)(await Call(handlers, "rebuildIndex", new { root }))!).Code);
        Assert.AreEqual(0, ((CliResult)(await Call(handlers, "addScaffold", new { root }))!).Code);
        Assert.AreEqual(0, ((CliResult)(await Call(handlers, "pruneDeselected", new { root }))!).Code);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ProjectHandlers_ReadAndWriteTheProjectDescription()
    {
        var (temp, _, handlers, root) = Scenario();
        using var scope = temp;

        var described = (ProjectInfoPayload)(await Call(
            handlers,
            "describeProject",
            new { root, name = "Payments", intention = "Track invoices" }))!;
        Assert.AreEqual("Payments", described.Name);
        var read = (ProjectInfoPayload)(await Call(handlers, "getProject", new { root }))!;
        Assert.AreEqual("Track invoices", read.Intention);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BootstrapAndAdopt_ReturnTheirPayloads()
    {
        var (temp, _, handlers, root) = Scenario();
        using var scope = temp;

        Assert.IsInstanceOfType<BootstrapPayload>(await Call(handlers, "bootstrap", new { root }));

        var (bareTemp, _, bareHandlers, bareRoot) = Scenario(withManifest: false);
        using var bareScope = bareTemp;
        Assert.IsInstanceOfType<AdoptPayload>(await Call(bareHandlers, "adopt", new { root = bareRoot }));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task VendorTooling_CopiesTheConfiguredVocabulary()
    {
        using var temp = new TempDirectory();
        temp.Write("bundle/catalog/presets.json", "{\"presets\":{}}\n");
        temp.Write("project/.ai/ai.json", "{\"version\":1,\"sources\":{\"template\":{\"repo\":\"x\",\"ref\":\"y\"}},\"skills\":[]}");
        var root = Path.Combine(temp.Root, "project");
        var services = new WorkspaceServices(Path.Combine(temp.Root, "bundle"), offline: true);
        var handlers = new WorkspaceHandlers(services).Create();

        var payload = (VendorToolingSummary)(await Call(handlers, "vendorTooling", new { root, overwrite = false }))!;

        CollectionAssert.Contains(payload.Copied.ToArray(), ".ai/catalog/presets.json");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MissingRoot_IsAnInvalidParamsFailure()
    {
        var (temp, _, handlers, _) = Scenario();
        using var scope = temp;

        var error = await Assert.ThrowsExactlyAsync<RpcException>(
            () => handlers["getBrowse"](Params(new { }), CancellationToken.None));

        Assert.AreEqual(RpcErrorCodes.InvalidParams, error.Code);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Session_CachesByNormalisedRootAndRejectsAnEmptyRoot()
    {
        var (temp, services, _, root) = Scenario();
        using var scope = temp;

        Assert.AreSame(services.Session(root), services.Session(Path.Combine(root, ".")));
        Assert.ThrowsExactly<ArgumentException>(() => services.Session(""));
    }
}
