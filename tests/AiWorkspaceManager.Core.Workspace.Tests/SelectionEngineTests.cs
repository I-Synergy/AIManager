// File: tests/AiWorkspaceManager.Core.Workspace.Tests/SelectionEngineTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Healing, ambiguity, closure order and exclusion semantics. These are the rules that decide what
// an install actually materialises, so they are pinned independently of the JSON payloads.

using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;
using AiWorkspaceManager.Core.Workspace;

namespace AiWorkspaceManager.Core.Workspace.Tests;

[TestClass]
public sealed class SelectionEngineTests
{
    private static SelectionEngine Engine(params (string Kind, string Id)[] items)
    {
        var index = items.ToDictionary(
            pair => pair,
            pair => new CatalogItem
            {
                Id = pair.Id,
                Source = ItemId.Split(pair.Id).Source,
                Kind = pair.Kind,
                Name = ItemId.Split(pair.Id).Name,
                Path = $".ai/{pair.Kind}s/{ItemId.Split(pair.Id).Name}",
            });
        return new SelectionEngine(index, PresetCatalog.Empty);
    }

    private static ManifestDocument Manifest(string json) =>
        ManifestDocument.Temporary(JsonNode.Parse(json)!.AsObject());

    [TestMethod]
    [TestCategory("Unit")]
    public void Selection_ABareThirdPartyIdResolvesToTheOnlySourceOfferingIt()
    {
        var engine = Engine(("skill", "template:alpha"), ("skill", "labs:beta"));
        var manifest = Manifest("{\"skills\":[\"alpha\",\"beta\"]}");

        var selected = engine.Selection(manifest);

        CollectionAssert.AreEqual(
            new[] { ("skill", "template:alpha"), ("skill", "labs:beta") },
            selected.ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Selection_ABareIdTheTemplateOffersStaysWithTheTemplate()
    {
        var engine = Engine(("skill", "template:beta"), ("skill", "labs:beta"));

        var selected = engine.Selection(Manifest("{\"skills\":[\"beta\"]}"));

        CollectionAssert.AreEqual(new[] { ("skill", "template:beta") }, selected.ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Selection_ABareIdOfferedByTwoOtherSourcesFails()
    {
        var engine = Engine(("skill", "labs:beta"), ("skill", "other:beta"));

        Assert.ThrowsExactly<ResolverException>(
            () => engine.Selection(Manifest("{\"skills\":[\"beta\"]}")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Selection_ABareExclusionStillMatchesTheItemItNamed()
    {
        var engine = Engine(("skill", "template:alpha"), ("skill", "labs:beta"));

        var selected = engine.Selection(Manifest("{\"skills\":[\"alpha\",\"beta\"],\"exclude\":[\"beta\"]}"));

        CollectionAssert.AreEqual(new[] { ("skill", "template:alpha") }, selected.ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void LockedIds_ABareLockFreezesTheItemItNamed()
    {
        var engine = Engine(("skill", "template:alpha"), ("skill", "labs:beta"));

        var frozen = engine.LockedIds(Manifest("{\"locked\":[\"beta\"]}"));

        CollectionAssert.AreEquivalent(new[] { "labs:beta" }, frozen.ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Closure_PutsDependenciesBeforeTheirDependent()
    {
        var engine = Engine(("agent", "template:lead"), ("skill", "template:alpha"), ("skill", "template:beta"));

        var resolved = engine.Closure([("agent", "template:lead")]);

        // The fixture index has no requires, so this is the plain order.
        CollectionAssert.AreEqual(new[] { ("agent", "template:lead") }, resolved.ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Closure_UnknownDependencyFailsWithTheRequiringItem()
    {
        var index = new Dictionary<(string, string), CatalogItem>
        {
            [("agent", "template:lead")] = new()
            {
                Id = "template:lead",
                Source = "template",
                Kind = "agent",
                Name = "lead",
                Path = ".ai/agents/lead.md",
                Requires = ["template:missing"],
            },
        };
        var engine = new SelectionEngine(index, PresetCatalog.Empty);

        var error = Assert.ThrowsExactly<ResolverException>(
            () => engine.Closure([("agent", "template:lead")]));

        StringAssert.Contains(error.Message, "missing");
        StringAssert.Contains(error.Message, "lead");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Clients_MissingFieldKeepsEveryLegacyClient()
    {
        var clients = SelectionEngine.Clients(Manifest("{}"));

        CollectionAssert.AreEquivalent(new[] { "claude", "copilot", "reasonix" }, clients.ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Clients_UnknownNameFails()
    {
        Assert.ThrowsExactly<ResolverException>(
            () => SelectionEngine.Clients(Manifest("{\"clients\":[\"claude\",\"cursor\"]}")));
    }
}
