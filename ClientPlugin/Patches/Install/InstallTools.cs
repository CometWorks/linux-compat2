using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LinuxCompat.Patches.Install;

/// <summary>
/// Shared plumbing for installing the LinuxCompat Harmony patches against the shipped
/// game assemblies. Every anchor mismatch throws, so a game update that moves a patch
/// target fails installation loudly instead of silently skipping a compatibility fix.
/// </summary>
internal static class InstallTools
{
    public static Assembly LoadAssembly(string name) => Assembly.Load(name);

    public static Type FindType(Assembly assembly, string fullName) =>
        assembly.GetType(fullName, throwOnError: true)!;

    public static MethodInfo FindMethod(Type type, string name, params Type[] parameters)
    {
        MethodInfo? method = parameters.Length == 0
            ? AccessTools.DeclaredMethod(type, name)
            : AccessTools.DeclaredMethod(type, name, parameters);
        return method ?? throw new MissingMethodException(type.FullName, name);
    }

    /// <summary>Finds the single method whose name contains the given fragment (used for
    /// compiler-generated local functions whose ordinal suffix changes between builds).</summary>
    public static MethodInfo FindMethodContaining(Type type, string fragment)
    {
        MethodInfo[] matches = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.Name.Contains(fragment, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new MissingMethodException(type.FullName, $"*{fragment}* (found {matches.Length})");
        return matches[0];
    }

    public static FieldInfo FindField(Type type, string name) =>
        AccessTools.DeclaredField(type, name) ?? throw new MissingFieldException(type.FullName, name);

    public static HarmonyMethod Declared(Type type, string name) =>
        new(AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name));

    public static void AssertCount(int actual, int expected, string what)
    {
        if (actual != expected)
            throw new InvalidOperationException(
                $"[LinuxCompat] Patch anchor mismatch: expected {expected} {what}, found {actual}. " +
                "The game update likely moved this patch target.");
    }

    public static bool CallsMethod(CodeInstruction instruction, MethodBase method) =>
        (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
        && instruction.operand is MethodBase operand
        && MethodsMatch(operand, method);

    /// <summary>Compares methods by declaring type and signature so tokens resolved through
    /// different reflection paths still match.</summary>
    private static bool MethodsMatch(MethodBase left, MethodBase right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left.Name != right.Name || left.DeclaringType != right.DeclaringType)
            return false;
        ParameterInfo[] leftParameters = left.GetParameters();
        ParameterInfo[] rightParameters = right.GetParameters();
        if (leftParameters.Length != rightParameters.Length)
            return false;
        for (int i = 0; i < leftParameters.Length; i++)
            if (leftParameters[i].ParameterType != rightParameters[i].ParameterType)
                return false;
        return true;
    }

    /// <summary>Replaces every call to <paramref name="from"/> with a call to the static
    /// stack-compatible adapter <paramref name="to"/>, asserting the expected match count.</summary>
    public static List<CodeInstruction> ReplaceCalls(
        IEnumerable<CodeInstruction> instructions, MethodBase from, MethodInfo to, int expected, string what)
    {
        List<CodeInstruction> result = [.. instructions];
        int count = 0;
        for (int i = 0; i < result.Count; i++)
        {
            if (!CallsMethod(result[i], from))
                continue;
            CodeInstruction replacement = new(OpCodes.Call, to);
            replacement.labels.AddRange(result[i].labels);
            replacement.blocks.AddRange(result[i].blocks);
            result[i] = replacement;
            count++;
        }
        AssertCount(count, expected, what);
        return result;
    }
}

/// <summary>
/// Reusable transpiler that replaces calls to one method with a static adapter taking the
/// same stack shape. Configured through static fields immediately before each Patch call;
/// Harmony runs the transpiler synchronously inside Patch, so this is safe.
/// </summary>
internal static class CallReplacementTranspiler
{
    private static MethodBase _from = null!;
    private static MethodInfo _to = null!;
    private static int _expected;

    public static void Apply(Harmony harmony, MethodBase target, MethodBase from, MethodInfo to, int expected)
    {
        _from = from;
        _to = to;
        _expected = expected;
        harmony.Patch(target, transpiler: InstallTools.Declared(typeof(CallReplacementTranspiler), nameof(Transpiler)));
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        InstallTools.ReplaceCalls(instructions, _from, _to, _expected, $"call(s) to {_from.Name} in {original.Name}");
}
