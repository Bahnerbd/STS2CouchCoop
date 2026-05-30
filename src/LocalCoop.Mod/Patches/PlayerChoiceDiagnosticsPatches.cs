using System.Collections;
using System.Reflection;
using HarmonyLib;
using LocalCoop.Mod.Runtime;

namespace LocalCoop.Mod.Patches;

internal static class PlayerChoiceDiagnostics
{
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
        "_localPlayerId",
        "_pageIndex",
        "_playerVotes"
    ];

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
            Enum value => value.ToString(),
            IEnumerable enumerable when arg is not string => FormatEnumerable(enumerable),
            _ => FormatObject(arg)
        };
    }

    private static string FormatObject(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        var type = value.GetType();
        var members = InterestingMemberNames
            .Select(name => TryFormatMember(type, value, name))
            .Where(member => member is not null)
            .Cast<string>()
            .ToArray();

        return members.Length == 0
            ? type.FullName ?? type.Name
            : $"{type.FullName ?? type.Name}{{{string.Join(", ", members)}}}";
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
            ulong number => number.ToString(),
            Enum enumValue => enumValue.ToString(),
            IEnumerable enumerable when value is not string => FormatEnumerable(enumerable),
            _ => value.GetType().FullName ?? value.GetType().Name
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

    public static void Prefix(MethodBase __originalMethod, object? __instance, object[] __args)
    {
        PlayerChoiceDiagnostics.Write("enter", __originalMethod, __instance, __args);
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
