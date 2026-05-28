using System.Reflection;
using HarmonyLib;
using LocalCoop.Mod.Runtime;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace LocalCoop.Mod.Patches;

[HarmonyPatch]
public static class BrokerClientJoinFlowPatch
{
    public static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Multiplayer.Game.JoinFlow");
        return type is null
            ? null
            : AccessTools.Method(type, "Begin", [typeof(IClientConnectionInitializer), AccessTools.TypeByName("Godot.SceneTree")]);
    }

    public static bool Prefix(ref Task<JoinResult> __result)
    {
        var settings = LoadSettings();
        if (!BrokerClientJoinFlow.ShouldUseBrokerJoin(settings))
        {
            return true;
        }

        new BrokerEventLog(settings.EventLogPath).Write("Broker client join flow: returning synthetic standard lobby join result.");
        __result = Task.FromResult(BrokerClientJoinFlow.CreateStandardLobbyJoinResult());
        return false;
    }

    private static BrokerModeSettings LoadSettings()
    {
        var modDirectory = Path.GetDirectoryName(typeof(LocalCoopMod).Assembly.Location);
        return string.IsNullOrWhiteSpace(modDirectory)
            ? new BrokerModeSettings(false, null, "client-0", "localcoop-events.txt", "mod directory unavailable")
            : BrokerModeSettings.LoadFromDirectory(modDirectory);
    }
}
