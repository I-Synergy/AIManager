// File: tests/AiWorkspaceManager.Core.Catalog.Tests/SemanticVersionTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// Release ordering decides whether the updates banner offers an upgrade; a prerelease must never
// be advertised as the newer release to a stable pin.

using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Catalog.Tests;

[TestClass]
public sealed class SemanticVersionTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void TryParse_OrdersNumerically()
    {
        var oneTen = SemanticVersion.TryParse("v1.10.0");
        var oneNine = SemanticVersion.TryParse("v1.9.9");

        Assert.IsNotNull(oneTen);
        Assert.IsNotNull(oneNine);
        Assert.IsTrue(oneTen > oneNine);
        Assert.AreEqual(SemanticVersion.TryParse("1.6.0"), SemanticVersion.TryParse("v1.6.0"));
        Assert.AreEqual(SemanticVersion.TryParse("v1.6.0+build.5"), SemanticVersion.TryParse("v1.6.0"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TryParse_PrereleaseSortsBelowItsRelease()
    {
        var prerelease = SemanticVersion.TryParse("v1.6.0-rc1");
        var release = SemanticVersion.TryParse("v1.6.0");

        Assert.IsNotNull(prerelease);
        Assert.IsNotNull(release);
        Assert.IsTrue(prerelease < release);
        Assert.IsFalse(prerelease.IsStable);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TryParse_RejectsWhatIsNotSemanticVersioning()
    {
        foreach (var tag in new[] { "main", "latest", "nightly", "v1.6", "v1.6.0.1", "" })
        {
            Assert.IsNull(SemanticVersion.TryParse(tag), tag);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ReleaseTags_OrdersNewestFirstAndDropsNonSemver()
    {
        var tags = SemanticVersion.ReleaseTags(["v1.5.0", "nightly", "v1.10.0", "latest", "v1.9.0", "main"]);

        CollectionAssert.AreEqual(new[] { "v1.10.0", "v1.9.0", "v1.5.0" }, tags.ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ReleaseTags_AcceptsCalendarVersions()
    {
        var tags = SemanticVersion.ReleaseTags(["2026.11026.23559", "nightly", "2026.11027.12343"]);

        CollectionAssert.AreEqual(new[] { "2026.11027.12343", "2026.11026.23559" }, tags.ToArray());
    }
}
