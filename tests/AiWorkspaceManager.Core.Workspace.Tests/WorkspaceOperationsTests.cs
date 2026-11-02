// File: tests/AiWorkspaceManager.Core.Workspace.Tests/WorkspaceOperationsTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Mutation parity: the manifest and lock the C# sidecar writes must be byte-identical to the
// Python resolver's, and the safety rules (locked items, prune boundary, orphan refusal) must hold.

using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;
using AiWorkspaceManager.Core.Workspace;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class WorkspaceOperationsTests
{
    private const string SyncSourceAlpha =
        "---\nname: alpha\ndescription: Alpha skill\ntags: [testing]\n---\n\nAlpha.\n";

    private const string SyncSourceBeta =
        "---\nname: beta\ndescription: Beta skill\nrequires:\n  - alpha\n---\n\nBeta.\n";

    private const string SyncSourceLead =
        "---\nname: lead\ndescription: Leads work\nskills:\n  - alpha\n---\n";

    private const string SyncSourceStyle =
        "---\ndescription: House style\n---\n";

    private static (TempDirectory Temp, WorkspacePaths Paths, WorkspaceOperations Operations) SyncScenario()
    {
        var temp = new TempDirectory();
        temp.Write("template/.ai/skills/alpha/SKILL.md", SyncSourceAlpha);
        temp.Write("template/.ai/skills/beta/SKILL.md", SyncSourceBeta);
        temp.Write("template/.ai/agents/lead.agent.md", SyncSourceLead);
        temp.Write("template/.ai/instructions/style.instructions.md", SyncSourceStyle);
        temp.Write("workspace/.ai/catalog/presets.json", "{\"presets\":{\"Everything\":{\"skills\":[{\"all\":true}]}}}");
        temp.Write(
            "workspace/.ai/ai.json",
            """
            {
              "version": 1,
              "sources": {
                "template": {
                  "repo": "I-Synergy/AI",
                  "ref": "v1",
                  "path": "../template"
                }
              },
              "skills": ["alpha", "beta"],
              "agents": ["lead"],
              "instructions": ["style"],
              "materialize": "copy",
              "exclude": []
            }
            """);

        var paths = new WorkspacePaths(Path.Combine(temp.Root, "workspace"));
        var queries = new WorkspaceQueries(paths, new GitSourceFetcher(paths, offline: true), offline: true);
        return (temp, paths, new WorkspaceOperations(queries));
    }

    private static string Read(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n");

    private static string Golden(string name) =>
        File.ReadAllText(TestPaths.Golden(name)).Replace("\r\n", "\n");

    [TestMethod]
    [TestCategory("Unit")]
    public void Add_OnALocalCatalog_MatchesThePythonManifest()
    {
        using var temp = new TempDirectory();
        temp.Write("workspace/.ai/skills/alpha/SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\n");
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "workspace"));
        var queries = new WorkspaceQueries(paths, new GitSourceFetcher(paths, offline: true), offline: true);
        var operations = new WorkspaceOperations(queries);

        var outcome = operations.Add(["alpha"]);

        Assert.AreEqual(0, outcome.Result.Code);
        Assert.AreEqual(Golden("add.manifest.json"), Read(paths.ManifestPath));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Sync_FromALocalSource_MatchesThePythonLockAndCopiesTheTree()
    {
        var (temp, paths, operations) = SyncScenario();
        using var scope = temp;

        var outcome = operations.Sync();

        Assert.AreEqual(0, outcome.Result.Code);
        Assert.AreEqual(Golden("sync.lock.json"), Read(paths.LockPath));
        Assert.IsTrue(File.Exists(Path.Combine(paths.Root, ".ai", "skills", "alpha", "SKILL.md")));
        Assert.IsTrue(File.Exists(Path.Combine(paths.Root, ".ai", "agents", "lead.md")));
        Assert.IsTrue(File.Exists(Path.Combine(paths.Root, ".ai", "instructions", "style.instructions.md")));
        Assert.AreEqual(4, outcome.Report.Performed.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SyncCheck_AfterSync_IsCleanAndWritesNothing()
    {
        var (temp, _, operations) = SyncScenario();
        using var scope = temp;
        Assert.AreEqual(0, operations.Sync().Result.Code);
        var before = File.ReadAllText(operations.Queries.Paths.LockPath);

        var outcome = operations.Sync(check: true);

        Assert.AreEqual(0, outcome.Result.Code);
        Assert.IsTrue(outcome.Report.Clean);
        Assert.AreEqual(before, File.ReadAllText(operations.Queries.Paths.LockPath));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SyncCheck_AfterALocalEdit_ReportsDrift()
    {
        var (temp, paths, operations) = SyncScenario();
        using var scope = temp;
        Assert.AreEqual(0, operations.Sync().Result.Code);
        File.AppendAllText(Path.Combine(paths.Root, ".ai", "skills", "alpha", "SKILL.md"), "\nedit\n");

        var outcome = operations.Sync(check: true);

        Assert.AreEqual(1, outcome.Result.Code);
        Assert.IsFalse(outcome.Report.Clean);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateApply_RewritesAnUpstreamChangedItem()
    {
        var (temp, paths, operations) = SyncScenario();
        using var scope = temp;
        Assert.AreEqual(0, operations.Sync().Result.Code);
        File.WriteAllText(
            Path.Combine(temp.Root, "template", ".ai", "skills", "alpha", "SKILL.md"),
            SyncSourceAlpha + "\nnew upstream\n");

        var outcome = operations.ApplyUpdates();

        Assert.AreEqual(0, outcome.Result.Code);
        var installed = File.ReadAllText(Path.Combine(paths.Root, ".ai", "skills", "alpha", "SKILL.md"));
        StringAssert.Contains(installed, "new upstream");
        Assert.AreEqual(0, operations.Sync(check: true).Result.Code);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ALockedItem_IsNeverOverwrittenByUpdate()
    {
        var (temp, paths, operations) = SyncScenario();
        using var scope = temp;
        Assert.AreEqual(0, operations.Sync().Result.Code);
        var installed = Path.Combine(paths.Root, ".ai", "skills", "alpha", "SKILL.md");
        File.AppendAllText(installed, "\nmy change\n");
        Assert.AreEqual(0, operations.SetLocked(["alpha"], locked: true).Result.Code);
        File.WriteAllText(
            Path.Combine(temp.Root, "template", ".ai", "skills", "alpha", "SKILL.md"),
            SyncSourceAlpha + "\nnew upstream\n");

        var outcome = operations.ApplyUpdates(force: true);

        Assert.AreEqual(0, outcome.Result.Code);
        StringAssert.Contains(File.ReadAllText(installed), "my change");
        Assert.IsFalse(File.ReadAllText(installed).Contains("new upstream", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RemoveWithPrune_DeletesTheLockRecordedPath()
    {
        var (temp, paths, operations) = SyncScenario();
        using var scope = temp;
        Assert.AreEqual(0, operations.Sync().Result.Code);
        var beta = Path.Combine(paths.Root, ".ai", "skills", "beta");

        var outcome = operations.Remove(["beta"], prune: true);

        Assert.AreEqual(0, outcome.Result.Code);
        Assert.IsFalse(Directory.Exists(beta));
        Assert.IsFalse(operations.Queries.LoadManifest()!.GetStringArray("skills").Contains("beta"));
        Assert.IsFalse(operations.Queries.LoadLock().Entries.Any(entry => entry.Id == "template:beta"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Remove_RefusesToOrphanAnItemSomethingStillRequires()
    {
        var (temp, _, operations) = SyncScenario();
        using var scope = temp;
        Assert.AreEqual(0, operations.Sync().Result.Code);

        var error = Assert.ThrowsExactly<ResolverException>(() => operations.Remove(["alpha"]));

        StringAssert.Contains(error.Message, "refusing to orphan");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_MatchesThePythonManifest()
    {
        using var temp = new TempDirectory();
        temp.Write("workspace/.ai/skills/alpha/SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\n");
        temp.Write("workspace/.ai/skills/beta/SKILL.md", "---\nname: beta\ndescription: Beta\n---\n");
        temp.Write(
            "workspace/.ai/ai.json",
            """
            {
              "version": 1,
              "sources": {
                "template": {
                  "repo": "I-Synergy/AI",
                  "ref": "local"
                }
              },
              "skills": ["alpha", "beta"],
              "agents": [],
              "materialize": "link",
              "exclude": []
            }
            """);
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "workspace"));
        var queries = new WorkspaceQueries(paths, new GitSourceFetcher(paths, offline: true), offline: true);
        var operations = new WorkspaceOperations(queries);

        var outcome = operations.Pin("v2");

        Assert.AreEqual(0, outcome.Result.Code);
        Assert.AreEqual(Golden("pin.manifest.json"), Read(paths.ManifestPath));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PruneEntry_RefusesPathsOutsideTheInstallRoots()
    {
        using var temp = new TempDirectory();
        temp.Write("project/.ai/instructions/style.instructions.md", "---\ndescription: x\n---\n");
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));
        var entry = new LockEntry(new JsonObject
        {
            ["id"] = "template:style",
            ["kind"] = "instruction",
            ["path"] = ".ai/instructions/style.instructions.md",
        });

        var (deleted, _) = Materializer.PruneEntry(paths, entry);

        Assert.IsNull(deleted);
        Assert.IsTrue(File.Exists(Path.Combine(paths.Root, ".ai", "instructions", "style.instructions.md")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Sync_LinkMode_CreatesALinkNotACopy()
    {
        var (temp, paths, operations) = SyncScenario();
        using var scope = temp;
        var manifest = operations.Queries.LoadManifest()!;
        manifest.Set("materialize", "link");
        manifest.Save();

        var outcome = operations.Sync();

        Assert.AreEqual(0, outcome.Result.Code);
        Assert.IsTrue(PathOps.IsLink(Path.Combine(paths.Root, ".ai", "skills", "alpha")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Add_UnknownId_ReturnsAConfigurationFailure()
    {
        var (temp, _, operations) = SyncScenario();
        using var scope = temp;

        var outcome = operations.Add(["nope"]);

        Assert.AreEqual(2, outcome.Result.Code);
        StringAssert.Contains(outcome.Result.Stderr, "unknown id");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Preset_AddAndRemove_EditsOnlyThePresetList()
    {
        var (temp, paths, operations) = SyncScenario();
        using var scope = temp;

        Assert.AreEqual(0, operations.Preset("add", ["Everything"]).Result.Code);
        CollectionAssert.Contains(operations.Queries.LoadManifest()!.GetStringArray("presets").ToArray(), "Everything");

        Assert.AreEqual(0, operations.Preset("remove", ["Everything"]).Result.Code);
        Assert.IsEmpty(operations.Queries.LoadManifest()!.GetStringArray("presets"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SetLocked_RefusesAnItemTheProjectDoesNotInstall()
    {
        var (temp, _, operations) = SyncScenario();
        using var scope = temp;

        Assert.ThrowsExactly<ResolverException>(() => operations.SetLocked(["missing"], locked: true));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Sync_DryRun_WritesNoLockOrTarget()
    {
        var (temp, paths, operations) = SyncScenario();
        using var scope = temp;

        var outcome = operations.Sync(dryRun: true);

        Assert.AreEqual(0, outcome.Result.Code);
        Assert.IsFalse(File.Exists(paths.LockPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(paths.Root, ".ai", "skills", "alpha")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Sync_Frozen_ReportsThatTheLockWouldChange()
    {
        var (temp, paths, operations) = SyncScenario();
        using var scope = temp;

        var outcome = operations.Sync(frozen: true);

        Assert.AreEqual(1, outcome.Result.Code);
        Assert.IsFalse(File.Exists(paths.LockPath));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Sync_WithPrune_DeletesADeselectedLockedPath()
    {
        var (temp, paths, operations) = SyncScenario();
        using var scope = temp;
        Assert.AreEqual(0, operations.Sync().Result.Code);
        Assert.AreEqual(0, operations.Remove(["beta"]).Result.Code);

        Assert.AreEqual(0, operations.Sync(prune: true).Result.Code);
        Assert.IsFalse(Directory.Exists(Path.Combine(paths.Root, ".ai", "skills", "beta")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Remove_Force_EditsTheManifestEvenWhenADependentExists()
    {
        var (temp, _, operations) = SyncScenario();
        using var scope = temp;
        Assert.AreEqual(0, operations.Sync().Result.Code);

        Assert.AreEqual(0, operations.Remove(["alpha"], force: true).Result.Code);
        Assert.IsFalse(operations.Queries.LoadManifest()!.GetStringArray("skills").Contains("alpha"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_RefusesToEraseAnExistingSelectionWhenTheCatalogResolvesNothing()
    {
        var (temp, _, operations) = SyncScenario();
        using var scope = temp;
        Assert.AreEqual(0, operations.Sync().Result.Code);
        var manifest = operations.Queries.LoadManifest()!;
        foreach (var key in new[] { "skills", "agents", "instructions", "prompts", "plugins", "hooks", "presets" })
        {
            manifest.Set(key, new System.Text.Json.Nodes.JsonArray());
        }

        manifest.Remove("preset");
        manifest.Save();

        var error = Assert.ThrowsExactly<ResolverException>(() => operations.Pin("v2"));
        StringAssert.Contains(error.Message, "refusing to pin with an empty selection");
    }
}
