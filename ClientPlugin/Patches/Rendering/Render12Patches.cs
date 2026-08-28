using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Tools;
using HarmonyLib;
using Keen.VRage.Core.Render;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Render.FrameData;
using Keen.VRage.Render12.Core;
using Keen.VRage.Render12.Core.CommandLists;
using Keen.VRage.Render12.Core.D3DWraps;
using Keen.VRage.Render12.Core.Device;
using Keen.VRage.Render12.Core.Profiling;
using Keen.VRage.Render12.Core.Systems;
using Keen.VRage.Render12.EngineComponents;
using Keen.VRage.Render12.Resources.BindableTextures;
using Keen.VRage.Render12.SceneSystem.Components;
using Keen.VRage.Render12.UIStage;
using Keen.VRage.Render12.Utils;
using Vortice.Direct3D12;

namespace LinuxCompat.Patches.Rendering;

[HarmonyPatch(typeof(SwapChain), nameof(SwapChain.CreateD3DSwapChain))]
[HarmonyPatchCategory("Finish")]
internal static class SwapChainCreatePatch
{
    private static void Prefix(in RenderDisplaySettings settings, nint windowHandle) =>
        SwapChainPatch.Prefix(in settings, windowHandle);
}

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class SwapChainDrawPatch
{
    private static readonly MethodInfo Update =
        AccessTools.Method(typeof(SwapChain), nameof(SwapChain.Update))
        ?? throw new MissingMethodException(typeof(SwapChain).FullName, nameof(SwapChain.Update));

    // SwapChain.Update contains an exception filter, which Harmony 2.4.2 cannot
    // round-trip, so the resize consumption runs from its single (filter-free) caller,
    // the render component's per-frame DrawInternal local function.
    private static MethodBase TargetMethod() =>
        TranspilerHelpers.FindMethodContaining(typeof(Render12EngineComponent), "g__DrawInternal|");

    /// <summary>Inserts the pending-resize consumption immediately before the per-frame
    /// <c>CoreSystems.SwapChain.Update()</c> call.</summary>
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo adapter = TranspilerHelpers.FindMethod(
            typeof(SwapChainDrawPatch),
            nameof(ConsumePendingSwapChainResize)
        );
        int count = 0;
        for (int i = 0; i < result.Count; i++)
        {
            if (!TranspilerHelpers.CallsMethod(result[i], Update))
                continue;
            result.InsertRange(
                i,
                [new CodeInstruction(OpCodes.Dup), new CodeInstruction(OpCodes.Call, adapter)]
            );
            i += 2;
            count++;
        }
        TranspilerHelpers.AssertCount(count, 1, "SwapChain.Update call in DrawInternal");
        return result;
    }

    public static void ConsumePendingSwapChainResize(SwapChain swapChain)
    {
        if (swapChain._windows == null)
            return;

        RenderDisplaySettings? requested = swapChain._requestedDisplaySettings;
        RenderDisplaySettings? updated = requested;
        SwapChainPatch.UpdatePrefix(
            swapChain._windows,
            swapChain._currentDisplaySettings,
            ref updated
        );
        if (!Nullable.Equals(updated, requested))
            swapChain._requestedDisplaySettings = updated;
    }
}

[HarmonyPatch(typeof(ScreenBuffers), nameof(ScreenBuffers.Update))]
[HarmonyPatchCategory("Finish")]
internal static class ScreenBuffersUpdatePatch
{
    private static readonly FieldInfo UsedMaxResolution =
        AccessTools.Field(typeof(ScreenBuffers), nameof(ScreenBuffers._usedMaxResolution))
        ?? throw new MissingFieldException(
            typeof(ScreenBuffers).FullName,
            nameof(ScreenBuffers._usedMaxResolution)
        );

