using LocalCoop.Mod.Runtime;
using LocalCoop.Mod.Patches;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class ControllerInputOwnershipTests
{
    [TestMethod]
    public void AllowsAllInputWhenControllerDeviceIsUnconfigured()
    {
        var result = ControllerInputOwnership.ShouldProcess(
            new FakeJoypadButton(device: 1),
            default);

        Assert.IsTrue(result.ShouldProcess);
        Assert.IsFalse(result.IsControllerInput);
    }

    [TestMethod]
    public void AllowsAssignedJoypadDevice()
    {
        var result = ControllerInputOwnership.ShouldProcess(
            new FakeJoypadButton(device: 1),
            BrokerControllerDeviceAssignment.ForDevice(1));

        Assert.IsTrue(result.ShouldProcess);
        Assert.IsTrue(result.IsControllerInput);
        Assert.AreEqual(1, result.Device);
    }

    [TestMethod]
    public void SuppressesUnassignedJoypadDevice()
    {
        var result = ControllerInputOwnership.ShouldProcess(
            new FakeJoypadButton(device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1));

        Assert.IsFalse(result.ShouldProcess);
        Assert.IsTrue(result.IsControllerInput);
        Assert.AreEqual(0, result.Device);
        StringAssert.Contains(result.Reason, "assigned controllerDevice=1");
    }

    [TestMethod]
    public void AllowsSelectedSteamGeneratedControllerInputWhenGodotDeviceDoesNotMatchAssignment()
    {
        var result = ControllerInputOwnership.ShouldProcess(
            new FakeInputEventAction("ui_select", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            trustAsSelectedControllerInput: true);

        Assert.IsTrue(result.ShouldProcess);
        Assert.IsTrue(result.IsControllerInput);
        Assert.AreEqual(0, result.Device);
        StringAssert.Contains(result.Reason, "selected Steam controller");
    }

    [TestMethod]
    public void StillSuppressesSelectedSteamGeneratedControllerInputWhenControllerDeviceIsNone()
    {
        var result = ControllerInputOwnership.ShouldProcess(
            new FakeInputEventAction("ui_select", device: 0),
            BrokerControllerDeviceAssignment.None,
            trustAsSelectedControllerInput: true);

        Assert.IsFalse(result.ShouldProcess);
        Assert.IsTrue(result.IsControllerInput);
        StringAssert.Contains(result.Reason, "controllerDevice=none");
    }

    [TestMethod]
    public void SuppressesAllControllerInputWhenControllerDeviceIsNone()
    {
        var result = ControllerInputOwnership.ShouldProcess(
            new FakeInputEventAction("ui_select", device: 0),
            BrokerControllerDeviceAssignment.None);

        Assert.IsFalse(result.ShouldProcess);
        StringAssert.Contains(result.Reason, "controllerDevice=none");
    }

    [TestMethod]
    public void AllowsKeyboardOrMouseInputWhenControllerDeviceIsAssigned()
    {
        var result = ControllerInputOwnership.ShouldProcess(
            new FakeInputEventMouseButton(),
            BrokerControllerDeviceAssignment.ForDevice(1));

        Assert.IsTrue(result.ShouldProcess);
        Assert.IsFalse(result.IsControllerInput);
    }

    [TestMethod]
    public void ControllerManagerObserverCanRegisterSelectedSteamCompanionWithoutConsumingInput()
    {
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamControllerBoundaryForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input",
            new FakeInputEventAction("controller_d_pad_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamControllerBoundaryForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input",
            new FakeInputEventAction("controller_face_button_south", device: 0, pressed: false),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamControllerBoundaryForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input",
            new FakeInputEventAction("ui_down", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamControllerBoundaryForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input",
            new FakeInputEventAction("controller_d_pad_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: false));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamControllerBoundaryForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input",
            new FakeInputEventAction("controller_d_pad_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(0),
            selectedSteamInput: true));
    }

    [TestMethod]
    public void ControllerManagerObserverCanRegisterSelectedOriginalSteamMotionWithoutConsumingInput()
    {
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldTrustSelectedOriginalSteamControllerBoundaryForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input",
            new FakeInputEventJoypadMotion(device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedOriginalSteamControllerBoundaryForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input",
            new FakeInputEventAction("controller_d_pad_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedOriginalSteamControllerBoundaryForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager",
            "_UnhandledInput",
            new FakeInputEventJoypadMotion(device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedOriginalSteamControllerBoundaryForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input",
            new FakeInputEventJoypadMotion(device: 0),
            BrokerControllerDeviceAssignment.ForDevice(0),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedOriginalSteamControllerBoundaryForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input",
            new FakeInputEventJoypadMotion(device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: false));
    }

    [TestMethod]
    public void OnlyRealInputSinksConsumeGeneratedUiCompanions()
    {
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldConsumeGeneratedUiCompanionAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "_Input"));
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldConsumeGeneratedUiCompanionAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NHotkeyManager",
            "_UnhandledInput"));
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldConsumeGeneratedUiCompanionAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager",
            "_UnhandledInput"));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldConsumeGeneratedUiCompanionAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input"));
    }

    [TestMethod]
    public void RealInputSinksDoNotTrustRawSelectedSteamControllerActions()
    {
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "_Input",
            new FakeInputEventAction("controller_d_pad_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "_Input",
            new FakeInputEventAction("controller_face_button_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NHotkeyManager",
            "_UnhandledInput",
            new FakeInputEventAction("controller_d_pad_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager",
            "_UnhandledInput",
            new FakeInputEventAction("controller_face_button_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldBridgeSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "_Input",
            new FakeInputEventAction("controller_d_pad_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldBridgeSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "_Input",
            new FakeInputEventAction("controller_face_button_south", device: 0, pressed: false),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "_Input",
            new FakeInputEventAction("ui_down", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "_Input",
            new FakeInputEventAction("controller_d_pad_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(0),
            selectedSteamInput: true));
    }

    [TestMethod]
    public void RealInputSinksTrustUnmappedOriginalSelectedSteamControllerInputs()
    {
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NHotkeyManager",
            "_UnhandledInput",
            new FakeInputEventAction("controller_face_button_west", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager",
            "_UnhandledInput",
            new FakeInputEventAction("controller_face_button_north", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager",
            "_UnhandledInput",
            new FakeInputEventJoypadMotion(device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager",
            "_UnhandledInput",
            new FakeInputEventAction("ui_down", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager",
            "_UnhandledInput",
            new FakeInputEventAction("controller_face_button_west", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(0),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager",
            "_UnhandledInput",
            new FakeInputEventAction("controller_face_button_west", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: false));
    }

    [TestMethod]
    public void GlobalMenuSinksBridgeSelectedSteamControllerActionsAfterControllerManagerDispatch()
    {
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldBridgeSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NHotkeyManager",
            "_UnhandledInput",
            new FakeInputEventAction("controller_d_pad_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldBridgeSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager",
            "_UnhandledInput",
            new FakeInputEventAction("controller_face_button_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldBridgeSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input",
            new FakeInputEventAction("controller_d_pad_south", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
    }

    [TestMethod]
    public void SelectedSteamSinkTrustIsDisabledWithoutAConsumedCompanion()
    {
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "_Input",
            new FakeInputEventAction("ui_down", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "_Input",
            new FakeInputEventAction("ui_down", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(0),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldTrustSelectedSteamInputAtSinkForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input",
            new FakeInputEventAction("ui_down", device: 0),
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
    }

    [TestMethod]
    public void GeneratedSteamInputIsSuppressedForNativeControllerDeviceZeroAtSinks()
    {
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldSuppressGeneratedSteamInputForNativeControllerDeviceZeroForTesting(
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "_Input",
            BrokerControllerDeviceAssignment.ForDevice(0),
            selectedSteamInput: true));
        Assert.IsTrue(ControllerInputOwnershipPatches.ShouldSuppressGeneratedSteamInputForNativeControllerDeviceZeroForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NHotkeyManager",
            "_UnhandledInput",
            BrokerControllerDeviceAssignment.ForDevice(0),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldSuppressGeneratedSteamInputForNativeControllerDeviceZeroForTesting(
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            "_Input",
            BrokerControllerDeviceAssignment.ForDevice(0),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldSuppressGeneratedSteamInputForNativeControllerDeviceZeroForTesting(
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "_Input",
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedSteamInput: true));
        Assert.IsFalse(ControllerInputOwnershipPatches.ShouldSuppressGeneratedSteamInputForNativeControllerDeviceZeroForTesting(
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "_Input",
            BrokerControllerDeviceAssignment.ForDevice(0),
            selectedSteamInput: false));
    }

    [TestMethod]
    public void ControllerOwnershipPatchLogLinesIncludeMethodAndBoundaryContext()
    {
        var inputEvent = new FakeInputEventAction("controller_d_pad_south", device: 0);
        var result = ControllerInputOwnership.ShouldProcess(
            inputEvent,
            BrokerControllerDeviceAssignment.ForDevice(1),
            trustAsSelectedControllerInput: true);

        var line = ControllerInputOwnershipPatches.FormatControllerOwnershipLogLineForTesting(
            result,
            inputEvent,
            "NControllerManager",
            "_Input",
            "boundary=selectedSteamController companionDispatched=True");

        StringAssert.Contains(line, "Controller input ownership: allowed device=0 action=controller_d_pad_south");
        StringAssert.Contains(line, "reason=selected Steam controller for controllerDevice=1.");
        StringAssert.Contains(line, "inputType=FakeInputEventAction");
        StringAssert.Contains(line, "method=NControllerManager._Input");
        StringAssert.Contains(line, "boundary=selectedSteamController companionDispatched=True");
    }

    private sealed class FakeJoypadButton(int device)
    {
        public int Device { get; } = device;
    }

    private sealed class FakeInputEventAction(string action, int device, bool pressed = true)
    {
        public string Action { get; } = action;
        public int Device { get; } = device;
        public bool Pressed { get; } = pressed;
    }

    private sealed class FakeInputEventJoypadMotion(int device)
    {
        public int Device { get; } = device;
    }

    private sealed class FakeInputEventMouseButton;
}
