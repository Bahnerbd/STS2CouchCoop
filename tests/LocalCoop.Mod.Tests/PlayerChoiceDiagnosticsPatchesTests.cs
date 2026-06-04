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
            ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "BeginEvent"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "GetLocalEvent"));
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

    [TestMethod]
    public void DiagnosticsDoNotWrapEventSceneUiTargetsThatCanReenterOwnerCycle()
    {
        var targets = PlayerChoiceDiagnosticsPatches.TargetSignaturesForTesting.ToArray();

        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Models.EventModel", "CreateScene"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Models.EventModel", "SetNode"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext", "Update"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom", "Create"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom", "_EnterTree"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom", "_Ready"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom", "OptionButtonClicked"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom", "RefreshEventState"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom", "SetOptions"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Events.NEventLayout", "_EnterTree"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Events.NEventLayout", "_Ready"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Events.NEventLayout", "ClearOptions"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Events.NEventLayout", "AddOptions"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Events.NEventLayout", "OnSetupComplete"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton", "Create"));
    }

    [TestMethod]
    public void DiagnosticsDoNotWrapRelicNodeMethodsThatCanReenterOwnerCycle()
    {
        var targets = PlayerChoiceDiagnosticsPatches.TargetSignaturesForTesting.ToArray();

        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Helpers.GodotTreeExtensions", "AddChildSafely"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Models.RelicModel", "UpdateTexture"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl", "ConnectSignals"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.CommonUi.NGlobalUi", "Initialize"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.NRun", "SetCurrentRoom"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.NSceneContainer", "SetCurrentScene"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventory", "Initialize"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventory", "Add"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder", "_Ready"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "Reload"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "get_Icon"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "add_ModelChanged"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "set_Model"));
    }

    [TestMethod]
    public void RelicNodeBreadcrumbsIncludeOnlyFocusedRelicNodeTargets()
    {
        var targets = PlayerChoiceRelicNodeBreadcrumbPatches.TargetSignaturesForTesting.ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                ("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "Reload"),
                ("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "get_Icon"),
                ("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "add_ModelChanged"),
                ("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "set_Model")
            },
            targets);
    }

    [TestMethod]
    public void DiagnosticsIncludeRelicHolderGetNodeTargets()
    {
        var targets = PlayerChoiceGetNodeDiagnosticsPatches.GenericTargetTypeNamesForTesting.ToArray();

        CollectionAssert.Contains(
            targets,
            "MegaCrit.Sts2.Core.Nodes.Relics.NRelic");
        CollectionAssert.Contains(
            targets,
            "MegaCrit.Sts2.addons.mega_text.MegaLabel");
    }

    [TestMethod]
    public void DiagnosticsIncludeTargetedRelicHolderReadyProbe()
    {
        Assert.AreEqual(
            ("MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder", "_Ready"),
            PlayerChoiceRelicHolderReadyProbePatches.TargetSignatureForTesting);
    }

    [TestMethod]
    public void PatchOwnerDiagnosticsIncludeRunUiAndRelicHolderTargets()
    {
        var targets = PlayerChoicePatchOwnerDiagnostics.TargetSignaturesForTesting.ToArray();

        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Runs.RunManager", "Launch"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.CommonUi.NGlobalUi", "Initialize"));
        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder", "_Ready"));
        CollectionAssert.Contains(
            targets,
            ("Godot.Node", "GetNode<MegaCrit.Sts2.Core.Nodes.Relics.NRelic>"));
        CollectionAssert.Contains(
            targets,
            ("Godot.Node", "GetNode<MegaCrit.Sts2.addons.mega_text.MegaLabel>"));
    }

    [TestMethod]
    public void DiagnosticsIncludeOnlyNonUiAsyncEventRoomTargets()
    {
        var targets = PlayerChoiceTaskDiagnosticsPatches.TargetSignaturesForTesting.ToArray();

        CollectionAssert.Contains(
            targets,
            ("MegaCrit.Sts2.Core.Rooms.EventRoom", "EnterInternal"));
        CollectionAssert.DoesNotContain(
            targets,
            ("MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom", "SetupLayout"));
    }
}
