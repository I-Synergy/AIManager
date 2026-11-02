// File: tests/AiWorkspaceManager.Core.Workspace.Tests/WorkspaceDocumentTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// The small seams every command relies on: path derivation, JSON formatting, manifest editing and
// lock ordering.

using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Workspace;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class WorkspaceDocumentTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void WorkspacePaths_DeriveTheInstallLayout()
    {
        using var temp = new TempDirectory();
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));

        Assert.AreEqual(Path.Combine(paths.Root, ".ai"), paths.AiDirectory);
        Assert.AreEqual(Path.Combine(paths.Root, ".ai", "skills"), paths.KindDirectory("skill"));
        Assert.AreEqual(
            Path.Combine(paths.Root, ".ai", "agents", "lead.md"),
            paths.TargetPath("agent", "lead"));
        Assert.AreEqual(Path.Combine(paths.Root, ".ai", "tmp", "catalog-index.json"), paths.IndexPath);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void WorkspacePaths_PreferTheProjectVocabularyThenTheRootOne()
    {
        using var temp = new TempDirectory();
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));
        temp.Write("project/.ai/catalog/presets.json", "{}");

        Assert.AreEqual(Path.Combine(paths.AiDirectory, "catalog"), paths.CatalogDirectory);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void WorkspacePaths_SamePathIsTrueForTheSameObjectAndFalseOtherwise()
    {
        using var temp = new TempDirectory();
        temp.Write("one/file.txt", "x");
        temp.Write("two/file.txt", "x");

        Assert.IsTrue(WorkspacePaths.SamePath(Path.Combine(temp.Root, "one"), Path.Combine(temp.Root, "one")));
        Assert.IsFalse(WorkspacePaths.SamePath(Path.Combine(temp.Root, "one"), Path.Combine(temp.Root, "two")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void JsonFiles_MissingAndMalformedObjectsAreNull()
    {
        using var temp = new TempDirectory();
        var valid = temp.Write("valid.json", "{\"a\":1}\n");
        var malformed = temp.Write("bad.json", "{oops\n");

        Assert.IsNull(JsonFiles.ReadObject(Path.Combine(temp.Root, "missing.json")));
        Assert.IsNull(JsonFiles.ReadObject(malformed));
        Assert.AreEqual(1, JsonFiles.ReadObject(valid)!["a"]!.GetValue<int>());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void JsonFiles_WriteUsesIndentedUtf8WithoutABomAndCanonicalSortsKeys()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Root, "out", "file.json");

        JsonFiles.Write(path, new JsonObject { ["z"] = 1, ["a"] = "ä" });

        var bytes = File.ReadAllBytes(path);
        Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        var text = File.ReadAllText(path);
        StringAssert.Contains(text, "\"ä\"");
        Assert.IsTrue(text.EndsWith('\n'));
        Assert.AreEqual(JsonFiles.Canonical(new JsonObject { ["a"] = 1, ["b"] = 2 }),
            JsonFiles.Canonical(new JsonObject { ["b"] = 2, ["a"] = 1 }));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ManifestDocument_EditsPreserveUnknownKeysAndBindForSaving()
    {
        using var temp = new TempDirectory();
        var path = temp.Write(
            "manifest.json",
            "{\"version\":1,\"sources\":{},\"custom\":\"kept\"}");
        var manifest = ManifestDocument.Load(path)!;

        manifest.Set("skills", new JsonArray("alpha"));
        manifest.EnsureArray("skills").Add("beta");
        manifest.Bind(path).Save();

        var reread = ManifestDocument.Load(path)!;
        Assert.AreEqual("kept", reread.GetString("custom"));
        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, reread.GetStringArray("skills").ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void LockDocument_MissingIsEmptyAndSaveSortsAndSignsStably()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Root, "ai.lock.json");
        Assert.IsEmpty(LockDocument.Load(path).Entries);

        var beta = new LockEntry(new JsonObject { ["id"] = "template:beta", ["kind"] = "skill", ["path"] = ".ai/skills/beta" });
        var alpha = new LockEntry(new JsonObject { ["id"] = "template:alpha", ["kind"] = "skill", ["path"] = ".ai/skills/alpha" });
        LockDocument.Save(path, [beta, alpha]);

        var loaded = LockDocument.Load(path);
        CollectionAssert.AreEqual(new[] { "template:alpha", "template:beta" }, loaded.Entries.Select(entry => entry.Id).ToArray());
        Assert.AreEqual(LockDocument.Signature(loaded), LockDocument.Signature(LockDocument.Load(path)));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void LockDocument_RejectsAFileWithoutResolved()
    {
        using var temp = new TempDirectory();
        var path = temp.Write("bad.lock.json", "{\"lockVersion\":1}");

        Assert.ThrowsExactly<ResolverException>(() => LockDocument.Load(path));
    }
}
