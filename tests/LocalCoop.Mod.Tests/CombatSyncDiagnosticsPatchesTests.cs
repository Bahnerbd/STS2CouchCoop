using LocalCoop.Mod.Patches;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class CombatSyncDiagnosticsPatchesTests
{
    [TestMethod]
    public void VoidDiagnosticsIncludeCombatSyncGateTargets()
    {
        var targets = CombatSyncVoidDiagnosticsPatches.TargetSignaturesForTesting.ToArray();

        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "StartSync"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "OnSyncPlayerMessageReceived"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "OnSyncRngMessageReceived"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "CheckSyncCompleted"));
    }

    [TestMethod]
    public void WaitDiagnosticsTargetCombatSyncWait()
    {
        Assert.AreEqual(
            ("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "WaitForSync"),
            CombatSyncWaitDiagnosticsPatches.TargetSignatureForTesting);
    }
}
