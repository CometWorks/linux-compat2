using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace LinuxCompat.Platform;

internal static class SdlGamepads
{
    private const string Sdl = "libSDL3.so.0";
    private const uint GamepadAxisMotion = 0x650;
    private const uint GamepadButtonDown = 0x651;
    private const uint GamepadButtonUp = 0x652;
    private const uint GamepadAdded = 0x653;
    private const uint GamepadRemoved = 0x654;
    private const uint GamepadRemapped = 0x655;
    private const uint JoystickAxisMotion = 0x600;
    private const uint JoystickHatMotion = 0x602;
    private const uint JoystickButtonDown = 0x603;
    private const uint JoystickButtonUp = 0x604;
    private const uint JoystickAdded = 0x605;
    private const uint JoystickRemoved = 0x606;

    private static readonly Dictionary<uint, OpenDevice> Devices = [];
    internal static readonly ConcurrentQueue<SdlGamepadEvent> Pending = new();

    internal static unsafe void Initialize()
    {
        nint ids = SDL_GetJoysticks(out int count);
        if (ids == 0)
            return;
        try
        {
            for (int i = 0; i < count; i++)
                Open(((uint*)ids)[i]);
        }
        finally
        {
            SDL_free(ids);
        }
    }

    internal static void HandleEvent(ref SdlThread.SdlEvent e)
    {
        uint id = e.GamepadDevice.Which;
        switch (e.Type)
        {
            case JoystickAdded:
                Open(id);
                break;
            case GamepadAdded:
                if (Devices.TryGetValue(id, out OpenDevice? existing) && !existing.IsGamepad)
                    Close(id, notify: true);
                Open(id);
                break;
            case JoystickRemoved:
            case GamepadRemoved:
                Close(id, notify: true);
                break;
            case GamepadRemapped:
                if (Devices.TryGetValue(id, out OpenDevice? remapped))
                    QueueSnapshot(id, remapped);
                else
                    Open(id);
                break;
            case GamepadAxisMotion:
                if (Devices.TryGetValue(id, out OpenDevice? gamepad) && gamepad.IsGamepad)
                    Pending.Enqueue(SdlGamepadEvent.Axis(id, e.GamepadAxis.Axis, e.GamepadAxis.Value));
                break;
            case GamepadButtonDown:
            case GamepadButtonUp:
                if (Devices.TryGetValue(id, out gamepad) && gamepad.IsGamepad)
                    Pending.Enqueue(SdlGamepadEvent.Button(id, MapGamepadButton(e.GamepadButton.Button),
                        e.Type == GamepadButtonDown));
                break;
            case JoystickAxisMotion:
                if (Devices.TryGetValue(id, out OpenDevice? joystick) && !joystick.IsGamepad)
                    Pending.Enqueue(SdlGamepadEvent.Axis(id, MapJoystickAxis(e.GamepadAxis.Axis),
                        e.GamepadAxis.Value));
                break;
            case JoystickButtonDown:
            case JoystickButtonUp:
                if (Devices.TryGetValue(id, out joystick) && !joystick.IsGamepad)
                    Pending.Enqueue(SdlGamepadEvent.Button(id, MapJoystickButton(e.GamepadButton.Button),
                        e.Type == JoystickButtonDown));
                break;
            case JoystickHatMotion:
                if (Devices.TryGetValue(id, out joystick) && !joystick.IsGamepad && e.JoystickHat.Hat == 0)
                    QueueHat(id, e.JoystickHat.Value);
                break;
        }
    }

    internal static void Shutdown()
    {
        foreach (uint id in Devices.Keys.ToArray())
            Close(id, notify: true);
    }

