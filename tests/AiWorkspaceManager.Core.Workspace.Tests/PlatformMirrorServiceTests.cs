// File: tests/AiWorkspaceManager.Core.Workspace.Tests/PlatformMirrorServiceTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Client folders are links onto .ai/; disabling a client removes only the links it created.

using System.Text.Json.Nodes;
using AiWorkspaceManager.Core.Workspace;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class PlatformMirrorServiceTests
{
    private static (TempDirectory Temp, WorkspacePaths Paths, PlatformMirrorService Service) Scenario(params string[] clients)
    {
        var temp = new TempDirectory();
        temp.Write("project/.ai/skills/alpha/SKILL.md", "---\nname: alpha\n---\n");
        temp.Write("project/.ai/agents/lead.md", "---\nname: lead\n---\n");
        var manifest = new JsonObject
        {
            ["version"] = 1,
            ["sources"] = JsonObject.Parse("{\"template\":{\"repo\":\"I-Synergy/AI\",\"ref\":\"local\"}}"),
            ["clients"] = new JsonArray(clients.Select(client => (JsonNode)client).ToArray()),
        };
        temp.Write("project/.ai/ai.json", manifest.ToJsonString());
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));
        return (temp, paths, new PlatformMirrorService(paths));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Sync_CreatesTheSelectedClientsJunctions()
    {
        var (temp, paths, service) = Scenario("claude");
        using var scope = temp;
        var manifest = ManifestDocument.Load(paths.ManifestPath)!;

        var report = service.Sync(manifest);

        Assert.IsTrue(report.Changed > 0);
        Assert.IsTrue(PathOps.IsLink(Path.Combine(paths.Root, ".claude", "skills")));
        Assert.IsFalse(Directory.Exists(Path.Combine(paths.Root, ".github", "skills")));
        Assert.IsFalse(Directory.Exists(Path.Combine(paths.Root, ".reasonix", "skills")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Sync_RemovesOnlyTheDisabledClientsJunctions()
    {
        var (temp, paths, service) = Scenario("claude", "copilot");
        using var scope = temp;
        var manifest = ManifestDocument.Load(paths.ManifestPath)!;
        _ = service.Sync(manifest);
        File.WriteAllText(Path.Combine(paths.Root, ".github", "workflow.yml"), "name: ci\n");
        manifest.Set("clients", new JsonArray(JsonValue.Create("claude")));
        manifest.Save();

        _ = service.Sync(ManifestDocument.Load(paths.ManifestPath)!);

        Assert.IsTrue(PathOps.IsLink(Path.Combine(paths.Root, ".claude", "skills")));
        Assert.IsFalse(Directory.Exists(Path.Combine(paths.Root, ".github", "skills")));
        Assert.IsTrue(Directory.Exists(Path.Combine(paths.Root, ".github")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Sync_PrunesAnEmptyDisabledClientDirectory()
    {
        var (temp, paths, service) = Scenario("claude");
        using var scope = temp;
        var manifest = ManifestDocument.Load(paths.ManifestPath)!;
        _ = service.Sync(manifest);
        manifest.Set("clients", new JsonArray());
        manifest.Save();

        _ = service.Sync(ManifestDocument.Load(paths.ManifestPath)!);

        Assert.IsFalse(Directory.Exists(Path.Combine(paths.Root, ".claude")));
    }
}
