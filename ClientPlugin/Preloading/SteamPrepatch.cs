using Mono.Cecil;
using Mono.Cecil.Cil;

namespace LinuxCompat.Preloading;

/// <summary>
/// Cecil rewrite of the shipped VRage.Steam.dll, applied by the Pulsar preloader before the
/// assembly loads. Under Pulsar on Linux the game binds to Pulsar's Steamworks.NET wrapper
/// (managed 1.0.0.0 with native import <c>steam_api</c>): the shipped 2024.8 wrapper is not
/// ABI-safe against Pulsar's Linux <c>libsteam_api.so</c>. Pulsar's wrapper drops
/// <c>SteamUserStats.RequestCurrentStats()</c> and adds an <c>includeLocallyDisabled</c>
/// argument to the two subscribed-UGC calls, so those call sites cannot even be read by
/// Harmony (their tokens no longer resolve) and must be rewritten in IL. The desktop-Steam
/// restart is additionally limited to an active Steam connection so daemonless Linux runs
/// keep SE2's inactive NoSteam state instead of terminating.
/// </summary>
public static class SteamPrepatch
{
    public static void Apply(AssemblyDefinition assembly)
    {
        ModuleDefinition module = assembly.MainModule;
        PatchRefreshSubscribedItemSet(module);
        PatchInitializeAsUser(module);
    }

    private static TypeDefinition FindType(ModuleDefinition module, string fullName) =>
        module.GetType(fullName)
        ?? throw new InvalidOperationException(
            $"[LinuxCompat] {fullName} was not found in VRage.Steam."
        );

    private static MethodDefinition FindMethod(TypeDefinition type, string name) =>
        type.Methods.SingleOrDefault(method => method.Name == name)
        ?? throw new InvalidOperationException(
            $"[LinuxCompat] {type.FullName}.{name} was not found in VRage.Steam."
        );

    private static int FindSingleCall(MethodDefinition method, string calleeName)
    {
        var body = method.Body.Instructions;
        int index = -1;
        for (int i = 0; i < body.Count; i++)
        {
            if (
                body[i].OpCode.Code is not (Code.Call or Code.Callvirt)
                || body[i].Operand is not MethodReference callee
                || callee.Name != calleeName
            )
                continue;
            if (index != -1)
                throw new InvalidOperationException(
                    $"[LinuxCompat] Multiple {calleeName} calls in {method.FullName}."
                );
            index = i;
        }
        if (index < 0)
            throw new InvalidOperationException(
                $"[LinuxCompat] No {calleeName} call in {method.FullName}."
            );
        return index;
    }

    /// <summary>Pass <c>includeLocallyDisabled: false</c> to both subscribed-UGC calls,
    /// matching Pulsar's extended wrapper signatures.</summary>
    private static void PatchRefreshSubscribedItemSet(ModuleDefinition module)
    {
        MethodDefinition method = FindMethod(
            FindType(module, "Keen.VRage.Steam.UGC.SteamUGCServiceComponent"),
            "RefreshSubscribedItemSet"
        );
        ILProcessor il = method.Body.GetILProcessor();

        foreach (string callee in new[] { "GetNumSubscribedItems", "GetSubscribedItems" })
        {
            Instruction call = method.Body.Instructions[FindSingleCall(method, callee)];
            var original = (MethodReference)call.Operand;
            var extended = new MethodReference(
                original.Name,
                original.ReturnType,
                original.DeclaringType
            )
            {
                HasThis = original.HasThis,
            };
            foreach (ParameterDefinition parameter in original.Parameters)
                extended.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
            extended.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));

