// File: tests/AiWorkspaceManager.Core.Catalog.Tests/FrontmatterTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Pins the small YAML reader that replaces PyYAML for the keys the resolver reads: name,
// description, category, tags, requires and skills.

using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Catalog.Tests;

[TestClass]
public sealed class FrontmatterTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_ScalarsAndQuotedValues_AreRead()
    {
        var meta = Frontmatter.Parse(
            "---\nname: alpha\ndescription: \"Quoted: yes\"\nmode: 'agent'\n---\nbody\n");

        Assert.AreEqual("alpha", meta.GetString("name"));
        Assert.AreEqual("Quoted: yes", meta.GetString("description"));
        Assert.AreEqual("agent", meta.GetString("mode"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_BlockList_IsReadInOrder()
    {
        var meta = Frontmatter.Parse("---\ntags:\n  - architecture\n  - core\n---\n");

        CollectionAssert.AreEqual(new[] { "architecture", "core" }, meta.GetNameList("tags").ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_InlineList_IsRead()
    {
        var meta = Frontmatter.Parse("---\ntags: [testing, quality]\n---\n");

        CollectionAssert.AreEqual(new[] { "testing", "quality" }, meta.GetNameList("tags").ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_FoldedAndLiteralBlocks_AreJoined()
    {
        var folded = Frontmatter.Parse("---\ndescription: >-\n  one line\n  and another\n---\n");
        var literal = Frontmatter.Parse("---\ndescription: |\n  one line\n  and another\n---\n");

        Assert.AreEqual("one line and another", folded.GetString("description"));
        Assert.AreEqual("one line\nand another", literal.GetString("description"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_CommaSeparatedScalar_IsANameList()
    {
        var meta = Frontmatter.Parse("---\nrequires: alpha, beta; gamma\n---\n");

        CollectionAssert.AreEqual(new[] { "alpha", "beta", "gamma" }, meta.GetNameList("requires").ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_CommentAfterValue_IsStrippedButAHashInsideQuotesIsKept()
    {
        var meta = Frontmatter.Parse("---\nname: alpha # the name\ndescription: \"a # b\"\n---\n");

        Assert.AreEqual("alpha", meta.GetString("name"));
        Assert.AreEqual("a # b", meta.GetString("description"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_FileWithoutFrontmatter_IsEmpty()
    {
        var meta = Frontmatter.Parse("# A plain markdown file\n");

        Assert.IsNull(meta.GetString("name"));
        Assert.IsEmpty(meta.GetNameList("requires"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void OneLine_CollapsesWhitespace()
    {
        Assert.AreEqual("a b c", Frontmatter.OneLine("  a\n b\t\tc "));
        Assert.AreEqual(string.Empty, Frontmatter.OneLine(null));
    }
}
