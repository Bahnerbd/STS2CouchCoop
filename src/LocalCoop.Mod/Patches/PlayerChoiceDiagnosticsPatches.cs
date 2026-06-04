using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LocalCoop.Mod.Runtime;

namespace LocalCoop.Mod.Patches;

internal static class PlayerChoiceDiagnostics
{
    private static readonly HashSet<string> AddChildSafelyInterestingTypeNames =
    [
        "MegaCrit.Sts2.Core.Nodes.NSceneContainer",
        "MegaCrit.Sts2.Core.Nodes.NRun",
        "MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom",
        "MegaCrit.Sts2.Core.Nodes.Events.NEventLayout",
        "MegaCrit.Sts2.Core.Nodes.Events.NAncientEventLayout",
        "MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton",
        "MegaCrit.Sts2.Core.Nodes.Relics.NRelic",
        "MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventory",
        "MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder"
    ];

    private static readonly string[] InterestingMemberNames =
    [
        "choiceId",
        "result",
        "senderId",
        "optionIndex",
        "pageIndex",
        "location",
        "actionId",
        "NetId",
        "Character",
        "IsShared",
        "ChoiceIds",
        "CurrentLocation",
        "CurrentOptions",
        "Events",
        "Index",
        "Option",
        "Layout",
        "Owner",
        "Relic",
        "TextKey",
        "_canonicalEvent",
        "_connectedOptions",
        "_event",
        "_events",
        "_isPreFinished",
        "_netService",
        "_messageBuffer",
        "_localPlayerId",
        "_optionsContainer",
        "_pageIndex",
        "_playerVotes"
    ];

    private static bool wrotePatchOwnerSnapshot;

    public static void Write(string phase, MethodBase method, object? instance, IReadOnlyList<object?> args)
    {
        var settings = LoadSettings();
        if (!settings.Enabled)
        {
            return;
        }

        if (phase == "enter" && !wrotePatchOwnerSnapshot && IsPatchOwnerSnapshotTrigger(method))
        {
            wrotePatchOwnerSnapshot = true;
            PlayerChoicePatchOwnerDiagnostics.WriteSnapshot(settings);
        }

        if (!ShouldLog(method, instance, args))
        {
            return;
        }

        try
        {
            new BrokerEventLog(settings.EventLogPath).Write(
                $"Choice flow {phase}: client={settings.ClientId} method={FormatMethod(method)} instance={FormatObject(instance)} args=[{string.Join(", ", args.Select(FormatArg))}].");
        }
        catch
        {
        }
    }

    public static Exception? LogFinalizer(MethodBase method, Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        var settings = LoadSettings();
        if (!settings.Enabled)
        {
            return exception;
        }

        try
        {
            new BrokerEventLog(settings.EventLogPath).Write(
                $"Choice flow threw: client={settings.ClientId} method={FormatMethod(method)} exception={FormatException(exception)}.");
        }
        catch
        {
        }

        return exception;
    }

    public static void WriteRelicHolderReadyProbe(string step, object? instance, string nodePath, object? result)
    {
        var settings = LoadSettings();
        if (!settings.Enabled)
        {
            return;
        }

        try
        {
            new BrokerEventLog(settings.EventLogPath).Write(
                $"Choice flow probe: client={settings.ClientId} method=MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder._Ready step={step} path={nodePath} instance={FormatObject(instance)} result={FormatObject(result)}.");
        }
        catch
        {
        }
    }

    public static void WriteRelicNodeBreadcrumb(string phase, MethodBase method, object? instance, IReadOnlyList<object?> args)
    {
        var settings = LoadSettings();
        if (!settings.Enabled)
        {
            return;
        }

        try
        {
            new BrokerEventLog(settings.EventLogPath).Write(
                $"Choice flow relic node {phase}: client={settings.ClientId} method={FormatMethod(method)} instance={FormatShallow(instance)} args=[{string.Join(", ", args.Select(FormatShallow))}].");
        }
        catch
        {
        }
    }