    /// <summary>
    /// Extends the buffer invalidation predicate with a FinalLDRTexture-vs-swapchain
    /// resolution comparison, correcting Keen's stale final LDR texture after a resize
    /// that leaves the DRS-scaled maximum unchanged.
    /// </summary>
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo combine = TranspilerHelpers.FindMethod(
            typeof(ScreenBuffersUpdatePatch),
            nameof(CombineBufferInvalidation)
        );
        int index = -1;
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].opcode == OpCodes.Ldfld && Equals(result[i].operand, UsedMaxResolution))
            {
                TranspilerHelpers.AssertCount(
                    index == -1 ? 0 : 2,
                    0,
                    "_usedMaxResolution loads in ScreenBuffers.Update"
                );
                index = i;
            }
        }
        if (
            index < 0
            || !TranspilerHelpers.CallsMethod(
                result[index + 1],
                AccessTools.Method(typeof(Vector2I), "op_Inequality")!
            )
        )
            throw new InvalidOperationException(
                "[LinuxCompat] ScreenBuffers.Update invalidation predicate anchor not found."
            );

        result.InsertRange(
            index + 2,
            [new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, combine)]
        );
        return result;
    }

    public static bool CombineBufferInvalidation(bool resized, ScreenBuffers screenBuffers)
    {
        if (resized)
            return true;

        ResizableRWRenderTargetTexture? finalLdr = screenBuffers.FinalLDRTexture;
        SwapChain? swapChain = CoreSystems.SwapChain;
        return finalLdr != null && swapChain != null && finalLdr.Resolution != swapChain.Resolution;
    }
}

[HarmonyPatch(typeof(DataUploader), MethodType.Constructor)]
[HarmonyPatchCategory("Finish")]
internal static class DataUploaderConstructorPatch
{
    // The matching 256 MiB constant in the generic Pin<TData> stays unpatched: MonoMod
    // cannot rewrite open generic definitions and Pin is instantiated with value types,
    // which do not share code. Only explicit CPU-rendering runs allocate follow-up
    // transient blocks large enough for this to matter.
    /// <summary>Routes each 256 MiB transient upload block size constant through
    /// DataUploaderPatch.GetBlockSize, which shrinks it only for Linux CPU rendering.</summary>
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original
    )
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo getBlockSize = TranspilerHelpers.FindMethod(
            typeof(DataUploaderPatch),
            nameof(DataUploaderPatch.GetBlockSize)
        );
        int count = 0;
        for (int i = 0; i < result.Count; i++)
        {
            if (
                result[i].opcode != OpCodes.Ldc_I4
                || result[i].operand is not int value
                || value != DataUploaderPatch.DefaultBlockSize
            )
                continue;
            result.Insert(i + 1, new CodeInstruction(OpCodes.Call, getBlockSize));
            i++;
            count++;
        }
        TranspilerHelpers.AssertCount(
            count,
            1,
            $"256 MiB block size constant in DataUploader.{original.Name}"
        );
        return result;
    }
}

[HarmonyPatch(typeof(Adapters), nameof(Adapters.CreateSupportedDevice))]
[HarmonyPatchCategory("Finish")]
internal static class CreateSupportedDevicePatch
{
    private static bool Prefix() => !AdaptersPatch.SkipProbeDevice();
}

[HarmonyPatch(typeof(Adapters), nameof(Adapters.CreateAdapterInfo))]
[HarmonyPatchCategory("Finish")]
internal static class CreateAdapterInfoPatch
{
    private static readonly FieldInfo DoublePrecisionField =
        AccessTools.Field(
            typeof(AdapterInfo.SupportDetailsData),
            nameof(AdapterInfo.SupportDetailsData.IsDoublePrecisionFloatShaderOps)
        )
        ?? throw new MissingFieldException(
            typeof(AdapterInfo.SupportDetailsData).FullName,
            nameof(AdapterInfo.SupportDetailsData.IsDoublePrecisionFloatShaderOps)
        );

    private static readonly FieldInfo IsFeatureLevelField =
        AccessTools.Field(
            typeof(AdapterInfo.SupportDetailsData),
            nameof(AdapterInfo.SupportDetailsData.IsFeatureLevel)
        )
        ?? throw new MissingFieldException(
            typeof(AdapterInfo.SupportDetailsData).FullName,
            nameof(AdapterInfo.SupportDetailsData.IsFeatureLevel)
        );

