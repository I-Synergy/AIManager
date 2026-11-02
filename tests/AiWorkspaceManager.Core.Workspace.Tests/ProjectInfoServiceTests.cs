// File: tests/AiWorkspaceManager.Core.Workspace.Tests/ProjectInfoServiceTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Name/intention writes fill only the still-template documents; a customized file is left alone.

using AiWorkspaceManager.Core.Workspace;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class ProjectInfoServiceTests
{
    private const string SessionTemplate =
        "# Session Context Template (CUSTOMIZE THIS)\n\n"
        + "**Instructions:** Copy this to `.ai/session-context.md` and customize for your project.\n\n"
        + "# {ApplicationName} Session Context\n\n"
        + "**Last Updated:** [DateTime]\n"
        + "**Updated By:** [Claude Code | Copilot | Reasonix Code | User]\n"
        + "**Project Version:** [Version]\n\n"
        + "**Application Name:** [{ApplicationName}]\n"
        + "**Purpose:** [What this application does]\n";

    private const string ArchitectureTemplate =
        "# Project Architecture (CUSTOMIZE THIS)\n\n"
        + "**Instructions:** Document your project's architecture decisions.\n";

    private static ProjectInfoService Subject(TempDirectory temp, out WorkspacePaths paths)
    {
        paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));
        return new ProjectInfoService(paths);
    }

    private static void Scaffold(TempDirectory temp)
    {
        temp.Write("project/.ai/session-context.md", SessionTemplate);
        temp.Write("project/.ai/project/architecture.md", ArchitectureTemplate);
        temp.Write("project/.ai/project/domains.md", ArchitectureTemplate);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectedName_PrefersASolutionOverTheFolder()
    {
        using var temp = new TempDirectory();
        var service = Subject(temp, out _);
        temp.Write("project/Payments.slnx", "<Solution />\n");

        Assert.AreEqual("Payments", service.DetectedName());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Describe_FillsTheTemplateDocumentsAndStoresTheInfo()
    {
        using var temp = new TempDirectory();
        var service = Subject(temp, out var paths);
        Scaffold(temp);

        var info = service.Describe("Payments", "Track invoices and payouts");

        Assert.AreEqual("Payments", info.Name);
        CollectionAssert.Contains(info.Written.ToArray(), ".ai/session-context.md");
        CollectionAssert.Contains(info.Written.ToArray(), ".ai/project/architecture.md");
        CollectionAssert.Contains(info.Written.ToArray(), ".ai/project/domains.md");
        StringAssert.Contains(File.ReadAllText(Path.Combine(paths.Root, ".ai", "session-context.md")), "# Payments Session Context");
        StringAssert.Contains(File.ReadAllText(Path.Combine(paths.Root, ".ai", "project", "architecture.md")), "# Payments — Project Architecture");
        Assert.IsTrue(File.Exists(paths.ProjectInfoPath));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Describe_NeverRewritesACustomizedDocument()
    {
        using var temp = new TempDirectory();
        var service = Subject(temp, out var paths);
        Scaffold(temp);
        var session = Path.Combine(paths.Root, ".ai", "session-context.md");
        File.WriteAllText(session, "# Payments Session Context\n\nOur own notes.\n");

        var info = service.Describe("Payments", "Track invoices");

        CollectionAssert.Contains(info.Skipped.ToArray(), ".ai/session-context.md");
        Assert.AreEqual("# Payments Session Context\n\nOur own notes.\n", File.ReadAllText(session));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Describe_FillsTheApplicationNameAcrossScaffoldFolders()
    {
        using var temp = new TempDirectory();
        var service = Subject(temp, out var paths);
        Scaffold(temp);
        temp.Write("project/.ai/patterns/api.md", "`{ApplicationName}.{Domain}.{Entity}`\n");
        temp.Write("project/.ai/skills/alpha/SKILL.md", "{ApplicationName}\n");

        var info = service.Describe("Payments", "Track invoices");

        CollectionAssert.Contains(info.Written.ToArray(), ".ai/patterns/api.md");
        Assert.AreEqual(
            "`Payments.{Domain}.{Entity}`\n",
            File.ReadAllText(Path.Combine(paths.Root, ".ai", "patterns", "api.md")));
        Assert.AreEqual(
            "{ApplicationName}\n",
            File.ReadAllText(Path.Combine(paths.Root, ".ai", "skills", "alpha", "SKILL.md")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Read_ReturnsWhatWasWritten()
    {
        using var temp = new TempDirectory();
        var service = Subject(temp, out _);
        Scaffold(temp);
        service.Describe("Payments", "Track invoices");

        var info = service.Read();

        Assert.AreEqual("Payments", info.Name);
        Assert.AreEqual("Track invoices", info.Intention);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void EnsureReadme_CreatesAStarterAndTheFormFillsIt()
    {
        using var temp = new TempDirectory();
        var service = Subject(temp, out var paths);

        Assert.AreEqual("created", service.EnsureReadme());
        service.Describe("Payments", "Track invoices");

        Assert.AreEqual("# Payments\n\nTrack invoices\n", File.ReadAllText(Path.Combine(paths.Root, "README.md")));
    }
}