    private static bool ShouldLog(MethodBase method, object? instance, IReadOnlyList<object?> args)
    {
        var typeName = method.DeclaringType?.FullName ?? string.Empty;
        if (typeName == "MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl" && method.Name == "ConnectSignals")
        {
            return instance?.GetType().FullName == "MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder";
        }

        if (typeName != "MegaCrit.Sts2.Core.Helpers.GodotTreeExtensions" || method.Name != "AddChildSafely")
        {
            return true;
        }

        return args.Any(arg => arg is not null && AddChildSafelyInterestingTypeNames.Contains(arg.GetType().FullName ?? arg.GetType().Name));
    }

    private static bool IsPatchOwnerSnapshotTrigger(MethodBase method)
    {
        return method.DeclaringType?.FullName == "MegaCrit.Sts2.Core.Nodes.CommonUi.NGlobalUi"
            && method.Name == "Initialize";
    }

    public static void WriteTaskReturned(MethodBase method, object? instance, Task? task)
    {
        var settings = LoadSettings();
        if (!settings.Enabled)
        {
            return;
        }

        var methodName = FormatMethod(method);
        var path = settings.EventLogPath;
        var clientId = settings.ClientId;
        try
        {
            new BrokerEventLog(path).Write(
                $"Choice flow task returned: client={clientId} method={methodName} status={task?.Status.ToString() ?? "null"} instance={FormatObject(instance)}.");
            task?.ContinueWith(
                completedTask => WriteTaskCompleted(path, clientId, methodName, completedTask),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch
        {
        }
    }

    private static void WriteTaskCompleted(string path, string clientId, string methodName, Task completedTask)
    {
        try
        {
            var status = completedTask.Status.ToString();
            var suffix = completedTask.Exception is null ? string.Empty : $" exception={FormatException(completedTask.Exception.GetBaseException())}";
            new BrokerEventLog(path).Write(
                $"Choice flow task completed: client={clientId} method={methodName} status={status}{suffix}.");
        }
        catch
        {
        }
    }

    private static string FormatMethod(MethodBase method)
    {
        return $"{method.DeclaringType?.FullName ?? "<unknown>"}.{method.Name}";
    }

    private static string FormatArg(object? arg)
    {
        if (arg is null)
        {
            return "null";
        }

        if (arg.GetType().FullName == "Godot.NodePath")
        {
            return arg.ToString() ?? "Godot.NodePath";
        }

        return arg switch
        {
            string value => value,
            bool value => value.ToString(),
            int value => value.ToString(),
            uint value => value.ToString(),
            long value => value.ToString(),
            ulong value => value.ToString(),
            Enum value => value.ToString(),
            IEnumerable enumerable when arg is not string => FormatEnumerable(enumerable),
            _ => FormatObject(arg)
        };
    }

    private static string FormatShallow(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        return value switch
        {
            string text => text,
            bool flag => flag.ToString(),
            int number => number.ToString(),
            uint number => number.ToString(),
            long number => number.ToString(),
            ulong number => FormatId(number),
            Enum enumValue => enumValue.ToString(),
            _ => $"{value.GetType().FullName ?? value.GetType().Name}#{RuntimeHelpers.GetHashCode(value):X8}"
        };
    }

    private static string FormatObject(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        var type = value.GetType();
        var typeName = type.FullName ?? type.Name;
        if (typeName == "MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer")
        {
            return FormatEventSynchronizer(type, value);
        }

        if (typeName == "MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom")
        {
            return FormatEventRoomNode(type, value);
        }

        if (typeName == "MegaCrit.Sts2.Core.Nodes.Events.NEventLayout")
        {
            return FormatEventLayout(type, value);
        }

        if (typeName == "MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton")
        {
            return FormatEventOptionButton(type, value);
        }

        if (IsTypeOrBaseType(type, "MegaCrit.Sts2.Core.Models.EventModel"))
        {
            return FormatEventModel(type, value);
        }

        if (typeName == "MegaCrit.Sts2.Core.Events.EventOption")
        {
            return FormatEventOption(type, value);
        }

        if (typeName == "MegaCrit.Sts2.Core.Entities.Players.Player")
        {
            return FormatPlayer(value);
        }

        var members = InterestingMemberNames
            .Select(name => TryFormatMember(type, value, name))
            .Where(member => member is not null)
            .Cast<string>()
            .ToArray();

        return members.Length == 0
            ? typeName
            : $"{typeName}{{{string.Join(", ", members)}}}";
    }

    private static string FormatEventSynchronizer(Type type, object instance)
    {
        var parts = new[]
        {
            $"localContextNetId={FormatId(TryGetLocalContextNetId())}",
            $"net={FormatNetService(TryGetField(type, instance, "_netService"))}",
            $"currentLocation={TryGetProperty(TryGetField(type, instance, "_messageBuffer"), "CurrentLocation") ?? "null"}",
            $"localPlayerId={FormatId(TryGetField(type, instance, "_localPlayerId"))}",
            $"localPlayer={FormatPlayer(TryGetProperty(instance, "LocalPlayer"))}",
            $"isShared={TryGetProperty(instance, "IsShared") ?? "null"}",
            $"pageIndex={TryGetField(type, instance, "_pageIndex") ?? "null"}",
            $"events={FormatEnumerable(TryGetField(type, instance, "_events") as IEnumerable ?? Array.Empty<object>())}"
        };

        return $"{type.FullName}{{{string.Join(", ", parts)}}}";
    }

    private static string FormatEventRoomNode(Type type, object instance)
    {
        var parts = new[]
        {
            $"event={FormatObject(TryGetField(type, instance, "_event"))}",
            $"isPreFinished={TryGetField(type, instance, "_isPreFinished") ?? "null"}",
            $"connectedOptions={FormatEnumerable(TryGetField(type, instance, "_connectedOptions") as IEnumerable ?? Array.Empty<object>())}",
            $"layout={FormatObject(TryGetProperty(instance, "Layout"))}"
        };

        return $"{type.FullName}{{{string.Join(", ", parts)}}}";
    }

    private static string FormatEventLayout(Type type, object instance)
    {
        var parts = new[]
        {
            $"event={FormatObject(TryGetField(type, instance, "_event"))}",
            $"optionButtonCount={FormatChildCount(TryGetField(type, instance, "_optionsContainer"))}"
        };

        return $"{type.FullName}{{{string.Join(", ", parts)}}}";
    }

    private static string FormatEventOptionButton(Type type, object instance)
    {
        var parts = new[]
        {
            $"event={FormatObject(TryGetProperty(instance, "Event"))}",
            $"option={FormatObject(TryGetProperty(instance, "Option"))}",
            $"index={TryGetProperty(instance, "Index") ?? "null"}"
        };

        return $"{type.FullName}{{{string.Join(", ", parts)}}}";
    }

    private static string FormatEventModel(Type type, object instance)
    {
        var parts = new[]
        {
            $"id={TryGetProperty(instance, "Id") ?? "null"}",
            $"owner={FormatPlayer(TryGetProperty(instance, "Owner"))}",
            $"finished={TryGetProperty(instance, "IsFinished") ?? "null"}",
            $"options={FormatEnumerable(TryGetProperty(instance, "CurrentOptions") as IEnumerable ?? Array.Empty<object>())}"
        };

        return $"{type.FullName ?? type.Name}{{{string.Join(", ", parts)}}}";
    }

    private static string FormatEventOption(Type type, object instance)
    {
        return $"{type.FullName ?? type.Name}{{textKey={TryGetProperty(instance, "TextKey") ?? "null"}, relic={FormatModelId(TryGetProperty(instance, "Relic"))}}}";
    }

    private static string FormatPlayer(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        return $"Player{{netId={FormatId(TryGetProperty(value, "NetId"))}, character={TryGetProperty(value, "Character") ?? "null"}}}";
    }

    private static string FormatNetService(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        return $"{value.GetType().FullName ?? value.GetType().Name}{{type={TryGetProperty(value, "Type") ?? "null"}, netId={FormatId(TryGetProperty(value, "NetId"))}}}";
    }

    private static string FormatModelId(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        return TryGetProperty(value, "Id")?.ToString()
            ?? value.GetType().FullName
            ?? value.GetType().Name;
    }

    private static string FormatChildCount(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        try
        {
            var method = value.GetType().GetMethod("GetChildCount", [typeof(bool)]);
            return method?.Invoke(value, [false])?.ToString() ?? "null";
        }
        catch
        {
            return "null";
        }
    }

    private static bool IsTypeOrBaseType(Type type, string fullName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.FullName == fullName)
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryFormatMember(Type type, object instance, string name)
    {
        try
        {
            var property = AccessTools.Property(type, name);
            if (property is not null && property.GetIndexParameters().Length == 0)
            {
                return $"{name}={FormatMemberValue(property.GetValue(instance))}";
            }

            var field = AccessTools.Field(type, name);
            if (field is not null)
            {
                return $"{name}={FormatMemberValue(field.GetValue(instance))}";
            }
        }
        catch
        {
        }

        return null;
    }

    private static string FormatMemberValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        return value switch
        {
            string text => text,
            bool flag => flag.ToString(),
            int number => number.ToString(),
            uint number => number.ToString(),
            long number => number.ToString(),
            ulong number => FormatId(number),
            Enum enumValue => enumValue.ToString(),
            IEnumerable enumerable when value is not string => FormatEnumerable(enumerable),
            _ => FormatObject(value)
        };
    }

    private static string FormatEnumerable(IEnumerable enumerable)
    {
        var items = new List<string>();
        var count = 0;
        foreach (var item in enumerable)
        {
            count++;
            if (items.Count < 12)
            {
                items.Add(FormatMemberValue(item));
            }
        }

        var suffix = count > items.Count ? $", +{count - items.Count}" : string.Empty;
        return $"[{string.Join(",", items)}{suffix}]";
    }

    private static object? TryGetField(Type type, object instance, string name)
    {
        try
        {
            return AccessTools.Field(type, name)?.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static object? TryGetProperty(object? instance, string name)
    {
        if (instance is null)
        {
            return null;
        }

        try
        {
            var property = AccessTools.Property(instance.GetType(), name);
            return property?.GetIndexParameters().Length == 0
                ? property.GetValue(instance)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static object? TryGetLocalContextNetId()
    {
        try
        {
            var localContext = AccessTools.TypeByName("MegaCrit.Sts2.Core.Context.LocalContext");
            return localContext is null
                ? null
                : AccessTools.Property(localContext, "NetId")?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatId(object? value)
    {
        return value is ulong id
            ? $"{id}/0x{id:X16}"
            : "null";
    }

    private static string FormatException(Exception exception)
    {
        return $"{exception.GetType().Name}: {exception.Message}";
    }

    private static BrokerModeSettings LoadSettings()
    {
        var modDirectory = Path.GetDirectoryName(typeof(LocalCoopMod).Assembly.Location);
        return string.IsNullOrWhiteSpace(modDirectory)
            ? new BrokerModeSettings(false, null, "client-0", "localcoop-events.txt", "mod directory unavailable")
            : BrokerModeSettings.LoadFromDirectory(modDirectory);
    }
}

public static class PlayerChoicePatchOwnerDiagnostics
{
    private static readonly PatchOwnerTarget[] Targets =
    [
        new("MegaCrit.Sts2.Core.Runs.RunManager", "Launch"),
        new("MegaCrit.Sts2.Core.Nodes.CommonUi.NGlobalUi", "Initialize"),
        new("MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventory", "Initialize"),
        new("MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventory", "Add"),
        new("MegaCrit.Sts2.Core.Helpers.GodotTreeExtensions", "AddChildSafely"),
        new("MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl", "ConnectSignals"),
        new("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "Reload"),
        new("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "get_Icon"),
        new("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "add_ModelChanged"),
        new("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "set_Model"),
        new("MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder", "_Ready"),
        new("Godot.Node", "GetNode", "MegaCrit.Sts2.Core.Nodes.Relics.NRelic"),
        new("Godot.Node", "GetNode", "MegaCrit.Sts2.addons.mega_text.MegaLabel")
    ];

    public static IReadOnlyList<(string TypeName, string MethodName)> TargetSignaturesForTesting =>
        Targets.Select(target => (target.TypeName, target.DisplayMethodName)).ToArray();

    internal static void WriteSnapshot(BrokerModeSettings settings)
    {
        if (!settings.Enabled)
        {
            return;
        }

        foreach (var target in Targets)
        {
            try
            {
                var method = ResolveTarget(target);
                var message = method is null
                    ? $"Choice flow Harmony patches: client={settings.ClientId} target={target.DisplayName} status=missing."
                    : FormatPatchInfo(settings.ClientId, target.DisplayName, method);
                new BrokerEventLog(settings.EventLogPath).Write(message);
            }
            catch (Exception exception)
            {
                new BrokerEventLog(settings.EventLogPath).Write(
                    $"Choice flow Harmony patches: client={settings.ClientId} target={target.DisplayName} status=error exception={exception.GetType().Name}: {exception.Message}.");
            }
        }
    }

    private static MethodBase? ResolveTarget(PatchOwnerTarget target)
    {
        var type = AccessTools.TypeByName(target.TypeName);
        if (type is null)
        {
            return null;
        }

        if (target.GenericTypeName is not null)
        {
            var genericType = AccessTools.TypeByName(target.GenericTypeName);
            if (genericType is null)
            {
                return null;
            }

            var genericMethod = AccessTools.GetDeclaredMethods(type).SingleOrDefault(candidate =>
                candidate.Name == target.MethodName
                && candidate.IsGenericMethodDefinition
                && candidate.GetParameters() is [{ ParameterType.FullName: "Godot.NodePath" }]);
            return genericMethod?.MakeGenericMethod(genericType);
        }

        return AccessTools.GetDeclaredMethods(type)
            .Where(method =>
                method.Name == target.MethodName
                && !method.IsAbstract
                && !method.ContainsGenericParameters
                && !method.IsGenericMethod
                && !method.IsGenericMethodDefinition)
            .OrderBy(method => method.GetParameters().Length)
            .FirstOrDefault();
    }

    private static string FormatPatchInfo(string clientId, string targetName, MethodBase method)
    {
        var patchInfo = Harmony.GetPatchInfo(method);
        return patchInfo is null
            ? $"Choice flow Harmony patches: client={clientId} target={targetName} prefixes=[] postfixes=[] transpilers=[] finalizers=[]."
            : $"Choice flow Harmony patches: client={clientId} target={targetName} prefixes={FormatPatches(patchInfo.Prefixes)} postfixes={FormatPatches(patchInfo.Postfixes)} transpilers={FormatPatches(patchInfo.Transpilers)} finalizers={FormatPatches(patchInfo.Finalizers)}.";
    }

    private static string FormatPatches(IEnumerable<Patch> patches)
    {
        var items = patches.Select(FormatPatch).ToArray();
        return items.Length == 0
            ? "[]"
            : $"[{string.Join(", ", items)}]";
    }

    private static string FormatPatch(Patch patch)
    {
        var owner = string.IsNullOrWhiteSpace(patch.owner) ? "<unknown>" : patch.owner;
        var method = patch.PatchMethod;
        var methodName = method is null
            ? "<unknown>"
            : $"{method.DeclaringType?.FullName ?? "<unknown>"}.{method.Name}";
        return $"{owner}#{patch.index}:p{patch.priority}:{methodName}";
    }

    private sealed record PatchOwnerTarget(string TypeName, string MethodName, string? GenericTypeName = null)
    {
        public string DisplayMethodName => GenericTypeName is null
            ? MethodName
            : $"{MethodName}<{GenericTypeName}>";

        public string DisplayName => $"{TypeName}.{DisplayMethodName}";
    }
}

[HarmonyPatch]
public static class PlayerChoiceDiagnosticsPatches
{
    private static readonly (string TypeName, string MethodName)[] Targets =
    [
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "ReserveChoiceId"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "SyncLocalChoice"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "WaitForRemoteChoice"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "OnPlayerChoiceMessageReceived"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "OnReceivePlayerChoice"),
        ("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "FastForwardChoiceIds"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "BeginEvent"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "GetLocalEvent"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "ChooseLocalOption"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "ChooseOptionForEvent"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "ChooseOptionForSharedEvent"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "ChooseSharedEventOption"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "HandleEventOptionChosenMessage"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "HandleSharedEventOptionChosenMessage"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "HandleVotedForSharedEventOptionMessage"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "PlayerVotedForSharedOptionIndex")
    ];

    public static IReadOnlyList<(string TypeName, string MethodName)> TargetSignaturesForTesting => Targets;

    public static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var (typeName, methodName) in Targets)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type is null)
            {
                continue;
            }

            foreach (var method in AccessTools.GetDeclaredMethods(type).Where(method => ShouldPatchMethod(method, methodName)))
            {
                yield return method;
            }
        }
    }

    private static bool ShouldPatchMethod(MethodBase method, string methodName)
    {
        if (method.Name != methodName || method.IsAbstract)
        {
            return false;
        }

        return !method.ContainsGenericParameters
            && !method.IsGenericMethod
            && !method.IsGenericMethodDefinition;
    }

    public static bool Prefix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        PlayerChoiceDiagnostics.Write("enter", __originalMethod, __instance, __args);
        return true;
    }

    public static void Postfix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        PlayerChoiceDiagnostics.Write("exit", __originalMethod, __instance, __args);
    }

    public static Exception? Finalizer(MethodBase __originalMethod, Exception? __exception)
    {
        return PlayerChoiceDiagnostics.LogFinalizer(__originalMethod, __exception);
    }
}

[HarmonyPatch]
public static class PlayerChoiceRelicNodeBreadcrumbPatches
{
    private static readonly (string TypeName, string MethodName)[] Targets =
    [
        ("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "Reload"),
        ("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "get_Icon"),
        ("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "add_ModelChanged"),
        ("MegaCrit.Sts2.Core.Nodes.Relics.NRelic", "set_Model")
    ];

    public static IReadOnlyList<(string TypeName, string MethodName)> TargetSignaturesForTesting => Targets;

    public static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var (typeName, methodName) in Targets)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type is null)
            {
                continue;
            }

            foreach (var method in AccessTools.GetDeclaredMethods(type).Where(method =>
                method.Name == methodName
                && !method.IsAbstract
                && !method.ContainsGenericParameters
                && !method.IsGenericMethod
                && !method.IsGenericMethodDefinition))
            {
                yield return method;
            }
        }
    }

    public static bool Prefix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        PlayerChoiceDiagnostics.WriteRelicNodeBreadcrumb("enter", __originalMethod, __instance, __args);
        return true;
    }

    public static void Postfix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        PlayerChoiceDiagnostics.WriteRelicNodeBreadcrumb("exit", __originalMethod, __instance, __args);
    }

    public static Exception? Finalizer(MethodBase __originalMethod, Exception? __exception)
    {
        return PlayerChoiceDiagnostics.LogFinalizer(__originalMethod, __exception);
    }
}

[HarmonyPatch]
public static class PlayerChoiceGetNodeDiagnosticsPatches
{
    private static readonly string[] GenericTargetTypeNames =
    [
        "MegaCrit.Sts2.Core.Nodes.Relics.NRelic",
        "MegaCrit.Sts2.addons.mega_text.MegaLabel"
    ];

    public static IReadOnlyList<string> GenericTargetTypeNamesForTesting => GenericTargetTypeNames;

    public static IEnumerable<MethodBase> TargetMethods()
    {
        var nodeType = AccessTools.TypeByName("Godot.Node");
        if (nodeType is null)
        {
            yield break;
        }

        var method = AccessTools
            .GetDeclaredMethods(nodeType)
            .SingleOrDefault(candidate =>
                candidate.Name == "GetNode"
                && candidate.IsGenericMethodDefinition
                && candidate.GetParameters() is [{ ParameterType.FullName: "Godot.NodePath" }]);
        if (method is null)
        {
            yield break;
        }

        foreach (var typeName in GenericTargetTypeNames)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type is not null)
            {
                yield return method.MakeGenericMethod(type);
            }
        }
    }

