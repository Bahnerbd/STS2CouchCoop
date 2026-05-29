using System.Reflection;
using HarmonyLib;
using LocalCoop.Mod.Runtime;

namespace LocalCoop.Mod.Patches;

[HarmonyPatch]
public static class ControllerInputOwnershipPatches
{
    private static readonly (string TypeName, string[] MethodNames)[] Targets =
    [
        (
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager",
            ["_Input"]
        ),
        (
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager",
            ["_UnhandledInput"]
        ),
        (
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NHotkeyManager",
            ["_UnhandledInput"]
        ),
        (
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            ["_Input"]
        )
    ];

    public static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var (typeName, methodNames) in Targets)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type is null)
            {
                continue;
            }

            foreach (var methodName in methodNames)
            {
                var method = AccessTools.Method(type, methodName);
                if (method is not null)
                {
                    yield return method;
                }
            }
        }
    }

    public static bool Prefix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        var settings = LoadSettings();
        if (!settings.Enabled || settings.Config is null)
        {
            return true;
        }

        var inputEvent = __args.FirstOrDefault();
        var result = ControllerInputOwnership.ShouldProcess(inputEvent, settings.Config.ControllerDevice);
        if (!result.IsControllerInput)
        {
            return true;
        }

        if (result.ShouldProcess)
        {
            LogAllowedIfUseful(settings, inputEvent, result);
            return true;
        }

        MarkInputHandled(__instance, inputEvent);
        new BrokerEventLog(settings.EventLogPath).Write(
            $"{ControllerInputOwnership.FormatLogLine(result, inputEvent)} method={__instance.GetType().Name}.{__originalMethod.Name}");
        return false;
    }

    private static void LogAllowedIfUseful(
        BrokerModeSettings settings,
        object? inputEvent,
        ControllerInputOwnershipResult result)
    {
        if (!IsPressedInput(inputEvent))
        {
            return;
        }

        new BrokerEventLog(settings.EventLogPath).Write(
            ControllerInputOwnership.FormatLogLine(result, inputEvent));
    }

    private static bool IsPressedInput(object? inputEvent)
    {
        if (inputEvent is null)
        {
            return false;
        }

        var typeName = inputEvent.GetType().FullName ?? inputEvent.GetType().Name;
        if (!typeName.Contains("InputEventAction", StringComparison.OrdinalIgnoreCase)
            && !typeName.Contains("Button", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var property = inputEvent.GetType().GetProperty(
            "Pressed",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(inputEvent) is true;
    }

    private static void MarkInputHandled(object instance, object? inputEvent)
    {
        TryInvoke(inputEvent, "SetAsHandled");
        var viewport = TryInvoke(instance, "GetViewport");
        TryInvoke(viewport, "SetInputAsHandled");
    }

    private static object? TryInvoke(object? source, string methodName)
    {
        if (source is null)
        {
            return null;
        }

        try
        {
            return source.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes)
                ?.Invoke(source, null);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static BrokerModeSettings LoadSettings()
    {
        var modDirectory = Path.GetDirectoryName(typeof(LocalCoopMod).Assembly.Location);
        return string.IsNullOrWhiteSpace(modDirectory)
            ? new BrokerModeSettings(false, null, "client-0", "localcoop-events.txt", "mod directory unavailable")
            : BrokerModeSettings.LoadFromDirectory(modDirectory);
    }
}
