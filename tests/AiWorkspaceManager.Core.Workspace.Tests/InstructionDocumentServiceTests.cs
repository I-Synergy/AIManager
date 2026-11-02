// File: tests/AiWorkspaceManager.Core.Workspace.Tests/InstructionDocumentServiceTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// AGENTS.md is the source of truth; the Copilot file is generated from it. References to missing
// items disappear, installed items are listed, and a generated file follows AGENTS edits.

using System.Text.Json.Nodes;
using AiWorkspaceManager.Core.Workspace;
using AiWorkspaceManager.TestUtilities;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class InstructionDocumentServiceTests
{
    private static (TempDirectory Temp, WorkspaceQueries Queries, JsonObject Manifest) Scenario(
        params string[] clients)
    {
        var temp = new TempDirectory();
        temp.Write("template/.keep", string.Empty);
        temp.Write("project/.ai/skills/alpha/SKILL.md", "---\nname: alpha\ndescription: Alpha work\n---\n");
        temp.Write("project/.ai/agents/lead.md", "---\nname: lead\ndescription: Leads work\n---\n");
        var manifest = new JsonObject
        {
            ["version"] = 1,
            ["sources"] = new JsonObject
            {
                ["template"] = new JsonObject
                {
                    ["repo"] = "I-Synergy/AI",
                    ["ref"] = "v1",
                    ["path"] = "../template",
                },
            },
            ["clients"] = new JsonArray(clients.Select(client => (JsonNode)client).ToArray()),
            ["materialize"] = "copy",
        };
        temp.Write("project/.ai/ai.json", manifest.ToJsonString());
        var paths = new WorkspacePaths(Path.Combine(temp.Root, "project"));
        return (temp, new WorkspaceQueries(paths, new GitSourceFetcher(paths, offline: true), offline: true), manifest);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Update_FiltersMissingReferencesAndListsInstalledItems()
    {
        var (temp, queries, manifest) = Scenario("claude", "copilot");
        using var scope = temp;
        temp.Write(
            "project/AGENTS.md",
            "# AGENTS\n\nRead `.ai/session-context.md` first.\n\n"
            + "See `.ai/skills/alpha/SKILL.md` and `.ai/skills/missing/SKILL.md`.\n");
        temp.Write(
            "project/.github/copilot-instructions.md",
            "## Project Context\n\nSee `.ai/project/architecture.md`.\n\n"
            + "## Work-Type Context Mapping\n\n| Work | Context |\n| --- | --- |\n"
            + "| Alpha | `.ai/skills/alpha/SKILL.md`, `.ai/patterns/x.md` |\n"
            + "| Missing | `.ai/skills/missing/SKILL.md` |\n");

        var result = new InstructionDocumentService(queries).Update(manifest);

        Assert.IsTrue(result.Count > 0);
        foreach (var relative in new[] { "AGENTS.md", ".github/copilot-instructions.md" })
        {
            var text = File.ReadAllText(Path.Combine(queries.Paths.Root, relative));
            Assert.IsFalse(text.Contains(".ai/skills/missing/SKILL.md", StringComparison.Ordinal));
            StringAssert.Contains(text, "## Installed skills and agents");
            StringAssert.Contains(text, "`alpha` — Alpha work");
            StringAssert.Contains(text, "`lead` — Leads work");
        }

        var copilot = File.ReadAllText(Path.Combine(queries.Paths.Root, ".github", "copilot-instructions.md"));
        StringAssert.Contains(copilot, "<!-- ai-workspace-manager:generated-from-agents -->");
        StringAssert.Contains(copilot, "# AGENTS");
        StringAssert.Contains(copilot, "## Project Context");
        Assert.IsFalse(copilot.Contains("| Missing |", StringComparison.Ordinal));
        StringAssert.Contains(copilot, "| Alpha |");
        StringAssert.Contains(copilot, "`.ai/patterns/x.md`");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Update_IsIdempotent()
    {
        var (temp, queries, manifest) = Scenario("claude", "copilot");
        using var scope = temp;
        temp.Write("project/AGENTS.md", "# AGENTS\n\nRead `.ai/session-context.md` first.\n");
        var service = new InstructionDocumentService(queries);

        service.Update(manifest);
        var before = File.ReadAllText(Path.Combine(queries.Paths.Root, "AGENTS.md"));
        var second = service.Update(manifest);

        Assert.AreEqual(before, File.ReadAllText(Path.Combine(queries.Paths.Root, "AGENTS.md")));
        Assert.AreEqual("unchanged", second["AGENTS.md"]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Update_MigratesLegacyMarkersWithoutDuplicating()
    {
        var (temp, queries, manifest) = Scenario("claude", "copilot");
        using var scope = temp;
        const string legacy = "<!-- ai-manager:";
        temp.Write(
            "project/AGENTS.md",
            "# AGENTS\n\nRead `.ai/session-context.md` first.\n\n"
            + "## Installed skills and agents\n\n"
            + legacy + "installed:start -->\n\n- `stale`\n\n"
            + legacy + "installed:end -->\n");
        temp.Write(
            "project/.github/copilot-instructions.md",
            "# GitHub Copilot Instructions\n\n" + legacy + "generated-from-agents -->\n\n"
            + "# AGENTS\n\nRead `.ai/session-context.md` first.\n\n"
            + legacy + "copilot-reference:start -->\n\n"
            + "## Project Context\n\nSee `.ai/project/architecture.md`.\n\n"
            + legacy + "copilot-reference:end -->\n");

        _ = new InstructionDocumentService(queries).Update(manifest);

        var agents = File.ReadAllText(Path.Combine(queries.Paths.Root, "AGENTS.md"));
        var copilot = File.ReadAllText(Path.Combine(queries.Paths.Root, ".github", "copilot-instructions.md"));
        foreach (var text in new[] { agents, copilot })
        {
            Assert.IsFalse(text.Contains(legacy, StringComparison.Ordinal));
        }

        Assert.AreEqual(1, agents.Split("<!-- ai-workspace-manager:installed:start -->").Length - 1);
        Assert.IsFalse(agents.Contains("- `stale`", StringComparison.Ordinal));
        StringAssert.Contains(agents, "`alpha` — Alpha work");
        Assert.AreEqual(1, copilot.Split("<!-- ai-workspace-manager:generated-from-agents -->").Length - 1);
        Assert.AreEqual(1, copilot.Split("## Project Context").Length - 1);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Update_RemovesTheGeneratedCopilotFileWhenCopilotIsDisabled()
    {
        var (temp, queries, manifest) = Scenario("claude");
        using var scope = temp;
        temp.Write("project/AGENTS.md", "# AGENTS\n\nRead `.ai/session-context.md` first.\n");
        temp.Write(
            "project/.github/copilot-instructions.md",
            "# GitHub Copilot Instructions\n\n<!-- ai-workspace-manager:generated-from-agents -->\n");

        var result = new InstructionDocumentService(queries).Update(manifest);

        Assert.AreEqual("removed", result[".github/copilot-instructions.md"]);
        Assert.IsFalse(File.Exists(Path.Combine(queries.Paths.Root, ".github", "copilot-instructions.md")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Update_TheCopilotCopyUsesGithubPathsButKeepsClaudeSettings()
    {
        var (temp, queries, manifest) = Scenario("claude", "copilot");
        using var scope = temp;
        temp.Write(
            "project/AGENTS.md",
            "# AGENTS\n\nRead `.ai/session-context.md` first.\n\n"
            + "Skills live in `.claude/skills/alpha/SKILL.md`; settings live in `.claude/settings.json`.\n");
        temp.Write(
            "project/.github/copilot-instructions.md",
            "## Project Context\n\nSee `.claude/agents/lead.md` and `.claude/settings.json`.\n");

        _ = new InstructionDocumentService(queries).Update(manifest);

        var copilot = File.ReadAllText(Path.Combine(queries.Paths.Root, ".github", "copilot-instructions.md"));
        Assert.IsFalse(copilot.Contains(".claude/skills", StringComparison.Ordinal));
        Assert.IsFalse(copilot.Contains(".claude/agents", StringComparison.Ordinal));
        StringAssert.Contains(copilot, ".github/skills/alpha/SKILL.md");
        StringAssert.Contains(copilot, ".github/agents/lead.md");
        StringAssert.Contains(copilot, ".claude/settings.json");
    }
}
