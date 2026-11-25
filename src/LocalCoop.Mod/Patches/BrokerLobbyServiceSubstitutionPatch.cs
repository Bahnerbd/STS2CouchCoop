using System.Reflection;
using HarmonyLib;
using LocalCoop.Mod.Runtime;

namespace LocalCoop.Mod.Patches;

[HarmonyPatch]
public static class BrokerLobbyServiceSubstitutionPatch
{
    private static readonly TimeSpan BrokerConnectTimeout = TimeSpan.FromSeconds(3);

    public static IEnumerable<MethodBase> TargetMethods()
    {
        string[] newLobbyScreens = [
            "CharacterSelect.NCharacterSelectScreen", "Blitz.NBlitzScreen", "Dread.NDreadScreen",
            "CustomRun.NCustomRunScreen", "DailyRun.NDailyRunScreen"
        ];
        string[] loadScreens = [
            "CharacterSelect.NMultiplayerLoadGameScreen", "DailyRun.NDailyRunLoadScreen",
            "CustomRun.NCustomRunLoadScreen"
        ];
        var targets = newLobbyScreens.Select(screen => (screen, host: "InitializeMultiplayerAsHost", client: "InitializeMultiplayerAsClient"))
            .Concat(loadScreens.Select(screen => (screen, host: "InitializeAsHost", client: "InitializeAsClient")));
        foreach (var target in targets)
        {
            var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens." + target.screen);
            if (type is null)
            {
                continue;
            }

            foreach (var methodName in new[] { target.host, target.client })
            {
                foreach (var method in AccessTools.GetDeclaredMethods(type).Where(method => method.Name == methodName))
                {
                    yield return method;
                }
            }
        }
    }

    public static void Prefix(MethodBase __originalMethod, object[] __args)
    {
        var settings = LoadSettings();
        if (!settings.Enabled)
        {
            return;
        }

        var log = new BrokerEventLog(settings.EventLogPath);
        try
        {
            var effectiveRole = BrokerLobbyServiceSubstitution.ResolveRoleForLifecycle(__originalMethod.Name);
            if (effectiveRole is null)
            {
                log.Write($"Broker lobby service substitution skipped: no broker role for {__originalMethod.Name}.");
                return;
            }

            BrokerLobbyServiceSubstitution.TrySubstituteFirstArgument(
                settings,
                __args,
                effectiveRole.Value,
                () => CreateTransport(settings, effectiveRole.Value),
                log.Write);
        }
        catch (Exception exception)
        {
            log.Write($"Broker lobby service substitution failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static IBrokerEnvelopeTransport CreateTransport(BrokerModeSettings settings, BrokerClientRole effectiveRole)
    {
        var config = BrokerLobbyServiceSubstitution.CreateRegistrationConfig(settings, effectiveRole);
        return BrokerEnvelopeTransportConnector.ConnectBlocking(
            config,
            settings.ClientId,
            BrokerConnectTimeout,
            CancellationToken.None);
    }

    private static BrokerModeSettings LoadSettings()
    {
        var modDirectory = Path.GetDirectoryName(typeof(LocalCoopMod).Assembly.Location);
        return string.IsNullOrWhiteSpace(modDirectory)
            ? new BrokerModeSettings(false, null, "client-0", "localcoop-events.txt", "mod directory unavailable")
            : BrokerModeSettings.LoadFromDirectory(modDirectory);
    }
}
