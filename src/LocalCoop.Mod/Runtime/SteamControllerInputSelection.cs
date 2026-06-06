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
    private static readonly HashSet<object> AcceptedOriginalSteamControllerInputEvents = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<string, Queue<DateTimeOffset>> PendingUiCompanionActions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Queue<DateTimeOffset>> PendingOriginalSteamControllerInputs = new(StringComparer.Ordinal);
    private static readonly TimeSpan UiCompanionTokenLifetime = TimeSpan.FromMilliseconds(250);
    private static string? _lastSelectionSummary;
    private static int? _selectedControllerDevice;
    private static int? _knownControllerDevice;
    private static object? _knownControllerHandle;

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

    public static SteamControllerHandleSelection<T> ChooseControllerHandle<T>(
        IReadOnlyList<T> handles,
        BrokerControllerDeviceAssignment assignment,
        T? knownControllerHandle)
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
        if (knownControllerHandle is null)
        {
            return ChooseControllerHandle(handles, assignment);
        }

        foreach (var handle in handles)
        {
            if (EqualityComparer<T>.Default.Equals(handle, knownControllerHandle))
            {
                return new SteamControllerHandleSelection<T>(
                    Selected: true,
                    Index: index,
                    Handle: handle,
                    Reason: $"retained previous selected Steam controller handle for controllerDevice={index}");
            }
        }

        return new SteamControllerHandleSelection<T>(
            Selected: false,
            Index: index,
            Handle: default,
            Reason: $"previous selected Steam controller handle is disconnected for controllerDevice={index}; refusing ordinal fallback");
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

    public static bool IsSelectedControllerActive(BrokerControllerDeviceAssignment assignment)
    {
        if (!assignment.IsConfigured || assignment.Device is not > 0)
        {
            return false;
        }

        lock (Lock)
        {
            return _selectedControllerDevice == assignment.Device.Value;
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

    public static bool CanTrustOriginalSteamControllerInput(object? inputEvent)
    {
        if (inputEvent is null || CanMapUiCompanionAction(inputEvent))
        {
            return false;
        }

        var action = GetActionName(inputEvent);
        if (action is not null)
        {
            return action.StartsWith("controller_", StringComparison.Ordinal);
        }

        var typeName = inputEvent.GetType().FullName ?? inputEvent.GetType().Name;
        return typeName.Contains("JoypadMotion", StringComparison.OrdinalIgnoreCase);
    }

    public static void RegisterGeneratedOriginalSteamControllerInput(object? inputEvent, DateTimeOffset? now = null)
    {
        var key = GetOriginalSteamControllerInputKey(inputEvent);
        if (key is null)
        {
            return;
        }

        lock (Lock)
        {
            if (!PendingOriginalSteamControllerInputs.TryGetValue(key, out var pendingTokens))
            {
                pendingTokens = new Queue<DateTimeOffset>();
                PendingOriginalSteamControllerInputs[key] = pendingTokens;
            }

            pendingTokens.Enqueue(now ?? DateTimeOffset.UtcNow);
        }
    }

    public static bool TryConsumeGeneratedOriginalSteamControllerInput(object? inputEvent, DateTimeOffset? now = null)
    {
        if (inputEvent is null)
        {
            return false;
        }

        var key = GetOriginalSteamControllerInputKey(inputEvent);
        if (key is null)
        {
            return false;
        }

        lock (Lock)
        {
            if (AcceptedOriginalSteamControllerInputEvents.Contains(inputEvent))
            {
                return true;
            }

            var currentTime = now ?? DateTimeOffset.UtcNow;
            if (!PendingOriginalSteamControllerInputs.TryGetValue(key, out var pendingTokens))
            {
                return false;
            }

            PruneExpiredOriginalSteamControllerInputTokens(key, pendingTokens, currentTime);
            if (pendingTokens.Count == 0)
            {
                return false;
            }

            pendingTokens.Dequeue();
            if (pendingTokens.Count == 0)
            {
                PendingOriginalSteamControllerInputs.Remove(key);
            }

            AcceptedOriginalSteamControllerInputEvents.Add(inputEvent);
            return true;
        }
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
            AcceptedOriginalSteamControllerInputEvents.Clear();
            PendingUiCompanionActions.Clear();
            PendingOriginalSteamControllerInputs.Clear();
            _lastSelectionSummary = null;
            _selectedControllerDevice = null;
            _knownControllerDevice = null;
            _knownControllerHandle = null;
        }
    }

    public static void SetSelectedControllerDeviceForTesting(int? controllerDevice)
    {
        lock (Lock)
        {
            _selectedControllerDevice = controllerDevice;
        }
    }

    public static bool IsSelectionAlreadyAppliedForTesting(
        BrokerControllerDeviceAssignment assignment,
        object? currentHandle,
        object? selectedHandle)
    {
        return IsSelectionAlreadyApplied(assignment, currentHandle, selectedHandle);
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
        if (!assignment.IsConfigured)
        {
            ClearSelectedControllerIdentity();
            return;
        }

        try
        {
            if (assignment.Device is null)
            {
                ClearGeneratedInputEvents();
                ClearSelectedControllerIdentity();
                ClearCurrentControllerHandle(strategy);
                ClearPressedInputs(strategy);
                return;
            }

            var handles = GetConnectedControllerHandles(strategy);
            var selection = ChooseControllerHandle(handles, assignment, GetKnownControllerHandle(assignment));
            if (!selection.Selected || selection.Handle is null)
            {
                ClearGeneratedInputEvents();
                ClearSelectedControllerDevice();
                ClearCurrentControllerHandle(strategy);
                ClearPressedInputs(strategy);
                LogIfChanged(
                    $"Steam controller selection: unavailable controllerDevice={selection.Index} connected={handles.Count} reason={selection.Reason}.",
                    log);
                return;
            }

            var previousHandle = GetCurrentControllerHandle(strategy);
            if (IsSelectionAlreadyApplied(assignment, previousHandle, selection.Handle))
            {
                return;
            }

            SetCurrentControllerHandle(strategy, selection.Handle);
            RefreshControllerConfig(strategy, selection.Handle);
            RegisterGeneratedInputEventsFromStrategy(strategy);

            if (!Equals(previousHandle, selection.Handle))
            {
                ClearPressedInputs(strategy);
            }

            SetSelectedControllerDevice(selection.Index, selection.Handle);
            LogIfChanged(
                $"Steam controller selection: selected controllerDevice={selection.Index} handle={selection.Handle} connected={handles.Count}.",
                log);
        }
        catch (Exception exception) when (exception is TargetInvocationException or MissingMemberException or InvalidOperationException or ArgumentException)
        {
            ClearGeneratedInputEvents();
            ClearSelectedControllerDevice();
            LogIfChanged(
                $"Steam controller selection failed: {exception.GetType().Name}: {exception.Message}",
                log);
        }
    }

    private static bool IsSelectionAlreadyApplied(
        BrokerControllerDeviceAssignment assignment,
        object? currentHandle,
        object? selectedHandle)
    {
        if (!assignment.IsConfigured
            || assignment.Device is null
            || selectedHandle is null
            || !Equals(currentHandle, selectedHandle))
        {
            return false;
        }

        lock (Lock)
        {
            return _selectedControllerDevice == assignment.Device.Value;
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

    private static void ClearCurrentControllerHandle(object strategy)
    {
        var field = strategy.GetType().GetField("_currentControllerHandle", Members)
            ?? throw new MissingMemberException(strategy.GetType().FullName, "_currentControllerHandle");
        field.SetValue(strategy, null);
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
            AcceptedOriginalSteamControllerInputEvents.Clear();
            PendingUiCompanionActions.Clear();
            PendingOriginalSteamControllerInputs.Clear();
        }
    }

    private static object? GetKnownControllerHandle(BrokerControllerDeviceAssignment assignment)
    {
        if (!assignment.IsConfigured || assignment.Device is null)
        {
            return null;
        }

        lock (Lock)
        {
            return _knownControllerDevice == assignment.Device.Value
                ? _knownControllerHandle
                : null;
        }
    }

    private static void SetSelectedControllerDevice(int controllerDevice, object handle)
    {
        lock (Lock)
        {
            _selectedControllerDevice = controllerDevice;
            _knownControllerDevice = controllerDevice;
            _knownControllerHandle = handle;
        }
    }

    private static void ClearSelectedControllerDevice()
    {
        lock (Lock)
        {
            _selectedControllerDevice = null;
        }
    }

    private static void ClearSelectedControllerIdentity()
    {
        lock (Lock)
        {
            _selectedControllerDevice = null;
            _knownControllerDevice = null;
            _knownControllerHandle = null;
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

    private static void PruneExpiredOriginalSteamControllerInputTokens(
        string key,
        Queue<DateTimeOffset> pendingTokens,
        DateTimeOffset now)
    {
        while (pendingTokens.Count > 0 && now - pendingTokens.Peek() > UiCompanionTokenLifetime)
        {
            pendingTokens.Dequeue();
        }

        if (pendingTokens.Count == 0)
        {
            PendingOriginalSteamControllerInputs.Remove(key);
        }
    }

    private static string? GetOriginalSteamControllerInputKey(object? inputEvent)
    {
        if (inputEvent is null || !CanTrustOriginalSteamControllerInput(inputEvent))
        {
            return null;
        }

        var typeName = inputEvent.GetType().Name;
        var device = GetPropertyValue(inputEvent, "Device")?.ToString() ?? "<none>";
        var action = GetActionName(inputEvent);
        if (action is not null)
        {
            var pressed = GetPropertyValue(inputEvent, "Pressed")?.ToString() ?? "<none>";
            return $"{typeName}|device={device}|action={action}|pressed={pressed}";
        }

        var axis = GetPropertyValue(inputEvent, "Axis")?.ToString() ?? "<none>";
        return $"{typeName}|device={device}|axis={axis}";
    }

    private static object? GetPropertyValue(object? source, string propertyName)
    {
        if (source is null)
        {
            return null;
        }

        try
        {
            return source.GetType().GetProperty(propertyName, Members)?.GetValue(source);
        }
        catch (TargetInvocationException)
        {
            return null;
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
