using LocalCoop.Mod.Patches;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class PlayerChoiceDiagnosticsPatchesTests
{
    [TestMethod]
    public void DiagnosticsIncludePlayerChoiceSynchronizerTargets()
    {
        var targets = PlayerChoiceDiagnosticsPatches.TargetSignaturesForTesting.ToArray();

        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "ReserveChoiceId"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "SyncLocalChoice"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "WaitForRemoteChoice"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "OnPlayerChoiceMessageReceived"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "OnReceivePlayerChoice"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "FastForwardChoiceIds"));
    }

    [TestMethod]
    public void DiagnosticsIncludeEventSynchronizerTargets()
    {
        var targets = PlayerChoiceDiagnosticsPatches.TargetSignaturesForTesting.ToArray();

        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "ChooseLocalOption"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "ChooseOptionForEvent"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "ChooseOptionForSharedEvent"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "HandleEventOptionChosenMessage"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "HandleSharedEventOptionChosenMessage"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "HandleVotedForSharedEventOptionMessage"));
    }
}
