using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Vortice.Dxc;

namespace LinuxCompat.Platform;

internal static class LinuxNativeLibraryResolver
{
    private static readonly Dictionary<string, nint> Handles = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly Lazy<string?> WrapperCacheDirectory = new(CreateWrapperCacheDirectory);
    private static readonly Lazy<string[]> NativeDirectories = new(FindNativeDirectories);

    public static void Install()
    {
        SetEnvironmentVariable("DXVK_WSI_DRIVER", "SDL3");
        PreloadSdl();
        if (IsEnabled("SE2_CPU_RENDERING"))
        {
            SetEnvironmentVariable("VK_LOADER_DRIVERS_SELECT", "*lvp*");
            SetEnvironmentVariable("DXVK_FILTER_DEVICE_NAME", "llvmpipe");
            SetEnvironmentVariable("VKD3D_FILTER_DEVICE_NAME", "llvmpipe");
            SetEnvironmentVariable("VKD3D_FEATURE_LEVEL", "12_0");
            SetEnvironmentVariable(
                "LP_NUM_THREADS",
                Math.Min(4, Environment.ProcessorCount).ToString()
            );
        }

        string core = GetPath("SE2_D3D12CORE_LIBRARY", NativePath("libvkd3d-proton-d3d12core.so"));
        Load(core);
        PrependDxcResolver();
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += Resolve;
    }

    private static void PrependDxcResolver()
    {
        // Event subscription appends, but the Linux resolver must run before the game's resolver.
        FieldInfo field =
            typeof(Dxc).GetField("ResolveLibrary", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(Dxc).FullName, "ResolveLibrary");
        var current = (DllImportResolver?)field.GetValue(null);
        field.SetValue(null, (DllImportResolver)ResolveDxc + current);
    }

    private static nint ResolveDxc(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath
    ) =>
        libraryName.Equals("dxcompiler.dll", StringComparison.OrdinalIgnoreCase)
            ? Load(GetPath("SE2_DXCOMPILER_LIBRARY", NativePath("libSE2DxcCompiler.so")))
            : 0;

    /// <summary>
    /// SDL3 is dlopen-ed by DXVK's SDL3 WSI backend by soname and P/Invoked by the SDL
    /// platform code. Loading the bundled build by absolute path first makes both the
    /// dynamic linker and the managed resolver reuse the same instance.
    /// </summary>
    private static void PreloadSdl()
    {
        if (NativeLibrary.TryLoad("libSDL3.so.0", out nint handle))
        {
            Handles["libSDL3.so.0"] = handle;
            return;
        }
        Load(GetPath("SE2_SDL3_LIBRARY", NativePath("libSDL3.so.0")));
    }

    private static nint Resolve(Assembly assembly, string libraryName)
    {
        if (libraryName is "libSDL3.so.0" or "libSDL3.so" or "SDL3")
            return Load(GetPath("SE2_SDL3_LIBRARY", NativePath("libSDL3.so.0")));
        if (libraryName.Equals("VRage.Physics.Native.dll", StringComparison.OrdinalIgnoreCase))
            return LoadWrapper(
                GetPath("SE2_PHYSICS_LIBRARY", NativePath("libVRage.Physics.Native.so")),
                libraryName
            );
        if (libraryName.Equals("VRage.Voxels.Native.dll", StringComparison.OrdinalIgnoreCase))
            return LoadWrapper(
                GetPath("SE2_VOXELS_LIBRARY", NativePath("libVRage.Voxels.Native.so")),
                libraryName
            );
        if (libraryName.Equals("VRage.Slug.Native.dll", StringComparison.OrdinalIgnoreCase))
            return LoadWrapper(
                GetPath("SE2_SLUG_LIBRARY", NativePath("libVRage.Slug.Native.so")),
                libraryName
            );

        string? path = libraryName.ToLowerInvariant() switch
        {
            "dxgi" or "dxgi.dll" => GetPath("SE2_DXGI_LIBRARY", NativePath("libdxvk_dxgi.so")),
            "d3d12" or "d3d12.dll" => GetPath(
                "SE2_D3D12_LIBRARY",
                NativePath("libvkd3d-proton-d3d12.so")
            ),
            "d3d12core" or "d3d12core.dll" => GetPath(
                "SE2_D3D12CORE_LIBRARY",
                NativePath("libvkd3d-proton-d3d12core.so")
            ),
            "dxcompiler" or "dxcompiler.dll" => GetPath(
                "SE2_DXCOMPILER_LIBRARY",
                NativePath("libSE2DxcCompiler.so")
            ),
            "steam_api" or "steam_api64" => GetPath(
                "SE2_STEAM_API_LIBRARY",
                NativePath("libsteam_api.so")
            ),
            "vrage.kytherav2.native.dll" => GetPath(
                "SE2_KYTHERA_LIBRARY",
                NativePath("libVRage.KytheraV2.Native.so")
            ),
            "fmod" => GetPath("SE2_FMOD_LIBRARY", NativePath("libfmod.so.14")),
            "fmodstudio" => GetPath("SE2_FMOD_STUDIO_LIBRARY", NativePath("libfmodstudio.so.14")),
            _ => null,
        };
        return path == null ? 0 : Load(path);
    }

