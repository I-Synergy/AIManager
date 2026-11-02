// File: tests/AiWorkspaceManager.Core.Catalog.Tests/CatalogPayloadsTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// The category tree puts the repository's own buckets before imported ones; hiding a stray bucket
// would make items disappear from the UI.

using AiWorkspaceManager.Core.Catalog;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Catalog.Tests;

[TestClass]
public sealed class CatalogPayloadsTests
{
    private static IReadOnlyList<CategoryRow> Rows(params (string Category, string Source)[] items)
    {
        var catalogItems = items.Select((entry, index) => new CatalogItem
        {
            Id = $"template:item{index}",
            Source = entry.Source,
            Kind = "skill",
            Name = $"item{index}",
            Path = $".ai/skills/item{index}",
            Category = entry.Category,
        }).ToList();

        return CatalogPayloads.CategoryRows(
            catalogItems,
            CategoryVocabulary.Load(Path.Combine(TestPaths.ParityWorkspace, ".ai", "catalog", "categories.json")));
    }

    private static string[] Visible(IEnumerable<CategoryRow> rows) =>
        rows.Where(row => row.Count > 0).Select(row => row.Id).ToArray();

    [TestMethod]
    [TestCategory("Unit")]
    public void CategoryRows_OwnBucketsComeBeforeImportedOnes()
    {
        var rows = Rows(
            ("copilot", "awesome-copilot"),
            ("uncategorised", "template"),
            ("architecture", "template"),
            ("testing", "template"));

        CollectionAssert.AreEqual(
            new[] { "architecture", "testing", "uncategorised", "copilot" },
            Visible(rows));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CategoryRows_ImportedOnlyCatalogKeepsVocabularyOrder()
    {
        var rows = Rows(("copilot", "awesome-copilot"), ("uncategorised", "awesome-copilot"));

        CollectionAssert.AreEqual(new[] { "uncategorised", "copilot" }, Visible(rows));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CategoryRows_ABucketHoldingAnOwnItemCountsAsOwn()
    {
        var rows = Rows(
            ("copilot", "awesome-copilot"),
            ("copilot", "template"),
            ("docs", "awesome-copilot"));

        CollectionAssert.AreEqual(new[] { "copilot", "docs" }, Visible(rows));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CategoryRows_EmptyVocabularyEntriesAreStillReported()
    {
        var counts = Rows(("copilot", "awesome-copilot")).ToDictionary(row => row.Id, row => row.Count);

        Assert.AreEqual(1, counts["copilot"]);
        Assert.AreEqual(0, counts["architecture"]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CategoryRows_OverTheParityIndex_MatchesThePythonPayload()
    {
        var index = CatalogIndexBuilderTests.BuildParityIndex();
        var rows = CatalogPayloads.CategoryRows(index.Items, CatalogIndexBuilderTests.ParityVocabulary());

        CollectionAssert.AreEqual(
            new[] { "architecture", "testing", "docs", "uncategorised", "labs", "copilot" },
            Visible(rows));
    }
}
