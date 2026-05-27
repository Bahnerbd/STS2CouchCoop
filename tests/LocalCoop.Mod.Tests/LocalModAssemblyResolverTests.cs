using System.Reflection;
using LocalCoop.Mod.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class LocalModAssemblyResolverTests
{
    [TestMethod]
    public void ResolveAssemblyPathFindsRequestedDllBesideModAssembly()
    {
        var directory = CreateTempDirectory();
        var dependencyPath = Path.Combine(directory, "LocalCoop.Protocol.dll");
        File.WriteAllText(dependencyPath, "placeholder");

        var result = LocalModAssemblyResolver.ResolveAssemblyPath(
            directory,
            new AssemblyName("LocalCoop.Protocol, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"));

        Assert.AreEqual(dependencyPath, result);
    }

    [TestMethod]
    public void ResolveAssemblyPathIgnoresMissingSiblingDll()
    {
        var directory = CreateTempDirectory();

        var result = LocalModAssemblyResolver.ResolveAssemblyPath(
            directory,
            new AssemblyName("LocalCoop.Protocol, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"));

        Assert.IsNull(result);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "LocalCoopModTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
