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
}
