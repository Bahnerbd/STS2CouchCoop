using LocalCoop.Mod.Runtime;
using LocalCoop.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LocalCoop.Broker;
using System.Net;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class BrokerBackedNetServiceTests
{
    [TestMethod]
    public void NetIdIsStableForClientIndex()
    {
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), BrokerPlayerId.ForClientIndex(0));
        Assert.AreNotEqual(BrokerPlayerId.ForClientIndex(0), BrokerPlayerId.ForClientIndex(1));
    }

    [TestMethod]
    public async Task SendMessageCreatesTypedEnvelope()
    {
        var transport = new CapturingTransport();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-0",
            clientIndex: 0,
            transport);

        await service.SendMessageAsync(new FakeLobbyMessage("ready"), targetPlayerId: BrokerPlayerId.ForClientIndex(1), CancellationToken.None);

        var envelope = transport.Sent.Single();
        Assert.AreEqual("local-test", envelope.SessionId);
        Assert.AreEqual("client-0", envelope.SourceClientId);
        Assert.AreEqual("client-1", envelope.TargetClientId);
        Assert.AreEqual(typeof(FakeLobbyMessage).AssemblyQualifiedName, envelope.MessageType);
        CollectionAssert.Contains(envelope.Payload, (byte)'r');
    }

    [TestMethod]
    public async Task DispatchEnvelopeInvokesRegisteredHandler()
    {
        var transport = new CapturingTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        FakeLobbyMessage? received = null;
        service.RegisterMessageHandler<FakeLobbyMessage>(message => received = message);

        await service.DispatchEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FakeLobbyMessage("pick-ironclad"),
            sequence: 1),
            CancellationToken.None);

        Assert.IsNotNull(received);
        Assert.AreEqual("pick-ironclad", received.Value.Kind);
    }

    [TestMethod]
    public async Task UnregisterMessageHandlerStopsDispatch()
    {
        var transport = new CapturingTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        var count = 0;
        Action<FakeLobbyMessage> handler = _ => count++;
        service.RegisterMessageHandler(handler);
        service.UnregisterMessageHandler(handler);

        await service.DispatchEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FakeLobbyMessage("ready"),
            sequence: 1),
            CancellationToken.None);

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task BrokerClientTransportSendsThroughConnection()
    {
        await using var server = new BrokerTcpServer("local-test", IPAddress.Loopback, port: 0);
        await server.StartAsync(CancellationToken.None);
        await using var host = await BrokerClientConnection.ConnectAsync(
            new BrokerClientConfig(BrokerClientRole.Host, 0, "127.0.0.1", server.Port, "local-test"),
            "client-0",
            CancellationToken.None);
        await using var client = await BrokerClientConnection.ConnectAsync(
            new BrokerClientConfig(BrokerClientRole.Client, 1, "127.0.0.1", server.Port, "local-test"),
            "client-1",
            CancellationToken.None);
        var service = new BrokerBackedNetService(
            "local-test",
            "client-0",
            0,
            new BrokerClientEnvelopeTransport(host));

        await service.SendMessageAsync(new FakeLobbyMessage("ready"), BrokerPlayerId.ForClientIndex(1), CancellationToken.None);

        var received = await client.ReadEnvelopeAsync(CancellationToken.None);
        Assert.IsNotNull(received);
        Assert.AreEqual(typeof(FakeLobbyMessage).AssemblyQualifiedName, received.MessageType);
    }

    private readonly record struct FakeLobbyMessage(string Kind);

    private sealed class CapturingTransport : IBrokerEnvelopeTransport
    {
        public List<BrokerEnvelope> Sent { get; } = [];

        public Task SendEnvelopeAsync(BrokerEnvelope envelope, CancellationToken cancellationToken)
        {
            Sent.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
