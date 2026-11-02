// File: tests/AiWorkspaceManager.Core.Tests/Protocol/RpcExceptionTests.cs
// Traces: none — legacy mode, this slice declares no acceptance criteria.
//
// The structured error carries a code the host maps onto user-facing text.

using AiWorkspaceManager.Core.Protocol;

namespace AiWorkspaceManager.Core.Tests.Protocol;

[TestClass]
public sealed class RpcExceptionTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Default_UsesTheInternalErrorCode()
    {
        var error = new RpcException();
        Assert.AreEqual(RpcErrorCodes.InternalError, error.Code);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CodeAndMessage_ArePreservedIncludingAnInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var error = new RpcException("invalidParams", "message", inner);

        Assert.AreEqual("invalidParams", error.Code);
        Assert.AreEqual("message", error.Message);
        Assert.AreSame(inner, error.InnerException);
    }
}
