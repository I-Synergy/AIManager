// File: tests/AiWorkspaceManager.Common.Tests/ItemIdTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Selection ids are the contract every manifest, lock and payload shares.

using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Common.Tests;

[TestClass]
public sealed class ItemIdTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Split_ABareIdBelongsToTheTemplateSource()
    {
        Assert.AreEqual(("template", "alpha"), ItemId.Split("alpha"));
        Assert.AreEqual(("labs", "beta"), ItemId.Split("labs:beta"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MakeAndDisplay_AreInverseForTheTemplateSource()
    {
        Assert.AreEqual("template:alpha", ItemId.Make("template", "alpha"));
        Assert.AreEqual("alpha", ItemId.Display("template:alpha"));
        Assert.AreEqual("labs:beta", ItemId.Display("labs:beta"));
        Assert.AreEqual(string.Empty, ItemId.Display(null));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Canonical_NormalisesOrFails()
    {
        Assert.AreEqual("template:alpha", ItemId.Canonical("alpha"));
        Assert.AreEqual("labs:beta", ItemId.Canonical(" labs:beta "));
        Assert.ThrowsExactly<ResolverException>(() => ItemId.Canonical("labs:"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SoftCanonical_ReturnsNullForAnUnusableValue()
    {
        Assert.AreEqual("template:alpha", ItemId.SoftCanonical("alpha"));
        Assert.IsNull(ItemId.SoftCanonical("labs:"));
        Assert.IsNull(ItemId.SoftCanonical(null));
    }
}
