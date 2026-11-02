// File: tests/AiWorkspaceManager.Core.Workspace.Tests/WorkspaceQueriesTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// The Python resolver's golden payloads for the shared parity fixture are the acceptance test
// for the read paths: browse, presets, status, updates and list must match field-for-field before
// the twin is deleted.

using System.Text.Json;
using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Workspace;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class WorkspaceQueriesTests
{
    private static WorkspaceQueries Subject() => new(
        new WorkspacePaths(TestPaths.ParityWorkspace),
        new StubFetcher(),
        offline: true);

    private static string Normalize(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root.Remove("command");
        root.Remove("applied");
        if (root["source"] is JsonObject source)
        {
            source["root"] = "<ROOT>";
        }

        return JsonFiles.Canonical(root);
    }

    private static void AssertMatchesGolden(object payload, string golden)
    {
        var actual = Normalize(JsonSerializer.Serialize(payload, JsonFiles.Wire));
        var expected = Normalize(File.ReadAllText(TestPaths.Golden(golden)));
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Browse_MatchesThePythonPayload()
    {
        AssertMatchesGolden(Subject().Browse(), "browse.json");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PresetPayload_MatchesThePythonPayload()
    {
        AssertMatchesGolden(Subject().PresetPayload(), "presets.json");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Status_MatchesThePythonPayload()
    {
        AssertMatchesGolden(Subject().Status(), "status.json");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Updates_MatchesThePythonPayload()
    {
        AssertMatchesGolden(Subject().Updates(), "update.json");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void List_MatchesThePythonPayload()
    {
        AssertMatchesGolden(Subject().List(), "list.json");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Browse_CountsInstalledDriftAndLockedItems()
    {
        var payload = Subject().Browse();

        Assert.AreEqual(10, payload.Summary.Available);
        Assert.AreEqual(7, payload.Summary.Selected);
        Assert.AreEqual(2, payload.Summary.Installed);
        Assert.AreEqual(5, payload.Summary.Drift);
        Assert.AreEqual(0, payload.Summary.Updates);
        Assert.AreEqual(0, payload.Summary.Conflicts);
        Assert.IsTrue(payload.Items.Single(item => item.Id == "template:alpha").Locked);
        Assert.AreEqual("ok", payload.Items.Single(item => item.Id == "template:alpha").Status);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Browse_RequiredByIsKeyedByKindAndId()
    {
        var payload = Subject().Browse();

        CollectionAssert.AreEqual(
            new[] { "template:lead" },
            payload.Items.Single(item => item.Id == "template:alpha").RequiredBy.ToArray());
        CollectionAssert.AreEqual(
            new[] { "template:alpha", "template:lead" },
            payload.Items.Single(item => item.Id == "template:beta").RequiredBy.ToArray());
    }

    private sealed class StubFetcher : ISourceFetcher
    {
        public string Fetch(string name, string repo, string reference, bool refresh) =>
            throw new InvalidOperationException("the parity fixture has no remote sources");

        public (IReadOnlyList<string>? Tags, string? Error) RemoteTags(string repo) => (null, "offline");
    }
}
