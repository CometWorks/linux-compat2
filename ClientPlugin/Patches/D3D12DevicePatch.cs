using Vortice.Direct3D12;

namespace LinuxCompat.Patches;

public static class D3D12DevicePatch
{
    public static unsafe ResourceAllocationInfo GetResourceAllocationInfo(ID3D12Device device, params ResourceDescription[] descriptions)
    {
        ResourceAllocationInfo result = default;
        fixed (ResourceDescription* description = descriptions)
        {
            nint* vtable = *(nint**)device.NativePointer;
            ((delegate* unmanaged[Stdcall]<nint, ResourceAllocationInfo*, uint, uint, ResourceDescription*, void*>)vtable[25])(
                device.NativePointer, &result, 0, (uint)descriptions.Length, description);
        }
        return result;
    }
}
