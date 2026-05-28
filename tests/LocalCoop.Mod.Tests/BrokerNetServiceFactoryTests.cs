using LocalCoop.Mod.Runtime;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class BrokerNetServiceFactoryTests
{
    [TestMethod]
    public void TryCreateReturnsNullWhenBrokerModeIsDisabled()
    {
        var settings = new BrokerModeSettings(false, null, "client-0", "events.txt", "disabled");

        var service = BrokerNetServiceFactory.TryCreate(settings, new CapturingTransport());

        Assert.IsNull(service);
    }

    [TestMethod]
    public void TryCreateBuildsServiceFromEnabledBrokerSettings()
    {
        var settings = new BrokerModeSettings(
            true,
            new BrokerClientConfig(BrokerClientRole.Host, 0, "127.0.0.1", 38989, "local-test"),
            "client-0",
            "events.txt",
            null);

        var service = BrokerNetServiceFactory.TryCreate(settings, new CapturingTransport());

        Assert.IsNotNull(service);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), service.NetId);
        Assert.AreEqual("local-test", service.GetRawLobbyIdentifier());
    }

    [TestMethod]
    public void BrokerNetGameServiceImplementsHostGameServiceForHostLobby()
    {
        var inner = new BrokerBackedNetService(
            "local-test",
            "client-0",
            0,
            new CapturingTransport());
        var service = new BrokerNetGameService(inner, NetGameType.Host);

        Assert.IsInstanceOfType<INetHostGameService>(service);
    }

    private sealed class CapturingTransport : IBrokerEnvelopeTransport
    {
        public Task SendEnvelopeAsync(BrokerEnvelope envelope, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
