using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace LinuxCompat.Platform;

internal static class SdlThread
{
    private const string Sdl = "libSDL3.so.0";
    private const uint InitVideo = 0x20;
    private const uint InitJoystick = 0x200;
    private const uint InitGamepad = 0x2000;
    private static readonly object Sync = new();
    private static readonly ConcurrentQueue<Action> Pending = new();
    private static readonly ConcurrentQueue<TaskCompletionSource<bool>> FrameWaiters = new();
    private static readonly AutoResetEvent Wake = new(false);
    private static readonly ManualResetEventSlim Started = new(false);
    private static Thread? _thread;
    private static Exception? _initializationError;
    private static volatile bool _running;
    private static bool _stopping;
    private static int _threadId;

    internal delegate void EventHandler(ref SdlEvent e);
    internal static event EventHandler? Event;
    internal static event Action? Tick;

    internal static bool IsCurrent => Environment.CurrentManagedThreadId == Volatile.Read(ref _threadId);
    internal static string? VideoDriver { get; private set; }
    internal static bool IsWayland => string.Equals(VideoDriver, "wayland", StringComparison.OrdinalIgnoreCase);

    internal static void Start()
    {
        lock (Sync)
        {
            if (_stopping)
                throw new InvalidOperationException("SDL is shutting down.");
            if (_thread == null)
            {
                Started.Reset();
                _initializationError = null;
                _thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = "LinuxCompat SDL"
                };
                _thread.Start();
            }
        }

        if (!Started.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("SDL video initialization did not complete within 10 seconds.");
        if (_initializationError != null)
            ExceptionDispatchInfo.Capture(_initializationError).Throw();
    }

    internal static void Invoke(Action action) => Invoke(() =>
    {
        action();
        return true;
    });

    internal static T Invoke<T>(Func<T> action)
    {
        Start();
        if (IsCurrent)
            return action();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Sync)
        {
            if (_stopping || _thread == null)
                throw new InvalidOperationException("SDL is shutting down.");
            Pending.Enqueue(() =>
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
            Wake.Set();
        }
        return completion.Task.GetAwaiter().GetResult();
    }

    internal static string GetClipboardText() => Invoke(() =>
    {
        SDL_ClearError();
        nint text = SDL_GetClipboardText();
        if (text == 0)
            throw new InvalidOperationException($"SDL clipboard read failed: {GetError()}");
        try
        {
            string result = Marshal.PtrToStringUTF8(text) ?? string.Empty;
            string error = GetError();
            if (result.Length == 0 && error.Length != 0)
                throw new InvalidOperationException($"SDL clipboard read failed: {error}");
            return result;
        }
        finally
        {
            SDL_free(text);
        }
    });

    internal static void SetClipboardText(string text) => Invoke(() =>
    {
        if (!SDL_SetClipboardText(text))
            throw new InvalidOperationException($"SDL clipboard write failed: {GetError()}");
    });

