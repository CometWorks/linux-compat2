using System.Runtime.InteropServices;
using System.Text;

namespace LinuxCompat.Platform;

/// <summary>
/// Reads the physical device type of each GPU straight from Vulkan.
///
/// DXVK derives its DXGI adapter description from the Vulkan device name, so the names match
/// exactly and can be used to look an adapter up here. What DXVK does not carry over is
/// whether the device is integrated, which the game needs in order to rank adapters, and the
/// vendor APIs that would otherwise supply a teraflop figure (NVAPI, AGS) are unavailable on
/// Linux. Vulkan is the authority DXVK itself is built on, so asking it directly is exact.
/// </summary>
internal static class VulkanAdapterTypes
{
    private const string Vulkan = "libvulkan.so.1";

    private const uint StructureTypeInstanceCreateInfo = 1;
    private const uint PhysicalDeviceTypeIntegratedGpu = 1;
    private const uint PhysicalDeviceTypeCpu = 4;

    // Offsets into VkPhysicalDeviceProperties: apiVersion, driverVersion, vendorID and
    // deviceID occupy the first 16 bytes, then deviceType, then the 256 byte deviceName.
    private const int DeviceTypeOffset = 16;
    private const int DeviceNameOffset = 20;
    private const int DeviceNameLength = 256;

    // Generously larger than VkPhysicalDeviceProperties (824 bytes on x86-64); only the
    // leading fields are read, but the driver writes the whole structure.
    private const int PropertiesSize = 4096;

    private static readonly Lazy<Dictionary<string, uint>> DeviceTypes = new(Query);

    /// <summary>
    /// True when Vulkan reports the named device as an integrated GPU, false when it reports
    /// it as something else, and null when the device is unknown or Vulkan is unavailable.
    /// </summary>
    public static bool? IsIntegrated(string deviceName) =>
        DeviceTypes.Value.TryGetValue(deviceName, out uint type)
            ? type == PhysicalDeviceTypeIntegratedGpu
            : null;

    /// <summary>
    /// True when Vulkan reports the named device as a software rasterizer running on the CPU.
    /// </summary>
    public static bool? IsSoftware(string deviceName) =>
        DeviceTypes.Value.TryGetValue(deviceName, out uint type)
            ? type == PhysicalDeviceTypeCpu
            : null;

    private static unsafe Dictionary<string, uint> Query()
    {
        Dictionary<string, uint> result = new(StringComparer.Ordinal);
        nint instance = 0;
        try
        {
            byte* createInfo = stackalloc byte[64];
            new Span<byte>(createInfo, 64).Clear();
            *(uint*)createInfo = StructureTypeInstanceCreateInfo;

            if (vkCreateInstance(createInfo, null, &instance) != 0 || instance == 0)
                return result;

            uint count = 0;
            if (vkEnumeratePhysicalDevices(instance, &count, null) != 0 || count == 0)
                return result;

            nint[] devices = new nint[count];
            fixed (nint* devicePointer = devices)
            {
                if (vkEnumeratePhysicalDevices(instance, &count, devicePointer) != 0)
                    return result;
            }

            byte* properties = stackalloc byte[PropertiesSize];
            foreach (nint device in devices.Take((int)count))
            {
                new Span<byte>(properties, PropertiesSize).Clear();
                vkGetPhysicalDeviceProperties(device, properties);

                var name = new ReadOnlySpan<byte>(properties + DeviceNameOffset, DeviceNameLength);
                int end = name.IndexOf((byte)0);
                string deviceName = Encoding.UTF8.GetString(end < 0 ? name : name[..end]);
                if (deviceName.Length != 0)
                    result[deviceName] = *(uint*)(properties + DeviceTypeOffset);
            }

            Console.WriteLine(
                "[LinuxCompat] Vulkan device types: "
                    + string.Join(
                        ", ",
                        result.Select(entry => $"{entry.Key}={DescribeType(entry.Value)}")
                    )
            );
        }
        catch (Exception exception)
        {
            // Any failure just leaves the game's own detection in place.
            Console.Error.WriteLine(
                $"[LinuxCompat] WARNING: cannot query Vulkan device types: {exception.Message}"
            );
        }
        finally
        {
            if (instance != 0)
            {
                try
                {
                    vkDestroyInstance(instance, null);
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            }
        }
        return result;
    }

    private static string DescribeType(uint type) =>
        type switch
        {
            0 => "other",
            1 => "integrated",
            2 => "discrete",
            3 => "virtual",
            4 => "cpu",
            _ => type.ToString(),
        };

    [DllImport(Vulkan)]
    private static extern unsafe int vkCreateInstance(
        byte* createInfo,
        void* allocator,
        nint* instance
    );

    [DllImport(Vulkan)]
    private static extern unsafe void vkDestroyInstance(nint instance, void* allocator);

    [DllImport(Vulkan)]
    private static extern unsafe int vkEnumeratePhysicalDevices(
        nint instance,
        uint* count,
        nint* devices
    );

    [DllImport(Vulkan)]
    private static extern unsafe void vkGetPhysicalDeviceProperties(nint device, byte* properties);
}
