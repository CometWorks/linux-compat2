using System.Diagnostics;
using System.Reflection;
using ClientPlugin.Tools;
using HarmonyLib;
using Keen.Game2.Simulation.GameSystems.TrashRemoval;

namespace ClientPlugin.Patches.SystemAbstraction;

// Floating object trash removal stamps every new floating object with
// TimeSpan.FromMilliseconds(Stopwatch.GetTimestamp()) and later removes the oldest ones first.
// Linux timestamps count nanoseconds since boot, so that call overflows TimeSpan once uptime
// passes about 10.7 days, and from then on every floating object spawn throws. Reported, with a
// suggested transpiler, by derrickmehaffy in SpaceGT/Pulsar#74.
[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class FloatingObjectTrashRemovalPatch
{
    private static readonly MethodInfo FromMilliseconds = TranspilerHelpers.FindMethod(
        typeof(TimeSpan),
        nameof(TimeSpan.FromMilliseconds),
        typeof(long),
        typeof(long)
    );

    private static MethodBase TargetMethod() =>
        TranspilerHelpers.FindMethodContaining(
            typeof(FloatingObjectTrashRemovalSessionComponent),
            "g__OnFloatingObjectAdded|"
        );

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    ) =>
        TranspilerHelpers.ReplaceCalls(
            instructions,
            FromMilliseconds,
            TranspilerHelpers.FindMethod(
                typeof(FloatingObjectTrashRemovalPatch),
                nameof(SpawnTime)
            ),
            1,
            "TimeSpan.FromMilliseconds call in OnFloatingObjectAdded"
        );

    /// <summary>Replaces <c>TimeSpan.FromMilliseconds(timestamp, 0)</c>, so it keeps that
    /// signature. Converts the timestamp properly: the time since boot cannot overflow.</summary>
    public static TimeSpan SpawnTime(long timestamp, long microseconds) =>
        Stopwatch.GetElapsedTime(0, timestamp);
}
