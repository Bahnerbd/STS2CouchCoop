using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LocalCoop.Mod.Runtime;

namespace LocalCoop.Mod.Patches;

[HarmonyPatch]
public static class BrokerJoinFriendScreenPatch
{
    private static readonly ConditionalWeakTable<object, TriggeredMarker> TriggeredScreens = new();

    public static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NJoinFriendScreen");
        return type is null ? null : AccessTools.Method(type, "OnSubmenuOpened");
    }

    public static void Postfix(object __instance)
    {
        var settings = LoadSettings();
        if (!BrokerClientJoinFlow.ShouldUseBrokerJoin(settings))
        {
            return;
        }

        var log = new BrokerEventLog(settings.EventLogPath);
        if (TriggeredScreens.TryGetValue(__instance, out _))
        {
            log.Write("Broker join friend screen: broker join already triggered for this screen.");
            return;
        }

        TriggeredScreens.Add(__instance, new TriggeredMarker());
        try
        {
            log.Write("Broker join friend screen: triggering broker client join.");
            AccessTools.Method(__instance.GetType(), "JoinGame")
                ?.Invoke(__instance, [new BrokerClientJoinFlow.PlaceholderClientConnectionInitializer()]);
            BrokerClientJoinFlow.TryHideJoinScreen(__instance, log.Write);
        }
        catch (Exception exception)
        {
            log.Write($"Broker join friend screen: failed to trigger broker join: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static BrokerModeSettings LoadSettings()
    {
        var modDirectory = Path.GetDirectoryName(typeof(LocalCoopMod).Assembly.Location);
        return string.IsNullOrWhiteSpace(modDirectory)
            ? new BrokerModeSettings(false, null, "client-0", "localcoop-events.txt", "mod directory unavailable")
            : BrokerModeSettings.LoadFromDirectory(modDirectory);
    }

    private sealed class TriggeredMarker;
}