    /// <summary>
    /// Routes the probe-device decision through AdaptersPatch.IsFeatureLevelSupported and
    /// guards the feature-analysis block with AdaptersPatch.FeatureAnalysisPrefix, so Linux
    /// CPU rendering can report feature level 12.0 without a throw-away probe device.
    /// </summary>
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator
    )
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo cppInequality =
            AccessTools.Method(
                typeof(SharpGen.Runtime.CppObject),
                "op_Inequality",
                [typeof(SharpGen.Runtime.CppObject), typeof(SharpGen.Runtime.CppObject)]
            ) ?? throw new MissingMethodException("SharpGen.Runtime.CppObject", "op_Inequality");
        MethodInfo isSupported = TranspilerHelpers.FindMethod(
            typeof(AdaptersPatch),
            nameof(AdaptersPatch.IsFeatureLevelSupported)
        );
        MethodInfo analysisPrefix = TranspilerHelpers.FindMethod(
            typeof(AdaptersPatch),
            nameof(AdaptersPatch.FeatureAnalysisPrefix)
        );

        // Edit A: insert IsFeatureLevelSupported after the single `device != null` comparison
        // and capture the local the result is stored into (the deviceSupported flag).
        int comparisonIndex = -1;
        for (int i = 0; i < result.Count; i++)
        {
            if (!TranspilerHelpers.CallsMethod(result[i], cppInequality))
                continue;
            TranspilerHelpers.AssertCount(
                comparisonIndex == -1 ? 0 : 2,
                0,
                "device comparisons in CreateAdapterInfo"
            );
            comparisonIndex = i;
        }
        if (
            comparisonIndex < 0
            || !TryGetLocal(
                result[comparisonIndex + 1],
                OpCodes.Stloc_S,
                OpCodes.Stloc,
                out object? supportedLocal
            )
        )
            throw new InvalidOperationException(
                "[LinuxCompat] CreateAdapterInfo device comparison anchor not found."
            );
        result.Insert(comparisonIndex + 1, new CodeInstruction(OpCodes.Call, isSupported));

        // Capture the SupportDetailsData local from the single IsFeatureLevel store
        // (sequence: ldloca.s supportDetails; ldloc.s flag; stfld IsFeatureLevel).
        int featureLevelStore = FindSingleFieldStore(result, IsFeatureLevelField, "IsFeatureLevel");
        if (
            !TryGetLocal(
                result[featureLevelStore - 2],
                OpCodes.Ldloca_S,
                OpCodes.Ldloca,
                out object? detailsLocal
            )
        )
            throw new InvalidOperationException(
                "[LinuxCompat] CreateAdapterInfo support details local not found."
            );

        // Edit B: find `ldloc supportedLocal; brfalse skip` preceded by the two `ldc.i4.0; stloc`
        // pairs that initialize the ray tracing and integrated flags, then insert the guarded
        // FeatureAnalysisPrefix call at the start of the analysis block.
        for (int i = 4; i < result.Count - 1; i++)
        {
            if (
                !TryGetLocal(result[i], OpCodes.Ldloc_S, OpCodes.Ldloc, out object? loaded)
                || !Equals(loaded, supportedLocal)
            )
                continue;
            if (
                result[i + 1].opcode != OpCodes.Brfalse
                && result[i + 1].opcode != OpCodes.Brfalse_S
            )
                continue;
            if (
                result[i - 1].opcode != OpCodes.Stloc_S
                || result[i - 3].opcode != OpCodes.Stloc_S
                || result[i - 2].opcode != OpCodes.Ldc_I4_0
                || result[i - 4].opcode != OpCodes.Ldc_I4_0
            )
                continue;

            object rayTracingLocal = result[i - 3].operand;
            object integratedLocal = result[i - 1].operand;
            object skipTarget = result[i + 1].operand;
            Label continueLabel = generator.DefineLabel();
            result[i + 2].labels.Add(continueLabel);
            result.InsertRange(
                i + 2,
                [
                    new CodeInstruction(OpCodes.Ldloca, supportedLocal),
                    new CodeInstruction(OpCodes.Ldloca, detailsLocal),
                    new CodeInstruction(OpCodes.Ldflda, DoublePrecisionField),
                    new CodeInstruction(OpCodes.Ldloca, rayTracingLocal),
                    new CodeInstruction(OpCodes.Ldloca, integratedLocal),
                    new CodeInstruction(OpCodes.Call, analysisPrefix),
                    new CodeInstruction(OpCodes.Brtrue, continueLabel),
                    new CodeInstruction(OpCodes.Br, skipTarget),
                ]
            );
            return result;
        }
        throw new InvalidOperationException(
            "[LinuxCompat] CreateAdapterInfo feature analysis anchor not found."
        );
    }

    private static void Postfix(ref AdapterInfo? __result) =>
        AdaptersPatch.FixAdapterType(ref __result);

    private static int FindSingleFieldStore(
        List<CodeInstruction> instructions,
        FieldInfo field,
        string what
    )
    {
        int index = -1;
        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].opcode != OpCodes.Stfld || !Equals(instructions[i].operand, field))
                continue;
            TranspilerHelpers.AssertCount(index == -1 ? 0 : 2, 0, $"{what} stores");
            index = i;
        }
        if (index < 0)
            throw new InvalidOperationException($"[LinuxCompat] {what} store not found.");
        return index;
    }

    private static bool TryGetLocal(
        CodeInstruction instruction,
        OpCode shortForm,
        OpCode longForm,
        out object? local
    )
    {
        local =
            instruction.opcode == shortForm || instruction.opcode == longForm
                ? instruction.operand
                : null;
        return local != null;
    }
}

