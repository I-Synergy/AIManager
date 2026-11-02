// File: tests/AiWorkspaceManager.Core.Workspace.Tests/MaterializerTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Installing and removing items is destructive; links must be removed as links, and prune must
// refuse paths outside the install roots.

using System.Text.Json.Nodes;
using AiWorkspaceManager.Core.Catalog;
using AiWorkspaceManager.Core.Workspace;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class MaterializerTests
{
    private static CatalogItem Item(TempDirectory temp)
    {
        temp.Write("source/SKILL.md", "---\nname: alpha\n---\n");
        return new CatalogItem
        {
            Id = "template:alpha",
            Source = "template",
            Kind = "skill",
            Name = "alpha",
            Path = ".ai/skills/alpha",
            SourcePath = "source",
        };
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MaterializeItem_CopyCreatesTheTree()
    {
        using var temp = new TempDirectory();
        var item = Item(temp);
        var target = Path.Combine(temp.Root, ".ai", "skills", "alpha");

        Assert.IsTrue(Materializer.MaterializeItem(item, Path.Combine(temp.Root, "source"), target, "copy"));
        Assert.AreEqual("---\nname: alpha\n---\n", File.ReadAllText(Path.Combine(target, "SKILL.md")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MaterializeItem_LinkCreatesALinkAndRemoveLeavesTheSource()
    {
        using var temp = new TempDirectory();
        var item = Item(temp);
        var source = Path.Combine(temp.Root, "source");
        var target = Path.Combine(temp.Root, ".ai", "skills", "alpha");

        Assert.IsTrue(Materializer.MaterializeItem(item, source, target, "link"));
        Assert.IsTrue(PathOps.IsLink(target));
        Assert.IsTrue(File.Exists(Path.Combine(target, "SKILL.md")));

        Materializer.RemovePath(target);

        Assert.IsFalse(PathOps.ExistsOrLink(target));
        Assert.IsTrue(File.Exists(Path.Combine(source, "SKILL.md")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RemovePath_DeletesAReadOnlyFileTree()
    {
        using var temp = new TempDirectory();
        var tree = temp.Write("tree/file.txt", "x");
        File.SetAttributes(tree, FileAttributes.ReadOnly);

        Materializer.RemovePath(Path.Combine(temp.Root, "tree"));

        Assert.IsFalse(Directory.Exists(Path.Combine(temp.Root, "tree")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PruneEntry_DeletesOnlyInsideTheInstallRoots()
    {
        using var temp = new TempDirectory();
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));
        temp.Write("project/.ai/skills/alpha/SKILL.md", "---\nname: alpha\n---\n");
        temp.Write("project/.ai/instructions/style.instructions.md", "---\ndescription: x\n---\n");
        var entry = new LockEntry(new JsonObject
        {
            ["id"] = "template:alpha",
            ["kind"] = "skill",
            ["path"] = ".ai/skills/alpha",
        });

        var (deleted, markers) = Materializer.PruneEntry(paths, entry);

        Assert.IsNotNull(deleted);
        Assert.AreEqual(0, markers);
        Assert.IsFalse(Directory.Exists(Path.Combine(paths.Root, ".ai", "skills", "alpha")));

        var outside = new LockEntry(new JsonObject
        {
            ["id"] = "template:style",
            ["kind"] = "instruction",
            ["path"] = ".ai/instructions/style.instructions.md",
        });
        Assert.IsNull(Materializer.PruneEntry(paths, outside).Deleted);
        Assert.IsTrue(File.Exists(Path.Combine(paths.Root, ".ai", "instructions", "style.instructions.md")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void LegacyMarkers_AreFoundAndRemoved()
    {
        using var temp = new TempDirectory();
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));
        temp.Write("project/.ai/skills/alpha/SKILL.md", "---\nname: alpha\n---\n");
        temp.Write("project/.ai/skills/alpha/.ai-installed.json", "{}");
        temp.Write("project/.ai/installed/template__alpha.skill.json", "{}");
        temp.Write("project/.ai/catalog/index.json", "{\"indexVersion\":1,\"items\":[]}");

        var markers = Materializer.LegacyMarkers(paths, [Path.Combine(paths.Root, ".ai", "skills", "alpha")]);
        Assert.HasCount(2, markers);

        var removed = Materializer.RemoveLegacyArtifacts(
            paths,
            [Path.Combine(paths.Root, ".ai", "skills", "alpha")]);

        Assert.IsTrue(removed["removed"].Contains(".ai/catalog/index.json"));
        Assert.IsFalse(File.Exists(Path.Combine(paths.Root, ".ai", "skills", "alpha", ".ai-installed.json")));
        Assert.IsFalse(Directory.Exists(paths.InstalledDirectory));
    }
}
