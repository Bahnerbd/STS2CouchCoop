using LocalCoop.Mod.Runtime;
using LocalCoop.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LocalCoop.Broker;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using System.Runtime.CompilerServices;
using System.Net;
using System.Threading.Channels;
using Runtime = LocalCoop.Mod.Runtime;

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
    public async Task SendMessageLogsOutboundEnvelope()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-0",
            clientIndex: 0,
            transport,
            logs.Add);

        await service.SendMessageAsync(new FakeLobbyMessage("ready"), targetPlayerId: BrokerPlayerId.ForClientIndex(1), CancellationToken.None);

        StringAssert.Contains(logs.Single(), "Broker outbound");
        StringAssert.Contains(logs.Single(), "local-test");
        StringAssert.Contains(logs.Single(), "client-0");
        StringAssert.Contains(logs.Single(), "client-1");
        StringAssert.Contains(logs.Single(), nameof(FakeLobbyMessage));
    }

    [TestMethod]
    public void SyncSendMessageBroadcastCreatesTypedEnvelope()
    {
        var transport = new CapturingTransport();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-0",
            clientIndex: 0,
            transport);

        service.SendMessage(new FakeLobbyMessage("appearance"));

        var envelope = transport.Sent.Single();
        Assert.AreEqual("client-0", envelope.SourceClientId);
        Assert.IsNull(envelope.TargetClientId);
        Assert.AreEqual(typeof(FakeLobbyMessage).AssemblyQualifiedName, envelope.MessageType);
    }

    [TestMethod]
    public void SendMessageSkipsPeerInputMessagesForLobbyOnlyBrokerMode()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-1",
            clientIndex: 1,
            transport,
            logs.Add);

        service.SendMessage(new PeerInputMessage());

        Assert.AreEqual(0, transport.Sent.Count);
        Assert.IsTrue(logs.Any(log => log.Contains("suppressed outbound", StringComparison.Ordinal)
            && log.Contains(nameof(PeerInputMessage), StringComparison.Ordinal)));
    }

    [TestMethod]
    public void SendMessageSkipsNullLobbyCharacterChanges()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-1",
            clientIndex: 1,
            transport,
            logs.Add);

        service.SendMessage(new LobbyPlayerChangedCharacterMessage());

        Assert.AreEqual(0, transport.Sent.Count);
        Assert.IsTrue(logs.Any(log => log.Contains("suppressed outbound", StringComparison.Ordinal)
            && log.Contains(nameof(LobbyPlayerChangedCharacterMessage), StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ClientSendMessageSkipsCharacterChangesBeforeHostJoinResponse()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-1",
            clientIndex: 1,
            transport,
            logs.Add);
        var concreteCharacterType = typeof(CharacterModel).Assembly.GetTypes()
            .First(type => !type.IsAbstract && typeof(CharacterModel).IsAssignableFrom(type));
        var message = new LobbyPlayerChangedCharacterMessage
        {
            character = (CharacterModel)RuntimeHelpers.GetUninitializedObject(concreteCharacterType)
        };

        service.SendMessage(message);

        Assert.AreEqual(0, transport.Sent.Count);
        Assert.IsTrue(logs.Any(log => log.Contains("suppressed outbound", StringComparison.Ordinal)
            && log.Contains("waiting for host join response", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ClientSendMessageSkipsCharacterChangeEchoAfterRemoteCharacterChange()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-1",
            clientIndex: 1,
            transport,
            logs.Add);
        var message = CreateUninitializedCharacterChange();
        await service.DispatchEnvelopeAsync(EnvelopeFor<ClientLobbyJoinResponseMessage>("client-0", "client-1", sequence: 1), CancellationToken.None);
        await service.DispatchEnvelopeAsync(EnvelopeFor<LobbyPlayerChangedCharacterMessage>("client-0", targetClientId: null, sequence: 2), CancellationToken.None);

        try
        {
            service.SendMessage(message);
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected immediate character-change echo to be suppressed, but send threw {exception.GetType().Name}: {exception.Message}");
        }

        Assert.AreEqual(0, transport.Sent.Count);
        Assert.IsTrue(logs.Any(log => log.Contains("suppressed outbound", StringComparison.Ordinal)
            && log.Contains("recent remote character change", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SendMessageSkipsCharacterChangeEchoDuringInboundLogging()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var message = CreateUninitializedCharacterChange();
        Exception? sendException = null;
        BrokerBackedNetService? service = null;
        service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-0",
            clientIndex: 0,
            transport,
            log =>
            {
                logs.Add(log);
                if (log.Contains("Broker inbound", StringComparison.Ordinal)
                    && log.Contains(nameof(LobbyPlayerChangedCharacterMessage), StringComparison.Ordinal))
                {
                    try
                    {
                        service!.SendMessage(message);
                    }
                    catch (Exception exception)
                    {
                        sendException = exception;
                    }
                }
            });

        await service.DispatchEnvelopeAsync(EnvelopeFor<LobbyPlayerChangedCharacterMessage>("client-1", targetClientId: null, sequence: 1), CancellationToken.None);

        Assert.IsNull(sendException, $"Expected character-change echo during inbound logging to be suppressed, but send threw {sendException?.GetType().Name}: {sendException?.Message}");
        Assert.AreEqual(0, transport.Sent.Count);
        Assert.IsTrue(logs.Any(log => log.Contains("suppressed outbound", StringComparison.Ordinal)
            && log.Contains("recent remote character change", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SendJoinResponseReplaysCachedLobbyCharacterStateToJoiningClient()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-0",
            clientIndex: 0,
            transport,
            logs.Add);
        await service.DispatchEnvelopeAsync(EnvelopeFor<LobbyPlayerChangedCharacterMessage>("client-0", targetClientId: null, sequence: 7), CancellationToken.None);
        var joinResponse = new ClientLobbyJoinResponseMessage
        {
            playersInLobby = [],
            modifiers = []
        };

        await service.SendMessageAsync(joinResponse, BrokerPlayerId.ForClientIndex(1), CancellationToken.None);

        Assert.AreEqual(2, transport.Sent.Count);
        Assert.AreEqual(typeof(ClientLobbyJoinResponseMessage).AssemblyQualifiedName, transport.Sent[0].MessageType);
        var replay = transport.Sent[1];
        Assert.AreEqual("local-test", replay.SessionId);
        Assert.AreEqual("client-0", replay.SourceClientId);
        Assert.AreEqual("client-1", replay.TargetClientId);
        Assert.AreEqual(typeof(LobbyPlayerChangedCharacterMessage).AssemblyQualifiedName, replay.MessageType);
        Assert.AreEqual(2, replay.Sequence);
        Assert.IsTrue(logs.Any(log => log.Contains("Broker replay outbound", StringComparison.Ordinal)
            && log.Contains(nameof(LobbyPlayerChangedCharacterMessage), StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SendJoinResponseDoesNotReplayJoiningClientsOwnCachedCharacterState()
    {
        var transport = new CapturingTransport();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-0",
            clientIndex: 0,
            transport);
        await service.DispatchEnvelopeAsync(EnvelopeFor<LobbyPlayerChangedCharacterMessage>("client-1", targetClientId: null, sequence: 7), CancellationToken.None);
        var joinResponse = new ClientLobbyJoinResponseMessage
        {
            playersInLobby = [],
            modifiers = []
        };

        await service.SendMessageAsync(joinResponse, BrokerPlayerId.ForClientIndex(1), CancellationToken.None);

        Assert.AreEqual(1, transport.Sent.Count);
        Assert.AreEqual(typeof(ClientLobbyJoinResponseMessage).AssemblyQualifiedName, transport.Sent[0].MessageType);
    }

    [TestMethod]
    public void TracksConnectionLoadingAndRawLobbyIdentifier()
    {
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-0",
            clientIndex: 0,
            new CapturingTransport());

        Assert.IsTrue(service.IsConnected);
        Assert.IsFalse(service.IsGameLoading);

        service.SetGameLoading(true);

        Assert.IsTrue(service.IsGameLoading);
        Assert.AreEqual("local-test", service.GetRawLobbyIdentifier());
        service.Update();
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
    public async Task DispatchEnvelopePreservesPublicFieldMessages()
    {
        var transport = new CapturingTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        FieldLobbyMessage received = default;
        service.RegisterMessageHandler<FieldLobbyMessage>(message => received = message);

        await service.DispatchEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FieldLobbyMessage { kind = "ready" },
            sequence: 1),
            CancellationToken.None);

        Assert.AreEqual("ready", received.kind);
    }

    [TestMethod]
    public async Task DispatchEnvelopeUsesPacketSerializationForNetMessages()
    {
        var transport = new CapturingTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        PacketSerializableMessage received = default;
        service.RegisterMessageHandler<PacketSerializableMessage>(message => received = message);

        await service.DispatchEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new PacketSerializableMessage { value = "ready", unsupported = new IntPtr(42) },
            sequence: 1),
            CancellationToken.None);

        Assert.AreEqual("ready", received.value);
    }

    [TestMethod]
    public async Task DispatchEnvelopeLogsInboundEnvelope()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport, logs.Add);

        await service.DispatchEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FakeLobbyMessage("ready"),
            sequence: 1),
            CancellationToken.None);

        StringAssert.Contains(logs.Single(), "Broker inbound");
        StringAssert.Contains(logs.Single(), "local-test");
        StringAssert.Contains(logs.Single(), "client-0");
        StringAssert.Contains(logs.Single(), "client-1");
        StringAssert.Contains(logs.Single(), nameof(FakeLobbyMessage));
    }

    [TestMethod]
    public async Task DispatchEnvelopeInvokesRegisteredHandlerWithSenderId()
    {
        var transport = new CapturingTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        FakeLobbyMessage? received = null;
        ulong? sender = null;
        service.RegisterMessageHandler<FakeLobbyMessage>((message, senderId) =>
        {
            received = message;
            sender = senderId;
        });

        await service.DispatchEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FakeLobbyMessage("ready"),
            sequence: 1),
            CancellationToken.None);

        Assert.IsNotNull(received);
        Assert.AreEqual("ready", received.Value.Kind);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), sender);
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
    public async Task ReceiveLoopDispatchesInboundEnvelopes()
    {
        var transport = new QueuedTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        var receivedSource = new TaskCompletionSource<FakeLobbyMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.RegisterMessageHandler<FakeLobbyMessage>(message => receivedSource.SetResult(message));

        var loop = service.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FakeLobbyMessage("appearance"),
            sequence: 1));

        var received = await receivedSource.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual("appearance", received.Kind);
    }

    [TestMethod]
    public async Task ReceiveLoopLogsHandlerExceptionsAndContinues()
    {
        var transport = new QueuedTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport, logs.Add);
        var receivedSource = new TaskCompletionSource<FakeLobbyMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.RegisterMessageHandler<FakeLobbyMessage>(_ => throw new InvalidOperationException("handler boom"));
        service.RegisterMessageHandler<FakeLobbyMessage>(message => receivedSource.SetResult(message));

        var loop = service.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FakeLobbyMessage("first"),
            sequence: 1));
        await transport.QueueEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FakeLobbyMessage("second"),
            sequence: 2));

        var received = await receivedSource.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual("first", received.Kind);
        Assert.IsTrue(logs.Any(log => log.Contains("handler boom", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ReceiveLoopStopsWhenCanceled()
    {
        var transport = new QueuedTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        using var cancellation = new CancellationTokenSource();

        var loop = service.RunReceiveLoopAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsFalse(service.IsConnected);
    }

    [TestMethod]
    public async Task BrokerClientTransportSendsThroughConnection()
    {
        await using var server = new BrokerTcpServer("local-test", IPAddress.Loopback, port: 0);
        await server.StartAsync(CancellationToken.None);
        await using var host = await Runtime.BrokerClientConnection.ConnectAsync(
            new Runtime.BrokerClientConfig(Runtime.BrokerClientRole.Host, 0, "127.0.0.1", server.Port, "local-test"),
            "client-0",
            CancellationToken.None);
        await using var client = await LocalCoop.Protocol.BrokerClientConnection.ConnectAsync(
            new LocalCoop.Protocol.BrokerClientConfig(LocalCoop.Protocol.BrokerClientRole.Client, 1, "127.0.0.1", server.Port, "local-test"),
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

    private struct FieldLobbyMessage
    {
        public string? kind;
    }

    private struct PacketSerializableMessage : IPacketSerializable
    {
        public string? value;
        public IntPtr unsupported;

        public void Serialize(PacketWriter writer)
        {
            writer.WriteString(value ?? string.Empty);
        }

        public void Deserialize(PacketReader reader)
        {
            value = reader.ReadString();
        }
    }

    private static LobbyPlayerChangedCharacterMessage CreateUninitializedCharacterChange()
    {
        var concreteCharacterType = typeof(CharacterModel).Assembly.GetTypes()
            .First(type => !type.IsAbstract && typeof(CharacterModel).IsAssignableFrom(type));
        return new LobbyPlayerChangedCharacterMessage
        {
            character = (CharacterModel)RuntimeHelpers.GetUninitializedObject(concreteCharacterType)
        };
    }

    private static Runtime.BrokerEnvelope EnvelopeFor<T>(string sourceClientId, string? targetClientId, long sequence)
    {
        return new Runtime.BrokerEnvelope(
            "local-test",
            sourceClientId,
            targetClientId,
            typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name,
            [],
            sequence);
    }

    private sealed class CapturingTransport : IBrokerEnvelopeTransport
    {
        public List<Runtime.BrokerEnvelope> Sent { get; } = [];

        public Task SendEnvelopeAsync(Runtime.BrokerEnvelope envelope, CancellationToken cancellationToken)
        {
            Sent.Add(envelope);
            return Task.CompletedTask;
        }

        public Task<Runtime.BrokerEnvelope?> ReceiveEnvelopeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<Runtime.BrokerEnvelope?>(null);
        }
    }

    private sealed class QueuedTransport : IBrokerEnvelopeTransport
    {
        private readonly Channel<Runtime.BrokerEnvelope?> _incoming = Channel.CreateUnbounded<Runtime.BrokerEnvelope?>();

        public Task SendEnvelopeAsync(Runtime.BrokerEnvelope envelope, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task<Runtime.BrokerEnvelope?> ReceiveEnvelopeAsync(CancellationToken cancellationToken)
        {
            return await _incoming.Reader.ReadAsync(cancellationToken);
        }

        public async Task QueueEnvelopeAsync(Runtime.BrokerEnvelope envelope)
        {
            await _incoming.Writer.WriteAsync(envelope);
        }

        public async Task CompleteAsync()
        {
            await _incoming.Writer.WriteAsync(null);
        }
    }
}
