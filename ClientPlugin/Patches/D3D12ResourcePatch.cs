using Vortice.Direct3D12;

namespace LinuxCompat.Patches;

public static class D3D12ResourcePatch
{
    public static unsafe ResourceDescription GetDescription(ID3D12Resource resource)
    {
        if (!OperatingSystem.IsLinux())
            return resource.Description;

        ResourceDescription result = default;
        nint* vtable = *(nint**)resource.NativePointer;
        ((delegate* unmanaged[Stdcall]<nint, ResourceDescription*, void*>)vtable[10])(resource.NativePointer, &result);
        return result;
    }
}
