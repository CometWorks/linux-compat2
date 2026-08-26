using System.Runtime.InteropServices;
using Keen.VRage.Core.Platform;
using Keen.VRage.Library.Mathematics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LinuxCompat.Platform;

public sealed unsafe class SdlPlatformWindow : IPlatformWindow
{
    private const string Sdl = "libSDL3.so.0";
    private const uint EventQuit = 0x100;
    private const uint EventWindowResized = 0x206;
    private const uint EventWindowPixelSizeChanged = 0x207;
    private const uint EventWindowCloseRequested = 0x210;
    private const uint EventWindowFocusLost = 0x20F;
    private const uint EventWindowDisplayChanged = 0x213;
    private const uint EventWindowDisplayScaleChanged = 0x214;
    private const uint EventKeyDown = 0x300;
    private const uint EventKeyUp = 0x301;
    private const uint EventTextEditing = 0x302;
    private const uint EventTextInput = 0x303;
    private const uint EventMouseMotion = 0x400;
    private const uint EventMouseButtonDown = 0x401;
    private const uint EventMouseButtonUp = 0x402;
    private const uint EventMouseWheel = 0x403;
    private const ulong WindowFullscreen = 0x1;
    private const ulong WindowOccluded = 0x4;
    private const ulong WindowHidden = 0x8;
    private const ulong WindowResizable = 0x20;
    private const ulong WindowMinimized = 0x40;
    private const ulong WindowInputFocus = 0x200;
    private const ulong WindowHighPixelDensity = 0x2000;
    private const ulong WindowVulkan = 0x10000000;
    private const uint PixelFormatRgba32 = 0x16762004;
    private const uint PixelFormatBgra32 = 0x16362004;
    private static string? _pendingIconPath;
    private static readonly object TextInputSync = new();
    private static SdlPlatformWindow? _textInputWindow;
    private static SdlPlatformWindow? _renderWindow;
    private static bool _textInputActive;
    private static (double X, double Y, double Width, double Height) _textInputArea;

    private nint _window;
    private uint _windowId;
    private readonly string? _iconPath;
    private int _iconDirty;
    private int _iconApplied;
    private int _exitRequested;
    private long _windowFlags;
    private readonly object _snapshotLock = new();
    private int _clientWidth;
    private int _clientHeight;
    private int _windowWidth;
    private int _windowHeight;
    private int _mouseX;
    private int _mouseY;
    private nint _cursor;
    private volatile bool _showCursor = true;
    private volatile bool _captureCursor;
    private int _cursorStateDirty = 1;
    private readonly object _cursorLock = new();
    private (byte[] Pixels, int Width, int Height, int HotspotX, int HotspotY)? _pendingCursorImage;
    private StandardPlatformCursor? _pendingStandardCursor;
    private int _cursorImageDirty;
    private volatile bool _showAllowed = true;
    private Action? _fullscreenToggle;
    private int _renderTargetWidth;
    private int _renderTargetHeight;
    private bool _resizePending;

    public SdlPlatformWindow(string title, int width, int height)
    {
        _renderTargetWidth = width;
        _renderTargetHeight = height;
        _iconPath = Interlocked.Exchange(ref _pendingIconPath, null)
            ?? Path.Combine(Environment.CurrentDirectory, "Game2.ico");
        SdlThread.Invoke(() =>
        {
            _window = SDL_CreateWindow(title, width, height,
                WindowHidden | WindowResizable | WindowHighPixelDensity | WindowVulkan);
            if (_window == 0)
                throw new InvalidOperationException($"SDL window creation failed: {GetError()}");
            _windowId = SDL_GetWindowID(_window);
            SdlThread.Event += HandleEvent;
            SdlThread.Tick += OnSdlTick;
            RefreshSnapshot();
            AttachTextInputWindow(this);
            Volatile.Write(ref _renderWindow, this);
        });
    }

    public nint WindowHandle => _window;
    public bool DrawEnabled => ((ulong)Interlocked.Read(ref _windowFlags) & (WindowHidden | WindowMinimized | WindowOccluded)) == 0;
    public bool IsActive => ((ulong)Interlocked.Read(ref _windowFlags) & (WindowInputFocus | WindowMinimized)) == WindowInputFocus;
    public Vector2I ClientSize
    {
        get
        {
            lock (_snapshotLock)
                return new Vector2I(_clientWidth, _clientHeight);
        }
    }
    public Vector2 ClientMousePosition
    {
        get
        {
            float x;
            float y;
            lock (_snapshotLock)
            {
                x = BitConverter.Int32BitsToSingle(_mouseX);
                y = BitConverter.Int32BitsToSingle(_mouseY);
            }
            return ScaleMousePosition(x, y);
        }
    }
    public int RepeatedInputDelay => 1;
    public int RepeatedInputSpeed => 20;
    public bool IsFullscreen => ((ulong)Interlocked.Read(ref _windowFlags) & WindowFullscreen) != 0;

