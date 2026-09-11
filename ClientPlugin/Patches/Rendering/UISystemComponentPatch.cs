using Keen.VRage.Render.FrameData;

namespace ClientPlugin.Patches.Rendering;

public static class UISystemComponentPatch
{
    public static void Postfix(RenderDrawCommandBuffer drawBatch, int sortLayer) =>
        MainUISystemPatch.BatchSubmitted(drawBatch, sortLayer);
}
