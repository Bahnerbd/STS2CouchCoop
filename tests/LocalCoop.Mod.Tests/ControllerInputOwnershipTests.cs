using LocalCoop.Mod.Runtime;
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

    private sealed class FakeJoypadButton(int device)
    {
        public int Device { get; } = device;
    }

    private sealed class FakeInputEventAction(string action, int device)
    {
        public string Action { get; } = action;
        public int Device { get; } = device;
    }

    private sealed class FakeInputEventMouseButton;
}
