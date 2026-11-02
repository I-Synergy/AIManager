// File: tests/AiWorkspaceManager.Core.Workspace.Tests/MaintenanceServiceTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Activation's bootstrap, plus adopt/index/notice/clients. These are the commands the host calls
// without a user-visible selection step.

using System.Text.Json.Nodes;
using AiWorkspaceManager.Core.Workspace;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class MaintenanceServiceTests
{
    private static (TempDirectory Temp, WorkspaceQueries Queries, MaintenanceService Service) Subject(
        bool withManifest = true)
    {
        var temp = new TempDirectory();
        temp.Write("template/.ai/skills/alpha/SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\n");
        temp.Write("template/catalog/presets.json", "{\"presets\":{}}\n");
        temp.Write("template/AGENTS.md", "# AGENTS\n\nRead `.ai/session-context.md` first.\n");
        temp.Write("template/.gitattributes", "*.sh text eol=lf\n");
        temp.Write("template/.claude/settings.json", "{}\n");
        temp.Write(
            "project/.ai/skills/alpha/SKILL.md",
            "---\nname: alpha\ndescription: Alpha\n---\n");
        if (withManifest)
        {
            temp.Write(
                "project/.ai/ai.json",
                """
                {
                  "version": 1,
                  "sources": {
                    "template": { "repo": "I-Synergy/AI", "ref": "v1", "path": "../template" }
                  },
                  "clients": ["claude"],
                  "skills": ["alpha"],
                  "agents": [],
                  "materialize": "copy",
                  "exclude": []
                }
                """);
        }

        var paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));
        var queries = new WorkspaceQueries(paths, new GitSourceFetcher(paths, offline: true), offline: true);
        var setup = new WorkspaceSetupService(queries);
        var operations = new WorkspaceOperations(queries, setup);
        return (temp, queries, new MaintenanceService(queries, setup, operations));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Bootstrap_SetsUpAnExistingProjectWithoutReseedingScaffold()
    {
        var (temp, queries, service) = Subject();
        using var scope = temp;

        var outcome = service.Bootstrap();

        Assert.AreEqual(0, outcome.Result.Code);
        var payload = (BootstrapPayload)outcome.Payload!;
        Assert.IsFalse(payload.Created);
        Assert.IsTrue(File.Exists(Path.Combine(queries.Paths.AiDirectory, "catalog", "presets.json")));
        Assert.IsTrue(File.Exists(Path.Combine(queries.Paths.Root, "AGENTS.md")));
        Assert.IsTrue(File.Exists(Path.Combine(queries.Paths.Root, ".gitignore")));
        Assert.IsTrue(File.Exists(Path.Combine(queries.Paths.Root, "README.md")));
        Assert.IsTrue(PathOps.IsLink(Path.Combine(queries.Paths.Root, ".claude", "skills")));
        Assert.IsFalse(File.Exists(Path.Combine(queries.Paths.Root, ".github", "copilot-instructions.md")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Adopt_WritesAStarterManifestAndNeverOverwrites()
    {
        var (temp, queries, service) = Subject(withManifest: false);
        using var scope = temp;

        var first = service.Adopt();
        var second = service.Adopt();

        var firstPayload = (AdoptPayload)first.Payload!;
        Assert.IsTrue(firstPayload.Created);
        Assert.IsTrue(File.Exists(queries.Paths.ManifestPath));
        var manifest = queries.LoadManifest()!;
        Assert.AreEqual("copy", manifest.GetString("materialize"));
        CollectionAssert.Contains(manifest.GetStringArray("skills").ToArray(), "alpha");
        Assert.IsFalse(((AdoptPayload)second.Payload!).Created);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Clients_StoresTheCompleteListAndRejectsUnknownNames()
    {
        var (temp, queries, service) = Subject();
        using var scope = temp;

        var outcome = service.Clients(["copilot", "reasonix"]);

        Assert.AreEqual(0, outcome.Result.Code);
        CollectionAssert.AreEqual(
            new[] { "copilot", "reasonix" },
            queries.LoadManifest()!.GetStringArray("clients").ToArray());
        Assert.ThrowsExactly<AiWorkspaceManager.Common.ResolverException>(
            () => service.Clients(["cursor"]));
    }


    [TestMethod]
    [TestCategory("Unit")]
    public void VendorTooling_CopiesTheBundledVocabularyAndWritesAStarterManifest()
    {
        using var temp = new TempDirectory();
        temp.Write("bundle/catalog/presets.json", "{\"presets\":{}}\n");
        temp.Write("bundle/catalog/schemas/ai.schema.json", "{}\n");
        temp.Write("bundle/tooling.json", "{}");
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));
        var queries = new WorkspaceQueries(
            paths,
            new GitSourceFetcher(paths, offline: true),
            offline: true,
            Path.Combine(temp.Root, "bundle"));
        var setup = new WorkspaceSetupService(queries);
        var service = new MaintenanceService(queries, setup, new WorkspaceOperations(queries, setup));

        var outcome = service.VendorTooling();

        Assert.AreEqual(0, outcome.Result.Code);
        var payload = (VendorToolingSummary)outcome.Payload!;
        CollectionAssert.Contains(payload.Copied.ToArray(), ".ai/catalog/presets.json");
        Assert.AreEqual("wrote", payload.Manifest);
        Assert.IsTrue(File.Exists(Path.Combine(paths.Root, ".ai", "catalog", "presets.json")));
        Assert.IsTrue(File.Exists(paths.ManifestPath));
        var second = (VendorToolingSummary)service.VendorTooling().Payload!;
        CollectionAssert.Contains(second.Skipped.ToArray(), ".ai/catalog/presets.json");
    }
    [TestMethod]
    [TestCategory("Unit")]
    public void Notice_WritesTheAttributionTable()
    {
        var (temp, queries, service) = Subject();
        using var scope = temp;

        var outcome = service.Notice();
        var path = ((NoticePayload)outcome.Payload!).Path;

        var text = File.ReadAllText(path);
        StringAssert.Contains(text, "| Item | Kind | Source | Ref | License | Upstream |");
        StringAssert.Contains(text, "`alpha` | skill | template | v1 | MIT |");
        Assert.AreEqual(0, outcome.Result.Code);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Index_WritesTheLocalCacheAndTheCompactView()
    {
        var (temp, queries, service) = Subject();
        using var scope = temp;

        var outcome = service.Index(compact: true);

        Assert.AreEqual(0, outcome.Result.Code);
        var payload = (IndexPayload)outcome.Payload!;
        Assert.IsTrue(payload.Written);
        Assert.IsTrue(File.Exists(queries.Paths.IndexPath));
        Assert.IsTrue(payload.Compact is not null && File.Exists(payload.Compact));
        StringAssert.Contains(File.ReadAllText(payload.Compact!), "`alpha` (skill)");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Scaffold_CopiesMissingTemplateFilesAndKeepsExistingOnes()
    {
        var (temp, queries, service) = Subject();
        using var scope = temp;
        temp.Write("template/.ai/plans/example.md", "# plan\n");
        temp.Write("project/.ai/reference/standards.md", "# ours\n");

        var outcome = service.Scaffold();

        Assert.AreEqual(0, outcome.Result.Code);
        var payload = (ScaffoldPayload)outcome.Payload!;
        CollectionAssert.Contains(payload.Copied.ToArray(), ".ai/plans/example.md");
        Assert.AreEqual("# ours\n", File.ReadAllText(Path.Combine(queries.Paths.Root, ".ai", "reference", "standards.md")));
    }
}
