using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Tools;
using HarmonyLib;
using Keen.Game2;
using Keen.Game2.Client.RuntimeSystems.CoreScenes;
using Keen.VRage.Core;
using Keen.VRage.Core.EngineComponents;
using Keen.VRage.Platform.Windows;
using Keen.VRage.Platform.Windows.EngineComponents;
using Keen.VRage.Render.CoreConfigurations;
using Keen.VRage.Render.EngineComponents;
using RenderEngineBuilderExtensions = Keen.VRage.Render12.Extensions.EngineBuilderExtensions;
using WindowsEngineBuilderExtensions = Keen.VRage.Platform.Windows.Extensions.EngineBuilderExtensions;

namespace LinuxCompat.Patches.PlatformGuards;

[HarmonyPatch(typeof(Program), nameof(Program.Main), typeof(string[]))]
[HarmonyPatchCategory("Finish")]
internal static class ProgramMainPatch
{
    private static readonly MethodInfo CheckInstallPathLength = TranspilerHelpers.FindMethod(
        typeof(Program),
        nameof(Program.CheckInstallPathLength),
        typeof(string[])
    );

    /// <summary>
    /// Replaces the single <c>newobj VRageWindows()</c> in Program.Main with the platform
    /// factory selector, moves the stored value into a fresh IPlatformFactory-typed local
    /// because the original local is typed as the concrete VRageWindows class, and redirects
    /// the install path check away from its WinForms-reporting original.
    /// </summary>
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator
    )
    {
        List<CodeInstruction> result = TranspilerHelpers.ReplaceCalls(
            instructions,
            CheckInstallPathLength,
            TranspilerHelpers.FindMethod(
                typeof(ProgramPatch),
                nameof(ProgramPatch.CheckInstallPath)
            ),
            1,
            "CheckInstallPathLength call in Program.Main"
        );
        ConstructorInfo windowsCtor =
            AccessTools.Constructor(typeof(VRageWindows), Type.EmptyTypes)
            ?? throw new MissingMethodException(typeof(VRageWindows).FullName, ".ctor");
        MethodInfo selector = TranspilerHelpers.FindMethod(
            typeof(ProgramPatch),
            nameof(ProgramPatch.SelectPlatformFactory)
        );
        LocalBuilder factoryLocal = generator.DeclareLocal(typeof(IPlatformFactory));

        int constructions = 0;
        int stores = 0;
        int loads = 0;
        for (int i = 0; i < result.Count; i++)
        {
            CodeInstruction instruction = result[i];
            if (instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, windowsCtor))
            {
                CodeInstruction replacement = new(OpCodes.Call, selector);
                replacement.labels.AddRange(instruction.labels);
                replacement.blocks.AddRange(instruction.blocks);
                result[i] = replacement;
                constructions++;
            }
            else if (instruction.opcode == OpCodes.Stloc_1)
            {
                result[i] = ReplaceLocalAccess(instruction, OpCodes.Stloc, factoryLocal);
                stores++;
            }
            else if (instruction.opcode == OpCodes.Ldloc_1)
            {
                result[i] = ReplaceLocalAccess(instruction, OpCodes.Ldloc, factoryLocal);
                loads++;
            }
        }

        TranspilerHelpers.AssertCount(
            constructions,
            1,
            "VRageWindows construction in Program.Main"
        );
        TranspilerHelpers.AssertCount(stores, 1, "platform factory store in Program.Main");
        TranspilerHelpers.AssertCount(loads, 2, "platform factory loads in Program.Main");
        return result;
    }

    private static CodeInstruction ReplaceLocalAccess(
        CodeInstruction instruction,
        OpCode opcode,
        LocalBuilder local
    )
    {
        CodeInstruction replacement = new(opcode, local);
        replacement.labels.AddRange(instruction.labels);
        replacement.blocks.AddRange(instruction.blocks);
        return replacement;
    }
}

