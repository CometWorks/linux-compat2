using Vortice.Direct3D12;

namespace LinuxCompat.Patches;

public static class FrameDispatcherPatch
{
    public static bool PollFence(ID3D12Fence fence, ulong value, int timeoutMilliseconds)
        => SpinWait.SpinUntil(() => fence.CompletedValue >= value, timeoutMilliseconds);
}