    private static void Open(uint id)
    {
        if (Devices.ContainsKey(id))
            return;

        bool isGamepad = SDL_IsGamepad(id);
        nint handle = isGamepad ? SDL_OpenGamepad(id) : SDL_OpenJoystick(id);
        if (handle == 0)
        {
            Console.Error.WriteLine($"[LinuxCompat] SDL could not open input device {id}: {GetError()}");
            return;
        }

        string name = GetString(isGamepad ? SDL_GetGamepadNameForID(id) : SDL_GetJoystickNameForID(id))
            ?? $"SDL input {id}";
        string? serial = GetString(isGamepad ? SDL_GetGamepadSerial(handle) : SDL_GetJoystickSerial(handle));
        string? path = GetString(isGamepad ? SDL_GetGamepadPathForID(id) : SDL_GetJoystickPathForID(id));
        SdlGuid guid = isGamepad ? SDL_GetGamepadGUIDForID(id) : SDL_GetJoystickGUIDForID(id);
        string prefix = $"SDL_GAMEPAD:{guid.High:X16}{guid.Low:X16}:";
        string deviceId = prefix + (serial ?? path ?? id.ToString());
        if (serial != null && path != null && Devices.Values.Any(device => device.DeviceId == deviceId))
            deviceId += $":{path}";
        if (Devices.Values.Any(device => device.DeviceId == deviceId))
            deviceId += $":{id}";
        var device = new OpenDevice(handle, isGamepad, name, deviceId);
        Devices.Add(id, device);
        Pending.Enqueue(SdlGamepadEvent.Connected(id, device.Name, device.DeviceId));
        QueueSnapshot(id, device);
        Console.WriteLine($"[LinuxCompat] SDL input connected: {name} ({(isGamepad ? "gamepad" : "joystick")})");
    }

    private static void Close(uint id, bool notify)
    {
        if (!Devices.Remove(id, out OpenDevice? device))
            return;
        device.Close();
        if (notify)
            Pending.Enqueue(SdlGamepadEvent.Disconnected(id));
        Console.WriteLine($"[LinuxCompat] SDL input disconnected: {device.Name}");
    }

    private static void QueueSnapshot(uint id, OpenDevice device)
    {
        if (device.IsGamepad)
        {
            for (int axis = 0; axis < 6; axis++)
                Pending.Enqueue(SdlGamepadEvent.Axis(id, axis, SDL_GetGamepadAxis(device.Handle, axis)));
            for (int button = 0; button < 15; button++)
                Pending.Enqueue(SdlGamepadEvent.Button(id, MapGamepadButton(button),
                    SDL_GetGamepadButton(device.Handle, button)));
            return;
        }

        int axes = SDL_GetNumJoystickAxes(device.Handle);
        for (int axis = 0; axis < axes; axis++)
            Pending.Enqueue(SdlGamepadEvent.Axis(id, MapJoystickAxis(axis),
                SDL_GetJoystickAxis(device.Handle, axis)));
        int buttons = SDL_GetNumJoystickButtons(device.Handle);
        for (int button = 0; button < buttons; button++)
            Pending.Enqueue(SdlGamepadEvent.Button(id, MapJoystickButton(button),
                SDL_GetJoystickButton(device.Handle, button)));
        if (SDL_GetNumJoystickHats(device.Handle) > 0)
            QueueHat(id, SDL_GetJoystickHat(device.Handle, 0));
    }

    private static void QueueHat(uint id, byte value)
    {
        Pending.Enqueue(SdlGamepadEvent.Button(id, 11, (value & 0x01) != 0));
        Pending.Enqueue(SdlGamepadEvent.Button(id, 12, (value & 0x04) != 0));
        Pending.Enqueue(SdlGamepadEvent.Button(id, 13, (value & 0x08) != 0));
        Pending.Enqueue(SdlGamepadEvent.Button(id, 14, (value & 0x02) != 0));
    }

    private static int MapGamepadButton(int button) => button switch
    {
        0 => 0, 1 => 1, 2 => 2, 3 => 3,
        4 => 4, 6 => 6, 7 => 7, 8 => 8, 9 => 9, 10 => 10,
        11 => 11, 12 => 12, 13 => 13, 14 => 14,
        _ => -1
    };

    private static int MapJoystickButton(int button) => button switch
    {
        0 => 1, 1 => 0, 2 => 3, 3 => 2, 4 => 9, 5 => 10,
        8 => 4, 9 => 6, 10 => 7, 11 => 8,
        _ => -1
    };

