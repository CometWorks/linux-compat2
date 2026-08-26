using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Keen.VRage.Core.Render;
using Keen.VRage.Library.Mathematics;
using Vortice.Direct3D12;

namespace LinuxCompat.Patches.Install;

/// <summary>
/// Harmony patches against the shipped VRage.Render12.dll. Each patch follows the migration
/// entry documented in the LinuxCompat patch ledger; anchors are asserted so a game update
/// that moves a target fails installation instead of silently dropping a fix.
/// </summary>
internal static class Render12Patches
{
    public static void Install(Harmony harmony)
    {
        Assembly render12 = InstallTools.LoadAssembly("VRage.Render12");

        InstallSwapChain(harmony, render12);
        InstallScreenBuffers(harmony, render12);
        InstallDataUploader(harmony, render12);
        InstallAdapters(harmony, render12);
        InstallD3D12AbiAdapters(harmony, render12);
        InstallFenceWaits(harmony, render12);
        InstallFramePacer(harmony, render12);
        InstallScreenshotsManager(harmony, render12);
        InstallUiResolution(harmony, render12);
        InstallOsDetails(harmony, render12);
    }

    private static void InstallSwapChain(Harmony harmony, Assembly render12)
    {
        Type swapChain = InstallTools.FindType(render12, "Keen.VRage.Render12.Core.Device.SwapChain");
        SwapChainState.Windows = InstallTools.FindField(swapChain, "_windows");
        SwapChainState.CurrentDisplaySettings = InstallTools.FindField(swapChain, "_currentDisplaySettings");
        SwapChainState.RequestedDisplaySettings = InstallTools.FindField(swapChain, "_requestedDisplaySettings");
        SwapChainState.Update = InstallTools.FindMethod(swapChain, "Update");

        MethodInfo createD3DSwapChain = swapChain
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "CreateD3DSwapChain"
                && method.GetParameters() is { Length: 3 } parameters
                && parameters[0].ParameterType == typeof(RenderDisplaySettings).MakeByRefType()
                && parameters[1].ParameterType == typeof(bool)
                && parameters[2].ParameterType == typeof(nint));
        harmony.Patch(createD3DSwapChain,
            prefix: InstallTools.Declared(typeof(SwapChainPatch), nameof(SwapChainPatch.Prefix)));

