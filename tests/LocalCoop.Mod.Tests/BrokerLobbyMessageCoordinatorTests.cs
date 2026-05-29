using LocalCoop.Mod.Runtime;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Runtime = LocalCoop.Mod.Runtime;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class BrokerLobbyMessageCoordinatorTests
{
    [TestMethod]
    public void StateEnvelopeWaitsForHandlerAndLobbyReady()
    {
        var coordinator = new BrokerLobbyMessageCoordinator();
        var envelope = Envelope<LobbyPlayerChangedCharacterMessage>("client-0", payload: [1], sequence: 1);

        coordinator.Enqueue(envelope);

        Assert.AreEqual(0, coordinator.DrainDispatchable(new HashSet<string>(StringComparer.Ordinal)).Count);
        Assert.AreEqual(0, coordinator.DrainDispatchable(Registered(envelope.MessageType)).Count);

        coordinator.MarkLobbyReady();
        var dispatchable = coordinator.DrainDispatchable(Registered(envelope.MessageType));

        Assert.AreEqual(1, dispatchable.Count);
        Assert.AreEqual(envelope, dispatchable[0]);
    }

    [TestMethod]
    public void DuplicateStateEnvelopeIsIgnored()
    {
        var coordinator = new BrokerLobbyMessageCoordinator();
        var logs = new List<string>();
        coordinator = new BrokerLobbyMessageCoordinator(logs.Add);
        var envelope = Envelope<LobbyPlayerChangedCharacterMessage>("client-0", payload: [1], sequence: 1);
        coordinator.MarkLobbyReady();

        coordinator.Enqueue(envelope);
        coordinator.Enqueue(envelope with { Sequence = 2 });
        var dispatchable = coordinator.DrainDispatchable(Registered(envelope.MessageType));

        Assert.AreEqual(1, dispatchable.Count);
        Assert.AreEqual(1, dispatchable[0].Sequence);
        Assert.IsTrue(logs.Any(log => log.Contains("duplicate", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void StateEnvelopeBurstCoalescesToLatestPayloadBeforeUpdate()
    {
        var coordinator = new BrokerLobbyMessageCoordinator();
        var messageType = typeof(LobbyPlayerChangedCharacterMessage).AssemblyQualifiedName!;
        coordinator.MarkLobbyReady();

        coordinator.Enqueue(new Runtime.BrokerEnvelope("local-test", "client-0", null, messageType, [1], 1));
        coordinator.Enqueue(new Runtime.BrokerEnvelope("local-test", "client-0", null, messageType, [2], 2));
        coordinator.Enqueue(new Runtime.BrokerEnvelope("local-test", "client-0", null, messageType, [3], 3));
        var dispatchable = coordinator.DrainDispatchable(Registered(messageType));

        Assert.AreEqual(1, dispatchable.Count);
        Assert.AreEqual(3, dispatchable[0].Sequence);
        CollectionAssert.AreEqual(new byte[] { 3 }, dispatchable[0].Payload);
    }

    [TestMethod]
    public void NonStateEnvelopeKeepsFifoOrder()
    {
        var coordinator = new BrokerLobbyMessageCoordinator();
        var first = Envelope<FakeLobbyMessage>("client-0", payload: [1], sequence: 1);
        var second = Envelope<FakeLobbyMessage>("client-0", payload: [2], sequence: 2);

        coordinator.Enqueue(first);
        coordinator.Enqueue(second);
        var dispatchable = coordinator.DrainDispatchable(Registered(first.MessageType));

        Assert.AreEqual(2, dispatchable.Count);
        Assert.AreEqual(1, dispatchable[0].Sequence);
        Assert.AreEqual(2, dispatchable[1].Sequence);
    }

    [TestMethod]
    public void JoinResponseDispatchesAfterLobbyReadyWithoutRegisteredHandler()
    {
        var coordinator = new BrokerLobbyMessageCoordinator();
        var envelope = Envelope<ClientLobbyJoinResponseMessage>("client-0", payload: [1], sequence: 1);

        coordinator.Enqueue(envelope);
        coordinator.MarkLobbyReady();
        var dispatchable = coordinator.DrainDispatchable(new HashSet<string>(StringComparer.Ordinal));

        Assert.AreEqual(1, dispatchable.Count);
        Assert.AreEqual(envelope, dispatchable[0]);
    }

    private readonly record struct FakeLobbyMessage(string Kind);

    private static Runtime.BrokerEnvelope Envelope<T>(string sourceClientId, byte[] payload, long sequence)
    {
        return new Runtime.BrokerEnvelope(
            "local-test",
            sourceClientId,
            null,
            typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name,
            payload,
            sequence);
    }

    private static HashSet<string> Registered(params string[] messageTypes)
    {
        return new HashSet<string>(messageTypes, StringComparer.Ordinal);
    }
}
