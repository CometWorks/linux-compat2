using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Keen.VRage.Core;
using Keen.VRage.Core.EngineComponents;
using Keen.VRage.Platform.Windows;
using Keen.VRage.Platform.Windows.EngineComponents;
using Keen.VRage.Render.CoreConfigurations;
using Keen.VRage.Render.EngineComponents;
using WindowsEngineBuilderExtensions = Keen.VRage.Platform.Windows.Extensions.EngineBuilderExtensions;

namespace LinuxCompat.Patches.Install;

/// <summary>
/// Harmony patches against the shipped SpaceEngineers2.dll: platform factory selection in
/// Program.Main, the WinForms install path report, Linux platform component registration in
/// GameApp.CreateEngine, render configuration in GameApp.AddRender12, and the CPU-rendering
/// pin wait budget in GameAppComponent.
/// </summary>
internal static class GameExePatches
{
    public static void Install(Harmony harmony)
    {
        Assembly game = InstallTools.LoadAssembly("SpaceEngineers2");
        Type program = InstallTools.FindType(game, "Keen.Game2.Program");
        Type gameApp = InstallTools.FindType(game, "Keen.Game2.GameApp");
        Type gameAppComponent = InstallTools.FindType(game, "Keen.Game2.GameAppComponent");

        GameState.CheckInstallPathLength = InstallTools.FindMethod(program, "CheckInstallPathLength", typeof(string[]));
        harmony.Patch(InstallTools.FindMethod(program, "Main", typeof(string[])),
            transpiler: InstallTools.Declared(typeof(GameExePatches), nameof(MainTranspiler)));
        harmony.Patch(InstallTools.FindMethod(gameApp, "CreateEngine", typeof(string[])),
            transpiler: InstallTools.Declared(typeof(GameExePatches), nameof(CreateEngineTranspiler)));
        harmony.Patch(InstallTools.FindMethod(gameApp, "AddRender12", typeof(EngineBuilder)),
            transpiler: InstallTools.Declared(typeof(GameExePatches), nameof(AddRender12Transpiler)));
        harmony.Patch(InstallTools.FindMethod(gameAppComponent, "WaitForPinning", typeof(TimeSpan), typeof(string)),
            prefix: InstallTools.Declared(typeof(GameExePatches), nameof(WaitForPinningPrefix)));
    }

    private static class GameState
    {
        public static MethodInfo CheckInstallPathLength = null!;
    }

    /// <summary>
    /// Replaces the single <c>newobj VRageWindows()</c> in Program.Main with the platform
    /// factory selector, moves the stored value into a fresh IPlatformFactory-typed local
    /// because the original local is typed as the concrete VRageWindows class, and redirects
    /// the install path check away from its WinForms-reporting original.
    /// </summary>
    private static IEnumerable<CodeInstruction> MainTranspiler(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> result = InstallTools.ReplaceCalls(
            instructions, GameState.CheckInstallPathLength,
            InstallTools.FindMethod(typeof(ProgramPatch), nameof(ProgramPatch.CheckInstallPath)),
            1, "CheckInstallPathLength call in Program.Main");
        ConstructorInfo windowsCtor = AccessTools.Constructor(typeof(VRageWindows), Type.EmptyTypes)
            ?? throw new MissingMethodException(typeof(VRageWindows).FullName, ".ctor");
        MethodInfo selector = InstallTools.FindMethod(typeof(ProgramPatch), nameof(ProgramPatch.SelectPlatformFactory));
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

        InstallTools.AssertCount(constructions, 1, "VRageWindows construction in Program.Main");
        InstallTools.AssertCount(stores, 1, "platform factory store in Program.Main");
        InstallTools.AssertCount(loads, 2, "platform factory loads in Program.Main");
        return result;
    }

    private static CodeInstruction ReplaceLocalAccess(
        CodeInstruction instruction, OpCode opcode, LocalBuilder local)
    {
        CodeInstruction replacement = new(opcode, local);
        replacement.labels.AddRange(instruction.labels);
        replacement.blocks.AddRange(instruction.blocks);
        return replacement;
    }

    /// <summary>
    /// Replaces the single Windows platform registration call in GameApp.CreateEngine with
    /// the Linux one.
    /// </summary>
    private static IEnumerable<CodeInstruction> CreateEngineTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo addPlatform = InstallTools.FindMethod(
            typeof(WindowsEngineBuilderExtensions), "AddPlatform",
            typeof(EngineBuilder), typeof(MemoryObjectBuilder), typeof(PlatformObjectBuilder), typeof(InputObjectBuilder));
        MethodInfo adapter = InstallTools.FindMethod(typeof(GameExePatches), nameof(AddPlatformAdapter));
        return InstallTools.ReplaceCalls(instructions, addPlatform, adapter, 1, "AddPlatform call in CreateEngine");
    }

    /// <summary>
    /// The signature mirrors the Windows AddPlatform call this replaces, so the unused
    /// builders keep the transpiled call site's argument list intact.
    /// </summary>
    public static EngineBuilder AddPlatformAdapter(
        EngineBuilder builder,
        MemoryObjectBuilder? memoryObjectBuilder,
        PlatformObjectBuilder platformObjectBuilder,
        InputObjectBuilder? inputObjectBuilder)
    {
        GameAppPatch.AddPlatform(builder, platformObjectBuilder);
        return builder;
    }

    /// <summary>
    /// Inserts the Linux render configuration call immediately before the final
    /// <c>engine.AddRender12(renderObjectBuilder)</c> registration.
    /// </summary>
    private static IEnumerable<CodeInstruction> AddRender12Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        Type extensions = InstallTools.FindType(
            InstallTools.LoadAssembly("VRage.Render12"), "Keen.VRage.Render12.Extensions.EngineBuilderExtensions");
        MethodInfo addRender12 = InstallTools.FindMethod(
            extensions, "AddRender12", typeof(EngineBuilder), typeof(RenderObjectBuilder));
        MethodInfo configure = InstallTools.FindMethod(typeof(GameExePatches), nameof(ConfigureRenderAdapter));

        List<CodeInstruction> result = [.. instructions];
        int count = 0;
        for (int i = 0; i < result.Count; i++)
        {
            if (!InstallTools.CallsMethod(result[i], addRender12))
                continue;
            if (result[i - 2].opcode != OpCodes.Ldarg_1 || result[i - 1].opcode != OpCodes.Ldloc_0)
                throw new InvalidOperationException(
                    "[LinuxCompat] GameApp.AddRender12 argument load anchor not found.");

            // The call site is `ldarg.1; ldloc.0; call AddRender12`; duplicate the two
            // argument loads for the configuration call inserted in front of it.
            result.InsertRange(i, [
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldloc_0),
                new CodeInstruction(OpCodes.Call, configure),
            ]);
            i += 3;
            count++;
        }
        InstallTools.AssertCount(count, 1, "AddRender12 call in GameApp.AddRender12");
        return result;
    }

    public static void ConfigureRenderAdapter(EngineBuilder engine, RenderObjectBuilder render) =>
        GameAppPatch.ConfigureRender(engine.Configure<RenderConfigurationObjectBuilder>(), render);

    private static void WaitForPinningPrefix(ref TimeSpan __0) => GameAppComponentPatch.Prefix(ref __0);
}
