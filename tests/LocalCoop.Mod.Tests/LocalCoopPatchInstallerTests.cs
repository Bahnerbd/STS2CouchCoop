using System.Reflection;
using LocalCoop.Mod.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class LocalCoopPatchInstallerTests
{
    [TestMethod]
    public void DefaultPatchTypesContainCommunicationBridgePatches()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                typeof(LocalCoop.Mod.Patches.BrokerClientJoinFlowPatch),
                typeof(LocalCoop.Mod.Patches.BrokerJoinFriendScreenPatch),
                typeof(LocalCoop.Mod.Patches.BrokerLobbyServiceSubstitutionPatch),
                typeof(LocalCoop.Mod.Patches.BrokerBeginRunPatch),
                typeof(LocalCoop.Mod.Patches.RunIdentityLaunchPatch)
            },
            LocalCoopPatchInstaller.DefaultPatchTypesForTesting.ToArray());
    }

    [TestMethod]
    public void DefaultPatchTypesDoNotContainDiagnosticsPatches()
    {
        Assert.IsFalse(LocalCoopPatchInstaller.DefaultPatchTypesForTesting.Any(type =>
            type.Name.Contains("Diagnostics", StringComparison.Ordinal)
            || type.Name.Contains("Probe", StringComparison.Ordinal)
            || type.Name.Contains("Breadcrumb", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void BrokerJoinFriendScreenPatchTargetsJoinFriendsPressedAfterSubmenuPush()
    {
        var target = LocalCoop.Mod.Patches.BrokerJoinFriendScreenPatch.TargetMethod();

        Assert.IsNotNull(target);
        Assert.AreEqual("OnJoinFriendsPressed", target!.Name);
        Assert.AreEqual(
            "MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayerSubmenu",
            target.DeclaringType?.FullName);
    }

    [TestMethod]
    public void InstallLogsPatchFailuresWithoutThrowing()
    {
        var messages = new List<string>();

        var result = LocalCoopPatchInstaller.Install(
            Assembly.GetExecutingAssembly(),
            [typeof(FailingPatchMarker)],
            messages.Add,
            _ => throw new InvalidOperationException("patch boom"));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(messages.Single(), "FailingPatchMarker");
        StringAssert.Contains(messages.Single(), "patch boom");
    }

    private sealed class FailingPatchMarker;
}
