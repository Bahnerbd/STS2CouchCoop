using System.Collections;
using System.Reflection;
using HarmonyLib;
using LocalCoop.Mod.Runtime;

namespace LocalCoop.Mod.Patches;

internal static class CombatSyncDiagnostics
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
                $"Combat sync {phase}: client={settings.ClientId} method={FormatMethod(method)} {FormatSynchronizer(instance)} args=[{string.Join(", ", args.Select(FormatArg))}].");
        }
        catch
        {
        }
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
                $"Combat sync task returned: client={clientId} method={methodName} status={task?.Status.ToString() ?? "null"} {FormatSynchronizer(instance)}.");
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
                $"Combat sync threw: client={settings.ClientId} method={FormatMethod(method)} exception={FormatException(exception)}.");
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
                $"Combat sync task completed: client={clientId} method={methodName} status={status}{suffix}.");
        }
        catch
        {
        }
    }

    private static string FormatSynchronizer(object? synchronizer)
    {
        if (synchronizer is null)
        {
            return "state=null";
        }

        var type = synchronizer.GetType();
        var netService = TryGetField(type, synchronizer, "_netService");
        var runLobby = TryGetField(type, synchronizer, "_runLobby");
        var runState = TryGetField(type, synchronizer, "_runState");
        var syncDataIds = TryGetDictionaryKeys(TryGetField(type, synchronizer, "_syncData")).ToArray();
        var runLobbyIds = TryGetIds(TryGetProperty(runLobby, "ConnectedPlayerIds")).ToArray();
        var runPlayerIds = TryGetRunPlayerIds(runState).ToArray();
        var missingIds = runLobbyIds.Except(syncDataIds).ToArray();
        var syncCompletionSource = TryGetField(type, synchronizer, "_syncCompletionSource");

        return string.Join(
            " ",
            $"localContextNetId={FormatId(TryGetLocalContextNetId())}",
            $"netType={TryGetProperty(netService, "Type") ?? "null"}",
            $"netId={FormatId(TryGetProperty(netService, "NetId"))}",
            $"isGameLoading={TryGetProperty(netService, "IsGameLoading") ?? "null"}",
            $"isDisabled={TryGetProperty(synchronizer, "IsDisabled") ?? "null"}",
            $"runLobbyIds={FormatIds(runLobbyIds)}",
            $"runPlayerIds={FormatIds(runPlayerIds)}",
            $"syncDataIds={FormatIds(syncDataIds)}",
            $"missingIds={FormatIds(missingIds)}",
            $"hasRng={TryGetField(type, synchronizer, "_rngSet") is not null}",
            $"hasRelicGrabBag={TryGetField(type, synchronizer, "_sharedRelicGrabBag") is not null}",
            $"syncTask={FormatTaskCompletionSource(syncCompletionSource)}");
    }

    private static IEnumerable<ulong> TryGetRunPlayerIds(object? runState)
    {
        foreach (var player in TryGetEnumerable(TryGetProperty(runState, "Players")))
        {
            if (TryGetProperty(player, "NetId") is ulong id)
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<ulong> TryGetDictionaryKeys(object? dictionary)
    {
        if (dictionary is IDictionary nonGenericDictionary)
        {
            foreach (var key in nonGenericDictionary.Keys)
            {
                if (key is ulong id)
                {
                    yield return id;
                }
            }

            yield break;
        }

        foreach (var key in TryGetEnumerable(TryGetProperty(dictionary, "Keys")))
        {
            if (key is ulong id)
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<ulong> TryGetIds(object? value)
    {
        foreach (var item in TryGetEnumerable(value))
        {
            if (item is ulong id)
            {
                yield return id;
            }
        }
    }

    private static IEnumerable TryGetEnumerable(object? value)
    {
        return value as IEnumerable ?? Array.Empty<object>();
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

    private static string FormatTaskCompletionSource(object? source)
    {
        var task = TryGetProperty(source, "Task") as Task;
        return task?.Status.ToString() ?? "null";
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
            ulong value => FormatId(value),
            Enum value => value.ToString(),
            _ => FormatObject(arg)
        };
    }

    private static string FormatObject(object value)
    {
        var type = value.GetType();
        var typeName = type.FullName ?? type.Name;
        if (typeName.EndsWith("SyncPlayerDataMessage", StringComparison.Ordinal))
        {
            var player = TryGetField(type, value, "player");
            return $"{typeName}{{playerNetId={FormatId(TryGetProperty(player, "NetId"))}}}";
        }

        if (typeName.EndsWith("SyncRngMessage", StringComparison.Ordinal))
        {
            return $"{typeName}{{hasRng={TryGetField(type, value, "rng") is not null}, hasRelicGrabBag={TryGetField(type, value, "sharedRelicGrabBag") is not null}}}";
        }

        return typeName;
    }

    private static string FormatIds(IEnumerable<ulong> ids)
    {
        var formatted = ids.OrderBy(id => id).Select(id => FormatId(id)).ToArray();
        return $"[{string.Join(",", formatted)}]";
    }

    private static string FormatId(object? value)
    {
        return value is ulong id
            ? $"{id}/0x{id:X16}"
            : "null";
    }

    private static string FormatMethod(MethodBase method)
    {
        return $"{method.DeclaringType?.FullName ?? "<unknown>"}.{method.Name}";
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
public static class CombatSyncVoidDiagnosticsPatches
{
    private static readonly (string TypeName, string MethodName)[] Targets =
    [
        ("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "StartSync"),
        ("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "OnSyncPlayerMessageReceived"),
        ("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "OnSyncRngMessageReceived"),
        ("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "CheckSyncCompleted")
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
        CombatSyncDiagnostics.Write("enter", __originalMethod, __instance, __args);
    }

    public static void Postfix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        CombatSyncDiagnostics.Write("exit", __originalMethod, __instance, __args);
    }

    public static Exception? Finalizer(MethodBase __originalMethod, Exception? __exception)
    {
        return CombatSyncDiagnostics.LogFinalizer(__originalMethod, __exception);
    }
}

[HarmonyPatch]
public static class CombatSyncWaitDiagnosticsPatches
{
    private const string TypeName = "MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer";
    private const string MethodName = "WaitForSync";

    public static (string TypeName, string MethodName) TargetSignatureForTesting => (TypeName, MethodName);

    public static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName(TypeName);
        return type is null
            ? null
            : AccessTools.GetDeclaredMethods(type).SingleOrDefault(method =>
                method.Name == MethodName && method.ReturnType == typeof(Task));
    }

    public static void Prefix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        CombatSyncDiagnostics.Write("enter", __originalMethod, __instance, __args);
    }

    public static void Postfix(MethodBase __originalMethod, object? __instance, Task? __result)
    {
        CombatSyncDiagnostics.WriteTaskReturned(__originalMethod, __instance, __result);
    }

    public static Exception? Finalizer(MethodBase __originalMethod, Exception? __exception)
    {
        return CombatSyncDiagnostics.LogFinalizer(__originalMethod, __exception);
    }
}
