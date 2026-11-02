// File: tests/AiWorkspaceManager.Core.Workspace.Tests/SourceCatalogTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Built-in source order decides who wins a duplicate name, and a source root is either a local
// path, the project tree or a fetched cache — never a silent fallback.

using System.Text.Json.Nodes;
using AiWorkspaceManager.Core.Workspace;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class SourceCatalogTests
{
    private static ManifestDocument Manifest(JsonObject sources) =>
        ManifestDocument.Temporary(new JsonObject { ["sources"] = sources, ["skills"] = new JsonArray() });

    private static JsonObject Entry(string repo, string reference = "main", string resolve = "remote") =>
        new() { ["repo"] = repo, ["ref"] = reference, ["resolve"] = resolve };

    [TestMethod]
    [TestCategory("Unit")]
    public void BuiltinSources_HaveTheirDeclaredRepositoriesAndNestedPaths()
    {
        CollectionAssert.AreEqual(
            new[] { "aspire-skills", "awesome-copilot", "dotnet-maui", "dotnet-maui-tooling" },
            BuiltinSources.Order.ToArray());

        var aspire = BuiltinSources.Create("aspire-skills")!;
        Assert.AreEqual("microsoft/aspire-skills", aspire["repo"]!.GetValue<string>());
        Assert.AreEqual("MIT", aspire["license"]!.GetValue<string>());

        var maui = BuiltinSources.Create("dotnet-maui")!;
        var paths = maui["paths"]!.AsObject();
        CollectionAssert.AreEqual(
            new[] { "plugins/dotnet-maui/skills" },
            paths["skill"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            paths["plugin"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
        Assert.IsNull(BuiltinSources.Create("unknown"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Sources_OrderTemplateThenBuiltinsThenUnknownDeclaredSources()
    {
        var manifest = Manifest(new JsonObject
        {
            ["template"] = Entry("I-Synergy/AI", "local", "local"),
            ["labs"] = Entry("example/labs", "main", "local"),
            ["awesome-copilot"] = Entry("github/awesome-copilot"),
            ["aspire-skills"] = Entry("microsoft/aspire-skills"),
        });

        var names = SourceCatalog.Sources(manifest).Select(source => source.Name).ToArray();

        CollectionAssert.AreEqual(new[] { "template", "aspire-skills", "awesome-copilot", "labs" }, names);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Sources_SkipADisabledSourceWithoutForgettingIt()
    {
        var disabled = Entry("github/awesome-copilot");
        disabled["enabled"] = false;
        var manifest = Manifest(new JsonObject
        {
            ["template"] = Entry("I-Synergy/AI", "local", "local"),
            ["awesome-copilot"] = disabled,
        });

        var names = SourceCatalog.Sources(manifest).Select(source => source.Name).ToArray();

        CollectionAssert.AreEqual(new[] { "template" }, names);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SourceRoot_ResolvesARelativePathAgainstTheProject()
    {
        using var temp = new TempDirectory();
        temp.Write("source/.keep", string.Empty);
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));
        var catalog = new SourceCatalog(paths, new GitSourceFetcher(paths, offline: true), offline: true);
        var entry = new JsonObject { ["path"] = "../source", ["resolve"] = "local" };

        var root = catalog.SourceRoot("template", entry);

        Assert.AreEqual(
            Path.GetFullPath(Path.Combine(temp.Root, "source")),
            Path.GetFullPath(root));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void IsRemote_RejectsAnUnknownResolveMode()
    {
        Assert.ThrowsExactly<AiWorkspaceManager.Common.ResolverException>(
            () => SourceCatalog.IsRemote(new JsonObject { ["resolve"] = "mirror" }));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RemoteTags_OfflineAndMissingRepoAreRowsNotExceptions()
    {
        using var temp = new TempDirectory();
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));
        var catalog = new SourceCatalog(paths, new GitSourceFetcher(paths, offline: true), offline: true);

        Assert.AreEqual((null, "offline"), catalog.RemoteTags("https://github.com/acme/repo.git"));
        Assert.AreEqual((null, "source declares no repo"), catalog.RemoteTags(""));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rows_ReportResolveLicenseAndHooks()
    {
        var manifest = Manifest(new JsonObject
        {
            ["template"] = Entry("I-Synergy/AI", "v1", "local"),
            ["labs"] = new JsonObject
            {
                ["repo"] = "example/labs",
                ["ref"] = "main",
                ["resolve"] = "local",
                ["license"] = "Apache-2.0",
                ["allowHooks"] = true,
                ["path"] = "../labs",
            },
        });

        var rows = SourceCatalog.Rows(manifest);

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("local", rows[0].Resolve);
        Assert.AreEqual("MIT", rows[0].License);
        Assert.AreEqual("Apache-2.0", rows[1].License);
        Assert.IsTrue(rows[1].AllowHooks);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ToCatalogSources_CarriesTheResolvedRootAndPaths()
    {
        using var temp = new TempDirectory();
        temp.Write("source/plugins/one/skills/alpha/SKILL.md", "---\nname: alpha\n---\n");
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));
        var catalog = new SourceCatalog(paths, new GitSourceFetcher(paths, offline: true), offline: true);
        var manifest = Manifest(new JsonObject
        {
            ["labs"] = new JsonObject
            {
                ["repo"] = "example/labs",
                ["ref"] = "main",
                ["resolve"] = "local",
                ["path"] = "../source",
                ["license"] = "MIT",
                ["paths"] = new JsonObject { ["skill"] = new JsonArray("plugins/one/skills"), ["agent"] = new JsonArray() },
            },
        });

        var sources = catalog.ToCatalogSources(manifest);
        var labs = sources.Single(source => source.Name == "labs");

        Assert.AreEqual("example/labs", labs.Repo);
        Assert.AreEqual("MIT", labs.License);
        Assert.IsNotNull(labs.Paths);
        CollectionAssert.AreEqual(new[] { "plugins/one/skills" }, labs.Paths["skill"].ToArray());
        Assert.IsEmpty(labs.Paths["agent"]);
    }
}
