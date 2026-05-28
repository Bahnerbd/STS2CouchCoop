using LocalCoop.Mod.Runtime;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Channels;

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
    public void TryCreatePassesLoggerToBrokerService()
    {
        var settings = new BrokerModeSettings(
            true,
            new BrokerClientConfig(BrokerClientRole.Host, 0, "127.0.0.1", 38989, "local-test"),
            "client-0",
            "events.txt",
            null);
        var logs = new List<string>();

        var service = BrokerNetServiceFactory.TryCreate(settings, new CapturingTransport(), logs.Add);
        service!.SendMessage(new FakeLobbyMessage("ready"));

        StringAssert.Contains(logs.Single(), "Broker outbound");
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

    [TestMethod]
    public async Task BrokerNetGameServiceStartsReceiveLoop()
    {
        var transport = new QueuedTransport();
        var inner = new BrokerBackedNetService(
            "local-test",
            "client-1",
            1,
            transport);
        var receivedSource = new TaskCompletionSource<FakeLobbyMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        inner.RegisterMessageHandler<FakeLobbyMessage>(message => receivedSource.SetResult(message));
        using var service = new BrokerNetGameService(inner, NetGameType.Client);

        await transport.QueueEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FakeLobbyMessage("ready"),
            sequence: 1));

        var received = await receivedSource.Task.WaitAsync(TimeSpan.FromSeconds(1));
        service.Disconnect(default, now: true);

        Assert.AreEqual("ready", received.Kind);
    }

    private sealed class CapturingTransport : IBrokerEnvelopeTransport
    {
        public Task SendEnvelopeAsync(BrokerEnvelope envelope, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<BrokerEnvelope?> ReceiveEnvelopeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<BrokerEnvelope?>(null);
        }
    }

    private readonly record struct FakeLobbyMessage(string Kind);

    private sealed class QueuedTransport : IBrokerEnvelopeTransport
    {
        private readonly Channel<BrokerEnvelope?> _incoming = Channel.CreateUnbounded<BrokerEnvelope?>();

        public Task SendEnvelopeAsync(BrokerEnvelope envelope, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task<BrokerEnvelope?> ReceiveEnvelopeAsync(CancellationToken cancellationToken)
        {
            return await _incoming.Reader.ReadAsync(cancellationToken);
        }

        public async Task QueueEnvelopeAsync(BrokerEnvelope envelope)
        {
            await _incoming.Writer.WriteAsync(envelope);
        }
    }
}
