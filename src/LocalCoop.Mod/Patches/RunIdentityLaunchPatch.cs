using System.Reflection;
using HarmonyLib;
using LocalCoop.Mod.Runtime;
using MegaCrit.Sts2.Core.Context;

namespace LocalCoop.Mod.Patches;

[HarmonyPatch]
public static class RunIdentityLaunchPatch
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

    public static void Postfix(object? __instance)
    {
        AlignLocalContextForBrokerRunForTesting(__instance);
    }

    public static bool AlignLocalContextForBrokerRunForTesting(object? instance)
    {
        var service = ResolveBrokerNetGameService(instance);
        if (service is null)
        {
            return false;
        }

        var expectedNetId = service.NetId;
        LocalContext.NetId = expectedNetId;
        AlignEventSynchronizerLocalPlayerId(instance, expectedNetId);
        return true;
    }

    private static void AlignEventSynchronizerLocalPlayerId(object? instance, ulong expectedNetId)
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

        localPlayerIdField.SetValue(eventSynchronizer, expectedNetId);
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
}
