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

    [TestMethod]
    public void CreatesPeerRegisteredMessage()
    {
        var message = BrokerTransportMessage.ForPeerRegistered(
            new BrokerClientRegistrationDto("client-1", BrokerClientRole.Client, 1));

        Assert.AreEqual(BrokerTransportMessageKind.PeerRegistered, message.Kind);
        Assert.AreEqual("client-1", message.PeerRegistration?.ClientId);
        Assert.AreEqual(BrokerClientRole.Client, message.PeerRegistration?.Role);
        Assert.AreEqual(1, message.PeerRegistration?.ClientIndex);
    }

    [TestMethod]
    public void TransportKindsCoverRegistrationEnvelopeAndPeerControl()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                BrokerTransportMessageKind.Registration,
                BrokerTransportMessageKind.RegistrationAccepted,
                BrokerTransportMessageKind.Envelope,
                BrokerTransportMessageKind.PeerRegistered
            },
            Enum.GetValues<BrokerTransportMessageKind>());
    }
}
