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
    public async Task ClientFlushesCachedCharacterChangeAfterHostJoinResponse()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-1",
            clientIndex: 1,
            transport,
            logs.Add);
        SetPendingLocalCharacter(service, EnvelopeFor<LobbyPlayerChangedCharacterMessage>(
            "client-1",
            targetClientId: "client-0",
            sequence: 0));
        await service.DispatchEnvelopeAsync(EnvelopeForMessage(
            "client-0",
            targetClientId: "client-1",
            new ClientLobbyJoinResponseMessage { playersInLobby = [], modifiers = [] },
            sequence: 1),
            CancellationToken.None);

        var envelope = transport.Sent.Single();
        Assert.AreEqual("client-1", envelope.SourceClientId);
        Assert.AreEqual("client-0", envelope.TargetClientId);
        Assert.AreEqual(typeof(LobbyPlayerChangedCharacterMessage).AssemblyQualifiedName, envelope.MessageType);
        Assert.AreEqual(1, envelope.Sequence);
        Assert.IsTrue(logs.Any(log => log.Contains("pending outbound flushed", StringComparison.Ordinal)
            && log.Contains(nameof(LobbyPlayerChangedCharacterMessage), StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task FailedPendingCharacterFlushDoesNotBlockJoinResponseDispatch()
    {
        var transport = new CapturingTransport();
        var logs = new List<string>();
        var service = new BrokerBackedNetService(
            sessionId: "local-test",
            clientId: "client-1",
            clientIndex: 1,
            transport,
            logs.Add);
        SetPendingLocalCharacter(service, _ => throw new InvalidDataException("bad character payload"));
        var receivedCount = 0;
        service.RegisterMessageHandler<ClientLobbyJoinResponseMessage>(_ => receivedCount++);

        await service.DispatchEnvelopeAsync(EnvelopeForMessage(
            "client-0",
            targetClientId: "client-1",
            new ClientLobbyJoinResponseMessage { playersInLobby = [], modifiers = [] },
            sequence: 1),
            CancellationToken.None);

        Assert.AreEqual(1, receivedCount);
        Assert.AreEqual(0, transport.Sent.Count);
        Assert.IsTrue(logs.Any(log => log.Contains("pending outbound flush failed", StringComparison.Ordinal)
            && log.Contains("bad character payload", StringComparison.Ordinal)));
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
    public async Task SendJoinResponseReplaysCachedLobbyCharacterStateWithoutTimer()
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

        Assert.AreEqual(2, transport.Sent.Count);
        Assert.AreEqual(typeof(ClientLobbyJoinResponseMessage).AssemblyQualifiedName, transport.Sent[0].MessageType);
        Assert.AreEqual(typeof(LobbyPlayerChangedCharacterMessage).AssemblyQualifiedName, transport.Sent[1].MessageType);
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
        Assert.IsTrue(logs.Any(log => log.Contains("Broker inbound dispatched", StringComparison.Ordinal)));
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
    public async Task DuplicateLobbyStateDispatchesOnce()
    {
        var transport = new QueuedTransport();
        var service = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        var envelope = EnvelopeForMessage(
            "client-0",
            targetClientId: null,
            new LobbyPlayerSetReadyMessage(),
            sequence: 1);
        var receivedCount = 0;
        service.RegisterMessageHandler<LobbyPlayerSetReadyMessage>(_ => receivedCount++);
        service.MarkLobbyReady();

        var loop = service.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(envelope);
        await transport.QueueEnvelopeAsync(envelope with { Sequence = 2 });
        await Task.Delay(50);
        service.Update();
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, receivedCount);
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
    public async Task JoinResponseWithoutRegisteredHandlerUnblocksClientLobbyState()
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
    public async Task OutboundEchoDuringRemoteStateDispatchIsSuppressedWithoutTimer()
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

        Assert.AreEqual(0, transport.Sent.Count);
        Assert.IsTrue(logs.Any(log => log.Contains("suppressed outbound", StringComparison.Ordinal)
            && log.Contains("applying remote state", StringComparison.Ordinal)));
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
    public async Task ClientBroadcastSendRoutesToHost()
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
        Assert.AreEqual("client-0", envelope.TargetClientId);
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

    private static void SetPendingLocalCharacter(BrokerBackedNetService service, Runtime.BrokerEnvelope envelope)
    {
        SetPendingLocalCharacter(service, sequence => envelope with { Sequence = sequence });
    }

    private static void SetPendingLocalCharacter(BrokerBackedNetService service, Func<long, Runtime.BrokerEnvelope> pending)
    {
        typeof(BrokerBackedNetService)
            .GetField("_pendingLocalCharacterBeforeJoinResponse", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, pending);
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
