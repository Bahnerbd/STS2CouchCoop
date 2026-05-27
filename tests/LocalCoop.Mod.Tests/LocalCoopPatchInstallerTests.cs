using System.Reflection;
using LocalCoop.Mod.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class LocalCoopPatchInstallerTests
{
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
