using System.Collections;
using System.Reflection;
using HarmonyLib;
using LocalCoop.Mod.Runtime;
using MegaCrit.Sts2.Core.Context;

namespace LocalCoop.Mod.Patches;

internal static class RunTransitionDiagnostics
{
    public static void Write(string phase, MethodBase method, object? instance, IReadOnlyList<object?> args)
    {
        var settings = LoadSettings();
        if (!settings.Enabled)
        {
            return;
        }

        try
        {
            new BrokerEventLog(settings.EventLogPath).Write(
                $"Run transition {phase}: client={settings.ClientId} method={FormatMethod(method)} {FormatRuntimeContext(instance, args)} instance={FormatArg(instance)} args=[{string.Join(", ", args.Select(FormatArg))}].");
        }
        catch
        {
        }
    }

    public static void WriteTaskReturned(MethodBase method, Task? task)
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
                $"Run transition task returned: client={clientId} method={methodName} status={task?.Status.ToString() ?? "null"}.");
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
                $"Run transition threw: client={settings.ClientId} method={FormatMethod(method)} exception={FormatException(exception)}.");
        }
        catch
        {
        }

        return exception;
    }

    private static void WriteTaskCompleted(string path, string clientId, string methodName, Task completedTask)
    {
        try
        {
            var status = completedTask.Status.ToString();
            var suffix = completedTask.Exception is null ? string.Empty : $" exception={FormatException(completedTask.Exception.GetBaseException())}";
            new BrokerEventLog(path).Write(
                $"Run transition task completed: client={clientId} method={methodName} status={status}{suffix}.");
        }
        catch
        {
        }
    }

    private static string FormatMethod(MethodBase method)
    {
        return $"{method.DeclaringType?.FullName ?? "<unknown>"}.{method.Name}";
    }

    private static string FormatRuntimeContext(object? instance, IReadOnlyList<object?> args)
    {
        var parts = new List<string>
        {
            $"localContextNetId={FormatId(TryGetLocalContextNetId())}"
        };

        AddObjectContext(parts, "instance", instance);
        for (var i = 0; i < args.Count; i++)
        {
            AddObjectContext(parts, $"arg{i}", args[i]);
        }

        return $"context=[{string.Join(" ", parts)}]";
    }

    private static void AddObjectContext(List<string> parts, string label, object? value)
    {
        if (value is null)
        {
            return;
        }

        var typeName = value.GetType().FullName ?? value.GetType().Name;
        if (typeName == "MegaCrit.Sts2.Core.Runs.RunManager")
        {
            var state = TryGetProperty(value, "State");
            var runLobby = TryGetProperty(value, "RunLobby");
            AddNetServiceContext(parts, $"{label}Net", TryGetProperty(value, "NetService"));
            parts.Add($"{label}StatePlayers={FormatIds(TryGetPlayerIds(state))}");
            parts.Add($"{label}RunLobbyIds={FormatIds(TryGetIds(TryGetProperty(runLobby, "ConnectedPlayerIds")))}");
            return;
        }

        if (typeName == "MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby")
        {
            AddNetServiceContext(parts, $"{label}LobbyNet", TryGetProperty(value, "NetService"));
            parts.Add($"{label}LobbyPlayers={FormatIds(TryGetPlayerIds(TryGetProperty(value, "Players")))}");
            parts.Add($"{label}LocalPlayer={FormatId(TryGetProperty(TryGetProperty(value, "LocalPlayer"), "NetId"))}");
            parts.Add($"{label}IsBeginningRun={TryGetField(value, "_isBeginningRun") ?? "null"}");
            return;
        }

        AddNetServiceContext(parts, label, value);
    }

    private static void AddNetServiceContext(List<string> parts, string label, object? value)
    {
        if (value is null)
        {
            return;
        }

        var netId = TryGetProperty(value, "NetId");
        var type = TryGetProperty(value, "Type");
        if (netId is not null || type is not null)
        {
            parts.Add($"{label}Type={type ?? "null"}");
            parts.Add($"{label}Id={FormatId(netId)}");
        }
    }

    private static string FormatArg(object? arg)
    {
        if (arg is null)
        {
            return "null";
        }

        return arg switch
        {
            string value => value,
            bool value => value.ToString(),
            int value => value.ToString(),
            uint value => value.ToString(),
            long value => value.ToString(),
            ulong value => value.ToString(),
            IEnumerable enumerable when arg is not string => FormatEnumerable(arg, enumerable),
            _ => arg.GetType().FullName ?? arg.GetType().Name
        };
    }

    private static IEnumerable<ulong> TryGetPlayerIds(object? source)
    {
        foreach (var player in TryGetEnumerable(TryGetProperty(source, "Players")).Concat(TryGetEnumerable(source)))
        {
            if (TryGetProperty(player, "NetId") is ulong id)
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<ulong> TryGetIds(object? source)
    {
        foreach (var item in TryGetEnumerable(source))
        {
            if (item is ulong id)
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<object?> TryGetEnumerable(object? value)
    {
        if (value is not IEnumerable enumerable || value is string)
        {
            yield break;
        }

        foreach (var item in enumerable)
        {
            yield return item;
        }
    }

    private static string FormatEnumerable(object source, IEnumerable enumerable)
    {
        var count = 0;
        foreach (var _ in enumerable)
        {
            count++;
            if (count > 20)
            {
                return $"{source.GetType().FullName} count=>20";
            }
        }

        return $"{source.GetType().FullName} count={count}";
    }

    private static object? TryGetField(object instance, string name)
    {
        try
        {
            return AccessTools.Field(instance.GetType(), name)?.GetValue(instance);
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

    private static string FormatIds(IEnumerable<ulong> ids)
    {
        var formatted = ids.Distinct().OrderBy(id => id).Select(id => FormatId(id)).ToArray();
        return $"[{string.Join(",", formatted)}]";
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

[HarmonyPatch]
public static class RunIdentityVoidDiagnosticsPatches
{
    private static readonly (string TypeName, string MethodName)[] Targets =
    [
        ("MegaCrit.Sts2.Core.Runs.RunManager", "SetUpNewMultiPlayer"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "InitializeShared"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "InitializeRunLobby"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "InitializeNewRun"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "CleanUp")
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
                method.Name == methodName && method.ReturnType == typeof(void)))
            {
                yield return method;
            }
        }
    }

    public static void Prefix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        RunTransitionDiagnostics.Write("enter", __originalMethod, __instance, __args);
    }

    public static void Postfix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        RunTransitionDiagnostics.Write("exit", __originalMethod, __instance, __args);
    }

    public static Exception? Finalizer(MethodBase __originalMethod, Exception? __exception)
    {
        return RunTransitionDiagnostics.LogFinalizer(__originalMethod, __exception);
    }
}

[HarmonyPatch]
public static class RunIdentityLaunchDiagnosticsPatches
{
    private const string TypeName = "MegaCrit.Sts2.Core.Runs.RunManager";
    private const string MethodName = "Launch";

    public static (string TypeName, string MethodName) TargetSignatureForTesting => (TypeName, MethodName);

    public static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName(TypeName);
        return type is null
            ? null
            : AccessTools.GetDeclaredMethods(type).SingleOrDefault(method =>
                method.Name == MethodName && method.GetParameters().Length == 0);
    }

    public static void Prefix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        RunTransitionDiagnostics.Write("enter", __originalMethod, __instance, __args);
    }

    public static void Postfix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        AlignLocalContextForBrokerRunForTesting(__instance, null);
        RunTransitionDiagnostics.Write("exit", __originalMethod, __instance, __args);
    }

    public static Exception? Finalizer(MethodBase __originalMethod, Exception? __exception)
    {
        return RunTransitionDiagnostics.LogFinalizer(__originalMethod, __exception);
    }

    public static bool AlignLocalContextForBrokerRunForTesting(object? instance, Action<string>? log)
    {
        var service = ResolveBrokerNetGameService(instance);
        if (service is null)
        {
            return false;
        }

        var previousNetId = LocalContext.NetId;
        var expectedNetId = service.NetId;
        LocalContext.NetId = expectedNetId;
        if (previousNetId != expectedNetId)
        {
            log?.Invoke($"Broker run identity: aligned LocalContext.NetId from {FormatId(previousNetId)} to {FormatId(expectedNetId)}.");
        }

        AlignEventSynchronizerLocalPlayerId(instance, expectedNetId, log);
        return true;
    }

    private static void AlignEventSynchronizerLocalPlayerId(object? instance, ulong expectedNetId, Action<string>? log)
    {
        var eventSynchronizer = TryGetProperty(instance, "EventSynchronizer");
        if (eventSynchronizer is null)
        {
            return;
        }

        var localPlayerIdField = AccessTools.Field(eventSynchronizer.GetType(), "_localPlayerId");
        if (localPlayerIdField?.FieldType != typeof(ulong))
        {
            return;
        }

        if (localPlayerIdField.GetValue(eventSynchronizer) is not ulong previousNetId || previousNetId == expectedNetId)
        {
            return;
        }

        try
        {
            localPlayerIdField.SetValue(eventSynchronizer, expectedNetId);
            log?.Invoke($"Broker run identity: aligned EventSynchronizer._localPlayerId from {FormatId(previousNetId)} to {FormatId(expectedNetId)}.");
        }
        catch (Exception exception)
        {
            log?.Invoke($"Broker run identity: failed to align EventSynchronizer._localPlayerId from {FormatId(previousNetId)} to {FormatId(expectedNetId)}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static BrokerNetGameService? ResolveBrokerNetGameService(object? instance)
    {
        if (instance is BrokerNetGameService service)
        {
            return service;
        }

        if (instance is null)
        {
            return null;
        }

        var type = instance.GetType();
        if (AccessTools.Property(type, "NetService")?.GetValue(instance) is BrokerNetGameService propertyService)
        {
            return propertyService;
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.GetValue(instance) is BrokerNetGameService fieldService)
            {
                return fieldService;
            }
        }

        return null;
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

    private static string FormatId(ulong? value)
    {
        return value is ulong id
            ? $"{id}/0x{id:X16}"
            : "null";
    }
}

[HarmonyPatch]
public static class RunTransitionTaskDiagnosticsPatches
{
    private static readonly (string TypeName, string MethodName)[] Targets =
    [
        ("MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen", "StartNewMultiplayerRun"),
        ("MegaCrit.Sts2.Core.Nodes.NGame", "StartNewMultiplayerRun"),
        ("MegaCrit.Sts2.Core.Nodes.NGame", "StartRun"),
        ("MegaCrit.Sts2.Core.Nodes.NGame", "ReturnToMainMenu"),
        ("MegaCrit.Sts2.Core.Nodes.NGame", "GoToTimeline"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "FinalizeStartingRelics"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "GenerateMap"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "EnterAct"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "SetActInternal"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "FadeIn"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "ReturnToMainMenuWithError"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "AbandonInternal")
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
                method.Name == methodName && typeof(Task).IsAssignableFrom(method.ReturnType)))
            {
                yield return method;
            }
        }
    }

    public static void Prefix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        RunTransitionDiagnostics.Write("enter", __originalMethod, __instance, __args);
    }

    public static void Postfix(MethodBase __originalMethod, Task? __result)
    {
        RunTransitionDiagnostics.WriteTaskReturned(__originalMethod, __result);
    }

    public static Exception? Finalizer(MethodBase __originalMethod, Exception? __exception)
    {
        return RunTransitionDiagnostics.LogFinalizer(__originalMethod, __exception);
    }
}

