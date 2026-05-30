using System.Collections;
using System.Reflection;
using HarmonyLib;
using LocalCoop.Mod.Runtime;

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
                $"Run transition {phase}: client={settings.ClientId} method={FormatMethod(method)} instance={FormatArg(instance)} args=[{string.Join(", ", args.Select(FormatArg))}].");
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
