using HarmonyLib;
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
        var modDirectory = Path.GetDirectoryName(typeof(LocalCoopMod).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(modDirectory))
        {
            BrokerModStartup.Initialize(modDirectory, _ => TransportSeamProbe.Run());
        }

        var harmony = new Harmony("localcoop.transport-broker");
        harmony.PatchAll(typeof(LocalCoopMod).Assembly);
    }
}

