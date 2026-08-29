using Vortice.Direct3D12;

namespace LinuxCompat.Patches.Rendering;

public static class D3D12DescriptorHeapPatch
{
    public static unsafe DescriptorHeapDescription GetDescription(ID3D12DescriptorHeap heap)
    {
        DescriptorHeapDescription result = default;
        nint* vtable = *(nint**)heap.NativePointer;
        ((delegate* unmanaged[Stdcall]<nint, DescriptorHeapDescription*, void*>)vtable[8])(
            heap.NativePointer,
            &result
        );
        return result;
    }
}
