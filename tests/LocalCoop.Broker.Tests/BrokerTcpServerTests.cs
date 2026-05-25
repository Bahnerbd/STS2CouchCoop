using LocalCoop.Broker;
using LocalCoop.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Sockets;

namespace LocalCoop.Broker.Tests;

[TestClass]
public sealed class BrokerTcpServerTests
{
    [TestMethod]
    public async Task RoutesEnvelopeBetweenRegisteredTcpClients()
    {
        await using var server = new BrokerTcpServer("local-test", IPAddress.Loopback, port: 0);
        await server.StartAsync(CancellationToken.None);

        using var host = new TcpClient();
        using var client = new TcpClient();
        await host.ConnectAsync(IPAddress.Loopback, server.Port);
        await client.ConnectAsync(IPAddress.Loopback, server.Port);

        await BrokerFrameCodec.WriteAsync(
            host.GetStream(),
            BrokerTransportMessage.ForRegistration(new BrokerClientRegistrationDto("host", BrokerClientRole.Host, 0)),
            CancellationToken.None);
        var hostAccepted = await BrokerFrameCodec.ReadAsync(host.GetStream(), CancellationToken.None);
        Assert.AreEqual(BrokerTransportMessageKind.RegistrationAccepted, hostAccepted?.Kind);
        Assert.AreEqual("host", hostAccepted?.RegistrationAccepted?.ClientId);

        await BrokerFrameCodec.WriteAsync(
            client.GetStream(),
            BrokerTransportMessage.ForRegistration(new BrokerClientRegistrationDto("client-1", BrokerClientRole.Client, 1)),
            CancellationToken.None);
        var clientAccepted = await BrokerFrameCodec.ReadAsync(client.GetStream(), CancellationToken.None);
        Assert.AreEqual(BrokerTransportMessageKind.RegistrationAccepted, clientAccepted?.Kind);
        Assert.AreEqual("client-1", clientAccepted?.RegistrationAccepted?.ClientId);

        await BrokerFrameCodec.WriteAsync(
            host.GetStream(),
            BrokerTransportMessage.ForEnvelope(BrokerEnvelope.Broadcast(
                "local-test",
                "host",
                "LobbyChanged",
                [8, 9],
                sequence: 1)),
            CancellationToken.None);

        var routed = await BrokerFrameCodec.ReadAsync(client.GetStream(), CancellationToken.None);

        Assert.IsNotNull(routed);
        Assert.AreEqual(BrokerTransportMessageKind.Envelope, routed.Kind);
        Assert.AreEqual("host", routed.Envelope?.SourceClientId);
        Assert.AreEqual("LobbyChanged", routed.Envelope?.MessageType);
        CollectionAssert.AreEqual(new byte[] { 8, 9 }, routed.Envelope?.Payload.ToArray());
    }
}