[HarmonyPatch(typeof(Adapters), nameof(Adapters.CreateAdaptersList))]
[HarmonyPatchCategory("Finish")]
internal static class CreateAdaptersListPatch
{
    /// <summary>
    /// Fixes Keen's infinite output enumeration loop: the shipped code advances the
    /// EnumOutputs index only for unseen monitor names, so DXVK's duplicated monitor
    /// fallback re-queries the same index forever. Retargets the duplicate-name branch
    /// to the index increment.
    /// </summary>
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator
    )
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo enumOutputs =
            AccessTools.Method(typeof(Vortice.DXGI.IDXGIAdapter), "EnumOutputs")
            ?? throw new MissingMethodException("Vortice.DXGI.IDXGIAdapter", "EnumOutputs");
        MethodInfo outputsAdd = AccessTools.Method(
            typeof(List<Vortice.DXGI.IDXGIOutput>),
            nameof(List<Vortice.DXGI.IDXGIOutput>.Add)
        )!;

        int enumIndex = -1;
        int containsIndex = -1;
        int addIndex = -1;
        for (int i = 0; i < result.Count; i++)
        {
            if (TranspilerHelpers.CallsMethod(result[i], enumOutputs))
            {
                TranspilerHelpers.AssertCount(
                    enumIndex == -1 ? 0 : 2,
                    0,
                    "EnumOutputs calls in CreateAdaptersList"
                );
                enumIndex = i;
            }
            else if (
                enumIndex >= 0
                && containsIndex == -1
                && result[i].operand is MethodBase { Name: "Contains" } contains
                && contains.DeclaringType is { IsGenericType: true } declarer
                && declarer.Name.StartsWith("Set`1", StringComparison.Ordinal)
            )
            {
                containsIndex = i;
            }
            else if (TranspilerHelpers.CallsMethod(result[i], outputsAdd))
            {
                TranspilerHelpers.AssertCount(
                    addIndex == -1 ? 0 : 2,
                    0,
                    "output list Add calls in CreateAdaptersList"
                );
                addIndex = i;
            }
        }
        if (
            containsIndex < 0
            || addIndex < containsIndex
            || (
                result[containsIndex + 1].opcode != OpCodes.Brtrue_S
                && result[containsIndex + 1].opcode != OpCodes.Brtrue
            )
            || result[addIndex + 1].opcode != OpCodes.Ldloc_S
        )
            throw new InvalidOperationException(
                "[LinuxCompat] CreateAdaptersList duplicate-name loop anchor not found."
            );

        Label increment = generator.DefineLabel();
        result[addIndex + 1].labels.Add(increment);
        result[containsIndex + 1] = new CodeInstruction(OpCodes.Brtrue, increment)
        {
            labels = result[containsIndex + 1].labels,
            blocks = result[containsIndex + 1].blocks,
        };
        return result;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class D3D12AbiPatch
{
    private static readonly Dictionary<
        MethodBase,
        (MethodBase From, MethodInfo To, int Expected)
    > Replacements = [];

    private static IEnumerable<MethodBase> TargetMethods()
    {
        MethodInfo allocationInfo =
            typeof(ID3D12Device).GetMethod(
                nameof(ID3D12Device.GetResourceAllocationInfo),
                [typeof(ResourceDescription[])]
            )
            ?? throw new MissingMethodException(
                nameof(ID3D12Device),
                nameof(ID3D12Device.GetResourceAllocationInfo)
            );
        MethodInfo allocationAdapter = TranspilerHelpers.FindMethod(
            typeof(D3D12DevicePatch),
            nameof(D3D12DevicePatch.GetResourceAllocationInfo)
        );
        MethodInfo resourceDescription = typeof(ID3D12Resource)
            .GetProperty(nameof(ID3D12Resource.Description))!
            .GetMethod!;
        MethodInfo resourceAdapter = TranspilerHelpers.FindMethod(
            typeof(D3D12ResourcePatch),
            nameof(D3D12ResourcePatch.GetDescription)
        );
        MethodInfo heapDescription = typeof(ID3D12DescriptorHeap)
            .GetProperty(nameof(ID3D12DescriptorHeap.Description))!
            .GetMethod!;
        MethodInfo heapAdapter = TranspilerHelpers.FindMethod(
            typeof(D3D12DescriptorHeapPatch),
            nameof(D3D12DescriptorHeapPatch.GetDescription)
        );

        List<MethodBase> targets = [];
        Replacements.Clear();
        Add(
            TranspilerHelpers.FindMethod(
                typeof(DeviceContext),
                nameof(DeviceContext.GetAllocationInfoAndFixDesc)
            ),
            allocationInfo,
            allocationAdapter,
            3
        );
        Add(
            TranspilerHelpers.FindMethod(
                typeof(DeviceContext),
                nameof(DeviceContext.GetResizableResourceTotalBytes)
            ),
            allocationInfo,
            allocationAdapter,
            1
        );
        Add(
            TranspilerHelpers.FindMethod(
                typeof(DeviceContext),
                nameof(DeviceContext.GetPreciseBufferTotalBytes)
            ),
            allocationInfo,
            allocationAdapter,
            1
        );
        Add(
            TranspilerHelpers.FindMethod(
                typeof(Render12EngineComponent),
                nameof(Render12EngineComponent.SendScreenshotToUser)
            ),
            allocationInfo,
            allocationAdapter,
            1
        );

        ConstructorInfo wrapConstructor =
            AccessTools.Constructor(
                typeof(D3DCommittedResourceWrap),
                [
                    typeof(ID3D12Resource),
                    typeof(string),
                    typeof(AllocationRecord),
                    typeof(HeapFlags),
                ]
            )
            ?? throw new MissingMethodException(typeof(D3DCommittedResourceWrap).FullName, ".ctor");
        Add(wrapConstructor, resourceDescription, resourceAdapter, 1);
        Add(
            TranspilerHelpers.FindMethod(typeof(BackBuffer), nameof(BackBuffer.Initialize)),
            resourceDescription,
            resourceAdapter,
            1
        );
        Add(
            TranspilerHelpers.FindMethod(typeof(AllocLog), nameof(AllocLog.GetResourceType)),
            resourceDescription,
            resourceAdapter,
            1
        );
        Add(
            TranspilerHelpers.FindMethod(typeof(AllocLog), nameof(AllocLog.WriteCreateResource)),
            resourceDescription,
            resourceAdapter,
            2
        );
        Add(
            TranspilerHelpers.FindMethod(typeof(AllocLog), nameof(AllocLog.WriteDisposeResource)),
            resourceDescription,
            resourceAdapter,
            2
        );
        Add(
            TranspilerHelpers.FindMethod(
                typeof(AllocLog),
                nameof(AllocLog.WriteCreateDescriptorHeap)
            ),
            heapDescription,
            heapAdapter,
            1
        );
        return targets;

        void Add(MethodBase target, MethodBase from, MethodInfo to, int expected)
        {
            targets.Add(target);
            Replacements.Add(target, (from, to, expected));
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original
    )
    {
        var replacement = Replacements[original];
        return TranspilerHelpers.ReplaceCalls(
            instructions,
            replacement.From,
            replacement.To,
            replacement.Expected,
            $"call(s) to {replacement.From.Name} in {original.Name}"
        );
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class WaitCpuPatch
{
    private static readonly MethodInfo GetD3DFence =
        AccessTools.Method(typeof(FrameDispatcher), nameof(FrameDispatcher.GetD3DFence))
        ?? throw new MissingMethodException(
            typeof(FrameDispatcher).FullName,
            nameof(FrameDispatcher.GetD3DFence)
        );

    private static MethodBase TargetMethod() =>
        TranspilerHelpers.FindMethodContaining(typeof(FrameDispatcher), "g__WaitCPU|");

    /// <summary>Inserts the Linux fence poll after the fence lookup in the WaitCPU local
    /// function of FrameDispatcher.FlushAllQueuesAndWaitCpu.</summary>
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator
    )
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo wait = TranspilerHelpers.FindMethod(typeof(WaitCpuPatch), nameof(WaitForFence));
        for (int i = 0; i < result.Count - 1; i++)
        {
            if (!TranspilerHelpers.CallsMethod(result[i], GetD3DFence))
                continue;
            if (result[i + 1].opcode != OpCodes.Stloc_0)
                throw new InvalidOperationException(
                    "[LinuxCompat] WaitCPU fence store anchor not found."
                );

            Label continueLabel = generator.DefineLabel();
            result[i + 2].labels.Add(continueLabel);
            result.InsertRange(
                i + 2,
                [
                    new CodeInstruction(OpCodes.Ldloc_0),
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Ldstr, "FlushAllQueuesAndWaitCpu"),
                    new CodeInstruction(OpCodes.Call, wait),
                    new CodeInstruction(OpCodes.Brfalse, continueLabel),
                    new CodeInstruction(OpCodes.Ret),
                ]
            );
            return result;
        }
        throw new InvalidOperationException("[LinuxCompat] WaitCPU fence lookup anchor not found.");
    }

    /// <summary>On Linux the fence event handle is an eventfd vkd3d cannot signal through the
    /// Windows handle contract, so poll the fence completion value instead.</summary>
    public static bool WaitForFence(ID3D12Fence fence, ulong value, string context)
    {
        if (!FrameDispatcherPatch.PollFence(fence, value, 20000))
            throw new TimeoutException(
                $"{context} failed to synchronize CPU with the GPU command queue."
            );
        return true;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class WaitCpuToDirectQueuePatch
{
    private static MethodBase TargetMethod() =>
        TranspilerHelpers.FindMethodContaining(typeof(GPUProfiler), "g__WaitCpuToDirectQueue|");

    /// <summary>Inserts the Linux fence poll after the queue signal in the
    /// WaitCpuToDirectQueue local function of GPUProfiler.SyncCPUAndGPU.</summary>
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator
    )
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo wait = TranspilerHelpers.FindMethod(
            typeof(WaitCpuPatch),
            nameof(WaitCpuPatch.WaitForFence)
        );
        MethodInfo checkError =
            AccessTools.Method(typeof(SharpGen.Runtime.Result), "CheckError", Type.EmptyTypes)
            ?? throw new MissingMethodException("SharpGen.Runtime.Result", "CheckError");
        for (int i = 0; i < result.Count - 1; i++)
        {
            if (!TranspilerHelpers.CallsMethod(result[i], checkError))
                continue;

            Label continueLabel = generator.DefineLabel();
            result[i + 1].labels.Add(continueLabel);
            result.InsertRange(
                i + 1,
                [
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Ldstr, "GPUProfiler.SyncCPUAndGPU"),
                    new CodeInstruction(OpCodes.Call, wait),
                    new CodeInstruction(OpCodes.Brfalse, continueLabel),
                    new CodeInstruction(OpCodes.Ret),
                ]
            );
            return result;
        }
        throw new InvalidOperationException(
            "[LinuxCompat] WaitCpuToDirectQueue signal anchor not found."
        );
    }
}

