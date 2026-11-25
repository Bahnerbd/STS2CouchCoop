using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// Run in a separate process: the unit-test assembly contains stand-in game types,
// and Harmony must patch the actual installed assemblies for this check.
if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: LocalCoop.CompatibilityCheck <game-root> <LocalCoop.dll> [report-path]");
    return 2;
}

var lines = new List<string>();
void Log(string message)
{
    lines.Add(message);
    Console.WriteLine(message);
}

try
{
    var root = Path.GetFullPath(args[0]);
    var modPath = Path.GetFullPath(args[1]);
    var gameDirectory = Path.Combine(root, "data_sts2_windows_x86_64");
    var gamePath = Path.Combine(gameDirectory, "sts2.dll");
    using var release = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "release_info.json")));
    Log($"STS2: {release.RootElement.GetProperty("version").GetString()}");
    Log($"Commit: {release.RootElement.GetProperty("commit").GetString()}");
    Log($"sts2.dll SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gamePath)))}");
    Log($"LocalCoop.dll SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(modPath)))}");

    AssemblyLoadContext.Default.Resolving += (_, name) =>
    {
        foreach (var directory in new[] { Path.GetDirectoryName(modPath)!, gameDirectory })
        {
            var candidate = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
        }
        return null;
    };

    // Godot is already loaded when the game loads mods; reproduce that order.
    AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameDirectory, "GodotSharp.dll"));
    var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(gamePath);
    var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(modPath);
    var installer = mod.GetType("LocalCoop.Mod.Runtime.LocalCoopPatchInstaller", throwOnError: true)!;
    var patches = ((IEnumerable<Type>)installer.GetMethod("PatchTypesFor")!.Invoke(null, [true])!).ToArray();
    var install = installer.GetMethods().Single(method => method.Name == "Install" && method.GetParameters().Length == 4);

    // v0.113.1 baseline. Checking each class's target count catches partial loss
    // in selectors that silently skip missing types or methods after an update.
    var expectedCounts = new Dictionary<string, int>
    {
        ["BrokerClientJoinFlowPatch"] = 1,
        ["BrokerJoinFriendScreenPatch"] = 1,
        ["BrokerHostSteamStartupBypassPatch"] = 1,
        ["BrokerHostENetStartupBypassPatch"] = 1,
        ["BrokerLocalPlayerIdPatch"] = 1,
        ["BrokerLobbyServiceSubstitutionPatch"] = 16,
        ["BrokerPlayerDisplayNamePlatformPatch"] = 1,
        ["BrokerPlayerDisplayNameNameplatePatch"] = 3,
        ["BaseLibHealthBarForecastCompatibilityPatch"] = 3,
        ["SteamControllerInputSelectionPatches"] = 1,
        ["DynamicControllerStartupPatches"] = 1,
        ["SteamControllerInputFramePatches"] = 1,
        ["ControllerInputBackgroundPollingPatches"] = 1,
        ["DynamicControllerInputFocusScopePatch"] = 3,
        ["DynamicControllerFocusedWindowPatch"] = 1,
        ["SteamControllerInputRuntimeDiagnosticsPatches"] = 7,
        ["ControllerInputOwnershipPatches"] = 5,
        ["RunIdentityLifecycleDiagnosticsPatches"] = 2,
        ["MerchantCardRemovalDiagnosticsPatches"] = 1,
        ["OneOffSynchronizerLocalPlayerDiagnosticsPatch"] = 1,
        ["CardSelectCmdDiagnosticsPatches"] = 1,
        ["CardSelectCmdShouldSelectLocalDiagnosticsPatch"] = 1,
        ["LocalContextIsMeDiagnosticsPatch"] = 1,
        ["PlayerChoiceSynchronizerDiagnosticsPatches"] = 5,
        ["RewardDiagnosticsPatches"] = 2,
        ["RewardStateDiagnosticsPatches"] = 20,
        ["PotionProcurementDiagnosticsPatches"] = 1,
        ["RunIdentityLocalUiDiagnosticsPatches"] = 12,
        ["PeerInputOwnershipDiagnosticsPatches"] = 5
    };
    var failures = 0;
    var targetCount = 0;
    foreach (var patch in patches)
    {
        try
        {
            var selector = patch.GetMethod("TargetMethods") ?? patch.GetMethod("TargetMethod")
                ?? throw new InvalidOperationException("No target selector");
            var selected = selector.Invoke(null, null);
            var targets = selected is IEnumerable<MethodBase> many ? many.ToArray()
                : selected is MethodBase one ? new[] { one } : [];
            Log($"{patch.Name}: {string.Join(", ", targets.Select(m => m.DeclaringType?.Name + "." + m.Name))}");
            if (!expectedCounts.Remove(patch.Name, out var expected))
                throw new InvalidOperationException("No compatibility baseline; audit the new patch's native targets");
            if (targets.Length != expected || targets.Any(target => target.DeclaringType?.Assembly != game))
                throw new InvalidOperationException($"Expected {expected} native targets; resolved {targets.Length}");
            var result = install.Invoke(null, [mod, new[] { patch }, (Action<string>)Log, null])!;
            if (!(bool)result.GetType().GetProperty("Success")!.GetValue(result)!)
                throw new InvalidOperationException("Harmony could not install this patch");
            targetCount += targets.Length;
        }
        catch (Exception exception)
        {
            failures++;
            Log($"FAIL {patch.Name}: {exception.GetBaseException().Message}");
        }
    }
    foreach (var missing in expectedCounts.Keys)
    {
        failures++;
        Log($"FAIL expected patch absent: {missing}");
    }
    Log($"{(failures == 0 ? "PASS" : "FAIL")}: {patches.Length} patch classes, {targetCount} native target bindings, {failures} failures.");
    Log("Scope: native target resolution and Harmony installation only. In-game multiplayer and controller playtesting are still required.");
    if (args.Length == 3)
    {
        var reportPath = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllLines(reportPath, lines);
    }
    return failures == 0 ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL: {exception.GetBaseException().Message}");
    return 1;
}