    private static int MapJoystickAxis(int axis) => axis switch
    {
        0 => 0,
        1 => 1,
        3 => 2,
        4 => 3,
        _ => -1
    };

    private static string? GetString(nint value) => value == 0 ? null : Marshal.PtrToStringUTF8(value);
    private static string GetError() => GetString(SDL_GetError()) ?? "unknown error";

    private sealed record OpenDevice(nint Handle, bool IsGamepad, string Name, string DeviceId)
    {
        internal void Close()
        {
            if (IsGamepad)
                SDL_CloseGamepad(Handle);
            else
                SDL_CloseJoystick(Handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SdlGuid
    {
        internal readonly ulong Low;
        internal readonly ulong High;
    }

    [DllImport(Sdl)] private static extern nint SDL_GetJoysticks(out int count);
    [DllImport(Sdl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_IsGamepad(uint instanceId);
    [DllImport(Sdl)] private static extern nint SDL_OpenGamepad(uint instanceId);
    [DllImport(Sdl)] private static extern void SDL_CloseGamepad(nint gamepad);
    [DllImport(Sdl)] private static extern nint SDL_OpenJoystick(uint instanceId);
    [DllImport(Sdl)] private static extern void SDL_CloseJoystick(nint joystick);
    [DllImport(Sdl)] private static extern nint SDL_GetGamepadNameForID(uint instanceId);
    [DllImport(Sdl)] private static extern nint SDL_GetJoystickNameForID(uint instanceId);
    [DllImport(Sdl)] private static extern nint SDL_GetGamepadPathForID(uint instanceId);
    [DllImport(Sdl)] private static extern nint SDL_GetJoystickPathForID(uint instanceId);
    [DllImport(Sdl)] private static extern SdlGuid SDL_GetGamepadGUIDForID(uint instanceId);
    [DllImport(Sdl)] private static extern SdlGuid SDL_GetJoystickGUIDForID(uint instanceId);
    [DllImport(Sdl)] private static extern nint SDL_GetGamepadSerial(nint gamepad);
    [DllImport(Sdl)] private static extern nint SDL_GetJoystickSerial(nint joystick);
    [DllImport(Sdl)] private static extern short SDL_GetGamepadAxis(nint gamepad, int axis);
    [DllImport(Sdl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetGamepadButton(nint gamepad, int button);
    [DllImport(Sdl)] private static extern int SDL_GetNumJoystickAxes(nint joystick);
    [DllImport(Sdl)] private static extern int SDL_GetNumJoystickButtons(nint joystick);
    [DllImport(Sdl)] private static extern int SDL_GetNumJoystickHats(nint joystick);
    [DllImport(Sdl)] private static extern short SDL_GetJoystickAxis(nint joystick, int axis);
    [DllImport(Sdl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetJoystickButton(nint joystick, int button);
    [DllImport(Sdl)] private static extern byte SDL_GetJoystickHat(nint joystick, int hat);
    [DllImport(Sdl)] private static extern nint SDL_GetError();
    [DllImport(Sdl)] private static extern void SDL_free(nint memory);
}

internal enum SdlGamepadEventKind
{
    Connected,
    Disconnected,
    Axis,
    Button
}

internal readonly record struct SdlGamepadEvent(
    SdlGamepadEventKind Kind, uint InstanceId, string? Name = null, string? DeviceId = null,
    int Input = -1, short Value = 0, bool Down = false)
{
    internal static SdlGamepadEvent Connected(uint id, string name, string deviceId) =>
        new(SdlGamepadEventKind.Connected, id, name, deviceId);
    internal static SdlGamepadEvent Disconnected(uint id) => new(SdlGamepadEventKind.Disconnected, id);
    internal static SdlGamepadEvent Axis(uint id, int axis, short value) =>
        new(SdlGamepadEventKind.Axis, id, Input: axis, Value: value);
    internal static SdlGamepadEvent Button(uint id, int button, bool down) =>
        new(SdlGamepadEventKind.Button, id, Input: button, Down: down);
}