    public static bool Prefix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        if (ShouldLog(__instance, __args))
        {
            PlayerChoiceDiagnostics.Write("enter", __originalMethod, __instance, __args);
        }

        return true;
    }

    public static void Postfix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        if (ShouldLog(__instance, __args))
        {
            PlayerChoiceDiagnostics.Write("exit", __originalMethod, __instance, __args);
        }
    }

    public static Exception? Finalizer(MethodBase __originalMethod, object? __instance, object[] __args, Exception? __exception)
    {
        return ShouldLog(__instance, __args)
            ? PlayerChoiceDiagnostics.LogFinalizer(__originalMethod, __exception)
            : __exception;
    }

    private static bool ShouldLog(object? instance, IReadOnlyList<object?> args)
    {
        return instance?.GetType().FullName == "MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder"
            && args.Count == 1
            && args[0]?.ToString() is "%Relic" or "%AmountLabel";
    }
}

[HarmonyPatch]
public static class PlayerChoiceRelicHolderReadyProbePatches
{
    private const string TargetTypeName = "MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder";
    private const string TargetMethodName = "_Ready";
    private const string RelicNodeTypeName = "MegaCrit.Sts2.Core.Nodes.Relics.NRelic";
    private const string AmountLabelNodeTypeName = "MegaCrit.Sts2.addons.mega_text.MegaLabel";

