// File: tests/AiWorkspaceManager.Core.Catalog.Tests/PresetCatalogTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Preset order is the curated reading order in the file; replacing it with alphabetical order
// changes what the UI leads with.

using AiWorkspaceManager.Core.Catalog;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Catalog.Tests;

[TestClass]
public sealed class PresetCatalogTests
{
    private static PresetCatalog LoadParity() =>
        PresetCatalog.Load(Path.Combine(TestPaths.ParityWorkspace, ".ai", "catalog", "presets.json"));

    [TestMethod]
    [TestCategory("Unit")]
    public void Load_KeepsFileOrderAndDescriptions()
    {
        var presets = LoadParity();

        CollectionAssert.AreEqual(new[] { "Minimal", "Everything" }, presets.Order.ToArray());
        Assert.AreEqual("Small baseline", presets.Presets["Minimal"].Description);
        Assert.IsFalse(presets.Presets["Minimal"].Recommended);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Load_ReadsIdEntriesAndTheAllMarker()
    {
        var presets = LoadParity();

        CollectionAssert.AreEqual(
            new[] { "alpha" },
            presets.Presets["Minimal"].Entries["skills"].Select(entry => entry.Id).ToArray());
        Assert.IsTrue(presets.Presets["Everything"].Entries["skills"][0].All);
        Assert.IsNull(presets.Presets["Everything"].Entries["skills"][0].Id);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Load_MissingFile_YieldsAnEmptyCatalog()
    {
        var presets = PresetCatalog.Load(Path.Combine(TestPaths.ParityRoot, "does-not-exist.json"));

        Assert.IsEmpty(presets.Order);
        Assert.IsEmpty(presets.Presets);
    }
}
