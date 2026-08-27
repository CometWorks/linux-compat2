using HarmonyLib;
using Keen.VRage.Core.Input;
using Keen.VRage.Input;
using InputDescriptionExtensions = Keen.VRage.Input.Extensions.InputExtensions;

namespace LinuxCompat.Patches.PlatformGuards;

[HarmonyPatch(
    typeof(InputDescriptionExtensions),
    nameof(InputDescriptionExtensions.GetDescription)
)]
[HarmonyPatchCategory("Finish")]
internal static class InputExtensionsGetDescriptionPatch
{
    static bool Prefix(ref InputId input, ref InputDescription __result)
    {
        if (
            !input.IsGeneric
            || !Keen.VRage.Library.Utils.ManualSingleton<InputDeviceManager>.HasValue
            || Keen.VRage.Library.Utils.Singleton<InputDeviceManager>.Instance.GetDefaultDeviceClass(
                input.GenericClass
            ) != null
        )
            return true;

        string text = input.ToString();
        __result = new InputDescription(text, text);
        return false;
    }
}