[HarmonyPatch(
    typeof(FramePacer.CPUThrottlingDetection),
    nameof(FramePacer.CPUThrottlingDetection.GetCPUThrottlingUnsafe)
)]
[HarmonyPatchCategory("Finish")]
internal static class FramePacerCpuThrottlingPatch
{
    private static bool Prefix(ref float? __result) => FramePacerPatch.Prefix(ref __result);
}

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class TakeRequestedScreenshotsPatch
{
    private static readonly FieldInfo DownsampleResolution =
        AccessTools.Field(
            typeof(ScreenshotsManager.Screenshot),
            nameof(ScreenshotsManager.Screenshot.DownsampleResolution)
        )
        ?? throw new MissingFieldException(
            typeof(ScreenshotsManager.Screenshot).FullName,
            nameof(ScreenshotsManager.Screenshot.DownsampleResolution)
        );

    // MonoMod cannot rewrite the open generic definition, so patch each reference-type
    // instantiation the renderer actually calls; both compile to correctly typed bodies.
    private static IEnumerable<MethodBase> TargetMethods()
    {
        MethodInfo definition = TranspilerHelpers.FindMethod(
            typeof(ScreenshotsManager),
            nameof(ScreenshotsManager.TakeRequestedScreenshots)
        );
        yield return definition.MakeGenericMethod(typeof(RenderTargetTexture));
        yield return definition.MakeGenericMethod(typeof(ResizableRWRenderTargetTexture));
    }

    /// <summary>
    /// Copies the queued screenshot downsample request into a local fitted against the
    /// capture-time source resolution and redirects every request-size address load to it,
    /// fixing Keen's request/capture resize race.
    /// </summary>
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator
    )
    {
        List<CodeInstruction> result = [.. instructions];
        MethodInfo fit = TranspilerHelpers.FindMethod(
            typeof(TakeRequestedScreenshotsPatch),
            nameof(FitScreenshotRequest)
        );
        LocalBuilder fitted = generator.DeclareLocal(typeof(Vector2I?));

        // Clone the source resolution load (ldarga.s copySource; constrained. T; callvirt
        // get_Resolution) so the initialization does not need to re-express the generic
        // constraint.
        int resolutionIndex = -1;
        for (int i = 2; i < result.Count; i++)
        {
            if (
                result[i].operand is MethodBase { Name: "get_Resolution" }
                && result[i - 1].opcode == OpCodes.Constrained
                && (
                    result[i - 2].opcode == OpCodes.Ldarga_S
                    || result[i - 2].opcode == OpCodes.Ldarga
                )
            )
            {
                resolutionIndex = i;
                break;
            }
        }
        if (resolutionIndex < 0)
            throw new InvalidOperationException(
                "[LinuxCompat] TakeRequestedScreenshots source resolution anchor not found."
            );
        CodeInstruction[] loadSource =
        [
            Bare(result[resolutionIndex - 2]),
            Bare(result[resolutionIndex - 1]),
            Bare(result[resolutionIndex]),
        ];

        List<int> fieldLoads = [];
        for (int i = 0; i < result.Count; i++)
        {
            if (
                result[i].opcode == OpCodes.Ldflda
                && Equals(result[i].operand, DownsampleResolution)
            )
                fieldLoads.Add(i);
        }
        TranspilerHelpers.AssertCount(
            fieldLoads.Count,
            5,
            "DownsampleResolution address loads in TakeRequestedScreenshots"
        );

        // Initialize the fitted local at the first request-size use, then replace every
        // address load (each preceded by a screenshot object load) with the local address.
        int first = fieldLoads[0];
        List<CodeInstruction> initialization =
        [
            Bare(result[first - 1]),
            new CodeInstruction(OpCodes.Ldfld, DownsampleResolution),
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
            if (
                result[i].opcode != OpCodes.Ldflda
                || !Equals(result[i].operand, DownsampleResolution)
            )
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

    public static Vector2I? FitScreenshotRequest(Vector2I? requested, Vector2I source) =>
        requested is { } value ? ScreenshotsManagerPatch.FitWithin(value, source) : null;

    /// <summary>Copy of an instruction without labels or exception block markers, safe to
    /// re-emit at another position.</summary>
    private static CodeInstruction Bare(CodeInstruction instruction) =>
        new(instruction.opcode, instruction.operand);
}

[HarmonyPatch(
    typeof(MainUISystem),
    nameof(MainUISystem.DoWork),
    new[] { typeof(DirectCommandList), typeof(UITargetSetup), typeof(Vector2I) },
    new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal }
)]
[HarmonyPatchCategory("Finish")]
internal static class MainUiDoWorkPatch
{
    private static readonly FieldInfo ViewportResolution =
        AccessTools.Field(typeof(UITargetSetup), nameof(UITargetSetup.ViewportResolution))
        ?? throw new MissingFieldException(
            typeof(UITargetSetup).FullName,
            nameof(UITargetSetup.ViewportResolution)
        );