    private static unsafe nint LoadWrapper(string path, string originalName)
    {
        nint handle = Load(path);
        string original = Path.Combine(Environment.CurrentDirectory, originalName);
        if (!File.Exists(original))
            throw new FileNotFoundException("The original native library was not found.", original);

        string? cacheDirectory = WrapperCacheDirectory.Value;
        string? sidecar =
            cacheDirectory == null ? null : Path.Combine(cacheDirectory, originalName);
        nint originalUtf8 = Marshal.StringToCoTaskMemUTF8(original);
        nint sidecarUtf8 = Marshal.StringToCoTaskMemUTF8(sidecar);
        try
        {
            ((delegate* unmanaged[Cdecl]<nint, nint, void>)NativeLibrary.GetExport(handle, "Init"))(
                originalUtf8,
                sidecarUtf8
            );
            Console.WriteLine(
                $"[LinuxCompat] initialized {originalName}: {original} (sidecar: {sidecar ?? "<none>"})"
            );
        }
        finally
        {
            Marshal.FreeCoTaskMem(originalUtf8);
            Marshal.FreeCoTaskMem(sidecarUtf8);
        }
        return handle;
    }

    private static string? CreateWrapperCacheDirectory()
    {
        // Beside the game's data folder, not inside a -appData: profile: the cache belongs to
        // the installed wrappers, not to the user data the argument switches between.
        string directory = Path.Combine(LinuxDataFolder.Root, "NativeWrapperCache");
        if (TryCreateDirectory(directory))
            return directory;

        directory = Path.Combine(
            Path.GetTempPath(),
            $"SpaceEngineers2-NativeWrapperCache-{Environment.UserName}"
        );
        return TryCreateDirectory(directory) ? directory : null;
    }

    private static bool TryCreateDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"[LinuxCompat] WARNING: cannot create NativeWrapperCache at {directory}: {exception.Message}"
            );
            return false;
        }
    }

    private static nint Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Linux compatibility library was not found.", path);
        if (!Handles.TryGetValue(path, out nint handle))
            Handles[path] = handle = NativeLibrary.Load(path);
        return handle;
    }

    private static string GetPath(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : fallback;

    /// <summary>
    /// Locates a bundled native library. Under Pulsar the libraries arrive as plugin assets in
    /// subdirectories next to the compiled plugin assembly; the recompiled prototype places them
    /// in <c>native/</c> next to the executable. Distribution archives name the versioned
    /// libraries without their version suffix (for example <c>libfmod.so</c> for
    /// <c>libfmod.so.14</c>), so each directory is probed with both spellings.
    /// </summary>
    private static string NativePath(string fileName)
    {
        string unversioned = TrimVersionSuffix(fileName);
        foreach (string directory in NativeDirectories.Value)
        {
            string path = Path.Combine(directory, fileName);
            if (File.Exists(path))
                return path;
            if (unversioned != fileName && File.Exists(path = Path.Combine(directory, unversioned)))
                return path;
        }
        return Path.Combine(AppContext.BaseDirectory, "native", fileName);
    }

    private static string TrimVersionSuffix(string fileName)
    {
        int marker = fileName.LastIndexOf(".so.", StringComparison.Ordinal);
        return marker > 0 && fileName.Skip(marker + 4).All(char.IsDigit)
            ? fileName.Substring(0, marker + 3)
            : fileName;
    }

    private static string[] FindNativeDirectories()
    {
        List<string> directories = [];
        void Add(string? directory)
        {
            if (
                directory is { Length: > 0 }
                && Directory.Exists(directory)
                && !directories.Contains(directory, StringComparer.Ordinal)
            )
                directories.Add(directory);
        }

        Add(Environment.GetEnvironmentVariable("SE2_NATIVE_DIR"));
        // Pulsar copies Bin-placed plugin assets flat next to the compiled plugin assembly.
        string? pluginDirectory = Path.GetDirectoryName(
            typeof(LinuxNativeLibraryResolver).Assembly.Location
        );
        if (pluginDirectory is { Length: > 0 })
        {
            Add(pluginDirectory);
            Add(Path.Combine(pluginDirectory, "native"));
        }
        Add(Path.Combine(AppContext.BaseDirectory, "native"));
        Console.WriteLine(
            $"[LinuxCompat] native library directories: {string.Join(", ", directories)}"
        );
        return directories.ToArray();
    }

    internal static bool IsEnabled(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { } value
        && (value == "1" || bool.TryParse(value, out bool enabled) && enabled);

    private static void SetEnvironmentVariable(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (setenv(name, value, overwrite: 1) != 0)
            throw new InvalidOperationException(
                $"Failed to set native environment variable {name}."
            );
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);
}