    public static (string TypeName, string MethodName) TargetSignatureForTesting => (TargetTypeName, TargetMethodName);

    public static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName(TargetTypeName);
        return type is null
            ? null
            : AccessTools.Method(type, TargetMethodName);
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var logAfterFieldStore = AccessTools.Method(typeof(PlayerChoiceRelicHolderReadyProbePatches), nameof(LogAfterFieldStore));

        foreach (var instruction in instructions)
        {
            yield return instruction;

            var fieldName = GetInterestingFieldStoreName(instruction);
            if (fieldName is not null)
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldstr, fieldName);
                yield return new CodeInstruction(OpCodes.Call, logAfterFieldStore);
            }
        }
    }

    private static string? GetInterestingFieldStoreName(CodeInstruction instruction)
    {
        if (instruction.opcode != OpCodes.Stfld || instruction.operand is not FieldInfo field)
        {
            return null;
        }

        return field.DeclaringType?.FullName == TargetTypeName
            && field.Name is "_relic" or "_amountLabel"
                ? field.Name
                : null;
    }

    private static void LogAfterFieldStore(object? instance, string fieldName)
    {
        PlayerChoiceDiagnostics.WriteRelicHolderReadyProbe("after-field-store", instance, fieldName, null);
    }
}

[HarmonyPatch]
public static class PlayerChoiceTaskDiagnosticsPatches
{
    private static readonly (string TypeName, string MethodName)[] Targets =
    [
        ("MegaCrit.Sts2.Core.Rooms.EventRoom", "EnterInternal")
    ];

    public static IReadOnlyList<(string TypeName, string MethodName)> TargetSignaturesForTesting => Targets;

    public static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var (typeName, methodName) in Targets)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type is null)
            {
                continue;
            }

            foreach (var method in AccessTools.GetDeclaredMethods(type).Where(method =>
                method.Name == methodName && method.ReturnType == typeof(Task)))
            {
                yield return method;
            }
        }
    }

    public static bool Prefix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        PlayerChoiceDiagnostics.Write("enter", __originalMethod, __instance, __args);
        return true;
    }

    public static void Postfix(MethodBase __originalMethod, object? __instance, Task? __result)
    {
        PlayerChoiceDiagnostics.WriteTaskReturned(__originalMethod, __instance, __result);
    }

    public static Exception? Finalizer(MethodBase __originalMethod, Exception? __exception)
    {
        return PlayerChoiceDiagnostics.LogFinalizer(__originalMethod, __exception);
    }
}
