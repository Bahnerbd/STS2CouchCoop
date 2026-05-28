namespace LocalCoop.Mod.Runtime;

using MegaCrit.Sts2.Core.Multiplayer.Game;

public static class BrokerLobbyServiceSubstitution
{
    public static bool TrySubstituteFirstArgument(
        BrokerModeSettings settings,
        object?[] args,
        Func<IBrokerEnvelopeTransport> createTransport,
        Action<string> log)
    {
        if (!settings.Enabled || settings.Config is null || args.Length == 0)
        {
            return false;
        }

        log($"Broker lobby service substitution connecting: clientId={settings.ClientId} endpoint={settings.Config.Host}:{settings.Config.Port} sessionId={settings.Config.SessionId}.");
        var brokerService = BrokerNetServiceFactory.TryCreate(settings, createTransport(), log);
        if (brokerService is null)
        {
            return false;
        }

        args[0] = new BrokerNetGameService(brokerService, ToNetGameType(settings.Config.Role));
        log($"Broker lobby service substituted: clientId={settings.ClientId} netId={brokerService.NetId}.");
        return true;
    }

    private static NetGameType ToNetGameType(BrokerClientRole role)
    {
        return role == BrokerClientRole.Host ? NetGameType.Host : NetGameType.Client;
    }
}
