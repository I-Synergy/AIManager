// File: tests/AiWorkspaceManager.Core.Workspace.Tests/StatusEvaluatorTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Every UI state is decided here; a wrong branch is a wrong update/remove recommendation.

using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;
using AiWorkspaceManager.Core.Workspace;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class StatusEvaluatorTests
{
    private static (TempDirectory Temp, CatalogItem Item, LockEntry Entry) Scenario()
    {
        var temp = new TempDirectory();
        temp.Write("item/SKILL.md", "---\nname: alpha\n---\n");
        var (hash, files) = ContentHash.ItemHash(Path.Combine(temp.Root, "item"));
        var item = new CatalogItem
        {
            Id = "template:alpha",
            Source = "template",
            Kind = "skill",
            Name = "alpha",
            Path = ".ai/skills/alpha",
            Sha256 = hash,
            Files = files,
        };
        var entry = new LockEntry(new JsonObject
        {
            ["id"] = item.Id,
            ["kind"] = item.Kind,
            ["path"] = item.Path,
            ["sha256"] = hash,
        });
        return (temp, item, entry);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_RemovedUpstreamWhenTheCatalogHasNoItem()
    {
        Assert.AreEqual(ItemStatus.RemovedUpstream, StatusEvaluator.Evaluate("skill", null, "x", null, false));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_SameDirectoryIsAlwaysOk()
    {
        var (temp, item, entry) = Scenario();
        using var scope = temp;

        Assert.AreEqual(ItemStatus.Ok, StatusEvaluator.Evaluate("skill", item, "missing", entry, true));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_MissingAndNotInstalled()
    {
        var (temp, item, entry) = Scenario();
        using var scope = temp;

        Assert.AreEqual(ItemStatus.Missing, StatusEvaluator.Evaluate("skill", item, Path.Combine(temp.Root, "nope"), entry, false));
        Assert.AreEqual(ItemStatus.NotInstalled, StatusEvaluator.Evaluate("skill", item, Path.Combine(temp.Root, "nope"), null, false));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_OkWhenTheTargetMatchesTheLock()
    {
        var (temp, item, entry) = Scenario();
        using var scope = temp;

        Assert.AreEqual(
            ItemStatus.Ok,
            StatusEvaluator.Evaluate("skill", item, Path.Combine(temp.Root, "item"), entry, false));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_LocalModifiedWhenTheTargetMoved()
    {
        var (temp, item, entry) = Scenario();
        using var scope = temp;
        File.AppendAllText(Path.Combine(temp.Root, "item", "SKILL.md"), "\nedit\n");

        Assert.AreEqual(
            ItemStatus.LocalModified,
            StatusEvaluator.Evaluate("skill", item, Path.Combine(temp.Root, "item"), entry, false));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_UpstreamChangedWhenOnlyTheCatalogMoved()
    {
        var (temp, item, entry) = Scenario();
        using var scope = temp;
        var moved = new CatalogItem
        {
            Id = item.Id,
            Source = item.Source,
            Kind = item.Kind,
            Name = item.Name,
            Path = item.Path,
            Sha256 = "different",
        };

        Assert.AreEqual(
            ItemStatus.UpstreamChanged,
            StatusEvaluator.Evaluate("skill", moved, Path.Combine(temp.Root, "item"), entry, false));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_BothWhenTargetAndCatalogMoved()
    {
        var (temp, item, entry) = Scenario();
        using var scope = temp;
        File.AppendAllText(Path.Combine(temp.Root, "item", "SKILL.md"), "\nedit\n");
        var moved = new CatalogItem
        {
            Id = item.Id,
            Source = item.Source,
            Kind = item.Kind,
            Name = item.Name,
            Path = item.Path,
            Sha256 = "different",
        };

        Assert.AreEqual(
            ItemStatus.Both,
            StatusEvaluator.Evaluate("skill", moved, Path.Combine(temp.Root, "item"), entry, false));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_AnExistingTargetWithNoLockEntryIsNotInstalled()
    {
        var (temp, item, _) = Scenario();
        using var scope = temp;

        Assert.AreEqual(
            ItemStatus.NotInstalled,
            StatusEvaluator.Evaluate("skill", item, Path.Combine(temp.Root, "item"), null, false));
    }
}
