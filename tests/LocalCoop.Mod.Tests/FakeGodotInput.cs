namespace Godot;

public static class Input
{
    public static List<object> Parsed { get; } = [];

    public static void ParseInputEvent(LocalCoop.Mod.Tests.LocalCoopInputRouterTests.ParsedInputEvent inputEvent)
    {
        Parsed.Add(inputEvent);
    }
}
