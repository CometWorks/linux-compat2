using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ClientPlugin.Preloading;

/// <summary>
/// Cecil rewrite of <c>Keen.VRage.Library.Memory.NativeMemory</c> in VRage.Library.dll, applied
/// by the Pulsar preloader before the assembly loads. Each of these methods throws
/// <see cref="PlatformNotSupportedException"/> off Windows. They used to be Harmony prefixes, but
/// a Harmony patch can get lost at runtime (Vikunja #1073), and a lost one here crashed the
/// player's game: in UI replay as a <c>ScissorStackEmpty</c> assertion that hides the original
/// exception, while loading a world, or when opening the F11 statistics (#1187). So the
/// methods' bodies are replaced with calls into <see cref="LinuxNativeMemory"/>.
/// </summary>
public static class NativeMemoryPrepatch
{
    private const string TypeName = "Keen.VRage.Library.Memory.NativeMemory";

    public static void Apply(AssemblyDefinition assembly)
    {
        ModuleDefinition module = assembly.MainModule;
        TypeDefinition type =
            module.GetType(TypeName)
            ?? throw new InvalidOperationException(
                $"[LinuxCompat] {TypeName} was not found in VRage.Library."
            );

        Replace(module, type, "get_TotalAllocated", nameof(LinuxNativeMemory.TotalAllocated), 0);
        Replace(module, type, "Allocate", nameof(LinuxNativeMemory.Allocate), 2);
        Replace(module, type, "GetAllocationSize", nameof(LinuxNativeMemory.GetAllocationSize), 1);
        Replace(module, type, "Free", nameof(LinuxNativeMemory.Free), 1);
    }

    /// <summary>Replaces the body of <paramref name="name"/> with a call passing its first
    /// <paramref name="arguments"/> arguments to the <see cref="LinuxNativeMemory"/> method of the
    /// same signature, then returning its result.</summary>
    private static void Replace(
        ModuleDefinition module,
        TypeDefinition type,
        string name,
        string helper,
        int arguments
    )
    {
        MethodDefinition method =
            type.Methods.SingleOrDefault(m => m.Name == name)
            ?? throw new InvalidOperationException(
                $"[LinuxCompat] {TypeName}.{name} was not found in VRage.Library."
            );

        // Only the stock body, which has no Linux implementation, is replaced. If a game update
        // adds one, stop here so it gets reviewed instead of silently overwritten.
        if (
            !method.Body.Instructions.Any(i =>
                i.OpCode.Code == Code.Newobj
                && i.Operand is MethodReference ctor
                && ctor.DeclaringType.FullName == "System.PlatformNotSupportedException"
            )
        )
            throw new InvalidOperationException(
                $"[LinuxCompat] {TypeName}.{name} no longer throws PlatformNotSupportedException; review the prepatch."
            );

        MethodReference target = module.ImportReference(
            typeof(LinuxNativeMemory).GetMethod(helper)
                ?? throw new MissingMethodException(nameof(LinuxNativeMemory), helper)
        );
        if (
            target.ReturnType.FullName != method.ReturnType.FullName
            || target.Parameters.Count != arguments
            || target
                .Parameters.Where(
                    (p, i) =>
                        p.ParameterType.FullName != method.Parameters[i].ParameterType.FullName
                )
                .Any()
        )
            throw new InvalidOperationException(
                $"[LinuxCompat] {nameof(LinuxNativeMemory)}.{helper} does not match {TypeName}.{name}."
            );

        MethodBody body = method.Body;
        body.Instructions.Clear();
        body.ExceptionHandlers.Clear();
        body.Variables.Clear();
        body.InitLocals = false;
        ILProcessor il = body.GetILProcessor();
        for (int i = 0; i < arguments; i++)
            il.Emit(OpCodes.Ldarg, method.Parameters[i]);
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
    }
}

/// <summary>
/// The Linux implementation <see cref="NativeMemoryPrepatch"/> writes into the game's
/// <c>NativeMemory</c>. VRage.Library calls these methods directly, so their signatures must not
/// name game types: resolving one would load the unpatched game assembly.
/// </summary>
public static class LinuxNativeMemory
{
    private static readonly ConcurrentDictionary<nint, nuint> Allocations = new();
    private static long _totalAllocated;

    public static nuint TotalAllocated() => (nuint)Interlocked.Read(ref _totalAllocated);

    public static unsafe nint Allocate(nuint size, nuint alignment)
    {
        nuint actualSize = checked((size + alignment - 1) / alignment * alignment);
        nint ptr = (nint)NativeMemory.AlignedAlloc(actualSize, alignment);
        if (ptr != 0)
        {
            Allocations[ptr] = size;
            Interlocked.Add(ref _totalAllocated, checked((long)size));
        }
        return ptr;
    }

    public static nuint GetAllocationSize(nint ptr)
    {
        Allocations.TryGetValue(ptr, out nuint size);
        return size;
    }

    public static unsafe void Free(nint ptr)
    {
        if (Allocations.TryRemove(ptr, out nuint size))
            Interlocked.Add(ref _totalAllocated, -checked((long)size));
        NativeMemory.AlignedFree((void*)ptr);
    }
}
