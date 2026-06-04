using LocalCoop.Mod.Patches;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LocalCoop.Mod.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class RunTransitionDiagnosticsPatchesTests
{
    [TestMethod]
    public void LaunchDiagnosticsAlignsLocalContextToBrokerNetService()
    {
        var previousNetId = LocalContext.NetId;
        using var service = new BrokerNetGameService(
            new BrokerBackedNetService("local-test", "client-1", 1, new CapturingTransport()),
            NetGameType.Client);

        try
        {
            LocalContext.NetId = BrokerPlayerId.ForClientIndex(0);

            var aligned = RunIdentityLaunchDiagnosticsPatches.AlignLocalContextForBrokerRunForTesting(
                new FakeRunManagerOwner(service),
                _ => { });

            Assert.IsTrue(aligned);
            Assert.AreEqual(BrokerPlayerId.ForClientIndex(1), LocalContext.NetId);
        }
        finally
        {
            LocalContext.NetId = previousNetId;
        }
    }

    [TestMethod]
    public void LaunchDiagnosticsAlignsEventSynchronizerLocalPlayerToBrokerNetService()
    {
        var previousNetId = LocalContext.NetId;
        using var service = new BrokerNetGameService(
            new BrokerBackedNetService("local-test", "client-0", 0, new CapturingTransport()),
            NetGameType.Client);

        try
        {
            LocalContext.NetId = BrokerPlayerId.ForClientIndex(0);

            var synchronizer = new FakeEventSynchronizer(BrokerPlayerId.ForClientIndex(1));
            var aligned = RunIdentityLaunchDiagnosticsPatches.AlignLocalContextForBrokerRunForTesting(
                new FakeRunManagerOwner(service, synchronizer),
                _ => { });

            Assert.IsTrue(aligned);
            Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), synchronizer.LocalPlayerId);
        }
        finally
        {
            LocalContext.NetId = previousNetId;
        }
    }

    [TestMethod]
    public void LaunchDiagnosticsDoesNotChangeLocalContextWithoutBrokerNetService()
    {
        var previousNetId = LocalContext.NetId;

        try
        {
            LocalContext.NetId = BrokerPlayerId.ForClientIndex(0);

            var aligned = RunIdentityLaunchDiagnosticsPatches.AlignLocalContextForBrokerRunForTesting(
                new object(),
                _ => { });

            Assert.IsFalse(aligned);
            Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), LocalContext.NetId);
        }
        finally
        {
            LocalContext.NetId = previousNetId;
        }
    }

    [TestMethod]
    public void RunIdentityVoidDiagnosticsIncludeRunIdentityTransitions()
    {
        var targets = RunIdentityVoidDiagnosticsPatches.TargetSignaturesForTesting.ToArray();

        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Runs.RunManager", "SetUpNewMultiPlayer"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Runs.RunManager", "InitializeShared"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Runs.RunManager", "InitializeRunLobby"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Runs.RunManager", "CleanUp"));
    }

    [TestMethod]
    public void RunIdentityLaunchDiagnosticsTargetsRunManagerLaunch()
    {
        Assert.AreEqual(
            ("MegaCrit.Sts2.Core.Runs.RunManager", "Launch"),
            RunIdentityLaunchDiagnosticsPatches.TargetSignatureForTesting);
    }

    [TestMethod]
    public void VoidDiagnosticsIncludeRunCleanupTriggers()
    {
        var targets = RunTransitionVoidDiagnosticsPatches.TargetSignaturesForTesting.ToArray();

        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Runs.RunManager", "CleanUp"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Runs.RunManager", "StateDiverged"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Runs.RunManager", "LocalPlayerDisconnected"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Runs.RunManager", "RemotePlayerDisconnected"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.NRun", "_Notification"));
    }

    [TestMethod]
    public void TaskDiagnosticsIncludeMenuTransitionTriggers()
    {
        var targets = RunTransitionTaskDiagnosticsPatches.TargetSignaturesForTesting.ToArray();

        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.NGame", "ReturnToMainMenu"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.NGame", "GoToTimeline"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Runs.RunManager", "ReturnToMainMenuWithError"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Runs.RunManager", "AbandonInternal"));
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
