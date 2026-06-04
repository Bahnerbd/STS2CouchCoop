using System.Reflection;
using HarmonyLib;
using LocalCoop.Mod.Patches;

namespace LocalCoop.Mod.Runtime;

public static class LocalCoopPatchInstaller
{
    private static readonly Type[] DefaultPatchTypes =
    [
        typeof(BrokerClientJoinFlowPatch),
        typeof(BrokerJoinFriendScreenPatch),
        typeof(BrokerLobbyServiceSubstitutionPatch),
        typeof(BrokerBeginRunPatch),
        typeof(RunIdentityVoidDiagnosticsPatches),
        typeof(RunIdentityLaunchDiagnosticsPatches),
        typeof(CombatSyncVoidDiagnosticsPatches),
        typeof(CombatSyncWaitDiagnosticsPatches),
        typeof(PlayerChoiceDiagnosticsPatches)
    ];

    public static IReadOnlyList<Type> DefaultPatchTypesForTesting => DefaultPatchTypes;

    public static LocalCoopPatchInstallResult Install(
        Assembly assembly,
        Action<string> log,
        Action<Type>? installPatchType = null)
    {
        return Install(assembly, DefaultPatchTypes, log, installPatchType);
    }

    public static LocalCoopPatchInstallResult Install(
        Assembly assembly,
        IReadOnlyList<Type> patchTypes,
        Action<string> log,
        Action<Type>? installPatchType = null)
    {
        Harmony? harmony = null;
        var installer = installPatchType ?? (patchType =>
        {
            harmony ??= new Harmony("localcoop.transport-broker");
            InstallPatchType(patchType, harmony);
        });
        var failures = new List<string>();

        foreach (var patchType in patchTypes)
        {
            try
            {
                installer(patchType);
                log($"Harmony patch installed: {patchType.FullName ?? patchType.Name}.");
            }
            catch (Exception exception)
            {
                var message = $"Harmony patch failed: {patchType.FullName ?? patchType.Name}: {exception.GetType().Name}: {exception.Message}";
                failures.Add(message);
                log(message);
            }
        }

        return new LocalCoopPatchInstallResult(failures.Count == 0, failures);
    }

    private static void InstallPatchType(Type patchType, Harmony harmony)
    {
        harmony.CreateClassProcessor(patchType).Patch();
    }
}

public sealed record LocalCoopPatchInstallResult(bool Success, IReadOnlyList<string> Failures);
