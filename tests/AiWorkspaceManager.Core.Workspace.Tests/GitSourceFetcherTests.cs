// File: tests/AiWorkspaceManager.Core.Workspace.Tests/GitSourceFetcherTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Remote sources are resolved with the real git executable against file:// fixtures: clone at a
// tag, fetch by commit SHA, refresh a branch, and read tags without cloning.

using System.Diagnostics;
using System.Text.Json.Nodes;
using AiWorkspaceManager.Core.Workspace;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class GitSourceFetcherTests
{
    private static void Git(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, error);
    }

    private static string CreateRepo(TempDirectory temp, params string[] tags)
    {
        var repo = Path.Combine(temp.Root, "repo");
        Directory.CreateDirectory(Path.Combine(repo, "skills", "alpha"));
        File.WriteAllText(
            Path.Combine(repo, "skills", "alpha", "SKILL.md"),
            "---\nname: alpha\ndescription: alpha\n---\n");
        Git(repo, "init", "-q", "-b", "main");
        Git(repo, "add", "-A");
        Git(repo, "-c", "user.name=Test", "-c", "user.email=t@example.com", "commit", "-q", "-m", "v1");
        foreach (var tag in tags)
        {
            Git(repo, "tag", tag);
        }

        return repo;
    }

    private static string Uri(string repo) => new Uri(repo).AbsoluteUri;

    private static GitSourceFetcher Subject(TempDirectory temp, bool offline = false) =>
        new(new WorkspacePaths(temp.Root), offline);

    [TestMethod]
    [TestCategory("Unit")]
    public void Fetch_ByTag_ClonesThePinnedTree()
    {
        using var temp = new TempDirectory();
        var repo = CreateRepo(temp, "v1.0.0");

        var checkout = Subject(temp).Fetch("template", Uri(repo), "v1.0.0", refresh: false);

        Assert.IsTrue(File.Exists(Path.Combine(checkout, "skills", "alpha", "SKILL.md")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Fetch_ByCommitSha_FallsBackToFetchHead()
    {
        using var temp = new TempDirectory();
        var repo = CreateRepo(temp, "v1.0.0");
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("rev-parse");
        start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!;
        var sha = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        var checkout = Subject(temp).Fetch("template", Uri(repo), sha, refresh: false);

        Assert.IsTrue(File.Exists(Path.Combine(checkout, "skills", "alpha", "SKILL.md")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Fetch_WithRefresh_PicksUpANewCommitOnTheBranch()
    {
        using var temp = new TempDirectory();
        var repo = CreateRepo(temp);
        var fetcher = Subject(temp);
        var checkout = fetcher.Fetch("template", Uri(repo), "main", refresh: false);
        Assert.IsFalse(File.Exists(Path.Combine(checkout, "new.txt")));

        File.WriteAllText(Path.Combine(repo, "new.txt"), "new\n");
        Git(repo, "add", "-A");
        Git(repo, "-c", "user.name=Test", "-c", "user.email=t@example.com", "commit", "-q", "-m", "v2");

        var refreshed = fetcher.Fetch("template", Uri(repo), "main", refresh: true);

        Assert.IsTrue(File.Exists(Path.Combine(refreshed, "new.txt")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CacheDirectory_IsSharedByRepositoryAndRef_AndDistinctPerRef()
    {
        using var temp = new TempDirectory();
        var fetcher = Subject(temp);

        var first = fetcher.CacheDirectory("dotnet/maui-labs", "main");
        var second = fetcher.CacheDirectory("dotnet/maui-labs", "main");
        var other = fetcher.CacheDirectory("dotnet/maui-labs", "v0.1.0-preview.10");

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, other);
        StringAssert.StartsWith(Path.GetFileName(first), "maui-labs-main-");
        Assert.IsTrue(Path.GetFileName(first).Length < 80);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RemoteTags_ReadsTagsWithoutCloning()
    {
        using var temp = new TempDirectory();
        var repo = CreateRepo(temp, "v1.5.0", "v1.7.0", "nightly");

        var (tags, error) = Subject(temp).RemoteTags(Uri(repo));

        Assert.IsNull(error);
        CollectionAssert.AreEquivalent(new[] { "v1.5.0", "v1.7.0", "nightly" }, tags!.ToArray());
        Assert.IsFalse(Directory.Exists(Path.Combine(temp.Root, ".ai", "tmp", "catalog-cache")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Release_ReportsAStableNewerTagAndIgnoresPrereleases()
    {
        using var temp = new TempDirectory();
        var repo = CreateRepo(temp, "v1.5.0", "v1.7.0", "v1.9.0-rc1", "nightly");
        var workspace = PrepareRemoteWorkspace(temp, Uri(repo), "v1.5.0");

        var payload = workspace.Release();

        var row = payload.Sources.Single();
        Assert.IsTrue(row.Checked);
        Assert.IsTrue(row.PinnedTag);
        Assert.AreEqual("v1.5.0", row.Current);
        Assert.AreEqual("v1.7.0", row.Latest);
        Assert.IsFalse(row.LatestIsPrerelease);
        Assert.IsTrue(row.Newer);
        CollectionAssert.DoesNotContain(row.Tags.ToArray(), "nightly");
        CollectionAssert.AreEqual(new[] { "template" }, payload.Newer.ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Release_APrereleaseIsNotFoundAsANewerRelease()
    {
        using var temp = new TempDirectory();
        var repo = CreateRepo(temp, "v1.8.0", "v1.9.0-rc1");
        var workspace = PrepareRemoteWorkspace(temp, Uri(repo), "v1.8.0");

        var row = workspace.Release().Sources.Single();

        Assert.AreEqual("v1.8.0", row.Latest);
        Assert.IsFalse(row.Newer);
        Assert.AreEqual("v1.9.0-rc1", row.Tags[0]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Release_ABranchPinReportsTagsWithoutClaimingNewer()
    {
        using var temp = new TempDirectory();
        var repo = CreateRepo(temp, "v1.7.0");
        var workspace = PrepareRemoteWorkspace(temp, Uri(repo), "main");

        var row = workspace.Release().Sources.Single();

        Assert.IsFalse(row.PinnedTag);
        Assert.IsNull(row.Current);
        Assert.AreEqual("v1.7.0", row.Latest);
        Assert.IsFalse(row.Newer);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Release_CapsTheTagListButReportsTheRealCount()
    {
        using var temp = new TempDirectory();
        var tags = Enumerable.Range(0, 25).Select(minor => $"v1.{minor}.0").ToArray();
        var repo = CreateRepo(temp, tags);
        var workspace = PrepareRemoteWorkspace(temp, Uri(repo), "v1.0.0");

        var row = workspace.Release().Sources.Single();

        Assert.AreEqual(25, row.TagCount);
        Assert.AreEqual(20, row.Tags.Count);
        Assert.AreEqual("v1.24.0", row.Tags[0]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Release_OfflineReportsARowNotAFailure()
    {
        using var temp = new TempDirectory();
        var workspace = PrepareRemoteWorkspace(temp, "https://github.com/acme/catalog.git", "v1.0.0", offline: true);

        var payload = workspace.Release();

        var row = payload.Sources.Single();
        Assert.IsFalse(row.Checked);
        Assert.AreEqual("offline", row.Error);
        Assert.IsFalse(row.Newer);
        Assert.IsEmpty(payload.Newer);
    }

    private static WorkspaceQueries PrepareRemoteWorkspace(
        TempDirectory temp,
        string repo,
        string reference,
        bool offline = false)
    {
        var root = temp.CopyFixture(TestPaths.ParityWorkspace, "workspace");
        var manifest = new JsonObject
        {
            ["version"] = 1,
            ["sources"] = new JsonObject
            {
                ["template"] = new JsonObject
                {
                    ["repo"] = repo,
                    ["ref"] = reference,
                    ["resolve"] = "remote",
                },
            },
            ["skills"] = new JsonArray(),
            ["agents"] = new JsonArray(),
            ["materialize"] = "copy",
            ["exclude"] = new JsonArray(),
        };
        File.WriteAllText(Path.Combine(root, ".ai", "ai.json"), manifest.ToJsonString());

        var paths = new WorkspacePaths(root);
        return new WorkspaceQueries(paths, new GitSourceFetcher(paths, offline), offline);
    }
}
