// File: tests/AiWorkspaceManager.Core.Workspace.Tests/PayloadContractTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Wire shapes are the host/webview contract. Construct every payload and pin the camelCase keys
// the TypeScript side reads.

using System.Text.Json;
using AiWorkspaceManager.Core.Workspace;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class PayloadContractTests
{
    private static string Serialize(object payload) =>
        JsonSerializer.Serialize(payload, JsonFiles.Wire)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

    [TestMethod]
    [TestCategory("Unit")]
    public void BootstrapPayload_CarriesTheSetupSummary()
    {
        var payload = new BootstrapPayload(
            true,
            "p",
            2,
            ["aspire-skills"],
            new BootstrapToolingSummary(["a"], ["b"], ["c"], ["d"], ["e"]),
            new BootstrapScaffoldSummary(["f"], ["g"]),
            ["h"],
            "created",
            "created",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["AGENTS.md"] = "updated" });

        var json = Serialize(payload);

        StringAssert.Contains(json, "\"created\":true");
        StringAssert.Contains(json, "\"tooling\":");
        StringAssert.Contains(json, "\"scaffold\":");
        StringAssert.Contains(json, "\"legacyRemoved\":");
        StringAssert.Contains(json, "\"gitignore\":\"created\"");
        StringAssert.Contains(json, "\"AGENTS.md\":\"updated\"");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MutationPayloads_CarryTheirCamelCaseFields()
    {
        StringAssert.Contains(Serialize(new LockPayload("lock", ["alpha"], ["alpha"])), "\"locked\":");
        StringAssert.Contains(Serialize(new AddPayload([new ItemRef("template:alpha", "skill")], "p")), "\"added\":");
        StringAssert.Contains(
            Serialize(new RemovePayload([new RemovedItem("template:alpha", "skill", "list")],
                [new PrunedItem("template:alpha", "skill", ".ai/skills/alpha", true, 1)])),
            "\"pruned\":");
        StringAssert.Contains(
            Serialize(new UpdateApplyPayload(true, false,
                [new UpdateAppliedItem("template:alpha", true, "ok")], [], [], false)),
            "\"appliedItems\":");
        StringAssert.Contains(Serialize(new PinPayload("template", "I-Synergy/AI", "v2", 1, "p")), "\"ref\":\"v2\"");
        StringAssert.Contains(
            Serialize(new PresetMutationPayload("add", ["Everything"], ["Everything"], "p")),
            "\"presets\":");
        StringAssert.Contains(Serialize(new IndexPayload(false, true, "p", 1, null)), "\"compact\":null");
        StringAssert.Contains(Serialize(new NoticePayload(false, "p", 1)), "\"items\":");
        StringAssert.Contains(Serialize(new ScaffoldPayload(["a"], ["b"])), "\"copied\":");
        StringAssert.Contains(Serialize(new ClientsPayload(["claude"])), "\"clients\":");
        StringAssert.Contains(Serialize(new VendorToolingSummary(["a"], ["b"], "wrote")), "\"manifest\":");
    }
}