    public bool ShowCursor
    {
        get => _showCursor;
        set
        {
            _showCursor = value;
            Interlocked.Exchange(ref _cursorStateDirty, 1);
        }
    }

    public bool CaptureCursor
    {
        get => _captureCursor;
        set
        {
            _captureCursor = value;
            Interlocked.Exchange(ref _cursorStateDirty, 1);
        }
    }

    public event Action? OnExit;

    public bool NextFrame()
    {
        SdlThread.SyncFrame();
        if (Interlocked.Exchange(ref _exitRequested, 0) != 0)
            OnExit?.Invoke();
        return _window != 0;
    }

    public void SetCursor(Stream stream) => SetCursor(stream, 0, 0);

    public void SetCursor(StandardPlatformCursor cursor)
    {
        lock (_cursorLock)
        {
            _pendingCursorImage = null;
            _pendingStandardCursor = cursor;
        }
        Interlocked.Exchange(ref _cursorImageDirty, 1);
    }

    public void SetCursor(Stream stream, int hotspotX, int hotspotY)
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(stream);
        byte[] pixels = new byte[checked(image.Width * image.Height * 4)];
        image.CopyPixelDataTo(pixels);
        lock (_cursorLock)
        {
            _pendingCursorImage = (pixels, image.Width, image.Height, hotspotX, hotspotY);
            _pendingStandardCursor = null;
        }
        Interlocked.Exchange(ref _cursorImageDirty, 1);
    }

    public void ClearCursor()
    {
        lock (_cursorLock)
        {
            _pendingCursorImage = null;
            _pendingStandardCursor = null;
        }
        Interlocked.Exchange(ref _cursorImageDirty, 1);
    }

    public void ShowAndFocus()
    {
        if (!_showAllowed)
            return;
        SdlThread.Invoke(() =>
        {
            SDL_ShowWindow(_window);
            SDL_RaiseWindow(_window);
            if (Volatile.Read(ref _iconApplied) == 0)
                Interlocked.Exchange(ref _iconDirty, 1);
        });
    }

    public void Hide() => SdlThread.Invoke(() => SDL_HideWindow(_window));

    public void OnModeChanged(bool fullscreenMode, int width, int height, bool topMost,
        bool isOutputAttachedToAdapter, in BoundingBox2I desktopBounds)
    {
        lock (_snapshotLock)
        {
            _renderTargetWidth = width;
            _renderTargetHeight = height;
        }
        BoundingBox2I bounds = desktopBounds;
        SdlThread.Invoke(() =>
        {
            bool wasFullscreen = ((ulong)SDL_GetWindowFlags(_window) & WindowFullscreen) != 0;
            SDL_SetWindowAlwaysOnTop(_window, topMost);
            SDL_SetWindowFullscreen(_window, fullscreenMode);
            // SDL_SyncWindow reports false on a timeout, which SDL documents as non-exceptional;
            // slower window managers (Cinnamon's Muffin) finish the fullscreen exit after SDL's
            // deadline. The explicit drawable measurement below corrects the size either way.
            if (wasFullscreen && !fullscreenMode && !SDL_SyncWindow(_window))
                Console.WriteLine("[LinuxCompat] SDL fullscreen exit synchronization timed out; continuing with an explicit resize.");
            bool drawableMatches = SDL_GetWindowSizeInPixels(_window, out int clientWidth, out int clientHeight)
                && Math.Abs(clientWidth - width) <= 1 && Math.Abs(clientHeight - height) <= 1;
            if (!fullscreenMode)
            {
                Vector2I? windowSize = drawableMatches ? null : ResizeWindowForRenderTarget();
                SDL_SetWindowBordered(_window, true);
                if (!SdlThread.IsWayland && windowSize.HasValue)
                    SDL_SetWindowPosition(_window,
                        bounds.Min.X + (bounds.Width - windowSize.Value.X) / 2,
                        bounds.Min.Y + (bounds.Height - windowSize.Value.Y) / 2);
            }
            SDL_SyncWindow(_window);
            RefreshSnapshot();
        });
    }

    public void UpdateRenderTargetSize(Vector2I size)
    {
        lock (_snapshotLock)
        {
            _renderTargetWidth = size.X;
            _renderTargetHeight = size.Y;
        }
    }

    public void Present(nint swapChain, int syncInterval, int flags)
    {
        void** vtable = *(void***)swapChain;
        _ = ((delegate* unmanaged<nint, uint, uint, int>)vtable[8])(swapChain, (uint)syncInterval, (uint)flags);
    }

    public int ResizeBuffers(nint swapChain, uint bufferCount, uint width, uint height, int format, uint flags)
    {
        void** vtable = *(void***)swapChain;
        return ((delegate* unmanaged<nint, uint, uint, uint, int, uint, int>)vtable[13])(
            swapChain, bufferCount, width, height, format, flags);
    }

    public void SetFullscreenToggleListener(Action listener) => _fullscreenToggle = listener;
    public void ToggleFullscreen() => _fullscreenToggle?.Invoke();

    internal void SetShowAllowed(bool allowed) => _showAllowed = allowed;
    internal static void SetWindowIcon(string path) => Interlocked.Exchange(ref _pendingIconPath, path);
    internal bool TryConsumeDrawableResize(out Vector2I size)
    {
        lock (_snapshotLock)
        {
            size = new Vector2I(_clientWidth, _clientHeight);
            if (!_resizePending || size.X <= 0 || size.Y <= 0)
                return false;
            _resizePending = false;
            return true;
        }
    }

    internal static void PrepareForSwapChain(nint windowHandle, Vector2I size)
    {
        SdlPlatformWindow? window = Volatile.Read(ref _renderWindow);
        if (!SdlThread.IsWayland || window == null || window._window != windowHandle)
            return;

        lock (window._snapshotLock)
        {
            window._renderTargetWidth = size.X;
            window._renderTargetHeight = size.Y;
        }
        SdlThread.Invoke(() =>
        {
            if (((ulong)SDL_GetWindowFlags(window._window) & WindowHidden) == 0)
                return;
            if (!SDL_ShowWindow(window._window) || !SDL_SyncWindow(window._window))
                throw new InvalidOperationException($"SDL Wayland window mapping failed: {GetError()}");
            window.ResizeWindowForRenderTarget();
            if (!SDL_SyncWindow(window._window))
                throw new InvalidOperationException($"SDL Wayland window resize failed: {GetError()}");
            if (!SDL_HideWindow(window._window) || !SDL_SyncWindow(window._window))
                throw new InvalidOperationException($"SDL Wayland window hide failed: {GetError()}");
            window.RefreshSnapshot();
            Console.WriteLine($"[LinuxCompat] SDL3 game window prepared: driver=wayland, logical={window._windowWidth}x{window._windowHeight}, pixels={window._clientWidth}x{window._clientHeight}");
        });
    }

    public void Dispose()
    {
        if (_window == 0)
            return;
        LinuxSplashScreen.Close();
        SdlThread.Invoke(() =>
        {
            SdlThread.Event -= HandleEvent;
            SdlThread.Tick -= OnSdlTick;
            DetachTextInputWindow(this);
            Interlocked.CompareExchange(ref _renderWindow, null, this);
            if (_cursor != 0)
            {
                SDL_SetCursor(SDL_GetDefaultCursor());
                SDL_DestroyCursor(_cursor);
                _cursor = 0;
            }
            SDL_DestroyWindow(_window);
            _window = 0;
            _windowId = 0;
        });
        SdlThread.Stop();
    }

    private static string GetError() => Marshal.PtrToStringUTF8(SDL_GetError()) ?? "unknown error";

    private void HandleEvent(ref SdlThread.SdlEvent e)
    {
        if (e.Type == EventQuit)
        {
            Interlocked.Exchange(ref _exitRequested, 1);
            return;
        }
        if (e.Window.WindowId != 0 && e.Window.WindowId != _windowId)
            return;
        if (e.Type == EventWindowCloseRequested)
            Interlocked.Exchange(ref _exitRequested, 1);
        else if (e.Type == EventWindowFocusLost)
            SdlInputEvents.Pending.Enqueue(new SdlInputEvent(SdlInputEventKind.FocusLost));
        else if (e.Type is EventWindowResized or EventWindowPixelSizeChanged)
        {
            RefreshSnapshot();
            if (!IsFullscreen)
            {
                lock (_snapshotLock)
                    _resizePending = _clientWidth > 0 && _clientHeight > 0;
            }
        }
        else if ((e.Type is EventWindowDisplayChanged or EventWindowDisplayScaleChanged) && !IsFullscreen)
        {
            ResizeWindowForRenderTarget();
            lock (_snapshotLock)
                _resizePending = false;
        }
        else if (e.Type is EventKeyDown or EventKeyUp)
            SdlInputEvents.Pending.Enqueue(new SdlInputEvent(SdlInputEventKind.Key,
                e.Keyboard.Scancode, e.Type == EventKeyDown));
        else if (e.Type == EventTextInput)
            SdlInputEvents.Pending.Enqueue(new SdlInputEvent(SdlInputEventKind.TextInput,
                Text: Marshal.PtrToStringUTF8(e.TextInput.Text) ?? string.Empty));
        else if (e.Type == EventTextEditing)
            SdlInputEvents.Pending.Enqueue(new SdlInputEvent(SdlInputEventKind.TextEditing,
                Code: e.TextEditing.Start,
                Text: Marshal.PtrToStringUTF8(e.TextEditing.Text) ?? string.Empty));
        else if (e.Type == EventMouseMotion)
        {
            Vector2 position = ScaleMousePosition(e.Motion.X, e.Motion.Y);
            SdlInputEvents.Pending.Enqueue(new SdlInputEvent(SdlInputEventKind.MouseMotion,
                X: position.X, Y: position.Y,
                DeltaX: e.Motion.Xrel, DeltaY: e.Motion.Yrel));
        }
        else if (e.Type is EventMouseButtonDown or EventMouseButtonUp)
            SdlInputEvents.Pending.Enqueue(new SdlInputEvent(SdlInputEventKind.MouseButton,
                e.Button.Button, e.Type == EventMouseButtonDown));
        else if (e.Type == EventMouseWheel)
        {
            float direction = e.Wheel.Direction == 1 ? -1 : 1;
            SdlInputEvents.Pending.Enqueue(new SdlInputEvent(SdlInputEventKind.MouseWheel,
                X: e.Wheel.X * direction, Y: e.Wheel.Y * direction));
        }
    }

    private void OnSdlTick()
    {
        if (Interlocked.Exchange(ref _cursorImageDirty, 0) != 0)
            ApplyCursorImage();
        if (Interlocked.Exchange(ref _cursorStateDirty, 0) != 0)
            ApplyCursorState();
        if (Interlocked.Exchange(ref _iconDirty, 0) != 0 && _iconPath != null)
        {
            if (_showAllowed)
            {
                if (ApplyWindowIcon(_window, _iconPath))
                {
                    Interlocked.Exchange(ref _iconApplied, 1);
                    Console.WriteLine("[LinuxCompat] Game window icon applied.");
                }
            }
            else
                Interlocked.Exchange(ref _iconDirty, 1);
        }
        RefreshSnapshot();
    }

    private void RefreshSnapshot()
    {
        Interlocked.Exchange(ref _windowFlags, (long)SDL_GetWindowFlags(_window));
        bool hasClientSize = SDL_GetWindowSizeInPixels(_window, out int clientWidth, out int clientHeight);
        bool hasWindowSize = SDL_GetWindowSize(_window, out int windowWidth, out int windowHeight);
        SDL_GetMouseState(out float x, out float y);
        lock (_snapshotLock)
        {
            if (hasClientSize)
            {
                _clientWidth = clientWidth;
                _clientHeight = clientHeight;
            }
            if (hasWindowSize)
            {
                _windowWidth = windowWidth;
                _windowHeight = windowHeight;
            }
            _mouseX = BitConverter.SingleToInt32Bits(x);
            _mouseY = BitConverter.SingleToInt32Bits(y);
        }
    }

    internal static bool ApplyWindowIcon(nint window, string path)
    {
        try
        {
            (byte[] pixels, int width, int height) = LoadIcon(path);
            fixed (byte* data = pixels)
            {
                nint surface = SDL_CreateSurfaceFrom(width, height, PixelFormatBgra32, data, width * 4);
                if (surface == 0)
                    throw new InvalidOperationException($"SDL icon surface creation failed: {GetError()}");
                try
                {
                    if (!SDL_SetWindowIcon(window, surface))
                        throw new InvalidOperationException($"SDL window icon failed: {GetError()}");
                }
                finally
                {
                    SDL_DestroySurface(surface);
                }
            }
            return true;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[LinuxCompat] Could not set the game icon: {exception.Message}");
            return false;
        }
    }

    private static (byte[] Pixels, int Width, int Height) LoadIcon(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream);
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1)
            throw new InvalidDataException("Game icon is not an ICO file.");
        int count = reader.ReadUInt16();
        int bestWidth = 0;
        int bestHeight = 0;
        uint bestSize = 0;
        uint bestOffset = 0;
        for (int i = 0; i < count; i++)
        {
            int width = reader.ReadByte();
            int height = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadUInt16();
            int bitsPerPixel = reader.ReadUInt16();
            uint size = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();
            width = width == 0 ? 256 : width;
            height = height == 0 ? 256 : height;
            if (bitsPerPixel == 32 && width * height > bestWidth * bestHeight)
                (bestWidth, bestHeight, bestSize, bestOffset) = (width, height, size, offset);
        }
        if (bestOffset == 0 || bestSize < 40)
            throw new InvalidDataException("Game icon has no 32-bit bitmap.");

        stream.Position = bestOffset;
        uint headerSize = reader.ReadUInt32();
        int bitmapWidth = reader.ReadInt32();
        int bitmapHeight = reader.ReadInt32() / 2;
        _ = reader.ReadUInt16();
        int bits = reader.ReadUInt16();
        uint compression = reader.ReadUInt32();
        if (headerSize < 40 || bitmapWidth != bestWidth || bitmapHeight != bestHeight || bits != 32 || compression != 0)
            throw new InvalidDataException("Game icon bitmap is unsupported.");

        stream.Position = bestOffset + headerSize;
        int stride = checked(bestWidth * 4);
        byte[] source = reader.ReadBytes(checked(stride * bestHeight));
        if (source.Length != stride * bestHeight)
            throw new EndOfStreamException("Game icon bitmap is truncated.");
        byte[] pixels = new byte[source.Length];
        for (int y = 0; y < bestHeight; y++)
            Buffer.BlockCopy(source, (bestHeight - 1 - y) * stride, pixels, y * stride, stride);
        return (pixels, bestWidth, bestHeight);
    }

    private void ApplyCursorState()
    {
        bool applied = SDL_SetWindowRelativeMouseMode(_window, _captureCursor && !_showCursor);
        SDL_SetWindowMouseGrab(_window, _captureCursor);
        applied &= _showCursor ? SDL_ShowCursor() : SDL_HideCursor();
        if (!applied)
            Interlocked.Exchange(ref _cursorStateDirty, 1);
    }

    private void ApplyCursorImage()
    {
        (byte[] Pixels, int Width, int Height, int HotspotX, int HotspotY)? image;
        StandardPlatformCursor? standardCursor;
        lock (_cursorLock)
        {
            image = _pendingCursorImage;
            standardCursor = _pendingStandardCursor;
        }

        nint cursor;
        bool owned;
        if (image is { } value)
        {
            fixed (byte* pixels = value.Pixels)
            {
                nint surface = SDL_CreateSurfaceFrom(value.Width, value.Height, PixelFormatRgba32,
                    pixels, value.Width * 4);
                cursor = surface == 0 ? 0 : SDL_CreateColorCursor(surface, value.HotspotX, value.HotspotY);
                SDL_DestroySurface(surface);
            }
            owned = true;
        }
        else if (standardCursor.HasValue)
        {
            cursor = SDL_CreateSystemCursor(ToSdlSystemCursor(standardCursor.Value));
            owned = true;
        }
        else
        {
            cursor = SDL_GetDefaultCursor();
            owned = false;
        }

        if (cursor == 0 || !SDL_SetCursor(cursor))
        {
            if (owned && cursor != 0)
                SDL_DestroyCursor(cursor);
            return;
        }
        if (_cursor != 0)
            SDL_DestroyCursor(_cursor);
        _cursor = owned ? cursor : 0;
    }

    private static int ToSdlSystemCursor(StandardPlatformCursor cursor) => cursor switch
    {
        StandardPlatformCursor.Ibeam => 1,
        StandardPlatformCursor.Wait => 2,
        StandardPlatformCursor.Cross => 3,
        StandardPlatformCursor.AppStarting => 4,
        StandardPlatformCursor.TopLeftCorner or StandardPlatformCursor.BottomRightCorner => 5,
        StandardPlatformCursor.TopRightCorner or StandardPlatformCursor.BottomLeftCorner => 6,
        StandardPlatformCursor.SizeWestEast => 7,
        StandardPlatformCursor.SizeNorthSouth => 8,
        StandardPlatformCursor.SizeAll or StandardPlatformCursor.DragMove => 9,
        StandardPlatformCursor.No => 10,
        StandardPlatformCursor.Hand or StandardPlatformCursor.DragCopy or StandardPlatformCursor.DragLink => 11,
        StandardPlatformCursor.TopSide => 13,
        StandardPlatformCursor.RightSide => 15,
        StandardPlatformCursor.BottomSide => 17,
        StandardPlatformCursor.LeftSide => 19,
        _ => 0
    };

    private Vector2 ScaleMousePosition(float x, float y)
    {
        Vector2 scale = GetMouseScale();
        return new Vector2(x * scale.X, y * scale.Y);
    }

    private Vector2 GetMouseScale()
    {
        lock (_snapshotLock)
        {
            if (_windowWidth == 0 || _windowHeight == 0)
                return Vector2.One;
            return new Vector2((float)_renderTargetWidth / _windowWidth, (float)_renderTargetHeight / _windowHeight);
        }
    }

    private Vector2I ResizeWindowForRenderTarget()
    {
        float density = SDL_GetWindowPixelDensity(_window);
        if (density <= 0)
            density = 1;
        Vector2I size;
        lock (_snapshotLock)
            size = new Vector2I(
                Math.Max(1, (int)MathF.Round(_renderTargetWidth / density)),
                Math.Max(1, (int)MathF.Round(_renderTargetHeight / density)));
        if (!SDL_SetWindowSize(_window, size.X, size.Y))
            throw new InvalidOperationException($"SDL window resize failed: {GetError()}");
        return size;
    }

    internal static void SetTextInputActive(bool active)
    {
        SdlPlatformWindow? window;
        lock (TextInputSync)
        {
            _textInputActive = active;
            window = _textInputWindow;
        }
        window?.ApplyTextInputActive(active);
    }

    internal static void ClearTextComposition()
    {
        SdlPlatformWindow? window;
        lock (TextInputSync)
            window = _textInputWindow;
        window?.ApplyClearTextComposition();
    }

    internal static void SetTextInputArea(double x, double y, double width, double height)
    {
        SdlPlatformWindow? window;
        lock (TextInputSync)
        {
            _textInputArea = (x, y, width, height);
            window = _textInputWindow;
        }
        window?.ApplyTextInputArea(_textInputArea);
    }

    private static void AttachTextInputWindow(SdlPlatformWindow window)
    {
        bool active;
        (double X, double Y, double Width, double Height) area;
        lock (TextInputSync)
        {
            _textInputWindow = window;
            active = _textInputActive;
            area = _textInputArea;
        }
        window.ApplyTextInputArea(area);
        window.ApplyTextInputActive(active);
    }

    private static void DetachTextInputWindow(SdlPlatformWindow window)
    {
        lock (TextInputSync)
        {
            if (_textInputWindow == window)
                _textInputWindow = null;
        }
        window.ApplyTextInputActive(false);
    }

    private void ApplyTextInputActive(bool active) => SdlThread.Invoke(() =>
    {
        bool success = active ? SDL_StartTextInput(_window) : SDL_StopTextInput(_window);
        if (!success)
            throw new InvalidOperationException($"SDL text input state change failed: {GetError()}");
    });

    private void ApplyClearTextComposition() => SdlThread.Invoke(() =>
    {
        if (!SDL_ClearComposition(_window))
            throw new InvalidOperationException($"SDL composition reset failed: {GetError()}");
    });

    private void ApplyTextInputArea((double X, double Y, double Width, double Height) area)
    {
        SdlRect rect;
        lock (_snapshotLock)
        {
            double scaleX = _renderTargetWidth == 0 ? 1 : (double)_windowWidth / _renderTargetWidth;
            double scaleY = _renderTargetHeight == 0 ? 1 : (double)_windowHeight / _renderTargetHeight;
            rect = new SdlRect
            {
                X = (int)Math.Round(area.X * scaleX),
                Y = (int)Math.Round(area.Y * scaleY),
                Width = Math.Max(1, (int)Math.Round(area.Width * scaleX)),
                Height = Math.Max(1, (int)Math.Round(area.Height * scaleY))
            };
        }
        SdlThread.Invoke(() =>
        {
            if (!SDL_SetTextInputArea(_window, in rect, 0))
                throw new InvalidOperationException($"SDL text input area update failed: {GetError()}");
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlRect
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    [DllImport(Sdl, EntryPoint = "SDL_CreateWindow", CharSet = CharSet.Ansi)]
    private static extern nint SDL_CreateWindow(string title, int width, int height, ulong flags);

    [DllImport(Sdl, EntryPoint = "SDL_GetWindowID")]
    private static extern uint SDL_GetWindowID(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_DestroyWindow")]
    private static extern void SDL_DestroyWindow(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_GetWindowFlags")]
    private static extern ulong SDL_GetWindowFlags(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_GetWindowSizeInPixels")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetWindowSizeInPixels(nint window, out int width, out int height);

    [DllImport(Sdl, EntryPoint = "SDL_GetWindowSize")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetWindowSize(nint window, out int width, out int height);

    [DllImport(Sdl, EntryPoint = "SDL_GetWindowPixelDensity")]
    private static extern float SDL_GetWindowPixelDensity(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_GetMouseState")]
    private static extern uint SDL_GetMouseState(out float x, out float y);

    [DllImport(Sdl, EntryPoint = "SDL_ShowWindow")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_ShowWindow(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_HideWindow")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_HideWindow(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_RaiseWindow")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_RaiseWindow(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_SetWindowSize")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowSize(nint window, int width, int height);

    [DllImport(Sdl, EntryPoint = "SDL_SetWindowPosition")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowPosition(nint window, int x, int y);

    [DllImport(Sdl, EntryPoint = "SDL_SetWindowBordered")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowBordered(nint window, [MarshalAs(UnmanagedType.I1)] bool bordered);

    [DllImport(Sdl, EntryPoint = "SDL_SetWindowAlwaysOnTop")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowAlwaysOnTop(nint window, [MarshalAs(UnmanagedType.I1)] bool onTop);

    [DllImport(Sdl, EntryPoint = "SDL_SetWindowFullscreen")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowFullscreen(nint window, [MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Sdl, EntryPoint = "SDL_SyncWindow")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SyncWindow(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_SetWindowRelativeMouseMode")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowRelativeMouseMode(nint window, [MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Sdl, EntryPoint = "SDL_SetWindowMouseGrab")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowMouseGrab(nint window, [MarshalAs(UnmanagedType.I1)] bool grabbed);

    [DllImport(Sdl, EntryPoint = "SDL_ShowCursor")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_ShowCursor();

    [DllImport(Sdl, EntryPoint = "SDL_HideCursor")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_HideCursor();

    [DllImport(Sdl, EntryPoint = "SDL_CreateSurfaceFrom")]
    private static extern nint SDL_CreateSurfaceFrom(int width, int height, uint format, void* pixels, int pitch);

    [DllImport(Sdl, EntryPoint = "SDL_SetWindowIcon")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowIcon(nint window, nint icon);

    [DllImport(Sdl, EntryPoint = "SDL_DestroySurface")]
    private static extern void SDL_DestroySurface(nint surface);

    [DllImport(Sdl, EntryPoint = "SDL_CreateColorCursor")]
    private static extern nint SDL_CreateColorCursor(nint surface, int hotX, int hotY);

    [DllImport(Sdl, EntryPoint = "SDL_CreateSystemCursor")]
    private static extern nint SDL_CreateSystemCursor(int id);

    [DllImport(Sdl, EntryPoint = "SDL_SetCursor")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetCursor(nint cursor);

    [DllImport(Sdl, EntryPoint = "SDL_GetDefaultCursor")]
    private static extern nint SDL_GetDefaultCursor();

    [DllImport(Sdl, EntryPoint = "SDL_StartTextInput")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_StartTextInput(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_StopTextInput")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_StopTextInput(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_ClearComposition")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_ClearComposition(nint window);

    [DllImport(Sdl, EntryPoint = "SDL_SetTextInputArea")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetTextInputArea(nint window, in SdlRect rect, int cursor);

    [DllImport(Sdl, EntryPoint = "SDL_DestroyCursor")]
    private static extern void SDL_DestroyCursor(nint cursor);

    [DllImport(Sdl, EntryPoint = "SDL_GetError")]
    private static extern nint SDL_GetError();
}