    /// <summary>Resolves the UI viewport resolution against the persistent batch layout at
    /// the start of MainUISystem.DoWork, keeping stale-batch coordinates consistent.</summary>
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        MethodInfo resolve = TranspilerHelpers.FindMethod(
            typeof(MainUISystemPatch),
            nameof(MainUISystemPatch.ResolveViewportResolution)
        );
        List<CodeInstruction> result = [.. instructions];
        result.InsertRange(
            0,
            [
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldfld, ViewportResolution),
                new CodeInstruction(OpCodes.Call, resolve),
                new CodeInstruction(OpCodes.Stfld, ViewportResolution),
            ]
        );
        return result;
    }
}

[HarmonyPatch(typeof(UISystemComponent), nameof(UISystemComponent.SubmitDrawBatch))]
[HarmonyPatchCategory("Finish")]
internal static class SubmitDrawBatchPatch
{
    private static void Postfix(RenderDrawCommandBuffer __0, int __2) =>
        UISystemComponentPatch.Postfix(__0, __2);
}

[HarmonyPatch(
    typeof(RenderOutputContractsUtils),
    nameof(RenderOutputContractsUtils.DisplaySettingsChanged)
)]
[HarmonyPatchCategory("Finish")]
internal static class DisplaySettingsChangedPatch
{
    private static void Postfix(RenderDisplaySettings __0) =>
        UIEngineComponentPatch.RecordDisplaySettings(in __0);
}

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class PrintOsDetailsPatch
{
    private static MethodBase TargetMethod() =>
        TranspilerHelpers.FindMethodContaining(
            typeof(Render12EngineComponent),
            "g__PrintOSDetails|"
        );

    private static bool Prefix(Keen.VRage.Library.Diagnostics.Log log) =>
        OsDetailsPatch.PrintOsDetails(log);
}
