using LocalCoop.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Protocol.Tests;

[TestClass]
public sealed class BrokerTransportMessageTests
{
    [TestMethod]
    public void CreatesRegistrationAcceptedMessage()
    {
        var accepted = BrokerTransportMessage.ForRegistrationAccepted("client-1", "local-test");

        Assert.AreEqual(BrokerTransportMessageKind.RegistrationAccepted, accepted.Kind);
        Assert.AreEqual("client-1", accepted.RegistrationAccepted?.ClientId);
        Assert.AreEqual("local-test", accepted.RegistrationAccepted?.SessionId);
    }
}

