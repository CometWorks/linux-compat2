using HarmonyLib;
using Keen.VRage.Core.Input;
using Keen.VRage.Input;
using InputDescriptionExtensions = Keen.VRage.Input.Extensions.InputExtensions;

namespace LinuxCompat.Patches;

internal static class InputExtensionsPatch
{
    public static void Install()
    {
        new Harmony("LinuxCompat.InputDescriptions").Patch(
            AccessTools.DeclaredMethod(typeof(InputDescriptionExtensions), nameof(InputDescriptionExtensions.GetDescription),
                [typeof(InputId).MakeByRefType()])!,
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(InputExtensionsPatch), nameof(Prefix))!));
    }

    private static bool Prefix(ref InputId input, ref InputDescription __result)
    {
        if (!OperatingSystem.IsLinux() || !input.IsGeneric
            || !Keen.VRage.Library.Utils.ManualSingleton<InputDeviceManager>.HasValue
            || Keen.VRage.Library.Utils.Singleton<InputDeviceManager>.Instance.GetDefaultDeviceClass(input.GenericClass) != null)
            return true;

        string text = input.ToString();
        __result = new InputDescription(text, text);
        return false;
    }
}
