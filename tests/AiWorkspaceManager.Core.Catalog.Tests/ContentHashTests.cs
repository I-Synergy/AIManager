// File: tests/AiWorkspaceManager.Core.Catalog.Tests/ContentHashTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Content identity must match what the Python resolver wrote into .ai/ai.lock.json, otherwise
// every installed item looks locally modified after the cutover.

using AiWorkspaceManager.Core.Catalog;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Catalog.Tests;

[TestClass]
public sealed class ContentHashTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void ItemHash_MatchesTheLockBasisHashOfThePythonResolver()
    {
        var alpha = Path.Combine(TestPaths.ParityTemplate, ".ai", "skills", "alpha");

        var (hash, files) = ContentHash.ItemHash(alpha);

        Assert.AreEqual("f127ab7aed473765f57c957e901d39f8bcbc5853b91e4567d68eefda0c28ea1f", hash);
        Assert.AreEqual(1, files);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ItemHash_OfASingleFile_IncludesTheRelativeName()
    {
        var lead = Path.Combine(TestPaths.ParityTemplate, ".ai", "agents", "lead.agent.md");

        var (hash, files) = ContentHash.ItemHash(lead);

        Assert.AreEqual("79880356a82eeafff20cfcd23ffb82e7a4f212febf02bbd72319f922ba6acb25", hash);
        Assert.AreEqual(1, files);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ItemFiles_AreSortedAndSkipTheMarkerAndBytecode()
    {
        using var temp = new TempDirectory();
        temp.Write("item/SKILL.md", "---\nname: a\n---\n");
        temp.Write("item/notes.txt", "notes\n");
        temp.Write("item/.ai-installed.json", "{}");
        temp.Write("item/__pycache__/x.pyc", "noise");
        temp.Write("item/old.pyc", "noise");

        var files = ContentHash.ItemFiles(Path.Combine(temp.Root, "item"))
            .Select(pair => pair.Relative)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "SKILL.md", "notes.txt" }, files);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InstalledHash_UsesTheSourceNameWhenTheTargetWasRenamed()
    {
        var target = Path.Combine(TestPaths.ParityWorkspace, ".ai", "agents", "lead.md");
        var item = new CatalogItem
        {
            Id = "template:lead",
            Source = "template",
            Kind = "agent",
            Name = "lead",
            Path = ".ai/agents/lead.md",
            SourcePath = ".ai/agents/lead.agent.md",
        };

        Assert.AreEqual(
            "79880356a82eeafff20cfcd23ffb82e7a4f212febf02bbd72319f922ba6acb25",
            ContentHash.InstalledHash(item, target));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AEditedCopy_HashesDifferently()
    {
        using var temp = new TempDirectory();
        var copy = temp.CopyFixture(Path.Combine(TestPaths.ParityTemplate, ".ai", "skills", "alpha"), "alpha");
        var before = ContentHash.ItemHash(copy).Hash;
        File.AppendAllText(Path.Combine(copy, "SKILL.md"), "\nedit\n");

        Assert.AreNotEqual(before, ContentHash.ItemHash(copy).Hash);
    }
}