[HarmonyPatch]
public static class RunTransitionVoidDiagnosticsPatches
{
    private static readonly (string TypeName, string MethodName)[] Targets =
    [
        ("MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby", "HandleLobbyBeginRunMessage"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby", "BeginRunForAllPlayers"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby", "BeginRunLocally"),
        ("MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen", "BeginRun"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "SetUpNewMultiPlayer"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "InitializeShared"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "InitializeRunLobby"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "InitializeNewRun"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "GenerateRooms"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "CleanUp"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "StateDiverged"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "LocalPlayerDisconnected"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "RemotePlayerDisconnected"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "ReturnToMainMenuWithError"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "Abandon"),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "AbandonInternal"),
        ("MegaCrit.Sts2.Core.Nodes.NSceneContainer", "SetCurrentScene"),
        ("MegaCrit.Sts2.Core.Nodes.NRun", "_Ready"),
        ("MegaCrit.Sts2.Core.Nodes.NRun", "_Notification"),
        ("MegaCrit.Sts2.Core.Nodes.Rooms.NMapRoom", "_Ready"),
        ("MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen", "_Ready"),
        ("MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen", "Initialize"),
        ("MegaCrit.Sts2.Core.Multiplayer.Game.ChecksumTracker", "LogStateDivergence")
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
                method.Name == methodName && method.ReturnType == typeof(void)))
            {
                yield return method;
            }
        }
    }

    public static void Prefix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        RunTransitionDiagnostics.Write("enter", __originalMethod, __instance, __args);
    }

    public static void Postfix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        RunTransitionDiagnostics.Write("exit", __originalMethod, __instance, __args);
    }

    public static Exception? Finalizer(MethodBase __originalMethod, Exception? __exception)
    {
        return RunTransitionDiagnostics.LogFinalizer(__originalMethod, __exception);
    }
}
