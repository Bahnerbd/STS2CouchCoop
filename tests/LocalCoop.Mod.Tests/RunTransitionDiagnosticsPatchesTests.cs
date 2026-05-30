using LocalCoop.Mod.Patches;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class RunTransitionDiagnosticsPatchesTests
{
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
}
