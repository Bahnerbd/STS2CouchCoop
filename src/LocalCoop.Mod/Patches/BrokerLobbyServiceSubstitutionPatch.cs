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
        var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen");
        if (type is null)
        {
            yield break;
        }

        foreach (var methodName in new[] { "InitializeMultiplayerAsHost", "InitializeMultiplayerAsClient" })
        {
            foreach (var method in AccessTools.GetDeclaredMethods(type).Where(method => method.Name == methodName))
            {
                yield return method;
            }
        }
    }

    public static void Prefix(object[] __args)
    {
        var settings = LoadSettings();
        if (!settings.Enabled)
        {
            return;
        }

        var log = new BrokerEventLog(settings.EventLogPath);
        try
        {
            BrokerLobbyServiceSubstitution.TrySubstituteFirstArgument(
                settings,
                __args,
                () => CreateTransport(settings),
                log.Write);
        }
        catch (Exception exception)
        {
            log.Write($"Broker lobby service substitution failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public static void Postfix(object[] __args)
    {
        var settings = LoadSettings();
        if (!settings.Enabled)
        {
            return;
        }

        var log = new BrokerEventLog(settings.EventLogPath);
        try
        {
            BrokerLobbyServiceSubstitution.MarkBrokerLobbyReady(__args, log.Write);
        }
        catch (Exception exception)
        {
            log.Write($"Broker lobby service readiness failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static IBrokerEnvelopeTransport CreateTransport(BrokerModeSettings settings)
    {
        var config = settings.Config ?? throw new InvalidOperationException("Broker config is missing.");
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
