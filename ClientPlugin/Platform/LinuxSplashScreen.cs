using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LinuxCompat.Platform;

internal static unsafe class LinuxSplashScreen
{
    private const string Sdl = "libSDL3.so.0";
    private const uint EventWindowExposed = 0x204;
    private const uint EventWindowPixelSizeChanged = 0x207;
    private const uint EventWindowDisplayScaleChanged = 0x214;
    private const ulong WindowHidden = 0x8;
    private const ulong WindowBorderless = 0x10;
    private const ulong WindowHighPixelDensity = 0x2000;
    private const ulong WindowAlwaysOnTop = 0x10000;
    private const ulong WindowTransparent = 0x40000000;
    private const ulong WindowNotFocusable = 0x80000000;
    private const int BlendModeBlend = 1;
    private const uint PixelFormatRgba32 = 0x16762004;
    private static readonly int WindowPositionCentered = unchecked((int)0x2FFF0000);
    private static nint _window;
    private static uint _windowId;
    private static byte[]? _pixels;
    private static int _width;
    private static int _height;

    internal static string? Status { get; private set; }

    internal static void Show(string imagePath, string iconPath)
    {
        if (!File.Exists(imagePath))
            return;

        try
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(imagePath);
            byte[] pixels = new byte[checked(image.Width * image.Height * 4)];
            image.CopyPixelDataTo(pixels);
            SdlThread.Invoke(() => ShowCore(pixels, image.Width, image.Height, iconPath));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[LinuxCompat] Could not show the splash screen: {exception.Message}");
        }
    }

    internal static void Close()
    {
        if (_window == 0)
            return;
        SdlThread.Invoke(CloseCore);
        Console.WriteLine("[LinuxCompat] Splash closed.");
    }

    private static void ShowCore(byte[] pixels, int width, int height, string iconPath)
    {
        CloseCore();
        _pixels = pixels;
        _width = width;
        _height = height;
        _window = SDL_CreateWindow("Space Engineers 2", width, height,
            WindowHidden | WindowBorderless | WindowHighPixelDensity | WindowAlwaysOnTop | WindowTransparent | WindowNotFocusable);
        if (_window == 0)
            throw new InvalidOperationException($"SDL splash window creation failed: {GetError()}");
        try
        {
            Resize();
            if (!SdlThread.IsWayland)
                SDL_SetWindowPosition(_window, WindowPositionCentered, WindowPositionCentered);
            if (!SDL_ShowWindow(_window))
                throw new InvalidOperationException($"SDL splash show failed: {GetError()}");
            if (!SDL_SyncWindow(_window))
                throw new InvalidOperationException($"SDL splash synchronization failed: {GetError()}");
            bool iconApplied = SdlPlatformWindow.ApplyWindowIcon(_window, iconPath);
            (int logicalWidth, int logicalHeight, int pixelWidth, int pixelHeight, float density) = Resize();
            Draw();
            if (Math.Abs(pixelWidth - width) > 1 || Math.Abs(pixelHeight - height) > 1)
                throw new InvalidOperationException($"Splash drawable is {pixelWidth}x{pixelHeight}, expected {width}x{height}.");

            _windowId = SDL_GetWindowID(_window);
            SdlThread.Event += HandleEvent;
            Status = $"driver={SdlThread.VideoDriver ?? "unknown"}, image={width}x{height}, logical={logicalWidth}x{logicalHeight}, pixels={pixelWidth}x{pixelHeight}, density={density:0.##}, icon={iconApplied.ToString().ToLowerInvariant()}";
            Console.WriteLine($"[LinuxCompat] Splash displayed: {Status}");
        }
        catch
        {
            CloseCore();
            throw;
        }
    }

    private static void CloseCore()
    {
        if (_window == 0)
            return;
        SdlThread.Event -= HandleEvent;
        SDL_DestroyWindow(_window);
        _window = 0;
        _windowId = 0;
        _pixels = null;
        Status = null;
    }

    private static void HandleEvent(ref SdlThread.SdlEvent e)
    {
        if (e.Window.WindowId != _windowId)
            return;
        if (e.Type == EventWindowDisplayScaleChanged)
            Resize();
        if (e.Type is EventWindowExposed or EventWindowPixelSizeChanged or EventWindowDisplayScaleChanged)
            Draw();
    }

    private static (int LogicalWidth, int LogicalHeight, int PixelWidth, int PixelHeight, float Density) Resize()
    {
        float density = SDL_GetWindowPixelDensity(_window);
        if (density <= 0)
            density = 1;
        int logicalWidth = Math.Max(1, (int)MathF.Round(_width / density));
        int logicalHeight = Math.Max(1, (int)MathF.Round(_height / density));
        if (!SDL_SetWindowSize(_window, logicalWidth, logicalHeight))
            throw new InvalidOperationException($"SDL splash resize failed: {GetError()}");
        if (SdlThread.IsWayland && !SDL_SyncWindow(_window))
            throw new InvalidOperationException($"SDL splash resize synchronization failed: {GetError()}");
        if (!SDL_GetWindowSizeInPixels(_window, out int pixelWidth, out int pixelHeight))
            throw new InvalidOperationException($"SDL splash drawable query failed: {GetError()}");
        return (logicalWidth, logicalHeight, pixelWidth, pixelHeight, density);
    }

    private static void Draw()
    {
        fixed (byte* data = _pixels)
        {
            nint source = SDL_CreateSurfaceFrom(_width, _height, PixelFormatRgba32, data, _width * 4);
            if (source == 0)
                throw new InvalidOperationException($"SDL splash surface creation failed: {GetError()}");
            try
            {
                nint destination = SDL_GetWindowSurface(_window);
                if (destination == 0 || !SDL_GetWindowSizeInPixels(_window, out int width, out int height))
                    throw new InvalidOperationException($"SDL splash window surface failed: {GetError()}");
                var destinationRect = new SdlRect(0, 0, width, height);
                if (!SDL_FillSurfaceRect(destination, null, 0)
                    || !SDL_SetSurfaceBlendMode(source, BlendModeBlend)
                    || !SDL_BlitSurfaceScaled(source, null, destination, &destinationRect, 1)
                    || !SDL_UpdateWindowSurface(_window))
                    throw new InvalidOperationException($"SDL splash drawing failed: {GetError()}");
            }
            finally
            {
                SDL_DestroySurface(source);
            }
        }
    }

    private static string GetError() => Marshal.PtrToStringUTF8(SDL_GetError()) ?? "unknown error";

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SdlRect(int x, int y, int width, int height)
    {
        public readonly int X = x;
        public readonly int Y = y;
        public readonly int Width = width;
        public readonly int Height = height;
    }

    [DllImport(Sdl, EntryPoint = "SDL_CreateWindow", CharSet = CharSet.Ansi)]
    private static extern nint SDL_CreateWindow(string title, int width, int height, ulong flags);

    [DllImport(Sdl, EntryPoint = "SDL_DestroyWindow")]
    private static extern void SDL_DestroyWindow(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_ShowWindow")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_ShowWindow(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_SetWindowPosition")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowPosition(nint window, int x, int y);

    [DllImport(Sdl, EntryPoint = "SDL_SetWindowSize")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowSize(nint window, int width, int height);

    [DllImport(Sdl, EntryPoint = "SDL_SyncWindow")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SyncWindow(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_GetWindowPixelDensity")]
    private static extern float SDL_GetWindowPixelDensity(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_GetWindowSizeInPixels")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetWindowSizeInPixels(nint window, out int width, out int height);

    [DllImport(Sdl, EntryPoint = "SDL_GetWindowID")]
    private static extern uint SDL_GetWindowID(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_GetWindowSurface")]
    private static extern nint SDL_GetWindowSurface(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_UpdateWindowSurface")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_UpdateWindowSurface(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_CreateSurfaceFrom")]
    private static extern nint SDL_CreateSurfaceFrom(int width, int height, uint format, void* pixels, int pitch);

    [DllImport(Sdl, EntryPoint = "SDL_BlitSurfaceScaled")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_BlitSurfaceScaled(nint source, SdlRect* sourceRect, nint destination, SdlRect* destinationRect, int scaleMode);

    [DllImport(Sdl, EntryPoint = "SDL_FillSurfaceRect")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_FillSurfaceRect(nint surface, SdlRect* rect, uint color);

    [DllImport(Sdl, EntryPoint = "SDL_SetSurfaceBlendMode")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetSurfaceBlendMode(nint surface, int blendMode);

    [DllImport(Sdl, EntryPoint = "SDL_DestroySurface")]
    private static extern void SDL_DestroySurface(nint surface);

    [DllImport(Sdl, EntryPoint = "SDL_GetError")]
    private static extern nint SDL_GetError();
}
