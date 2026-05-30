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
        var typeName = __instance.GetType().FullName ?? __instance.GetType().Name;
        var methodName = __originalMethod.Name;
        var isGeneratedSteamInput = SteamControllerInputSelection.IsGeneratedInputEvent(inputEvent);
        var isSelectedSteamControllerBoundary = ShouldTrustSelectedSteamControllerBoundary(
            typeName,
            methodName,
            inputEvent,
            settings.Config.ControllerDevice,
            isGeneratedSteamInput);
        if (isSelectedSteamControllerBoundary)
        {
            SteamControllerInputSelection.RegisterGeneratedUiCompanionAction(inputEvent);
            SteamControllerInputSelection.TryDispatchUiCompanionInputEvent(inputEvent);
        }

        if (IsControllerManagerObserver(typeName, methodName))
        {
            return true;
        }

        if (ShouldSuppressGeneratedSteamInputForNativeControllerDeviceZero(
            typeName,
            methodName,
            settings.Config.ControllerDevice,
            isGeneratedSteamInput))
        {
            var duplicateResult = ControllerInputOwnership.ShouldProcess(inputEvent, settings.Config.ControllerDevice) with
            {
                ShouldProcess = false,
                Reason = "native controllerDevice=0 ignores generated Steam input"
            };
            new BrokerEventLog(settings.EventLogPath).Write(
                $"{ControllerInputOwnership.FormatLogLine(duplicateResult, inputEvent)} method={__instance.GetType().Name}.{__originalMethod.Name}");
            return false;
        }

        var shouldBridgeSelectedSteamInput = ShouldBridgeSelectedSteamInputAtSink(
            typeName,
            methodName,
            inputEvent,
            settings.Config.ControllerDevice,
            isGeneratedSteamInput);
        if (shouldBridgeSelectedSteamInput)
        {
            SteamControllerInputSelection.RegisterGeneratedUiCompanionAction(inputEvent);
            var bridgedResult = ControllerInputOwnership.ShouldProcess(
                inputEvent,
                settings.Config.ControllerDevice,
                trustAsSelectedControllerInput: true) with
            {
                ShouldProcess = false,
                Reason = "bridged selected Steam controller action to ui companion"
            };
            new BrokerEventLog(settings.EventLogPath).Write(
                $"{ControllerInputOwnership.FormatLogLine(bridgedResult, inputEvent)} method={__instance.GetType().Name}.{__originalMethod.Name}");
            return false;
        }

        var isGeneratedUiCompanionInput = ShouldConsumeGeneratedUiCompanionAtSink(typeName, methodName)
            && SteamControllerInputSelection.TryConsumeGeneratedUiCompanionInputEvent(inputEvent);
        var isSelectedSteamInput = ShouldTrustSelectedSteamInputAtSink(
            typeName,
            methodName,
            inputEvent,
            settings.Config.ControllerDevice,
            isGeneratedSteamInput);

        var result = ControllerInputOwnership.ShouldProcess(
            inputEvent,
            settings.Config.ControllerDevice,
            isGeneratedUiCompanionInput || isSelectedSteamInput);
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

    public static bool ShouldTrustSelectedSteamControllerBoundaryForTesting(
        string typeName,
        string methodName,
        object? inputEvent,
        BrokerControllerDeviceAssignment assignment,
        bool selectedSteamInput)
    {
        return ShouldTrustSelectedSteamControllerBoundary(typeName, methodName, inputEvent, assignment, selectedSteamInput);
    }

    public static bool ShouldConsumeGeneratedUiCompanionAtSinkForTesting(
        string typeName,
        string methodName)
    {
        return ShouldConsumeGeneratedUiCompanionAtSink(typeName, methodName);
    }

    public static bool ShouldTrustSelectedSteamInputAtSinkForTesting(
        string typeName,
        string methodName,
        object? inputEvent,
        BrokerControllerDeviceAssignment assignment,
        bool selectedSteamInput)
    {
        return ShouldTrustSelectedSteamInputAtSink(typeName, methodName, inputEvent, assignment, selectedSteamInput);
    }

    public static bool ShouldBridgeSelectedSteamInputAtSinkForTesting(
        string typeName,
        string methodName,
        object? inputEvent,
        BrokerControllerDeviceAssignment assignment,
        bool selectedSteamInput)
    {
        return ShouldBridgeSelectedSteamInputAtSink(typeName, methodName, inputEvent, assignment, selectedSteamInput);
    }

    public static bool ShouldSuppressGeneratedSteamInputForNativeControllerDeviceZeroForTesting(
        string typeName,
        string methodName,
        BrokerControllerDeviceAssignment assignment,
        bool selectedSteamInput)
    {
        return ShouldSuppressGeneratedSteamInputForNativeControllerDeviceZero(
            typeName,
            methodName,
            assignment,
            selectedSteamInput);
    }

    private static bool ShouldTrustSelectedSteamControllerBoundary(
        string typeName,
        string methodName,
        object? inputEvent,
        BrokerControllerDeviceAssignment assignment,
        bool selectedSteamInput)
    {
        return selectedSteamInput
            && IsAssignedSelectedSteamDevice(assignment)
            && IsControllerManagerObserver(typeName, methodName)
            && SteamControllerInputSelection.CanMapUiCompanionAction(inputEvent);
    }

    private static bool ShouldConsumeGeneratedUiCompanionAtSink(
        string typeName,
        string methodName)
    {
        return (string.Equals(methodName, "_Input", StringComparison.Ordinal)
                && typeName.EndsWith(".NCharacterSelectScreen", StringComparison.Ordinal))
            || (string.Equals(methodName, "_UnhandledInput", StringComparison.Ordinal)
                && (typeName.EndsWith(".NHotkeyManager", StringComparison.Ordinal)
                    || typeName.EndsWith(".NInputManager", StringComparison.Ordinal)));
    }

    private static bool ShouldTrustSelectedSteamInputAtSink(
        string typeName,
        string methodName,
        object? inputEvent,
        BrokerControllerDeviceAssignment assignment,
        bool selectedSteamInput)
    {
        return false;
    }

    private static bool ShouldBridgeSelectedSteamInputAtSink(
        string typeName,
        string methodName,
        object? inputEvent,
        BrokerControllerDeviceAssignment assignment,
        bool selectedSteamInput)
    {
        return selectedSteamInput
            && IsAssignedSelectedSteamDevice(assignment)
            && IsRealInputSink(typeName, methodName)
            && SteamControllerInputSelection.CanMapUiCompanionAction(inputEvent);
    }

    private static bool ShouldSuppressGeneratedSteamInputForNativeControllerDeviceZero(
        string typeName,
        string methodName,
        BrokerControllerDeviceAssignment assignment,
        bool selectedSteamInput)
    {
        return selectedSteamInput
            && assignment.IsConfigured
            && assignment.Device == 0
            && ShouldConsumeGeneratedUiCompanionAtSink(typeName, methodName);
    }

    private static bool IsControllerManagerObserver(
        string typeName,
        string methodName)
    {
        return string.Equals(methodName, "_Input", StringComparison.Ordinal)
            && typeName.EndsWith(".NControllerManager", StringComparison.Ordinal);
    }

    private static bool IsCharacterSelectInputSink(
        string typeName,
        string methodName)
    {
        return string.Equals(methodName, "_Input", StringComparison.Ordinal)
            && typeName.EndsWith(".NCharacterSelectScreen", StringComparison.Ordinal);
    }

    private static bool IsGlobalMenuSink(
        string typeName,
        string methodName)
    {
        return string.Equals(methodName, "_UnhandledInput", StringComparison.Ordinal)
            && (typeName.EndsWith(".NHotkeyManager", StringComparison.Ordinal)
                || typeName.EndsWith(".NInputManager", StringComparison.Ordinal));
    }

    private static bool IsRealInputSink(
        string typeName,
        string methodName)
    {
        return IsCharacterSelectInputSink(typeName, methodName)
            || IsGlobalMenuSink(typeName, methodName);
    }

    private static bool IsAssignedSelectedSteamDevice(BrokerControllerDeviceAssignment assignment)
    {
        return assignment.IsConfigured
            && assignment.Device is > 0;
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
