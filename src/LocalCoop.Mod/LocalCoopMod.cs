using LocalCoop.Mod.Runtime;
using MegaCrit.Sts2.Core.Modding;

namespace LocalCoop.Mod;

[ModInitializer(nameof(Initialize))]
public static class LocalCoopMod
{
    public const string ModId = "LocalCoop";
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        LocalModAssemblyResolver.Install(typeof(LocalCoopMod).Assembly);

        var modDirectory = Path.GetDirectoryName(typeof(LocalCoopMod).Assembly.Location);
        BrokerModStartupResult? startup = null;
        if (!string.IsNullOrWhiteSpace(modDirectory))
        {
            startup = BrokerModStartup.Initialize(modDirectory, _ => TransportSeamProbe.Run());
        }

        LocalCoopPatchInstaller.Install(
            typeof(LocalCoopMod).Assembly,
            message =>
            {
                if (startup is not null)
                {
                    new BrokerEventLog(startup.Settings.EventLogPath).Write(message);
                }
            });
    }
}
