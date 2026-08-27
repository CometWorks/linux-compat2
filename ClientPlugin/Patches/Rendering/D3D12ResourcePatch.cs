using Vortice.Direct3D12;

namespace LinuxCompat.Patches.Rendering;

public static class D3D12ResourcePatch
{
    public static unsafe ResourceDescription GetDescription(ID3D12Resource resource)
    {
        ResourceDescription result = default;
        nint* vtable = *(nint**)resource.NativePointer;
        ((delegate* unmanaged[Stdcall]<nint, ResourceDescription*, void*>)vtable[10])(
            resource.NativePointer,
            &result
        );
        return result;
    }
}