            il.InsertBefore(call, il.Create(OpCodes.Ldc_I4_0));
            call.Operand = extended;
        }
    }

    /// <summary>Skip the desktop-Steam restart when Steam is inactive and replace the removed
    /// <c>RequestCurrentStats()</c> with <c>RequestUserStats(_steamUserId)</c>.</summary>
    private static void PatchInitializeAsUser(ModuleDefinition module)
    {
        TypeDefinition component = FindType(
            module,
            "Keen.VRage.Steam.EngineComponents.SteamGameServiceComponent"
        );
        MethodDefinition method = FindMethod(component, "InitializeAsUser");
        MethodDefinition isActiveGetter = FindMethod(component, "get_IsActive");
        FieldDefinition steamUserId =
            component.Fields.SingleOrDefault(field => field.Name == "_steamUserId")
            ?? throw new InvalidOperationException("[LinuxCompat] _steamUserId was not found.");
        ILProcessor il = method.Body.GetILProcessor();
        var body = method.Body.Instructions;

        // Guard the restart: the call site is `ldarg.0; ldfld _steamAppId; call
        // RestartAppIfNecessary; brfalse <after>`. Insert `ldarg.0; call get_IsActive;
        // brfalse <after>` in front of it and retarget branches and handler bounds that
        // referenced the original first instruction.
        int restart = FindSingleCall(method, "RestartAppIfNecessary");
        if (
            body[restart - 2].OpCode.Code != Code.Ldarg_0
            || body[restart - 1].OpCode.Code != Code.Ldfld
            || body[restart + 1].OpCode.Code is not (Code.Brfalse_S or Code.Brfalse)
        )
            throw new InvalidOperationException(
                "[LinuxCompat] RestartAppIfNecessary anchor mismatch."
            );
        Instruction blockStart = body[restart - 2];
        var afterRestart = (Instruction)body[restart + 1].Operand;

        Instruction guardLoad = il.Create(OpCodes.Ldarg_0);
        il.InsertBefore(blockStart, guardLoad);
        il.InsertBefore(blockStart, il.Create(OpCodes.Call, isActiveGetter));
        il.InsertBefore(blockStart, il.Create(OpCodes.Brfalse, afterRestart));
        RetargetReferences(method, blockStart, guardLoad);

        // Replace the stats refresh; the original result is discarded by the existing pop and
        // Pulsar's RequestUserStats returns a SteamAPICall_t that the pop discards the same way.
        int stats = FindSingleCall(method, "RequestCurrentStats");
        Instruction statsCall = body[stats];
        if (statsCall.Next?.OpCode.Code != Code.Pop)
            throw new InvalidOperationException(
                "[LinuxCompat] RequestCurrentStats anchor mismatch."
            );
        var currentStats = (MethodReference)statsCall.Operand;
        TypeReference steamApiCall = FindSteamApiCallType(module, currentStats.DeclaringType.Scope);
        var requestUserStats = new MethodReference(
            "RequestUserStats",
            steamApiCall,
            currentStats.DeclaringType
        );
        requestUserStats.Parameters.Add(new ParameterDefinition(steamUserId.FieldType));

        Instruction statsLoad = il.Create(OpCodes.Ldarg_0);
        il.InsertBefore(statsCall, statsLoad);
        il.InsertBefore(statsCall, il.Create(OpCodes.Ldfld, steamUserId));
        statsCall.Operand = requestUserStats;
        RetargetReferences(method, statsCall, statsLoad);
    }

    private static TypeReference FindSteamApiCallType(
        ModuleDefinition module,
        IMetadataScope steamworksScope
    )
    {
        TypeReference? existing = module
            .GetTypeReferences()
            .FirstOrDefault(reference => reference.FullName == "Steamworks.SteamAPICall_t");
        if (existing != null)
            return existing;

        return new TypeReference(
            "Steamworks",
            "SteamAPICall_t",
            module,
            steamworksScope,
            valueType: true
        );
    }

    /// <summary>Retargets branch operands and exception handler boundaries from
    /// <paramref name="from"/> to <paramref name="to"/> after an insertion in front of it.</summary>
    private static void RetargetReferences(
        MethodDefinition method,
        Instruction from,
        Instruction to
    )
    {
        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (ReferenceEquals(instruction.Operand, from))
                instruction.Operand = to;
            else if (instruction.Operand is Instruction[] targets)
                for (int i = 0; i < targets.Length; i++)
                    if (ReferenceEquals(targets[i], from))
                        targets[i] = to;
        }

        foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
        {
            if (ReferenceEquals(handler.TryStart, from))
                handler.TryStart = to;
            if (ReferenceEquals(handler.TryEnd, from))
                handler.TryEnd = to;
            if (ReferenceEquals(handler.HandlerStart, from))
                handler.HandlerStart = to;
            if (ReferenceEquals(handler.HandlerEnd, from))
                handler.HandlerEnd = to;
            if (ReferenceEquals(handler.FilterStart, from))
                handler.FilterStart = to;
        }
    }
}