[HarmonyPatch(typeof(GameApp), nameof(GameApp.CreateEngine), typeof(string[]))]
[HarmonyPatchCategory("Finish")]
internal static class GameAppCreateEnginePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        MethodInfo addPlatform = TranspilerHelpers.FindMethod(
            typeof(WindowsEngineBuilderExtensions),
            "AddPlatform",
            typeof(EngineBuilder),
            typeof(MemoryObjectBuilder),
            typeof(PlatformObjectBuilder),
            typeof(InputObjectBuilder)
        );
        MethodInfo adapter = TranspilerHelpers.FindMethod(
            typeof(GameAppCreateEnginePatch),
            nameof(AddPlatformAdapter)
        );
        return TranspilerHelpers.ReplaceCalls(
            instructions,
            addPlatform,
            adapter,
            1,
            "AddPlatform call in CreateEngine"
        );
    }

    /// <summary>
    /// The signature mirrors the Windows AddPlatform call this replaces, so the unused
    /// builders keep the transpiled call site's argument list intact.
    /// </summary>
    public static EngineBuilder AddPlatformAdapter(
        EngineBuilder builder,
        MemoryObjectBuilder? memoryObjectBuilder,
        PlatformObjectBuilder platformObjectBuilder,
        InputObjectBuilder? inputObjectBuilder
    )
    {
        GameAppPatch.AddPlatform(builder, platformObjectBuilder);
        return builder;
    }
}

[HarmonyPatch(typeof(GameApp), nameof(GameApp.AddRender12), typeof(EngineBuilder))]
[HarmonyPatchCategory("Finish")]
internal static class GameAppAddRender12Patch
{
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        MethodInfo addRender12 = TranspilerHelpers.FindMethod(
            typeof(RenderEngineBuilderExtensions),
            "AddRender12",
            typeof(EngineBuilder),
            typeof(RenderObjectBuilder)
        );
        MethodInfo configure = TranspilerHelpers.FindMethod(
            typeof(GameAppAddRender12Patch),
            nameof(ConfigureRenderAdapter)
        );

        List<CodeInstruction> result = [.. instructions];
        int count = 0;
        for (int i = 0; i < result.Count; i++)
        {
            if (!TranspilerHelpers.CallsMethod(result[i], addRender12))
                continue;
            if (result[i - 2].opcode != OpCodes.Ldarg_1 || result[i - 1].opcode != OpCodes.Ldloc_0)
                throw new InvalidOperationException(
                    "[LinuxCompat] GameApp.AddRender12 argument load anchor not found."
                );

            // The call site is `ldarg.1; ldloc.0; call AddRender12`; duplicate the two
            // argument loads for the configuration call inserted in front of it.
            result.InsertRange(
                i,
                [
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Ldloc_0),
                    new CodeInstruction(OpCodes.Call, configure),
                ]
            );
            i += 3;
            count++;
        }
        TranspilerHelpers.AssertCount(count, 1, "AddRender12 call in GameApp.AddRender12");
        return result;
    }

    public static void ConfigureRenderAdapter(EngineBuilder engine, RenderObjectBuilder render) =>
        GameAppPatch.ConfigureRender(engine.Configure<RenderConfigurationObjectBuilder>(), render);
}

[HarmonyPatch(
    typeof(GameAppComponent),
    nameof(GameAppComponent.WaitForPinning),
    typeof(TimeSpan),
    typeof(string)
)]
[HarmonyPatchCategory("Finish")]
internal static class GameAppWaitForPinningPatch
{
    private static bool Prefix(ref TimeSpan __0, ref Keen.VRage.Library.Threading.Task __result) =>
        GameAppComponentPatch.Prefix(ref __0, ref __result);
}

[HarmonyPatch(typeof(GameRenderComponent), nameof(GameRenderComponent.EndOfUiLoading))]
[HarmonyPatchCategory("Finish")]
internal static class GameRenderEndOfUiLoadingPatch
{
    private static bool Prefix(ref Keen.VRage.Library.Threading.Task __result) =>
        GameAppComponentPatch.EndOfUiLoadingPrefix(ref __result);
}
