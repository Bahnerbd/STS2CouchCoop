using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LocalCoop.Mod.Runtime;

public static class SteamControllerInputSelection
{
    private static readonly BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly object Lock = new();
    private static readonly HashSet<object> GeneratedInputEvents = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<object> AcceptedUiCompanionInputEvents = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<string, Queue<DateTimeOffset>> PendingUiCompanionActions = new(StringComparer.Ordinal);
    private static readonly TimeSpan UiCompanionTokenLifetime = TimeSpan.FromMilliseconds(250);
    private static string? _lastSelectionSummary;

    public static SteamControllerHandleSelection<T> ChooseControllerHandle<T>(
        IReadOnlyList<T> handles,
        BrokerControllerDeviceAssignment assignment)
    {
        if (!assignment.IsConfigured || assignment.Device is null)
        {
            return new SteamControllerHandleSelection<T>(
                Selected: false,
                Index: -1,
                Handle: default,
                Reason: "controllerDevice is unconfigured or none");
        }

        var index = assignment.Device.Value;
        if (index < 0 || index >= handles.Count)
        {
            return new SteamControllerHandleSelection<T>(
                Selected: false,
                Index: index,
                Handle: default,
                Reason: $"no connected Steam controller at controllerDevice={index}");
        }

        return new SteamControllerHandleSelection<T>(
            Selected: true,
            Index: index,
            Handle: handles[index],
            Reason: $"selected controllerDevice={index}");
    }

    public static bool IsGeneratedInputEvent(object? inputEvent)
    {
        if (inputEvent is null)
        {
            return false;
        }

        lock (Lock)
        {
            return GeneratedInputEvents.Contains(inputEvent);
        }
    }

    public static void RegisterGeneratedInputEvents(IEnumerable<object?> inputEvents)
    {
        lock (Lock)
        {
            foreach (var inputEvent in inputEvents)
            {
                if (inputEvent is not null)
                {
                    GeneratedInputEvents.Add(inputEvent);
                }
            }
        }
    }

    public static void RegisterGeneratedUiCompanionAction(object? inputEvent, DateTimeOffset? now = null)
    {
        if (!CanCreateUiCompanionAction(inputEvent))
        {
            return;
        }

        var companionAction = MapSteamControllerActionToUiCompanion(GetActionName(inputEvent));
        if (companionAction is null)
        {
            return;
        }

        lock (Lock)
        {
            if (!PendingUiCompanionActions.TryGetValue(companionAction, out var pendingTokens))
            {
                pendingTokens = new Queue<DateTimeOffset>();
                PendingUiCompanionActions[companionAction] = pendingTokens;
            }

            pendingTokens.Enqueue(now ?? DateTimeOffset.UtcNow);
        }
    }

    public static bool CanCreateUiCompanionAction(object? inputEvent)
    {
        return IsPressedInput(inputEvent)
            && MapSteamControllerActionToUiCompanion(GetActionName(inputEvent)) is not null;
    }

    public static bool CanMapUiCompanionAction(object? inputEvent)
    {
        return MapSteamControllerActionToUiCompanion(GetActionName(inputEvent)) is not null;
    }

    public static bool TryCreateUiCompanionInputEvent(object? inputEvent, out object? uiCompanionInputEvent)
    {
        uiCompanionInputEvent = null;
        if (!CanMapUiCompanionAction(inputEvent) || inputEvent is null)
        {
            return false;
        }

        var companionAction = MapSteamControllerActionToUiCompanion(GetActionName(inputEvent));
        if (companionAction is null)
        {
            return false;
        }

        var duplicate = TryDuplicateInputEvent(inputEvent);
        if (duplicate is null || !TrySetActionName(duplicate, companionAction))
        {
            return false;
        }

        uiCompanionInputEvent = duplicate;
        return true;
    }

