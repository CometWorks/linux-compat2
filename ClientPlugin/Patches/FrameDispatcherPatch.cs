using Vortice.Direct3D12;

namespace LinuxCompat.Patches;

public static class FrameDispatcherPatch
{
    public static bool TryWaitForFence(ID3D12Fence fence, ulong value, int timeoutMilliseconds, out bool completed)
    {
        if (!OperatingSystem.IsLinux())
        {
            completed = false;
            return false;
        }

        completed = SpinWait.SpinUntil(() => fence.CompletedValue >= value, timeoutMilliseconds);
        return true;
    }
}
