using LocalCoop.Mod.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class SteamControllerInputSelectionTests
{
    [TestMethod]
    public void SelectsConfiguredControllerOrdinal()
    {
        var selection = SteamControllerInputSelection.ChooseControllerHandle(
            ["first", "second"],
            BrokerControllerDeviceAssignment.ForDevice(1));

        Assert.IsTrue(selection.Selected);
        Assert.AreEqual(1, selection.Index);
        Assert.AreEqual("second", selection.Handle);
    }

    [TestMethod]
    public void DoesNotSelectWhenControllerDeviceIsUnconfigured()
    {
        var selection = SteamControllerInputSelection.ChooseControllerHandle(
            ["first", "second"],
            default(BrokerControllerDeviceAssignment));

        Assert.IsFalse(selection.Selected);
    }

    [TestMethod]
    public void DoesNotSelectWhenConfiguredOrdinalIsMissing()
    {
        var selection = SteamControllerInputSelection.ChooseControllerHandle(
            ["first"],
            BrokerControllerDeviceAssignment.ForDevice(1));

        Assert.IsFalse(selection.Selected);
        Assert.AreEqual(1, selection.Index);
    }

    [TestMethod]
    public void KeepsKnownControllerHandleWhenSteamHandleOrderChanges()
    {
        var selection = SteamControllerInputSelection.ChooseControllerHandle(
            ["first", "third", "second"],
            BrokerControllerDeviceAssignment.ForDevice(1),
            "second");

        Assert.IsTrue(selection.Selected);
        Assert.AreEqual(1, selection.Index);
        Assert.AreEqual("second", selection.Handle);
    }

    [TestMethod]
    public void FallsBackToConfiguredSlotWhenKnownControllerHandleIsMissing()
    {
        var selection = SteamControllerInputSelection.ChooseControllerHandle(
            ["first", "third", "fourth"],
            BrokerControllerDeviceAssignment.ForDevice(2),
            "second");

        Assert.IsTrue(selection.Selected);
        Assert.AreEqual(2, selection.Index);
        Assert.AreEqual("fourth", selection.Handle);
        StringAssert.Contains(selection.Reason, "reacquired configured playerSlot=2");
    }

    [TestMethod]
    public void TracksSelectedControllerDevice()
    {
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();
        SteamControllerInputSelection.SetSelectedControllerDeviceForTesting(2);

        Assert.IsTrue(SteamControllerInputSelection.IsSelectedControllerActive(
            BrokerControllerDeviceAssignment.ForDevice(2)));
        SteamControllerInputSelection.SetSelectedControllerDeviceForTesting(0);
        Assert.IsTrue(SteamControllerInputSelection.IsSelectedControllerActive(
            BrokerControllerDeviceAssignment.ForDevice(0)));
        SteamControllerInputSelection.SetSelectedControllerDeviceForTesting(2);
        Assert.IsFalse(SteamControllerInputSelection.IsSelectedControllerActive(
            BrokerControllerDeviceAssignment.ForDevice(1)));
        Assert.IsFalse(SteamControllerInputSelection.IsSelectedControllerActive(
            BrokerControllerDeviceAssignment.None));
    }

    [TestMethod]
    public void DetectsAlreadyAppliedSelectionByControllerDeviceAndHandle()
    {
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();
        SteamControllerInputSelection.SetSelectedControllerDeviceForTesting(2);
        var selectedHandle = new object();
        var differentHandle = new object();

        Assert.IsTrue(SteamControllerInputSelection.IsSelectionAlreadyAppliedForTesting(
            BrokerControllerDeviceAssignment.ForDevice(2),
            selectedHandle,
            selectedHandle));
        Assert.IsFalse(SteamControllerInputSelection.IsSelectionAlreadyAppliedForTesting(
            BrokerControllerDeviceAssignment.ForDevice(1),
            selectedHandle,
            selectedHandle));
        Assert.IsFalse(SteamControllerInputSelection.IsSelectionAlreadyAppliedForTesting(
            BrokerControllerDeviceAssignment.ForDevice(2),
            differentHandle,
            selectedHandle));
        Assert.IsFalse(SteamControllerInputSelection.IsSelectionAlreadyAppliedForTesting(
            BrokerControllerDeviceAssignment.None,
            selectedHandle,
            selectedHandle));
    }

    [TestMethod]
    public void TracksGeneratedInputEventsByReference()
    {
        var generated = new object();
        var sameShapeButDifferentReference = new object();

        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();
        SteamControllerInputSelection.RegisterGeneratedInputEvents([generated]);

        Assert.IsTrue(SteamControllerInputSelection.IsGeneratedInputEvent(generated));
        Assert.IsFalse(SteamControllerInputSelection.IsGeneratedInputEvent(sameShapeButDifferentReference));
    }

    [TestMethod]
    public void ConsumesUiCompanionActionForSelectedSteamControllerAction()
    {
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();

        SteamControllerInputSelection.RegisterGeneratedUiCompanionAction(new FakeInputEventAction("controller_d_pad_east", device: 0, pressed: true));

        Assert.IsTrue(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(new FakeInputEventAction("ui_right", device: 0)));
        Assert.IsFalse(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(new FakeInputEventAction("ui_right", device: 0)));
        Assert.IsFalse(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(new FakeInputEventAction("ui_left", device: 0)));
    }

    [TestMethod]
    public void RegistersUiCompanionForRepeatedPressesFromSameGeneratedInputEvent()
    {
        var generatedInputEvent = new FakeInputEventAction("controller_d_pad_east", device: 0, pressed: true);
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();

        SteamControllerInputSelection.RegisterGeneratedUiCompanionAction(generatedInputEvent);
        SteamControllerInputSelection.RegisterGeneratedUiCompanionAction(generatedInputEvent);

        Assert.IsTrue(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(new FakeInputEventAction("ui_right", device: 0)));
        Assert.IsTrue(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(new FakeInputEventAction("ui_right", device: 0)));
    }

    [TestMethod]
    public void DoesNotCreateUiCompanionActionForButtonRelease()
    {
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();

        SteamControllerInputSelection.RegisterGeneratedUiCompanionAction(new FakeInputEventAction("controller_d_pad_east", device: 0, pressed: false));

        Assert.IsFalse(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(new FakeInputEventAction("ui_right", device: 0)));
    }

    [TestMethod]
    public void AcceptedUiCompanionReleaseEventStaysAcceptedByReference()
    {
        var uiCompanionRelease = new FakeInputEventAction("ui_select", device: 0, pressed: false);
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();
        SteamControllerInputSelection.AcceptGeneratedUiCompanionInputEventForTesting(uiCompanionRelease);

        Assert.IsTrue(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(uiCompanionRelease));
        Assert.IsTrue(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(uiCompanionRelease));
        Assert.IsFalse(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(new FakeInputEventAction("ui_select", device: 0, pressed: false)));
    }

    [TestMethod]
    public void AcceptedUiCompanionEventStaysAcceptedByReference()
    {
        var uiCompanionInputEvent = new FakeInputEventAction("ui_right", device: 0, pressed: true);
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();

        SteamControllerInputSelection.RegisterGeneratedUiCompanionAction(new FakeInputEventAction("controller_d_pad_east", device: 0, pressed: true));

        Assert.IsTrue(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(uiCompanionInputEvent));
        Assert.IsTrue(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(uiCompanionInputEvent));
        Assert.IsFalse(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(new FakeInputEventAction("ui_right", device: 0, pressed: true)));
    }

    [TestMethod]
    public void ExpiresPendingUiCompanionActions()
    {
        var now = new DateTimeOffset(2026, 5, 29, 19, 0, 0, TimeSpan.Zero);
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();

        SteamControllerInputSelection.RegisterGeneratedUiCompanionAction(
            new FakeInputEventAction("controller_d_pad_east", device: 0, pressed: true),
            now);

        Assert.IsFalse(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(
            new FakeInputEventAction("ui_right", device: 0, pressed: true),
            now.AddMilliseconds(251)));
    }

    [TestMethod]
    public void MismatchedUiCompanionActionDoesNotConsumePendingMatch()
    {
        var now = new DateTimeOffset(2026, 5, 29, 19, 0, 0, TimeSpan.Zero);
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();

        SteamControllerInputSelection.RegisterGeneratedUiCompanionAction(
            new FakeInputEventAction("controller_d_pad_east", device: 0, pressed: true),
            now);

        Assert.IsFalse(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(
            new FakeInputEventAction("ui_left", device: 0, pressed: true),
            now.AddMilliseconds(10)));
        Assert.IsTrue(SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(
            new FakeInputEventAction("ui_right", device: 0, pressed: true),
            now.AddMilliseconds(20)));
    }

    [TestMethod]
    public void CreatesTranslatedUiInputEventWithoutMutatingOriginalSteamControllerAction()
    {
        var steamControllerAction = new FakeInputEventAction("controller_d_pad_east", device: 0, pressed: true);

        Assert.IsTrue(SteamControllerInputSelection.TryCreateUiCompanionInputEvent(
            steamControllerAction,
            out var uiCompanionInputEvent));

        var companion = (FakeInputEventAction)uiCompanionInputEvent!;
        Assert.AreNotSame(steamControllerAction, companion);
        Assert.AreEqual("controller_d_pad_east", steamControllerAction.Action);
        Assert.AreEqual("ui_right", companion.Action);
        Assert.AreEqual(0, companion.Device);
        Assert.IsTrue(companion.Pressed);
    }

    [TestMethod]
    public void CreatesTranslatedUiInputEventForButtonRelease()
    {
        var steamControllerRelease = new FakeInputEventAction("controller_face_button_south", device: 0, pressed: false);

        Assert.IsTrue(SteamControllerInputSelection.TryCreateUiCompanionInputEvent(
            steamControllerRelease,
            out var uiCompanionInputEvent));

        var companion = (FakeInputEventAction)uiCompanionInputEvent!;
        Assert.AreEqual("ui_select", companion.Action);
        Assert.AreEqual(0, companion.Device);
        Assert.IsFalse(companion.Pressed);
    }

    [TestMethod]
    public void DoesNotCreateTranslatedUiInputEventForUnknownSteamControllerAction()
    {
        Assert.IsFalse(SteamControllerInputSelection.TryCreateUiCompanionInputEvent(
            new FakeInputEventAction("controller_unknown", device: 0, pressed: true),
            out var uiCompanionInputEvent));
        Assert.IsNull(uiCompanionInputEvent);
    }

    [TestMethod]
    public void DerivesNativeGeneratedActionMapFromNativeControllerConfig()
    {
        var config = new FakeControllerConfig(
            new Dictionary<string, string>
            {
                ["Top_Panel"] = "controller_face_button_west",
                ["Select"] = "controller_face_button_south"
            },
            new Dictionary<string, string>
            {
                ["mega_top_panel"] = "controller_face_button_west",
                ["mega_select"] = "controller_face_button_south"
            });

        var map = SteamControllerInputSelection.CreateNativeGeneratedActionMapForTesting(config);

        Assert.AreEqual("mega_top_panel", map["controller_face_button_west"]);
        Assert.AreEqual("mega_select", map["controller_face_button_south"]);
    }

    [TestMethod]
    public void DerivesPs4TouchpadNativeGeneratedActionMapFromNativeControllerConfig()
    {
        var config = new FakeControllerConfig(
            new Dictionary<string, string>
            {
                ["View_Map"] = "controller_ps4_touchpad"
            },
            new Dictionary<string, string>
            {
                ["mega_view_map"] = "controller_ps4_touchpad"
            });

        var map = SteamControllerInputSelection.CreateNativeGeneratedActionMapForTesting(config);

        Assert.AreEqual("mega_view_map", map["controller_ps4_touchpad"]);
    }

    [TestMethod]
    public void ConsumesNativeGeneratedActionForSelectedSteamControllerAction()
    {
        var now = new DateTimeOffset(2026, 6, 12, 22, 30, 0, TimeSpan.Zero);
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();
        SteamControllerInputSelection.SetNativeGeneratedActionMapForTesting(
            new Dictionary<string, string>
            {
                ["controller_face_button_west"] = "mega_top_panel"
            });

        SteamControllerInputSelection.RegisterGeneratedNativeAction(
            new FakeInputEventAction("controller_face_button_west", device: 0),
            now);

        Assert.IsTrue(SteamControllerInputSelection.TryConsumeGeneratedNativeInputEvent(
            new FakeInputEventAction("mega_top_panel", device: 0),
            now.AddMilliseconds(10)));
        Assert.IsFalse(SteamControllerInputSelection.TryConsumeGeneratedNativeInputEvent(
            new FakeInputEventAction("mega_top_panel", device: 0),
            now.AddMilliseconds(20)));
    }

    [TestMethod]
    public void NativeGeneratedActionTokenExpiresAndDoesNotMatchOtherActions()
    {
        var now = new DateTimeOffset(2026, 6, 12, 22, 30, 0, TimeSpan.Zero);
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();
        SteamControllerInputSelection.SetNativeGeneratedActionMapForTesting(
            new Dictionary<string, string>
            {
                ["controller_face_button_west"] = "mega_top_panel"
            });

        SteamControllerInputSelection.RegisterGeneratedNativeAction(
            new FakeInputEventAction("controller_face_button_west", device: 0),
            now);

        Assert.IsFalse(SteamControllerInputSelection.TryConsumeGeneratedNativeInputEvent(
            new FakeInputEventAction("mega_view_map", device: 0),
            now.AddMilliseconds(10)));
        Assert.IsFalse(SteamControllerInputSelection.TryConsumeGeneratedNativeInputEvent(
            new FakeInputEventAction("mega_top_panel", device: 0),
            now.AddMilliseconds(251)));
    }

    [TestMethod]
    public void FallsBackToObservedXboxTopPanelNativeActionWhenConfigMapIsUnavailable()
    {
        var now = new DateTimeOffset(2026, 6, 12, 22, 30, 0, TimeSpan.Zero);
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();

        SteamControllerInputSelection.RegisterGeneratedNativeAction(
            new FakeInputEventAction("controller_face_button_west", device: 0),
            now);

        Assert.IsTrue(SteamControllerInputSelection.TryConsumeGeneratedNativeInputEvent(
            new FakeInputEventAction("mega_top_panel", device: 0),
            now.AddMilliseconds(10)));
    }

    [TestMethod]
    public void ConsumesGeneratedOriginalSteamControllerMotionByShapeForSinkClone()
    {
        SteamControllerInputSelection.ClearGeneratedInputEventsForTesting();
        SteamControllerInputSelection.RegisterGeneratedOriginalSteamControllerInput(
            new FakeInputEventJoypadMotion(device: 0, axis: 1));

        Assert.IsTrue(SteamControllerInputSelection.TryConsumeGeneratedOriginalSteamControllerInput(
            new FakeInputEventJoypadMotion(device: 0, axis: 1)));
        Assert.IsFalse(SteamControllerInputSelection.TryConsumeGeneratedOriginalSteamControllerInput(
            new FakeInputEventJoypadMotion(device: 0, axis: 1)));
        Assert.IsFalse(SteamControllerInputSelection.TryConsumeGeneratedOriginalSteamControllerInput(
            new FakeInputEventJoypadMotion(device: 0, axis: 0)));
    }

    private sealed class FakeInputEventAction(string action, int device, bool pressed = true)
    {
        public string Action { get; set; } = action;
        public int Device { get; set; } = device;
        public bool Pressed { get; set; } = pressed;

        public FakeInputEventAction Duplicate()
        {
            return new FakeInputEventAction(Action, Device, Pressed);
        }
    }

    private sealed class FakeInputEventJoypadMotion(int device, int axis)
    {
        public int Device { get; } = device;
        public int Axis { get; } = axis;
    }

    private sealed class FakeControllerConfig(
        Dictionary<string, string> steamInputControllerMap,
        Dictionary<string, string> defaultControllerInputMap)
    {
        public Dictionary<string, string> SteamInputControllerMap { get; } = steamInputControllerMap;
        public Dictionary<string, string> DefaultControllerInputMap { get; } = defaultControllerInputMap;
    }
}