    public static bool TryDispatchUiCompanionInputEvent(object? inputEvent)
    {
        if (!TryCreateUiCompanionInputEvent(inputEvent, out var uiCompanionInputEvent)
            || uiCompanionInputEvent is null)
        {
            return false;
        }

        try
        {
            var inputType = AccessTools.TypeByName("Godot.Input");
            var parseInputEvent = inputType?.GetMethods(Members)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "ParseInputEvent", StringComparison.Ordinal)
                    && method.GetParameters() is [var parameter]
                    && parameter.ParameterType.IsAssignableFrom(uiCompanionInputEvent.GetType()));
            if (parseInputEvent is null)
            {
                return false;
            }

            AcceptGeneratedUiCompanionInputEvent(uiCompanionInputEvent);
            parseInputEvent.Invoke(null, [uiCompanionInputEvent]);
            return true;
        }
        catch (Exception exception) when (exception is TargetInvocationException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    public static bool TryConsumeGeneratedUiCompanionInputEvent(object? inputEvent, DateTimeOffset? now = null)
    {
        if (inputEvent is null)
        {
            return false;
        }

        lock (Lock)
        {
            if (AcceptedUiCompanionInputEvents.Contains(inputEvent))
            {
                return true;
            }
        }

        if (!IsPressedInput(inputEvent))
        {
            return false;
        }

        var action = GetActionName(inputEvent);
        if (action is null)
        {
            return false;
        }

        lock (Lock)
        {
            if (AcceptedUiCompanionInputEvents.Contains(inputEvent))
            {
                return true;
            }

            var currentTime = now ?? DateTimeOffset.UtcNow;
            if (!PendingUiCompanionActions.TryGetValue(action, out var pendingTokens))
            {
                return false;
            }

            PruneExpiredUiCompanionTokens(action, pendingTokens, currentTime);
            if (pendingTokens.Count == 0)
            {
                return false;
            }

            pendingTokens.Dequeue();
            if (pendingTokens.Count == 0)
            {
                PendingUiCompanionActions.Remove(action);
            }

            AcceptedUiCompanionInputEvents.Add(inputEvent);
            return true;
        }
    }

    public static void AcceptGeneratedUiCompanionInputEventForTesting(object? inputEvent)
    {
        AcceptGeneratedUiCompanionInputEvent(inputEvent);
    }

    public static void ClearGeneratedInputEventsForTesting()
    {
        lock (Lock)
        {
            GeneratedInputEvents.Clear();
            AcceptedUiCompanionInputEvents.Clear();
            PendingUiCompanionActions.Clear();
            _lastSelectionSummary = null;
        }
    }

    private static void AcceptGeneratedUiCompanionInputEvent(object? inputEvent)
    {
        if (inputEvent is null)
        {
            return;
        }

        lock (Lock)
        {
            AcceptedUiCompanionInputEvents.Add(inputEvent);
        }
    }

    public static void ApplySelection(
        object strategy,
        BrokerControllerDeviceAssignment assignment,
        Action<string> log)
    {
        if (!assignment.IsConfigured || assignment.Device is null)
        {
            return;
        }

        try
        {
            var handles = GetConnectedControllerHandles(strategy);
            var selection = ChooseControllerHandle(handles, assignment);
            if (!selection.Selected || selection.Handle is null)
            {
                ClearGeneratedInputEvents();
                LogIfChanged(
                    $"Steam controller selection: unavailable controllerDevice={selection.Index} connected={handles.Count} reason={selection.Reason}.",
                    log);
                return;
            }

            var previousHandle = GetCurrentControllerHandle(strategy);
            SetCurrentControllerHandle(strategy, selection.Handle);
            RefreshControllerConfig(strategy, selection.Handle);
            RegisterGeneratedInputEventsFromStrategy(strategy);

            if (!Equals(previousHandle, selection.Handle))
            {
                ClearPressedInputs(strategy);
            }

            LogIfChanged(
                $"Steam controller selection: selected controllerDevice={selection.Index} handle={selection.Handle} connected={handles.Count}.",
                log);
        }
        catch (Exception exception) when (exception is TargetInvocationException or MissingMemberException or InvalidOperationException or ArgumentException)
        {
            ClearGeneratedInputEvents();
            LogIfChanged(
                $"Steam controller selection failed: {exception.GetType().Name}: {exception.Message}",
                log);
        }
    }

    private static List<object> GetConnectedControllerHandles(object strategy)
    {
        var handleField = strategy.GetType().GetField("_currentControllerHandle", Members)
            ?? throw new MissingMemberException(strategy.GetType().FullName, "_currentControllerHandle");
        var nullableHandleType = handleField.FieldType;
        var handleType = Nullable.GetUnderlyingType(nullableHandleType) ?? nullableHandleType;
        var steamInputType = AccessTools.TypeByName("Steamworks.SteamInput")
            ?? throw new MissingMemberException("Steamworks.SteamInput");
        var method = steamInputType.GetMethod("GetConnectedControllers", Members, [handleType.MakeArrayType()])
            ?? throw new MissingMethodException("Steamworks.SteamInput", "GetConnectedControllers");

        var handlesArray = Array.CreateInstance(handleType, 16);
        var count = method.Invoke(null, [handlesArray]) is int connectedCount ? connectedCount : 0;
        var handles = new List<object>(Math.Max(count, 0));
        for (var index = 0; index < count && index < handlesArray.Length; index++)
        {
            var handle = handlesArray.GetValue(index);
            if (handle is not null)
            {
                handles.Add(handle);
            }
        }

        return handles;
    }

    private static object? GetCurrentControllerHandle(object strategy)
    {
        var field = strategy.GetType().GetField("_currentControllerHandle", Members);
        return field?.GetValue(strategy);
    }

    private static void SetCurrentControllerHandle(object strategy, object handle)
    {
        var field = strategy.GetType().GetField("_currentControllerHandle", Members)
            ?? throw new MissingMemberException(strategy.GetType().FullName, "_currentControllerHandle");
        field.SetValue(strategy, handle);
    }

    private static void RefreshControllerConfig(object strategy, object handle)
    {
        var steamInputType = AccessTools.TypeByName("Steamworks.SteamInput")
            ?? throw new MissingMemberException("Steamworks.SteamInput");
        var controllerType = steamInputType.GetMethod("GetInputTypeForHandle", Members, [handle.GetType()])
            ?.Invoke(null, [handle]);
        if (controllerType is not null)
        {
            strategy.GetType().GetMethod("UpdateControllerConfig", Members, [controllerType.GetType()])
                ?.Invoke(strategy, [controllerType]);
            strategy.GetType().GetMethod("UpdateInputMap", Members, Type.EmptyTypes)
                ?.Invoke(strategy, null);
        }

        var actionSet = steamInputType.GetMethod("GetActionSetHandle", Members, [typeof(string)])
            ?.Invoke(null, ["Controls"]);
        if (actionSet is null)
        {
            return;
        }

        strategy.GetType().GetField("_currentActionSetHandle", Members)?.SetValue(strategy, actionSet);
        steamInputType.GetMethod("ActivateActionSet", Members, [handle.GetType(), actionSet.GetType()])
            ?.Invoke(null, [handle, actionSet]);
    }

    private static void RegisterGeneratedInputEventsFromStrategy(object strategy)
    {
        var events = new List<object?>();
        var inputEvents = strategy.GetType().GetField("_inputEvents", Members)?.GetValue(strategy);
        if (inputEvents is IDictionary dictionary)
        {
            foreach (var value in dictionary.Values)
            {
                events.Add(value);
            }
        }

        events.Add(strategy.GetType().GetField("_joystickXAxis", Members)?.GetValue(strategy));
        events.Add(strategy.GetType().GetField("_joystickYAxis", Members)?.GetValue(strategy));
        RegisterGeneratedInputEvents(events);
    }

    private static void ClearPressedInputs(object strategy)
    {
        if (strategy.GetType().GetField("_pressedInputs", Members)?.GetValue(strategy) is IList pressedInputs)
        {
            pressedInputs.Clear();
        }
    }

    private static void ClearGeneratedInputEvents()
    {
        lock (Lock)
        {
            GeneratedInputEvents.Clear();
            AcceptedUiCompanionInputEvents.Clear();
            PendingUiCompanionActions.Clear();
        }
    }

    private static void PruneExpiredUiCompanionTokens(
        string action,
        Queue<DateTimeOffset> pendingTokens,
        DateTimeOffset now)
    {
        while (pendingTokens.Count > 0 && now - pendingTokens.Peek() > UiCompanionTokenLifetime)
        {
            pendingTokens.Dequeue();
        }

        if (pendingTokens.Count == 0)
        {
            PendingUiCompanionActions.Remove(action);
        }
    }

    private static object? TryDuplicateInputEvent(object inputEvent)
    {
        try
        {
            var duplicateWithSubresources = inputEvent.GetType()
                .GetMethod("Duplicate", Members, [typeof(bool)])
                ?.Invoke(inputEvent, [false]);
            if (duplicateWithSubresources is not null)
            {
                return duplicateWithSubresources;
            }

            var duplicate = inputEvent.GetType()
                .GetMethod("Duplicate", Members, Type.EmptyTypes)
                ?.Invoke(inputEvent, null);
            if (duplicate is not null)
            {
                return duplicate;
            }
        }
        catch (TargetInvocationException)
        {
            return null;
        }

        return null;
    }

    private static bool TrySetActionName(object inputEvent, string actionName)
    {
        var actionProperty = inputEvent.GetType().GetProperty("Action", Members);
        if (actionProperty is null || !actionProperty.CanWrite)
        {
            return false;
        }

        try
        {
            var actionValue = ConvertActionName(actionName, actionProperty.PropertyType);
            if (actionValue is null)
            {
                return false;
            }

            actionProperty.SetValue(inputEvent, actionValue);
            return true;
        }
        catch (Exception exception) when (exception is TargetInvocationException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static object? ConvertActionName(string actionName, Type targetType)
    {
        if (targetType == typeof(string) || targetType.IsAssignableFrom(typeof(string)))
        {
            return actionName;
        }

        var implicitConversion = targetType.GetMethod(
            "op_Implicit",
            BindingFlags.Static | BindingFlags.Public,
            [typeof(string)]);
        if (implicitConversion is not null && implicitConversion.ReturnType == targetType)
        {
            return implicitConversion.Invoke(null, [actionName]);
        }

        var constructor = targetType.GetConstructor([typeof(string)]);
        if (constructor is not null)
        {
            return constructor.Invoke([actionName]);
        }

        return null;
    }

    private static string? GetActionName(object? inputEvent)
    {
        if (inputEvent is null)
        {
            return null;
        }

        try
        {
            return inputEvent.GetType().GetProperty("Action", Members)?.GetValue(inputEvent)?.ToString();
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static bool IsPressedInput(object? inputEvent)
    {
        if (inputEvent is null)
        {
            return false;
        }

        try
        {
            var pressedProperty = inputEvent.GetType().GetProperty("Pressed", Members);
            return pressedProperty is null || pressedProperty.GetValue(inputEvent) is not false;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
    }

    private static string? MapSteamControllerActionToUiCompanion(string? action)
    {
        return action switch
        {
            "controller_d_pad_west" => "ui_left",
            "controller_d_pad_east" => "ui_right",
            "controller_d_pad_north" => "ui_up",
            "controller_d_pad_south" => "ui_down",
            "controller_face_button_south" => "ui_select",
            "controller_face_button_east" => "ui_cancel",
            _ => null
        };
    }

    private static void LogIfChanged(string message, Action<string> log)
    {
        lock (Lock)
        {
            if (message == _lastSelectionSummary)
            {
                return;
            }

            _lastSelectionSummary = message;
        }

        log(message);
    }
}

public sealed record SteamControllerHandleSelection<T>(
    bool Selected,
    int Index,
    T? Handle,
    string Reason);
