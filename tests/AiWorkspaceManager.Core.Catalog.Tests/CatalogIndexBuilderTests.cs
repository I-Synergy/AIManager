// File: tests/AiWorkspaceManager.Core.Catalog.Tests/CatalogIndexBuilderTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// The golden file was produced by the Python resolver over the shared parity fixture before the
// port. Byte-for-byte equality here is what makes the benchmark meaningful: a wrong index would
// make every timing worthless.

using System.Text.Json;
using AiWorkspaceManager.Core.Catalog;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Catalog.Tests;

[TestClass]
public sealed class CatalogIndexBuilderTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [TestMethod]
    [TestCategory("Unit")]
    public void Build_OverTheParityFixture_MatchesThePythonIndex()
    {
        var index = BuildParityIndex();

        var actual = JsonSerializer.Serialize(index, Indented).Replace("\r\n", "\n").TrimEnd()
            .Replace(CatalogIndex.Generator, "python .ai/scripts/catalog.py index", StringComparison.Ordinal);
        var expected = File.ReadAllText(TestPaths.Golden("index.json")).Replace("\r\n", "\n").TrimEnd();

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Build_RecordsNestedSourcePathsAndDependencies()
    {
        var index = BuildParityIndex();
        var byKey = index.ByKey();

        Assert.AreEqual("plugins/one/skills/gamma", byKey[("skill", "labs:gamma")].SourcePath);
        Assert.AreEqual(
            "https://github.com/example/labs/tree/main/plugins/one/skills/gamma",
            byKey[("skill", "labs:gamma")].Upstream);
        CollectionAssert.AreEqual(
            new[] { "template:alpha", "template:beta" },
            byKey[("agent", "template:lead")].Requires.ToArray());
        CollectionAssert.AreEqual(
            new[] { "template:beta" },
            byKey[("skill", "template:alpha")].Requires.ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Build_ShadowsASameNamedItemFromALaterSource()
    {
        var index = BuildParityIndex();

        Assert.AreEqual(1, index.Shadowed.Count);
        Assert.AreEqual("awesome-copilot:alpha", index.Shadowed[0].Id);
        Assert.AreEqual("template:alpha", index.Shadowed[0].By);
        Assert.IsFalse(index.ByKey().ContainsKey(("skill", "awesome-copilot:alpha")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Build_WithAnEmptyPathsEntry_DisablesThatKind()
    {
        using var temp = new TempDirectory();
        temp.Write("source/skills/alpha/SKILL.md", "---\nname: alpha\n---\n");
        temp.Write("source/plugins/p/plugin.json", "{\"name\":\"p\"}");
        var paths = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["skill"] = ["skills"],
            ["plugin"] = [],
        };

        var index = new CatalogIndexBuilder(
            [new CatalogSource("labs", Path.Combine(temp.Root, "source"), "example/labs", "main", "MIT", paths)])
            .Build();

        Assert.AreEqual(1, index.Items.Count);
        Assert.AreEqual("labs:alpha", index.Items[0].Id);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Build_NotesShadowingOnTheDiagnosticsChannel()
    {
        var notes = new List<string>();

        _ = new CatalogIndexBuilder(ParitySources(), ParityVocabulary(), notes.Add).Build();

        Assert.HasCount(1, notes);
        StringAssert.Contains(notes[0], "awesome-copilot:alpha");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Build_ClassifiesOurOwnItemsFromTheirOwnKeysAndImportedOnesBySource()
    {
        var byKey = BuildParityIndex().ByKey();

        Assert.AreEqual("architecture", byKey[("skill", "template:alpha")].Category);
        CollectionAssert.AreEqual(new[] { "core" }, byKey[("skill", "template:alpha")].Tags.ToArray());
        Assert.AreEqual("testing", byKey[("skill", "template:beta")].Category);
        Assert.AreEqual("copilot", byKey[("skill", "awesome-copilot:delta")].Category);
        Assert.AreEqual("labs", byKey[("skill", "labs:gamma")].Category);
    }

    internal static CatalogIndex BuildParityIndex() =>
        new CatalogIndexBuilder(ParitySources(), ParityVocabulary()).Build();

    internal static CategoryVocabulary ParityVocabulary() =>
        CategoryVocabulary.Load(Path.Combine(TestPaths.ParityWorkspace, ".ai", "catalog", "categories.json"));

    internal static IReadOnlyList<CatalogSource> ParitySources()
    {
        var labsPaths = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["skill"] = ["plugins/one/skills"],
            ["agent"] = [".github/agents"],
            ["instruction"] = [],
            ["plugin"] = [],
            ["hook"] = [],
        };

        return
        [
            new CatalogSource(
                "template", TestPaths.ParityTemplate, "I-Synergy/AI", "v1", "MIT"),
            new CatalogSource(
                "awesome-copilot",
                Path.Combine(TestPaths.ParityRoot, "awesome"),
                "github/awesome-copilot",
                "main",
                "MIT"),
            new CatalogSource(
                "labs", Path.Combine(TestPaths.ParityRoot, "labs"), "example/labs", "main", "MIT", labsPaths),
        ];
    }
}
