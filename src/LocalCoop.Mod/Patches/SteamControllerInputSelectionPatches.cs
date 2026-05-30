using System.Reflection;
using HarmonyLib;
using LocalCoop.Mod.Runtime;

namespace LocalCoop.Mod.Patches;

[HarmonyPatch]
public static class SteamControllerInputSelectionPatches
{
    public static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.ControllerInput.SteamControllerInputStrategy");
        return type is null ? null : AccessTools.Method(type, "UpdateControllerConnections");
    }

    public static void Postfix(object __instance)
    {
        var settings = LoadSettings();
        if (!settings.Enabled || settings.Config is null)
        {
            return;
        }

        SteamControllerInputSelection.ApplySelection(
            __instance,
            settings.Config.ControllerDevice,
            message => new BrokerEventLog(settings.EventLogPath).Write(message));
    }

    private static BrokerModeSettings LoadSettings()
    {
        var modDirectory = Path.GetDirectoryName(typeof(LocalCoopMod).Assembly.Location);
        return string.IsNullOrWhiteSpace(modDirectory)
            ? new BrokerModeSettings(false, null, "client-0", "localcoop-events.txt", "mod directory unavailable")
            : BrokerModeSettings.LoadFromDirectory(modDirectory);
    }
}
