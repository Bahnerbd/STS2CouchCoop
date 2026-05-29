using LocalCoop.Mod.Runtime;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Channels;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class BrokerLobbyServiceSubstitutionTests
{
    [TestMethod]
    public void ResolveRoleForLifecycleUsesSts2LifecycleInsteadOfConfigRole()
    {
        Assert.AreEqual(
            BrokerClientRole.Host,
            BrokerLobbyServiceSubstitution.ResolveRoleForLifecycle("InitializeMultiplayerAsHost"));
        Assert.AreEqual(
            BrokerClientRole.Client,
            BrokerLobbyServiceSubstitution.ResolveRoleForLifecycle("InitializeMultiplayerAsClient"));
        Assert.IsNull(BrokerLobbyServiceSubstitution.ResolveRoleForLifecycle("InitializeSinglePlayer"));
    }

    [TestMethod]
    public void TrySubstituteFirstArgumentReplacesServiceWhenBrokerModeIsEnabled()
    {
        var originalService = new object();
        object?[] args = [originalService, 4];
        var settings = EnabledSettings(BrokerClientRole.Client);

        var substituted = BrokerLobbyServiceSubstitution.TrySubstituteFirstArgument(
            settings,
            args,
            BrokerClientRole.Host,
            () => new CapturingTransport(),
            _ => { });

        Assert.IsTrue(substituted);
        Assert.AreNotSame(originalService, args[0]);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), args[0]!.GetType().GetProperty("NetId")!.GetValue(args[0]));
        Assert.AreEqual(NetGameType.Host, args[0]!.GetType().GetProperty("Type")!.GetValue(args[0]));
    }

    [TestMethod]
    public void TrySubstituteFirstArgumentUsesClientLifecycleRoleWhenConfigSaysHost()
    {
        object?[] args = [new object()];
        var settings = EnabledSettings(BrokerClientRole.Host);

        var substituted = BrokerLobbyServiceSubstitution.TrySubstituteFirstArgument(
            settings,
            args,
            BrokerClientRole.Client,
            () => new CapturingTransport(),
            _ => { });

        Assert.IsTrue(substituted);
        Assert.AreEqual(NetGameType.Client, args[0]!.GetType().GetProperty("Type")!.GetValue(args[0]));
    }

    [TestMethod]
    public void CreateRegistrationConfigUsesEffectiveLifecycleRole()
    {
        var settings = EnabledSettings(BrokerClientRole.Client);

        var config = BrokerLobbyServiceSubstitution.CreateRegistrationConfig(settings, BrokerClientRole.Host);

        Assert.AreEqual(BrokerClientRole.Host, config.Role);
        Assert.AreEqual(0, config.ClientIndex);
        Assert.AreEqual("local-test", config.SessionId);
    }

    [TestMethod]
    public void TrySubstituteFirstArgumentLeavesArgumentsWhenBrokerModeIsDisabled()
    {
        var originalService = new object();
        object?[] args = [originalService, 4];
        var settings = new BrokerModeSettings(false, null, "client-0", "events.txt", null);

        var substituted = BrokerLobbyServiceSubstitution.TrySubstituteFirstArgument(
            settings,
            args,
            BrokerClientRole.Host,
            () => throw new InvalidOperationException("transport should not be created"),
            _ => { });

        Assert.IsFalse(substituted);
        Assert.AreSame(originalService, args[0]);
    }

    [TestMethod]
    public async Task MarkBrokerLobbyReadyFlushesQueuedLobbyState()
    {
        var transport = new QueuedTransport();
        var inner = new BrokerBackedNetService("local-test", "client-1", 1, transport);
        var receivedCount = 0;
        inner.RegisterMessageHandler<LobbyPlayerSetReadyMessage>(_ => receivedCount++);
        using var service = new BrokerNetGameService(inner, NetGameType.Client);
        object?[] args = [service];

        var loop = inner.RunReceiveLoopAsync(CancellationToken.None);
        await transport.QueueEnvelopeAsync(BrokerEnvelopeMessageSerializer.ToEnvelope(
            "local-test",
            "client-0",
            targetClientId: "client-1",
            new LobbyPlayerSetReadyMessage(),
            sequence: 1));
        await Task.Delay(50);
        service.Update();

        BrokerLobbyServiceSubstitution.MarkBrokerLobbyReady(args, _ => { });
        service.Update();
        await transport.CompleteAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, receivedCount);
    }

    private static BrokerModeSettings EnabledSettings(BrokerClientRole role)
    {
        return new BrokerModeSettings(
            true,
            new BrokerClientConfig(role, 0, "127.0.0.1", 38989, "local-test"),
            "client-0",
            "events.txt",
            null);
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

        public async Task CompleteAsync()
        {
            await _incoming.Writer.WriteAsync(null);
        }
    }
}
