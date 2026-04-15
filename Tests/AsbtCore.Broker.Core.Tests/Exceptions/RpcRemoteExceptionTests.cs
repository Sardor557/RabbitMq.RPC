using AsbtCore.Broker.Core.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.Core.Tests.Exceptions
{
    [TestClass]
    public class RpcRemoteExceptionTests
    {
        [TestMethod]
        public void Ctor_AllFields_ArePopulated()
        {
            var ex = new RpcRemoteException("msg", "CODE", "System.Exception", "details");

            Assert.AreEqual("msg", ex.Message);
            Assert.AreEqual("CODE", ex.RemoteCode);
            Assert.AreEqual("System.Exception", ex.RemoteExceptionType);
            Assert.AreEqual("details", ex.RemoteDetails);
        }

        [TestMethod]
        public void Ctor_OptionalArgsOmitted_DoesNotThrow()
        {
            var ex = new RpcRemoteException("msg");

            Assert.AreEqual("msg", ex.Message);
            Assert.IsNull(ex.RemoteCode);
            Assert.IsNull(ex.RemoteExceptionType);
            Assert.IsNull(ex.RemoteDetails);
        }
    }
}
