using LocalCoop.Mod.Patches;
using LocalCoop.Mod.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class RunIdentityLaunchPatchTests
{
    [TestMethod]
    public void LaunchPatchAlignsLocalContextToBrokerNetService()
    {
        var previousNetId = LocalContext.NetId;
        using var service = new BrokerNetGameService(
            new BrokerBackedNetService("local-test", "client-1", 1, new CapturingTransport()),
            NetGameType.Client);

        try
        {
            LocalContext.NetId = BrokerPlayerId.ForClientIndex(0);

            var aligned = RunIdentityLaunchPatch.AlignLocalContextForBrokerRunForTesting(
                new FakeRunManagerOwner(service));

            Assert.IsTrue(aligned);
            Assert.AreEqual(BrokerPlayerId.ForClientIndex(1), LocalContext.NetId);
        }
        finally
        {
            LocalContext.NetId = previousNetId;
        }
    }

    [TestMethod]
    public void LaunchPatchAlignsEventSynchronizerLocalPlayerToBrokerNetService()
    {
        var previousNetId = LocalContext.NetId;
        using var service = new BrokerNetGameService(
            new BrokerBackedNetService("local-test", "client-0", 0, new CapturingTransport()),
            NetGameType.Client);

        try
        {
            LocalContext.NetId = BrokerPlayerId.ForClientIndex(0);

            var synchronizer = new FakeEventSynchronizer(BrokerPlayerId.ForClientIndex(1));
            var aligned = RunIdentityLaunchPatch.AlignLocalContextForBrokerRunForTesting(
                new FakeRunManagerOwner(service, synchronizer));

            Assert.IsTrue(aligned);
            Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), synchronizer.LocalPlayerId);
        }
        finally
        {
            LocalContext.NetId = previousNetId;
        }
    }

    [TestMethod]
    public void LaunchPatchDoesNotChangeLocalContextWithoutBrokerNetService()
    {
        var previousNetId = LocalContext.NetId;

        try
        {
            LocalContext.NetId = BrokerPlayerId.ForClientIndex(0);

            var aligned = RunIdentityLaunchPatch.AlignLocalContextForBrokerRunForTesting(new object());

            Assert.IsFalse(aligned);
            Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), LocalContext.NetId);
        }
        finally
        {
            LocalContext.NetId = previousNetId;
        }
    }

    [TestMethod]
    public void RunIdentityLaunchPatchTargetsRunManagerLaunch()
    {
        Assert.AreEqual(
            ("MegaCrit.Sts2.Core.Runs.RunManager", "Launch"),
            RunIdentityLaunchPatch.TargetSignatureForTesting);
    }

    private sealed class FakeRunManagerOwner(BrokerNetGameService netService, object? eventSynchronizer = null)
    {
        public BrokerNetGameService NetService { get; } = netService;

        public object? EventSynchronizer { get; } = eventSynchronizer;
    }

    private sealed class FakeEventSynchronizer(ulong localPlayerId)
    {
        private readonly ulong _localPlayerId = localPlayerId;

        public ulong LocalPlayerId => _localPlayerId;
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
}
