using LocalCoop.Mod.Runtime;
using LocalCoop.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LocalCoop.Broker;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using System.Runtime.CompilerServices;
using System.Net;
using System.Reflection;
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
    public async Task SendMessageLogsThinTransportForLobbyMessage()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-0",
            clientIndex: 0,
            transport,
            logs.Add);

        await service.SendMessageAsync(new LobbyPlayerSetReadyMessage { ready = true }, targetPlayerId: null, CancellationToken.None);

        Assert.IsTrue(logs.Any(log => log.Contains("Broker thin transport outbound lobby message", StringComparison.Ordinal)
            && log.Contains(nameof(LobbyPlayerSetReadyMessage), StringComparison.Ordinal)));
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
    public void SendMessagePreservesPeerInputMessages()
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

        var envelope = transport.Sent.Single();
        Assert.AreEqual(typeof(PeerInputMessage).AssemblyQualifiedName, envelope.MessageType);
        Assert.IsFalse(logs.Any(log => log.Contains("suppressed outbound", StringComparison.Ordinal)
            && log.Contains(nameof(PeerInputMessage), StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ClientSyncPlayerDataForDifferentPlayerIsSuppressed()
    {
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-1",
            clientIndex: 1,
            transport: new CapturingTransport(),
            role: Runtime.BrokerClientRole.Client);
        var message = CreateSyncPlayerDataMessage(BrokerPlayerId.ForClientIndex(0));

        var suppressed = IsOutboundSuppressed(service, message, out var reason);

        Assert.IsTrue(suppressed);
        StringAssert.Contains(reason, "non-local player data");
        StringAssert.Contains(reason, BrokerPlayerId.ForClientIndex(0).ToString());
        StringAssert.Contains(reason, BrokerPlayerId.ForClientIndex(1).ToString());
    }

    [TestMethod]
    public void InboundBeginRunMessagePreservesHostPlayerOrder()
    {
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-1",
            clientIndex: 1,
            transport: new CapturingTransport(),
            role: Runtime.BrokerClientRole.Client);
        var message = new LobbyBeginRunMessage
        {
            playersInLobby =
            [
                new LobbyPlayer
                {
                    id = BrokerPlayerId.ForClientIndex(0),
                    slotId = 0
                },
                new LobbyPlayer
                {
                    id = BrokerPlayerId.ForClientIndex(1),
                    slotId = 1
                }
            ]
        };

        InspectInboundBeginRunPlayerOrder(service, message);

        var players = message.playersInLobby;
        Assert.IsNotNull(players);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), players[0].id);
        Assert.AreEqual(0, players[0].slotId);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(1), players[1].id);
        Assert.AreEqual(1, players[1].slotId);
    }

    [TestMethod]
    public void InboundBeginRunMessagePreservesRuntimeHostOrderWhenClientZeroIsLocal()
    {
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-0",
            clientIndex: 0,
            transport: new CapturingTransport(),
            role: Runtime.BrokerClientRole.Client);
        var message = new LobbyBeginRunMessage
        {
            playersInLobby =
            [
                new LobbyPlayer
                {
                    id = BrokerPlayerId.ForClientIndex(1),
                    slotId = 0
                },
                new LobbyPlayer
                {
                    id = BrokerPlayerId.ForClientIndex(0),
                    slotId = 1
                }
            ]
        };

        InspectInboundBeginRunPlayerOrder(service, message);

        var players = message.playersInLobby;
        Assert.IsNotNull(players);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(1), players[0].id);
        Assert.AreEqual(0, players[0].slotId);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), players[1].id);
        Assert.AreEqual(1, players[1].slotId);
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
    public void ClientCharacterChangesBeforeHostJoinResponseAreNotSuppressed()
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

        var suppressed = IsOutboundSuppressed(service, message, out var reason);

        Assert.IsFalse(suppressed, reason);
        Assert.AreEqual(0, transport.Sent.Count);
        Assert.AreEqual(0, logs.Count);
    }

    [TestMethod]
    public async Task SendJoinResponseDoesNotReplayCachedLobbyCharacterStateToJoiningClient()
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

        Assert.AreEqual(1, transport.Sent.Count);
        Assert.AreEqual(typeof(ClientLobbyJoinResponseMessage).AssemblyQualifiedName, transport.Sent.Single().MessageType);
        Assert.IsFalse(logs.Any(log => log.Contains("Broker replay outbound", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SendJoinResponseCompletesWithoutReplayTimer()
    {
        var transport = new CapturingTransport();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-0",
            clientIndex: 0,
            transport);
        await service.DispatchEnvelopeAsync(EnvelopeFor<LobbyPlayerChangedCharacterMessage>("client-0", targetClientId: null, sequence: 7), CancellationToken.None);
        var joinResponse = new ClientLobbyJoinResponseMessage
        {
            playersInLobby = [],
            modifiers = []
        };

        var sendTask = service.SendMessageAsync(joinResponse, BrokerPlayerId.ForClientIndex(1), CancellationToken.None);

        Assert.IsTrue(sendTask.IsCompleted);
        await sendTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, transport.Sent.Count);
        Assert.AreEqual(typeof(ClientLobbyJoinResponseMessage).AssemblyQualifiedName, transport.Sent[0].MessageType);
    }

    [TestMethod]
    public async Task SendJoinResponseDoesNotReplayAnyCachedCharacterState()
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
        var log = new List<string>();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-0",
            clientIndex: 0,
            new CapturingTransport(),
            log.Add);

        Assert.IsTrue(service.IsConnected);
        Assert.IsFalse(service.IsGameLoading);

        service.SetGameLoading(true);

        Assert.IsTrue(service.IsGameLoading);
        Assert.AreEqual("local-test", service.GetRawLobbyIdentifier());
        Assert.IsTrue(log.Any(line => line.Contains("Broker game loading changed", StringComparison.Ordinal)
            && line.Contains("isGameLoading=True", StringComparison.Ordinal)));
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
    public async Task DispatchEnvelopeLogsThinTransportForLobbyMessage()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport, logs.Add);

        await service.DispatchEnvelopeAsync(EnvelopeForMessage(
            "client-0",
            targetClientId: "client-1",
            new LobbyPlayerSetReadyMessage { ready = true },
            sequence: 1),
            CancellationToken.None);

        Assert.IsTrue(logs.Any(log => log.Contains("Broker thin transport inbound lobby message", StringComparison.Ordinal)
            && log.Contains(nameof(LobbyPlayerSetReadyMessage), StringComparison.Ordinal)));
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

        await Task.Delay(50);
        service.Update();
        var received = await receivedSource.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual("appearance", received.Kind);
    }

    [TestMethod]
    public async Task ReceiveLoopQueuesInboundEnvelopesUntilUpdate()
    {
        var transport = new QueuedTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        FakeLobbyMessage? received = null;
        service.RegisterMessageHandler<FakeLobbyMessage>(message => received = message);

        var loop = service.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FakeLobbyMessage("queued"),
            sequence: 1));
        await Task.Delay(50);

        Assert.IsNull(received);

        service.Update();
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsNotNull(received);
        Assert.AreEqual("queued", received.Value.Kind);
    }

    [TestMethod]
    public async Task ReceiveLoopHandlersRunOnUpdateThread()
    {
        var transport = new QueuedTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        int? handlerThreadId = null;
        service.RegisterMessageHandler<FakeLobbyMessage>(_ => handlerThreadId = Environment.CurrentManagedThreadId);

        var loop = service.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FakeLobbyMessage("thread"),
            sequence: 1));
        await Task.Delay(50);

        var updateThreadId = Environment.CurrentManagedThreadId;
        service.Update();
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(updateThreadId, handlerThreadId);
    }

    [TestMethod]
    public async Task UpdateLogsFlushedAndDispatchedInboundEnvelope()
    {
        var transport = new QueuedTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport, logs.Add);
        service.RegisterMessageHandler<FakeLobbyMessage>(_ => { });

        var loop = service.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FakeLobbyMessage("logs"),
            sequence: 1));
        await Task.Delay(50);
        service.Update();
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsTrue(logs.Any(log => log.Contains("Broker inbound flushed", StringComparison.Ordinal)));
        Assert.IsTrue(logs.Any(log => log.Contains("Broker inbound dispatched", StringComparison.Ordinal)
            && log.Contains("handlerCount=1", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task UpdateLogsInboundEnvelopeWithoutRegisteredHandler()
    {
        var transport = new QueuedTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport, logs.Add);

        var loop = service.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new FakeLobbyMessage("logs"),
            sequence: 1));
        await Task.Delay(50);
        service.Update();
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsTrue(logs.Any(log => log.Contains("Broker inbound skipped", StringComparison.Ordinal)
            && log.Contains("reason=no registered handler", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void RegisterMessageHandlerLogsMessageTypeAndCount()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport, logs.Add);

        service.RegisterMessageHandler<FakeLobbyMessage>(_ => { });

        Assert.IsTrue(logs.Any(log => log.Contains("Broker handler registered", StringComparison.Ordinal)
            && log.Contains(typeof(FakeLobbyMessage).AssemblyQualifiedName!, StringComparison.Ordinal)
            && log.Contains("handlerCount=1", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task LobbyStateReceivedBeforeHandlerRegistrationFlushesAfterReady()
    {
        var transport = new QueuedTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        var envelope = EnvelopeForMessage(
            "client-0",
            targetClientId: null,
            new LobbyPlayerSetReadyMessage(),
            sequence: 1);

        var loop = service.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(envelope);
        await Task.Delay(50);
        service.Update();
        service.MarkLobbyReady();
        service.Update();

        var receivedCount = 0;
        service.RegisterMessageHandler<LobbyPlayerSetReadyMessage>(_ => receivedCount++);
        service.Update();
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, receivedCount);
    }

    [TestMethod]
    public async Task DuplicateLobbyStateDispatchesInFifoOrder()
    {
        var transport = new QueuedTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        var envelope = EnvelopeForMessage(
            "client-0",
            targetClientId: null,
            new LobbyPlayerSetReadyMessage(),
            sequence: 1);
        var receivedCount = 0;
        service.RegisterMessageHandler<LobbyPlayerSetReadyMessage>((_, _) => receivedCount++);
        service.MarkLobbyReady();

        var loop = service.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(envelope);
        await transport.QueueEnvelopeAsync(envelope with { Sequence = 2 });
        await Task.Delay(50);
        service.Update();
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(2, receivedCount);
    }

    [TestMethod]
    public async Task EarlyJoinResponseAndCharacterStateFlushAfterLobbyReady()
    {
        var transport = new QueuedTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        var receivedTypes = new List<string>();
        service.RegisterMessageHandler<ClientLobbyJoinResponseMessage>(_ => receivedTypes.Add(nameof(ClientLobbyJoinResponseMessage)));
        service.RegisterMessageHandler<LobbyPlayerSetReadyMessage>(_ => receivedTypes.Add(nameof(LobbyPlayerSetReadyMessage)));

        var loop = service.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(EnvelopeForMessage(
            "client-0",
            targetClientId: "client-1",
            new ClientLobbyJoinResponseMessage { playersInLobby = [], modifiers = [] },
            sequence: 1));
        await transport.QueueEnvelopeAsync(EnvelopeForMessage(
            "client-0",
            targetClientId: "client-1",
            new LobbyPlayerSetReadyMessage(),
            sequence: 2));
        await Task.Delay(50);
        service.Update();

        Assert.AreEqual(0, receivedTypes.Count);

        service.MarkLobbyReady();
        service.Update();
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        CollectionAssert.AreEqual(
            new[] { nameof(ClientLobbyJoinResponseMessage), nameof(LobbyPlayerSetReadyMessage) },
            receivedTypes);
    }

    [TestMethod]
    public async Task ClientCharacterStateIsNotSuppressedWithoutJoinResponseHandler()
    {
        var transport = new QueuedTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);

        var loop = service.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(EnvelopeForMessage(
            "client-0",
            targetClientId: "client-1",
            new ClientLobbyJoinResponseMessage { playersInLobby = [], modifiers = [] },
            sequence: 1));
        await Task.Delay(50);
        service.MarkLobbyReady();
        service.Update();

        var suppressed = IsOutboundSuppressed(service, CreateUninitializedCharacterChange(), out var reason);
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsFalse(suppressed, reason);
    }

    [TestMethod]
    public async Task OutboundDuringRemoteStateDispatchIsAllowed()
    {
        var transport = new QueuedTransport();
        var logs = new List<string>();
        BrokerBackedNetService? service = null;
        service = new BrokerBackedNetService("local-test", "client-1", 1, transport, logs.Add);
        service.RegisterMessageHandler<LobbyPlayerSetReadyMessage>(_ => service.SendMessage(new LobbyPlayerSetReadyMessage()));
        service.MarkLobbyReady();

        var loop = service.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(EnvelopeForMessage(
            "client-0",
            targetClientId: null,
            new LobbyPlayerSetReadyMessage(),
            sequence: 1));
        await Task.Delay(50);
        service.Update();
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        var sent = transport.Sent.Single();
        Assert.AreEqual(typeof(LobbyPlayerSetReadyMessage).AssemblyQualifiedName, sent.MessageType);
        Assert.IsNull(sent.TargetClientId);
        Assert.IsFalse(logs.Any(log => log.Contains("applying remote state", StringComparison.Ordinal)));
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

        await Task.Delay(50);
        service.Update();
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
    public async Task ClientUntargetedSendRemainsUntargetedForBrokerHostRouting()
    {
        var transport = new CapturingTransport();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-1",
            clientIndex: 1,
            role: Runtime.BrokerClientRole.Client,
            transport: transport);

        await service.SendMessageAsync(new FakeLobbyMessage("host-only"), targetPlayerId: null, CancellationToken.None);

        var envelope = transport.Sent.Single();
        Assert.IsNull(envelope.TargetClientId);
    }

    [TestMethod]
    public async Task HostBroadcastSendRemainsBroadcast()
    {
        var transport = new CapturingTransport();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-0",
            clientIndex: 0,
            role: Runtime.BrokerClientRole.Host,
            transport: transport);

        await service.SendMessageAsync(new FakeLobbyMessage("broadcast"), targetPlayerId: null, CancellationToken.None);

        var envelope = transport.Sent.Single();
        Assert.IsNull(envelope.TargetClientId);
    }

    [TestMethod]
    public void ClientServiceDoesNotAssumeClientZeroIsConnectedHost()
    {
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-1",
            clientIndex: 1,
            role: Runtime.BrokerClientRole.Client,
            transport: new CapturingTransport());

        Assert.AreEqual(0, service.ConnectedPeerIds.Count);
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

    private static Runtime.BrokerEnvelope EnvelopeForMessage<T>(string sourceClientId, string? targetClientId, T message, long sequence)
    {
        return BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            sourceClientId,
            targetClientId,
            message,
            sequence);
    }

    private static object CreateSyncPlayerDataMessage(ulong playerNetId)
    {
        var messageType = typeof(PeerInputMessage).Assembly.GetType(
            "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.SyncPlayerDataMessage",
            throwOnError: true)!;
        var playerMember = FindFieldOrProperty(messageType, "player")
            ?? throw new MissingMemberException(messageType.FullName, "player");
        var playerType = GetMemberType(playerMember);
        var message = RuntimeHelpers.GetUninitializedObject(messageType);
        var player = RuntimeHelpers.GetUninitializedObject(playerType);

        SetFieldOrProperty(player, "NetId", playerNetId);
        SetFieldOrProperty(message, "player", player);
        return message;
    }

    private static bool IsOutboundSuppressed<T>(BrokerBackedNetService service, T message, out string reason)
    {
        var method = typeof(BrokerBackedNetService)
            .GetMethod("ShouldSuppressOutboundMessage", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(BrokerBackedNetService), "ShouldSuppressOutboundMessage");
        object?[] args = [message, null];
        var suppressed = (bool)method.MakeGenericMethod(typeof(T)).Invoke(service, args)!;
        reason = (string)(args[1] ?? string.Empty);
        return suppressed;
    }

    private static bool IsOutboundSuppressed(BrokerBackedNetService service, object message, out string reason)
    {
        var method = typeof(BrokerBackedNetService)
            .GetMethod("ShouldSuppressOutboundMessage", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(BrokerBackedNetService), "ShouldSuppressOutboundMessage");
        object?[] args = [message, null];
        var suppressed = (bool)method.MakeGenericMethod(message.GetType()).Invoke(service, args)!;
        reason = (string)(args[1] ?? string.Empty);
        return suppressed;
    }

    private static void InspectInboundBeginRunPlayerOrder(BrokerBackedNetService service, object message)
    {
        var method = typeof(BrokerBackedNetService)
            .GetMethod("InspectInboundBeginRunPlayerOrder", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(BrokerBackedNetService), "InspectInboundBeginRunPlayerOrder");
        method.Invoke(service, [message]);
    }

    private static MemberInfo? FindFieldOrProperty(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return (MemberInfo?)type.GetField(name, flags)
            ?? type.GetProperty(name, flags);
    }

    private static Type GetMemberType(MemberInfo member)
    {
        return member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property => property.PropertyType,
            _ => throw new ArgumentException($"Unsupported member {member.MemberType}.", nameof(member))
        };
    }

    private static void SetFieldOrProperty(object instance, string name, object? value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();
        var field = type.GetField(name, flags)
            ?? type.GetField($"<{name}>k__BackingField", flags)
            ?? type.GetField(char.ToLowerInvariant(name[0]) + name[1..], flags);
        if (field is not null)
        {
            field.SetValue(instance, value);
            return;
        }

        var property = type.GetProperty(name, flags);
        if (property is not null && property.CanWrite)
        {
            property.SetValue(instance, value);
            return;
        }

        throw new MissingMemberException(type.FullName, name);
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

        public List<Runtime.BrokerEnvelope> Sent { get; } = [];

        public Task SendEnvelopeAsync(Runtime.BrokerEnvelope envelope, CancellationToken cancellationToken)
        {
            Sent.Add(envelope);
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