    internal static void SyncFrame()
    {
        Start();
        if (IsCurrent)
            return;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Sync)
        {
            if (_stopping || _thread == null)
                throw new InvalidOperationException("SDL is shutting down.");
            Pending.Enqueue(() => FrameWaiters.Enqueue(completion));
            Wake.Set();
        }
        completion.Task.GetAwaiter().GetResult();
    }

    internal static void Stop()
    {
        Thread? thread;
        lock (Sync)
        {
            thread = _thread;
            if (thread == null)
                return;
            _stopping = true;
            _running = false;
            Wake.Set();
        }

        if (IsCurrent)
            return;
        if (!thread.Join(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("SDL did not shut down within 10 seconds.");
        lock (Sync)
        {
            if (_thread == thread)
            {
                _thread = null;
                _stopping = false;
            }
        }
    }

    private static void Run()
    {
        Volatile.Write(ref _threadId, Environment.CurrentManagedThreadId);
        try
        {
            SDL_SetHint("SDL_VIDEO_X11_NET_WM_PING", "0");
            SDL_SetHint("SDL_IME_IMPLEMENTED_UI", "composition");
            SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            if (!SDL_Init(InitVideo | InitJoystick | InitGamepad))
                throw new InvalidOperationException($"SDL video initialization failed: {GetError()}");
            VideoDriver = Marshal.PtrToStringUTF8(SDL_GetCurrentVideoDriver());
            Console.WriteLine($"[LinuxCompat] SDL3 video driver: {VideoDriver ?? "unknown"}");
            SdlGamepads.Initialize();
            _running = true;
        }
        catch (Exception exception)
        {
            VideoDriver = null;
            _initializationError = exception;
            Started.Set();
            return;
        }

        Started.Set();
        try
        {
            while (_running)
            {
                while (Pending.TryDequeue(out Action? action))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine($"[LinuxCompat] SDL dispatch failed: {exception}");
                    }
                }

                while (SDL_PollEvent(out SdlEvent e))
                {
                    try
                    {
                        SdlGamepads.HandleEvent(ref e);
                        Event?.Invoke(ref e);
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine($"[LinuxCompat] SDL event failed: {exception}");
                    }
                }

                try
                {
                    Tick?.Invoke();
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"[LinuxCompat] SDL tick failed: {exception}");
                }
                while (FrameWaiters.TryDequeue(out TaskCompletionSource<bool>? completion))
                    completion.SetResult(true);
                Wake.WaitOne(1);
            }
        }
        finally
        {
            while (Pending.TryDequeue(out Action? action))
                action();
            while (FrameWaiters.TryDequeue(out TaskCompletionSource<bool>? completion))
                completion.SetException(new InvalidOperationException("SDL shut down before completing the frame."));
            SdlGamepads.Shutdown();
            SDL_Quit();
            VideoDriver = null;
            Volatile.Write(ref _threadId, 0);
            lock (Sync)
            {
                if (_thread == Thread.CurrentThread)
                {
                    _thread = null;
                    _stopping = false;
                }
            }
        }
    }

    private static string GetError() => Marshal.PtrToStringUTF8(SDL_GetError()) ?? "unknown error";

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    internal struct SdlEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(0)] public SdlWindowEvent Window;
        [FieldOffset(0)] public SdlKeyboardEvent Keyboard;
        [FieldOffset(0)] public SdlMouseMotionEvent Motion;
        [FieldOffset(0)] public SdlMouseButtonEvent Button;
        [FieldOffset(0)] public SdlMouseWheelEvent Wheel;
        [FieldOffset(0)] public SdlTextInputEvent TextInput;
        [FieldOffset(0)] public SdlTextEditingEvent TextEditing;
        [FieldOffset(0)] public SdlGamepadAxisEvent GamepadAxis;
        [FieldOffset(0)] public SdlGamepadButtonEvent GamepadButton;
        [FieldOffset(0)] public SdlGamepadDeviceEvent GamepadDevice;
        [FieldOffset(0)] public SdlJoystickHatEvent JoystickHat;
    }

    [StructLayout(LayoutKind.Explicit, Size = 28)]
    internal struct SdlWindowEvent
    {
        [FieldOffset(16)] public uint WindowId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct SdlKeyboardEvent
    {
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(24)] public int Scancode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 44)]
    internal struct SdlMouseMotionEvent
    {
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(28)] public float X;
        [FieldOffset(32)] public float Y;
        [FieldOffset(36)] public float Xrel;
        [FieldOffset(40)] public float Yrel;
    }

    [StructLayout(LayoutKind.Explicit, Size = 36)]
    internal struct SdlMouseButtonEvent
    {
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(24)] public byte Button;
    }

    [StructLayout(LayoutKind.Explicit, Size = 36)]
    internal struct SdlMouseWheelEvent
    {
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(24)] public float X;
        [FieldOffset(28)] public float Y;
        [FieldOffset(32)] public uint Direction;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct SdlTextInputEvent
    {
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(24)] public nint Text;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct SdlTextEditingEvent
    {
        [FieldOffset(16)] public uint WindowId;
        [FieldOffset(24)] public nint Text;
        [FieldOffset(32)] public int Start;
        [FieldOffset(36)] public int Length;
    }

    [StructLayout(LayoutKind.Explicit, Size = 28)]
    internal struct SdlGamepadAxisEvent
    {
        [FieldOffset(16)] public uint Which;
        [FieldOffset(20)] public byte Axis;
        [FieldOffset(24)] public short Value;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct SdlGamepadButtonEvent
    {
        [FieldOffset(16)] public uint Which;
        [FieldOffset(20)] public byte Button;
    }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    internal struct SdlGamepadDeviceEvent
    {
        [FieldOffset(16)] public uint Which;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct SdlJoystickHatEvent
    {
        [FieldOffset(16)] public uint Which;
        [FieldOffset(20)] public byte Hat;
        [FieldOffset(21)] public byte Value;
    }

    [DllImport(Sdl, EntryPoint = "SDL_Init")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_Init(uint flags);

    [DllImport(Sdl, EntryPoint = "SDL_Quit")]
    private static extern void SDL_Quit();

    [DllImport(Sdl, EntryPoint = "SDL_SetHint", CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetHint(string name, string value);

    [DllImport(Sdl, EntryPoint = "SDL_PollEvent")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_PollEvent(out SdlEvent e);

    [DllImport(Sdl, EntryPoint = "SDL_GetCurrentVideoDriver")]
    private static extern nint SDL_GetCurrentVideoDriver();

    [DllImport(Sdl, EntryPoint = "SDL_GetError")]
    private static extern nint SDL_GetError();

    [DllImport(Sdl, EntryPoint = "SDL_ClearError")]
    private static extern void SDL_ClearError();

    [DllImport(Sdl, EntryPoint = "SDL_GetClipboardText")]
    private static extern nint SDL_GetClipboardText();

    [DllImport(Sdl, EntryPoint = "SDL_SetClipboardText")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetClipboardText([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [DllImport(Sdl, EntryPoint = "SDL_free")]
    private static extern void SDL_free(nint memory);
}