        // SwapChain.Update contains an exception filter, which Harmony 2.4.2 cannot
        // round-trip, so the resize consumption runs from its single (filter-free) caller,
        // the render component's per-frame DrawInternal local function.
        Type engineComponent = InstallTools.FindType(
            render12, "Keen.VRage.Render12.EngineComponents.Render12EngineComponent");
        harmony.Patch(InstallTools.FindMethodContaining(engineComponent, "g__DrawInternal|"),
            transpiler: InstallTools.Declared(typeof(Render12Patches), nameof(DrawInternalTranspiler)));
    }

    private static class SwapChainState
    {
        public static FieldInfo Windows = null!;
        public static FieldInfo CurrentDisplaySettings = null!;
        public static FieldInfo RequestedDisplaySettings = null!;
        public static MethodInfo Update = null!;
    }

    /// <summary>Inserts the pending-resize consumption immediately before the per-frame
    /// <c>CoreSystems.SwapChain.Update()</c> call.</summary>
    private static IEnumerable<CodeInstruction> DrawInternalTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo adapter = InstallTools.FindMethod(typeof(Render12Patches), nameof(ConsumePendingSwapChainResize));
        int count = 0;
        for (int i = 0; i < result.Count; i++)
        {
            if (!InstallTools.CallsMethod(result[i], SwapChainState.Update))
                continue;
            result.InsertRange(i, [
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Call, adapter),
            ]);
            i += 2;
            count++;
        }
        InstallTools.AssertCount(count, 1, "SwapChain.Update call in DrawInternal");
        return result;
    }

    public static void ConsumePendingSwapChainResize(object swapChain)
    {
        var windows = (Keen.VRage.Core.Platform.IPlatformWindows?)SwapChainState.Windows.GetValue(swapChain);
        if (windows == null)
            return;
        var current = (RenderDisplaySettings)SwapChainState.CurrentDisplaySettings.GetValue(swapChain)!;
        var requested = (RenderDisplaySettings?)SwapChainState.RequestedDisplaySettings.GetValue(swapChain);
        RenderDisplaySettings? updated = requested;
        SwapChainPatch.UpdatePrefix(windows, current, ref updated);
        if (!Nullable.Equals(updated, requested))
            SwapChainState.RequestedDisplaySettings.SetValue(swapChain, updated);
    }

    private static void InstallScreenBuffers(Harmony harmony, Assembly render12)
    {
        Type screenBuffers = InstallTools.FindType(render12, "Keen.VRage.Render12.Core.Systems.ScreenBuffers");
        ScreenBuffersState.UsedMaxResolution = InstallTools.FindField(screenBuffers, "_usedMaxResolution");
        ScreenBuffersState.FinalLdrGetter = InstallTools.FindMethod(screenBuffers, "get_FinalLDRTexture");
        ScreenBuffersState.SwapChainField = InstallTools.FindField(
            InstallTools.FindType(render12, "Keen.VRage.Render12.Core.CoreSystems"), "SwapChain");
        Type swapChain = InstallTools.FindType(render12, "Keen.VRage.Render12.Core.Device.SwapChain");
        ScreenBuffersState.SwapResolutionGetter = InstallTools.FindMethod(swapChain, "get_Resolution");
        ScreenBuffersState.TextureResolutionGetter = InstallTools.FindMethod(
            ScreenBuffersState.FinalLdrGetter.ReturnType, "get_Resolution");

        MethodInfo update = screenBuffers
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.Name == "Update" && method.GetParameters().Length == 3);
        harmony.Patch(update, transpiler: InstallTools.Declared(typeof(Render12Patches), nameof(ScreenBuffersTranspiler)));
    }

    /// <summary>
    /// Extends the buffer invalidation predicate with a FinalLDRTexture-vs-swapchain
    /// resolution comparison, correcting Keen's stale final LDR texture after a resize
    /// that leaves the DRS-scaled maximum unchanged.
    /// </summary>
    private static IEnumerable<CodeInstruction> ScreenBuffersTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo combine = InstallTools.FindMethod(typeof(Render12Patches), nameof(CombineBufferInvalidation));
        int index = -1;
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].opcode == OpCodes.Ldfld && Equals(result[i].operand, ScreenBuffersState.UsedMaxResolution))
            {
                InstallTools.AssertCount(index == -1 ? 0 : 2, 0, "_usedMaxResolution loads in ScreenBuffers.Update");
                index = i;
            }
        }
        if (index < 0 || !InstallTools.CallsMethod(result[index + 1],
                AccessTools.Method(typeof(Vector2I), "op_Inequality")!))
            throw new InvalidOperationException(
                "[LinuxCompat] ScreenBuffers.Update invalidation predicate anchor not found.");

        result.InsertRange(index + 2, [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call, combine),
        ]);
        return result;
    }

    private static class ScreenBuffersState
    {
        public static FieldInfo UsedMaxResolution = null!;
        public static MethodInfo FinalLdrGetter = null!;
        public static MethodInfo TextureResolutionGetter = null!;
        public static FieldInfo SwapChainField = null!;
        public static MethodInfo SwapResolutionGetter = null!;
    }

    public static bool CombineBufferInvalidation(bool resized, object screenBuffers)
    {
        if (resized)
            return true;

        object? finalLdr = ScreenBuffersState.FinalLdrGetter.Invoke(screenBuffers, null);
        object? swapChain = ScreenBuffersState.SwapChainField.GetValue(null);
        if (finalLdr == null || swapChain == null)
            return false;

        Vector2I textureResolution = (Vector2I)ScreenBuffersState.TextureResolutionGetter.Invoke(finalLdr, null)!;
        Vector2I swapChainResolution = (Vector2I)ScreenBuffersState.SwapResolutionGetter.Invoke(swapChain, null)!;
        return textureResolution != swapChainResolution;
    }

    private static void InstallDataUploader(Harmony harmony, Assembly render12)
    {
        Type dataUploader = InstallTools.FindType(render12, "Keen.VRage.Render12.Core.Systems.DataUploader");
        HarmonyMethod transpiler = InstallTools.Declared(typeof(Render12Patches), nameof(BlockSizeTranspiler));
        harmony.Patch(AccessTools.Constructor(dataUploader, Type.EmptyTypes)
            ?? throw new MissingMethodException(dataUploader.FullName, ".ctor"), transpiler: transpiler);
        // The matching 256 MiB constant in the generic Pin<TData> stays unpatched: MonoMod
        // cannot rewrite open generic definitions and Pin is instantiated with value types,
        // which do not share code. Only explicit CPU-rendering runs allocate follow-up
        // transient blocks large enough for this to matter.
    }

    /// <summary>Routes each 256 MiB transient upload block size constant through
    /// DataUploaderPatch.GetBlockSize, which shrinks it only for Linux CPU rendering.</summary>
    private static IEnumerable<CodeInstruction> BlockSizeTranspiler(
        IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo getBlockSize = InstallTools.FindMethod(typeof(DataUploaderPatch), nameof(DataUploaderPatch.GetBlockSize));
        int count = 0;
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].opcode != OpCodes.Ldc_I4 || result[i].operand is not int value
                || value != DataUploaderPatch.DefaultBlockSize)
                continue;
            result.Insert(i + 1, new CodeInstruction(OpCodes.Call, getBlockSize));
            i++;
            count++;
        }
        InstallTools.AssertCount(count, 1, $"256 MiB block size constant in DataUploader.{original.Name}");
        return result;
    }

    private static void InstallAdapters(Harmony harmony, Assembly render12)
    {
        Type adapters = InstallTools.FindType(render12, "Keen.VRage.Render12.Core.Device.Adapters");
        harmony.Patch(InstallTools.FindMethod(adapters, "CreateSupportedDevice"),
            prefix: InstallTools.Declared(typeof(Render12Patches), nameof(CreateSupportedDevicePrefix)));

        AdapterState.DoublePrecisionField = InstallTools.FindField(
            AccessTools.Inner(AccessTools.TypeByName("Keen.VRage.Core.Render.AdapterInfo")!, "SupportDetailsData")
                ?? throw new TypeLoadException("AdapterInfo.SupportDetailsData was not found."),
            "IsDoublePrecisionFloatShaderOps");
        AdapterState.IsFeatureLevelField = InstallTools.FindField(
            AccessTools.Inner(AccessTools.TypeByName("Keen.VRage.Core.Render.AdapterInfo")!, "SupportDetailsData")!,
            "IsFeatureLevel");
        harmony.Patch(InstallTools.FindMethod(adapters, "CreateAdapterInfo"),
            transpiler: InstallTools.Declared(typeof(Render12Patches), nameof(CreateAdapterInfoTranspiler)),
            postfix: InstallTools.Declared(typeof(AdaptersPatch), nameof(AdaptersPatch.FixAdapterType)));
        harmony.Patch(InstallTools.FindMethod(adapters, "CreateAdaptersList"),
            transpiler: InstallTools.Declared(typeof(Render12Patches), nameof(CreateAdaptersListTranspiler)));
    }

    private static bool CreateSupportedDevicePrefix() => !AdaptersPatch.SkipProbeDevice();

    private static class AdapterState
    {
        public static FieldInfo DoublePrecisionField = null!;
        public static FieldInfo IsFeatureLevelField = null!;
    }

    /// <summary>
    /// Routes the probe-device decision through AdaptersPatch.IsFeatureLevelSupported and
    /// guards the feature-analysis block with AdaptersPatch.FeatureAnalysisPrefix, so Linux
    /// CPU rendering can report feature level 12.0 without a throw-away probe device.
    /// </summary>
    private static IEnumerable<CodeInstruction> CreateAdapterInfoTranspiler(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo cppInequality = AccessTools.Method(
            typeof(SharpGen.Runtime.CppObject), "op_Inequality",
            [typeof(SharpGen.Runtime.CppObject), typeof(SharpGen.Runtime.CppObject)])
            ?? throw new MissingMethodException("SharpGen.Runtime.CppObject", "op_Inequality");
        MethodInfo isSupported = InstallTools.FindMethod(
            typeof(AdaptersPatch), nameof(AdaptersPatch.IsFeatureLevelSupported));
        MethodInfo analysisPrefix = InstallTools.FindMethod(
            typeof(AdaptersPatch), nameof(AdaptersPatch.FeatureAnalysisPrefix));

        // Edit A: insert IsFeatureLevelSupported after the single `device != null` comparison
        // and capture the local the result is stored into (the deviceSupported flag).
        int comparisonIndex = -1;
        for (int i = 0; i < result.Count; i++)
        {
            if (!InstallTools.CallsMethod(result[i], cppInequality))
                continue;
            InstallTools.AssertCount(comparisonIndex == -1 ? 0 : 2, 0, "device comparisons in CreateAdapterInfo");
            comparisonIndex = i;
        }
        if (comparisonIndex < 0 || !TryGetLocal(result[comparisonIndex + 1], OpCodes.Stloc_S, OpCodes.Stloc,
                out object? supportedLocal))
            throw new InvalidOperationException("[LinuxCompat] CreateAdapterInfo device comparison anchor not found.");
        result.Insert(comparisonIndex + 1, new CodeInstruction(OpCodes.Call, isSupported));

        // Capture the SupportDetailsData local from the single IsFeatureLevel store
        // (sequence: ldloca.s supportDetails; ldloc.s flag; stfld IsFeatureLevel).
        int featureLevelStore = FindSingleFieldStore(result, AdapterState.IsFeatureLevelField, "IsFeatureLevel");
        if (!TryGetLocal(result[featureLevelStore - 2], OpCodes.Ldloca_S, OpCodes.Ldloca, out object? detailsLocal))
            throw new InvalidOperationException("[LinuxCompat] CreateAdapterInfo support details local not found.");

        // Edit B: find `ldloc supportedLocal; brfalse skip` preceded by the two `ldc.i4.0; stloc`
        // pairs that initialize the ray tracing and integrated flags, then insert the guarded
        // FeatureAnalysisPrefix call at the start of the analysis block.
        for (int i = 4; i < result.Count - 1; i++)
        {
            if (!TryGetLocal(result[i], OpCodes.Ldloc_S, OpCodes.Ldloc, out object? loaded)
                || !Equals(loaded, supportedLocal))
                continue;
            if (result[i + 1].opcode != OpCodes.Brfalse && result[i + 1].opcode != OpCodes.Brfalse_S)
                continue;
            if (result[i - 1].opcode != OpCodes.Stloc_S || result[i - 3].opcode != OpCodes.Stloc_S
                || result[i - 2].opcode != OpCodes.Ldc_I4_0 || result[i - 4].opcode != OpCodes.Ldc_I4_0)
                continue;

            object rayTracingLocal = result[i - 3].operand;
            object integratedLocal = result[i - 1].operand;
            object skipTarget = result[i + 1].operand;
            Label continueLabel = generator.DefineLabel();
            result[i + 2].labels.Add(continueLabel);
            result.InsertRange(i + 2, [
                new CodeInstruction(OpCodes.Ldloca, supportedLocal),
                new CodeInstruction(OpCodes.Ldloca, detailsLocal),
                new CodeInstruction(OpCodes.Ldflda, AdapterState.DoublePrecisionField),
                new CodeInstruction(OpCodes.Ldloca, rayTracingLocal),
                new CodeInstruction(OpCodes.Ldloca, integratedLocal),
                new CodeInstruction(OpCodes.Call, analysisPrefix),
                new CodeInstruction(OpCodes.Brtrue, continueLabel),
                new CodeInstruction(OpCodes.Br, skipTarget),
            ]);
            return result;
        }
        throw new InvalidOperationException("[LinuxCompat] CreateAdapterInfo feature analysis anchor not found.");
    }

    private static int FindSingleFieldStore(List<CodeInstruction> instructions, FieldInfo field, string what)
    {
        int index = -1;
        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].opcode != OpCodes.Stfld || !Equals(instructions[i].operand, field))
                continue;
            InstallTools.AssertCount(index == -1 ? 0 : 2, 0, $"{what} stores");
            index = i;
        }
        if (index < 0)
            throw new InvalidOperationException($"[LinuxCompat] {what} store not found.");
        return index;
    }

    private static bool TryGetLocal(CodeInstruction instruction, OpCode shortForm, OpCode longForm, out object? local)
    {
        local = instruction.opcode == shortForm || instruction.opcode == longForm ? instruction.operand : null;
        return local != null;
    }

    /// <summary>
    /// Fixes Keen's infinite output enumeration loop: the shipped code advances the
    /// EnumOutputs index only for unseen monitor names, so DXVK's duplicated monitor
    /// fallback re-queries the same index forever. Retargets the duplicate-name branch
    /// to the index increment.
    /// </summary>
    private static IEnumerable<CodeInstruction> CreateAdaptersListTranspiler(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo enumOutputs = AccessTools.Method(typeof(Vortice.DXGI.IDXGIAdapter), "EnumOutputs")
            ?? throw new MissingMethodException("Vortice.DXGI.IDXGIAdapter", "EnumOutputs");
        MethodInfo outputsAdd = AccessTools.Method(
            typeof(List<Vortice.DXGI.IDXGIOutput>), nameof(List<Vortice.DXGI.IDXGIOutput>.Add))!;

        int enumIndex = -1;
        int containsIndex = -1;
        int addIndex = -1;
        for (int i = 0; i < result.Count; i++)
        {
            if (InstallTools.CallsMethod(result[i], enumOutputs))
            {
                InstallTools.AssertCount(enumIndex == -1 ? 0 : 2, 0, "EnumOutputs calls in CreateAdaptersList");
                enumIndex = i;
            }
            else if (enumIndex >= 0 && containsIndex == -1
                && result[i].operand is MethodBase { Name: "Contains" } contains
                && contains.DeclaringType is { IsGenericType: true } declarer
                && declarer.Name.StartsWith("Set`1", StringComparison.Ordinal))
            {
                containsIndex = i;
            }
            else if (InstallTools.CallsMethod(result[i], outputsAdd))
            {
                InstallTools.AssertCount(addIndex == -1 ? 0 : 2, 0, "output list Add calls in CreateAdaptersList");
                addIndex = i;
            }
        }
        if (containsIndex < 0 || addIndex < containsIndex
            || (result[containsIndex + 1].opcode != OpCodes.Brtrue_S && result[containsIndex + 1].opcode != OpCodes.Brtrue)
            || result[addIndex + 1].opcode != OpCodes.Ldloc_S)
            throw new InvalidOperationException("[LinuxCompat] CreateAdaptersList duplicate-name loop anchor not found.");

        Label increment = generator.DefineLabel();
        result[addIndex + 1].labels.Add(increment);
        result[containsIndex + 1] = new CodeInstruction(OpCodes.Brtrue, increment)
        {
            labels = result[containsIndex + 1].labels,
            blocks = result[containsIndex + 1].blocks,
        };
        return result;
    }

    private static void InstallD3D12AbiAdapters(Harmony harmony, Assembly render12)
    {
        MethodInfo allocationInfo = typeof(ID3D12Device).GetMethod(
            "GetResourceAllocationInfo", [typeof(ResourceDescription[])])
            ?? throw new MissingMethodException("ID3D12Device", "GetResourceAllocationInfo");
        MethodInfo allocationAdapter = InstallTools.FindMethod(
            typeof(D3D12DevicePatch), nameof(D3D12DevicePatch.GetResourceAllocationInfo));
        MethodInfo resourceDescription = typeof(ID3D12Resource).GetProperty("Description")!.GetMethod!;
        MethodInfo resourceAdapter = InstallTools.FindMethod(
            typeof(D3D12ResourcePatch), nameof(D3D12ResourcePatch.GetDescription));
        MethodInfo heapDescription = typeof(ID3D12DescriptorHeap).GetProperty("Description")!.GetMethod!;
        MethodInfo heapAdapter = InstallTools.FindMethod(
            typeof(D3D12DescriptorHeapPatch), nameof(D3D12DescriptorHeapPatch.GetDescription));

        Type deviceContext = InstallTools.FindType(render12, "Keen.VRage.Render12.Core.Device.DeviceContext");
        Type engineComponent = InstallTools.FindType(
            render12, "Keen.VRage.Render12.EngineComponents.Render12EngineComponent");
        Type allocLog = InstallTools.FindType(render12, "Keen.VRage.Render12.Core.Device.AllocLog");
        Type committedWrap = InstallTools.FindType(render12, "Keen.VRage.Render12.Core.D3DWraps.D3DCommittedResourceWrap");
        Type backBuffer = InstallTools.FindType(render12, "Keen.VRage.Render12.Resources.BindableTextures.BackBuffer");

        CallReplacementTranspiler.Apply(harmony,
            InstallTools.FindMethod(deviceContext, "GetAllocationInfoAndFixDesc"), allocationInfo, allocationAdapter, 3);
        CallReplacementTranspiler.Apply(harmony,
            InstallTools.FindMethod(deviceContext, "GetResizableResourceTotalBytes"), allocationInfo, allocationAdapter, 1);
        CallReplacementTranspiler.Apply(harmony,
            InstallTools.FindMethod(deviceContext, "GetPreciseBufferTotalBytes"), allocationInfo, allocationAdapter, 1);
        CallReplacementTranspiler.Apply(harmony,
            InstallTools.FindMethod(engineComponent, "SendScreenshotToUser"), allocationInfo, allocationAdapter, 1);

        ConstructorInfo wrapConstructor = committedWrap.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters() is { Length: > 0 } parameters
                && parameters[0].ParameterType == typeof(ID3D12Resource));
        CallReplacementTranspiler.Apply(harmony, wrapConstructor, resourceDescription, resourceAdapter, 1);
        CallReplacementTranspiler.Apply(harmony,
            InstallTools.FindMethod(backBuffer, "Initialize"), resourceDescription, resourceAdapter, 1);
        CallReplacementTranspiler.Apply(harmony,
            InstallTools.FindMethod(allocLog, "GetResourceType"), resourceDescription, resourceAdapter, 1);
        CallReplacementTranspiler.Apply(harmony,
            InstallTools.FindMethod(allocLog, "WriteCreateResource"), resourceDescription, resourceAdapter, 2);
        CallReplacementTranspiler.Apply(harmony,
            InstallTools.FindMethod(allocLog, "WriteDisposeResource"), resourceDescription, resourceAdapter, 2);
        CallReplacementTranspiler.Apply(harmony,
            InstallTools.FindMethod(allocLog, "WriteCreateDescriptorHeap"), heapDescription, heapAdapter, 1);
    }

    private static void InstallFenceWaits(Harmony harmony, Assembly render12)
    {
        Type frameDispatcher = InstallTools.FindType(render12, "Keen.VRage.Render12.Core.Systems.FrameDispatcher");
        Type gpuProfiler = InstallTools.FindType(render12, "Keen.VRage.Render12.Core.Profiling.GPUProfiler");
        FenceState.GetD3DFence = InstallTools.FindMethod(frameDispatcher, "GetD3DFence");

        harmony.Patch(InstallTools.FindMethodContaining(frameDispatcher, "g__WaitCPU|"),
            transpiler: InstallTools.Declared(typeof(Render12Patches), nameof(WaitCpuTranspiler)));
        harmony.Patch(InstallTools.FindMethodContaining(gpuProfiler, "g__WaitCpuToDirectQueue|"),
            transpiler: InstallTools.Declared(typeof(Render12Patches), nameof(WaitCpuToDirectQueueTranspiler)));
    }

    private static class FenceState
    {
        public static MethodInfo GetD3DFence = null!;
    }

    /// <summary>On Linux the fence event handle is an eventfd vkd3d cannot signal through the
    /// Windows handle contract, so poll the fence completion value instead.</summary>
    public static bool WaitForFence(ID3D12Fence fence, ulong value, string context)
    {
        if (!FrameDispatcherPatch.TryWaitForFence(fence, value, 20000, out bool completed))
            return false;
        if (!completed)
            throw new TimeoutException($"{context} failed to synchronize CPU with the GPU command queue.");
        return true;
    }

    /// <summary>Inserts the Linux fence poll after the fence lookup in the WaitCPU local
    /// function of FrameDispatcher.FlushAllQueuesAndWaitCpu.</summary>
    private static IEnumerable<CodeInstruction> WaitCpuTranspiler(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo wait = InstallTools.FindMethod(typeof(Render12Patches), nameof(WaitForFence));
        for (int i = 0; i < result.Count - 1; i++)
        {
            if (!InstallTools.CallsMethod(result[i], FenceState.GetD3DFence))
                continue;
            if (result[i + 1].opcode != OpCodes.Stloc_0)
                throw new InvalidOperationException("[LinuxCompat] WaitCPU fence store anchor not found.");

            Label continueLabel = generator.DefineLabel();
            result[i + 2].labels.Add(continueLabel);
            result.InsertRange(i + 2, [
                new CodeInstruction(OpCodes.Ldloc_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldstr, "FlushAllQueuesAndWaitCpu"),
                new CodeInstruction(OpCodes.Call, wait),
                new CodeInstruction(OpCodes.Brfalse, continueLabel),
                new CodeInstruction(OpCodes.Ret),
            ]);
            return result;
        }
        throw new InvalidOperationException("[LinuxCompat] WaitCPU fence lookup anchor not found.");
    }

    /// <summary>Inserts the Linux fence poll after the queue signal in the
    /// WaitCpuToDirectQueue local function of GPUProfiler.SyncCPUAndGPU.</summary>
    private static IEnumerable<CodeInstruction> WaitCpuToDirectQueueTranspiler(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo wait = InstallTools.FindMethod(typeof(Render12Patches), nameof(WaitForFence));
        MethodInfo checkError = AccessTools.Method(typeof(SharpGen.Runtime.Result), "CheckError", Type.EmptyTypes)
            ?? throw new MissingMethodException("SharpGen.Runtime.Result", "CheckError");
        for (int i = 0; i < result.Count - 1; i++)
        {
            if (!InstallTools.CallsMethod(result[i], checkError))
                continue;

            Label continueLabel = generator.DefineLabel();
            result[i + 1].labels.Add(continueLabel);
            result.InsertRange(i + 1, [
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldstr, "GPUProfiler.SyncCPUAndGPU"),
                new CodeInstruction(OpCodes.Call, wait),
                new CodeInstruction(OpCodes.Brfalse, continueLabel),
                new CodeInstruction(OpCodes.Ret),
            ]);
            return result;
        }
        throw new InvalidOperationException("[LinuxCompat] WaitCpuToDirectQueue signal anchor not found.");
    }

    private static void InstallFramePacer(Harmony harmony, Assembly render12)
    {
        Type detection = InstallTools.FindType(
            render12, "Keen.VRage.Render12.Core.Systems.FramePacer+CPUThrottlingDetection");
        harmony.Patch(InstallTools.FindMethod(detection, "GetCPUThrottlingUnsafe"),
            prefix: InstallTools.Declared(typeof(FramePacerPatch), nameof(FramePacerPatch.Prefix)));
    }

    private static void InstallScreenshotsManager(Harmony harmony, Assembly render12)
    {
        Type manager = InstallTools.FindType(render12, "Keen.VRage.Render12.Core.Systems.ScreenshotsManager");
        Type screenshot = AccessTools.Inner(manager, "Screenshot")
            ?? throw new TypeLoadException("ScreenshotsManager.Screenshot was not found.");
        ScreenshotState.DownsampleResolution = InstallTools.FindField(screenshot, "DownsampleResolution");

        // MonoMod cannot rewrite the open generic definition, so patch each reference-type
        // instantiation the renderer actually calls; both compile to correctly typed bodies.
        MethodInfo definition = InstallTools.FindMethod(manager, "TakeRequestedScreenshots");
        foreach (string textureTypeName in new[]
        {
            "Keen.VRage.Render12.Resources.BindableTextures.RenderTargetTexture",
            "Keen.VRage.Render12.Resources.BindableTextures.ResizableRWRenderTargetTexture",
        })
        {
            Type textureType = InstallTools.FindType(render12, textureTypeName);
            harmony.Patch(definition.MakeGenericMethod(textureType),
                transpiler: InstallTools.Declared(typeof(Render12Patches), nameof(TakeRequestedScreenshotsTranspiler)));
        }
    }

    private static class ScreenshotState
    {
        public static FieldInfo DownsampleResolution = null!;
    }

    public static Vector2I? FitScreenshotRequest(Vector2I? requested, Vector2I source) =>
        requested is { } value ? ScreenshotsManagerPatch.FitWithin(value, source) : null;

    /// <summary>
    /// Copies the queued screenshot downsample request into a local fitted against the
    /// capture-time source resolution and redirects every request-size address load to it,
    /// fixing Keen's request/capture resize race.
    /// </summary>
    private static IEnumerable<CodeInstruction> TakeRequestedScreenshotsTranspiler(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo fit = InstallTools.FindMethod(typeof(Render12Patches), nameof(FitScreenshotRequest));
        LocalBuilder fitted = generator.DeclareLocal(typeof(Vector2I?));

        // Clone the source resolution load (ldarga.s copySource; constrained. T; callvirt
        // get_Resolution) so the initialization does not need to re-express the generic
        // constraint.
        int resolutionIndex = -1;
        for (int i = 2; i < result.Count; i++)
        {
            if (result[i].operand is MethodBase { Name: "get_Resolution" }
                && result[i - 1].opcode == OpCodes.Constrained
                && (result[i - 2].opcode == OpCodes.Ldarga_S || result[i - 2].opcode == OpCodes.Ldarga))
            {
                resolutionIndex = i;
                break;
            }
        }
        if (resolutionIndex < 0)
            throw new InvalidOperationException("[LinuxCompat] TakeRequestedScreenshots source resolution anchor not found.");
        CodeInstruction[] loadSource = [
            Bare(result[resolutionIndex - 2]),
            Bare(result[resolutionIndex - 1]),
            Bare(result[resolutionIndex]),
        ];

        List<int> fieldLoads = [];
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].opcode == OpCodes.Ldflda && Equals(result[i].operand, ScreenshotState.DownsampleResolution))
                fieldLoads.Add(i);
        }
        InstallTools.AssertCount(fieldLoads.Count, 5, "DownsampleResolution address loads in TakeRequestedScreenshots");

        // Initialize the fitted local at the first request-size use, then replace every
        // address load (each preceded by a screenshot object load) with the local address.
        int first = fieldLoads[0];
        List<CodeInstruction> initialization = [
            Bare(result[first - 1]),
            new CodeInstruction(OpCodes.Ldfld, ScreenshotState.DownsampleResolution),
            .. loadSource,
            new CodeInstruction(OpCodes.Call, fit),
            new CodeInstruction(OpCodes.Stloc, fitted),
        ];
        // Move the branch labels of the original screenshot load onto the initialization.
        initialization[0].labels.AddRange(result[first - 1].labels);
        result[first - 1].labels.Clear();
        result.InsertRange(first - 1, initialization);

        // Recompute the load positions after the insertion and swap each pair
        // (load screenshot; ldflda field) for (load screenshot; pop; ldloca fitted).
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].opcode != OpCodes.Ldflda || !Equals(result[i].operand, ScreenshotState.DownsampleResolution))
                continue;
            result[i] = new CodeInstruction(OpCodes.Pop)
            {
                labels = result[i].labels,
                blocks = result[i].blocks,
            };
            result.Insert(i + 1, new CodeInstruction(OpCodes.Ldloca, fitted));
            i++;
        }
        return result;
    }

    /// <summary>Copy of an instruction without labels or exception block markers, safe to
    /// re-emit at another position.</summary>
    private static CodeInstruction Bare(CodeInstruction instruction) =>
        new(instruction.opcode, instruction.operand);

    private static void InstallUiResolution(Harmony harmony, Assembly render12)
    {
        Type mainUiSystem = InstallTools.FindType(render12, "Keen.VRage.Render12.UIStage.MainUISystem");
        Type targetSetup = InstallTools.FindType(render12, "Keen.VRage.Render12.UIStage.UITargetSetup");
        UiState.ViewportResolution = InstallTools.FindField(targetSetup, "ViewportResolution");
        MethodInfo doWork = mainUiSystem
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.Name == "DoWork"
                && method.GetParameters() is { Length: 3 } parameters
                && parameters[1].ParameterType == targetSetup.MakeByRefType());
        harmony.Patch(doWork, transpiler: InstallTools.Declared(typeof(Render12Patches), nameof(DoWorkTranspiler)));

        Type uiSystemComponent = InstallTools.FindType(
            render12, "Keen.VRage.Render12.SceneSystem.Components.UISystemComponent");
        harmony.Patch(InstallTools.FindMethod(uiSystemComponent, "SubmitDrawBatch"),
            postfix: InstallTools.Declared(typeof(Render12Patches), nameof(SubmitDrawBatchPostfix)));

        Type contractsUtils = InstallTools.FindType(
            render12, "Keen.VRage.Render12.Utils.RenderOutputContractsUtils");
        harmony.Patch(InstallTools.FindMethod(contractsUtils, "DisplaySettingsChanged"),
            postfix: InstallTools.Declared(typeof(Render12Patches), nameof(DisplaySettingsChangedPostfix)));
    }

    private static class UiState
    {
        public static FieldInfo ViewportResolution = null!;
    }

    /// <summary>Resolves the UI viewport resolution against the persistent batch layout at
    /// the start of MainUISystem.DoWork, keeping stale-batch coordinates consistent.</summary>
    private static IEnumerable<CodeInstruction> DoWorkTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo resolve = InstallTools.FindMethod(
            typeof(MainUISystemPatch), nameof(MainUISystemPatch.ResolveViewportResolution));
        List<CodeInstruction> result = [.. instructions];
        result.InsertRange(0, [
            new CodeInstruction(OpCodes.Ldarg_2),
            new CodeInstruction(OpCodes.Ldarg_2),
            new CodeInstruction(OpCodes.Ldfld, UiState.ViewportResolution),
            new CodeInstruction(OpCodes.Call, resolve),
            new CodeInstruction(OpCodes.Stfld, UiState.ViewportResolution),
        ]);
        return result;
    }

    private static void SubmitDrawBatchPostfix(
        Keen.VRage.Render.FrameData.RenderDrawCommandBuffer __0, int __2) =>
        UISystemComponentPatch.Postfix(__0, __2);

    private static void DisplaySettingsChangedPostfix(RenderDisplaySettings __0) =>
        UIEngineComponentPatch.RecordDisplaySettings(in __0);

    private static void InstallOsDetails(Harmony harmony, Assembly render12)
    {
        Type engineComponent = InstallTools.FindType(
            render12, "Keen.VRage.Render12.EngineComponents.Render12EngineComponent");
        harmony.Patch(InstallTools.FindMethodContaining(engineComponent, "g__PrintOSDetails|"),
            prefix: InstallTools.Declared(typeof(OsDetailsPatch), nameof(OsDetailsPatch.PrintOsDetails)));
    }
}
