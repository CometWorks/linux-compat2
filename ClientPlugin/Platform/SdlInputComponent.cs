using System.Collections.Concurrent;
using Keen.VRage.Core.EngineComponents;
using Keen.VRage.Core.Input;
using Keen.VRage.Core.Platform;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Mathematics;
using Avalonia;
using Avalonia.Input.TextInput;

namespace LinuxCompat.Platform;

[DefaultTag("IPlatformInput")]
public sealed class SdlInputComponent : EngineComponent, IPlatformInput
{
    private readonly SdlInputDevice _keyboard;
    private readonly SdlMouseDevice _mouse;
    private readonly SdlInputDeviceClass _gamepadClass;
    private readonly IInputDeviceClass[] _classes;
    private readonly List<IInputDevice> _devices;
    private readonly Dictionary<uint, SdlGamepadDevice> _gamepads = [];
    private readonly HashSet<uint> _pendingGamepadDisconnects = [];
    private readonly Dictionary<uint, SdlGamepadEvent> _pendingGamepadConnections = [];
    private readonly Dictionary<uint, List<SdlGamepadEvent>> _pendingGamepadInputs = [];
    private readonly SdlInputMethodEditor _inputMethodEditor = new();
    private readonly bool _testInput = LinuxNativeLibraryResolver.IsEnabled("SE2_INPUT_TEST");
    private bool _sawKeyDown;
    private bool _sawKeyUp;
    private bool _sawMouseMotion;
    private bool _sawLeftDown;
    private bool _sawLeftUp;
    private bool _testComplete;

    public SdlInputComponent()
    {
        var keyboardClass = new SdlInputDeviceClass("SDL Keyboard", GenericDeviceClass.Keyboard,
            GenericDeviceClassFlags.Keyboard, KeyboardInputs.Inputs);
        var mouseClass = new SdlInputDeviceClass("SDL Mouse", GenericDeviceClass.Mouse,
            GenericDeviceClassFlags.Mouse, MouseInputs.Inputs);
        _gamepadClass = new SdlInputDeviceClass("SDL Game Controller", GenericDeviceClass.GameController,
            GenericDeviceClassFlags.GameController, GameControllerInputs.Inputs);
        _keyboard = new SdlKeyboardDevice(keyboardClass);
        _mouse = new SdlMouseDevice(mouseClass);
        keyboardClass.Add(_keyboard);
        mouseClass.Add(_mouse);
        _classes = [keyboardClass, mouseClass, _gamepadClass];
        _devices = [_keyboard, _mouse];
        AvaloniaLocator.CurrentMutable.Bind<ITextInputMethodImpl>().ToConstant(_inputMethodEditor);
    }

    public IEnumerable<IInputDeviceClass> SupportedDeviceClasses => _classes;
    public IEnumerable<IInputDevice> ConnectedDevices => _devices;
    public IInputMethodEditor DefaultInputMethodEditor => _inputMethodEditor;

    public event Action<IInputDevice>? DeviceConnected;
    public event Action<IInputDevice>? DeviceDisconnected;

    public void ProcessInput()
    {
        foreach (uint id in _pendingGamepadDisconnects)
            DisconnectGamepad(id);
        _pendingGamepadDisconnects.Clear();
        SdlGamepadEvent[] connections = _pendingGamepadConnections.Values.ToArray();
        _pendingGamepadConnections.Clear();

        _keyboard.BeginFrame();
        _mouse.BeginFrame();
        foreach (SdlGamepadDevice gamepad in _gamepads.Values)
            gamepad.BeginFrame();
        foreach (SdlGamepadEvent connection in connections)
        {
            ConnectGamepad(connection);
            if (_pendingGamepadInputs.Remove(connection.InstanceId, out List<SdlGamepadEvent>? inputs))
            {
                foreach (SdlGamepadEvent input in inputs)
                    ApplyGamepad(input);
            }
        }
        while (SdlGamepads.Pending.TryDequeue(out SdlGamepadEvent gamepadInput))
            ApplyGamepad(gamepadInput);
        while (SdlInputEvents.Pending.TryDequeue(out SdlInputEvent input))
        {
            switch (input.Kind)
            {
                case SdlInputEventKind.Key:
                    var keyboard = (SdlKeyboardDevice)_keyboard;
                    keyboard.Apply(input.Code, input.Down);
                    if (_testInput && input.Code == 41)
                    {
                        _sawKeyDown |= input.Down && keyboard.GetDigitalState(KeyboardInputs.Escape);
                        _sawKeyUp |= !input.Down && _sawKeyDown && !keyboard.GetDigitalState(KeyboardInputs.Escape);
                    }
                    break;
                case SdlInputEventKind.MouseMotion:
                    var motionMouse = (SdlMouseDevice)_mouse;
                    motionMouse.ApplyMotion(input.X, input.Y, input.DeltaX, input.DeltaY);
                    _sawMouseMotion |= _testInput
                        && motionMouse.GetPointerState(MouseInputs.Position, PointerStateKind.Relative) != Vector2.Zero;
                    break;
                case SdlInputEventKind.MouseButton:
                    var buttonMouse = (SdlMouseDevice)_mouse;
                    buttonMouse.ApplyButton(input.Code, input.Down);
                    if (_testInput && input.Code == 1)
                    {
                        _sawLeftDown |= input.Down && buttonMouse.GetDigitalState(MouseInputs.Left);
                        _sawLeftUp |= !input.Down && _sawLeftDown && !buttonMouse.GetDigitalState(MouseInputs.Left);
                    }
                    break;
                case SdlInputEventKind.MouseWheel:
                    ((SdlMouseDevice)_mouse).ApplyWheel(input.X, input.Y);
                    break;
                case SdlInputEventKind.FocusLost:
                    _keyboard.Clear();
                    _mouse.Clear();
                    _inputMethodEditor.Reset();
                    break;
                case SdlInputEventKind.TextInput:
                    _inputMethodEditor.Commit(input.Text ?? string.Empty);
                    break;
                case SdlInputEventKind.TextEditing:
                    _inputMethodEditor.SetPreedit(input.Text ?? string.Empty, input.Code);
                    break;
            }
        }
        if (_testInput && !_testComplete && _sawKeyDown && _sawKeyUp && _sawMouseMotion && _sawLeftDown && _sawLeftUp)
        {
            _testComplete = true;
            Log.Default.WriteLine("SE2_INPUT COMPLETE keyboard=Escape mouse=motion,left");
        }
    }

    private void ApplyGamepad(SdlGamepadEvent input)
    {
        if (input.Kind == SdlGamepadEventKind.Connected)
        {
            if (_pendingGamepadDisconnects.Contains(input.InstanceId))
            {
                _pendingGamepadConnections[input.InstanceId] = input;
                return;
            }
            KeyValuePair<uint, SdlGamepadDevice> replaced = _gamepads.FirstOrDefault(pair =>
                pair.Value.DeviceId == input.DeviceId && pair.Key != input.InstanceId);
            if (replaced.Value != null)
            {
                replaced.Value.Clear();
                _pendingGamepadDisconnects.Add(replaced.Key);
                _pendingGamepadConnections[input.InstanceId] = input;
                return;
            }
            ConnectGamepad(input);
            return;
        }
        if (input.Kind == SdlGamepadEventKind.Disconnected)
        {
            if (_pendingGamepadConnections.Remove(input.InstanceId))
            {
                _pendingGamepadInputs.Remove(input.InstanceId);
                return;
            }
            if (_gamepads.TryGetValue(input.InstanceId, out SdlGamepadDevice? gamepad))
            {
                gamepad.Clear();
                _pendingGamepadDisconnects.Add(input.InstanceId);
            }
            return;
        }
        if (_pendingGamepadConnections.ContainsKey(input.InstanceId))
        {
            if (!_pendingGamepadInputs.TryGetValue(input.InstanceId, out List<SdlGamepadEvent>? inputs))
                _pendingGamepadInputs.Add(input.InstanceId, inputs = []);
            inputs.Add(input);
            return;
        }
        if (!_gamepads.TryGetValue(input.InstanceId, out SdlGamepadDevice? device))
            return;
        if (input.Kind == SdlGamepadEventKind.Axis)
            device.ApplyAxis(input.Input, input.Value);
        else if (input.Kind == SdlGamepadEventKind.Button)
            device.ApplyButton(input.Input, input.Down);
    }

    private void ConnectGamepad(SdlGamepadEvent input)
    {
        if (_gamepads.ContainsKey(input.InstanceId))
            return;
        var gamepad = new SdlGamepadDevice(_gamepadClass, input.Name!, input.DeviceId!);
        _gamepads.Add(input.InstanceId, gamepad);
        _devices.Add(gamepad);
        _gamepadClass.Add(gamepad);
        DeviceConnected?.Invoke(gamepad);
    }

    private void DisconnectGamepad(uint id)
    {
        if (!_gamepads.Remove(id, out SdlGamepadDevice? gamepad))
            return;
        _devices.Remove(gamepad);
        _gamepadClass.Remove(gamepad);
        DeviceDisconnected?.Invoke(gamepad);
    }
}

internal enum SdlInputEventKind
{
    Key,
    MouseMotion,
    MouseButton,
    MouseWheel,
    FocusLost,
    TextInput,
    TextEditing
}

internal readonly record struct SdlInputEvent(
    SdlInputEventKind Kind, int Code = 0, bool Down = false,
    float X = 0, float Y = 0, float DeltaX = 0, float DeltaY = 0,
    string? Text = null);

internal static class SdlInputEvents
{
    internal static readonly ConcurrentQueue<SdlInputEvent> Pending = new();
}

internal sealed class SdlInputDeviceClass : IInputDeviceClass
{
    private readonly IReadOnlyDictionary<InputId, InputDescription> _inputs;
    private readonly Dictionary<string, InputId> _inputsByName;

    private readonly List<SdlInputDevice> _devices = [];
    public string Name { get; }
    public int DeviceClassId { get; }
    public string VendorInfo => "SDL 3";
    public GenericDeviceClassFlags GenericClasses { get; }
    public IEnumerable<IInputDevice> ConnectedDevices => _devices;
    public IEnumerable<IInputDevice> ActiveDevices => _devices.Where(device => device.HasActive);

    public event Action<IInputDevice>? DeviceConnected;
    public event Action<IInputDevice>? DeviceDisconnected;

    internal SdlInputDeviceClass(string name, GenericDeviceClass genericClass,
        GenericDeviceClassFlags genericClasses, IReadOnlyDictionary<InputId, InputDescription> inputs)
    {
        Name = name;
        DeviceClassId = 64 + (int)genericClass;
        GenericClasses = genericClasses;
        _inputs = inputs;
        _inputsByName = inputs.ToDictionary(pair => pair.Value.Name, pair => pair.Key);
    }

    public IEnumerable<InputId> GetInputs(InputType type) => _inputs.Keys.Where(input => input.Type == type);
    public IEnumerable<InputId> GetInputs(IInputDevice device, InputType type) => GetInputs(type);
    public InputDescription GetDescription(InputId id) => _inputs.TryGetValue(id, out InputDescription value)
        ? value
        : new InputDescription("Unknown Input", "{Unknown Input}");
    public InputId? TryFindInput(string inputName) => _inputsByName.TryGetValue(inputName, out InputId id) ? id : null;

    internal void Add(SdlInputDevice device)
    {
        _devices.Add(device);
        DeviceConnected?.Invoke(device);
    }

    internal void Remove(SdlInputDevice device)
    {
        _devices.Remove(device);
        DeviceDisconnected?.Invoke(device);
    }
}

internal abstract class SdlInputDevice : IInputDevice
{
    protected readonly HashSet<InputId> Active = [];
    protected readonly HashSet<InputId> Changed = [];

    public abstract string Name { get; }
    public abstract string DeviceId { get; }
    public IInputDeviceClass Class { get; }
    public bool HasActive => Active.Count != 0;
    public bool HasChanged => Changed.Count != 0;

    protected SdlInputDevice(IInputDeviceClass deviceClass) => Class = deviceClass;

    internal virtual void BeginFrame() => Changed.Clear();

    internal virtual void Clear()
    {
        Changed.UnionWith(Active);
        Active.Clear();
    }

    public void FillActive(HashSet<InputId> destination) => destination.UnionWith(Active);
    public void FillChanged(HashSet<InputId> destination) => destination.UnionWith(Changed);
    public bool GetDigitalState(InputId input) => Active.Contains(input);
    public virtual float GetAnalogState(InputId input) => 0;
    public virtual Vector2 GetPointerState(InputId input, PointerStateKind kind) => Vector2.Zero;
}

internal sealed class SdlKeyboardDevice : SdlInputDevice
{
    private readonly HashSet<int> _pressedScancodes = [];

    public override string Name => "SDL Keyboard";
    public override string DeviceId => "SDL_KEYBOARD";

    internal SdlKeyboardDevice(IInputDeviceClass deviceClass) : base(deviceClass) { }

    internal void Apply(int scancode, bool down)
    {
        InputId? mapped = MapScancode(scancode);
        if (!mapped.HasValue)
            return;

        InputId input = mapped.Value;
        bool wasActive = Active.Contains(input);
        if (down)
            _pressedScancodes.Add(scancode);
        else
            _pressedScancodes.Remove(scancode);
        bool isActive = _pressedScancodes.Any(code => MapScancode(code) == input);
        if (wasActive == isActive)
            return;

        Changed.Add(input);
        if (isActive)
            Active.Add(input);
        else
            Active.Remove(input);
    }

    internal override void Clear()
    {
        base.Clear();
        _pressedScancodes.Clear();
    }

    private static InputId? MapScancode(int code)
    {
        if (code is >= 4 and <= 29)
            return new DigitalInput(65 + code - 4, GenericDeviceClass.Keyboard);
        if (code is >= 30 and <= 38)
            return new DigitalInput(49 + code - 30, GenericDeviceClass.Keyboard);
        if (code == 39)
            return KeyboardInputs.D0;
        if (code is >= 58 and <= 69)
            return new DigitalInput(112 + code - 58, GenericDeviceClass.Keyboard);
        if (code is >= 89 and <= 97)
            return new DigitalInput(97 + code - 89, GenericDeviceClass.Keyboard);
        if (code is >= 104 and <= 115)
            return new DigitalInput(124 + code - 104, GenericDeviceClass.Keyboard);

        return code switch
        {
            40 or 88 => KeyboardInputs.Enter,
            41 => KeyboardInputs.Escape,
            42 => KeyboardInputs.Back,
            43 => KeyboardInputs.Tab,
            44 => KeyboardInputs.Space,
            45 => KeyboardInputs.OemMinus,
            46 => KeyboardInputs.OemEquals,
            47 => KeyboardInputs.OemOpenBrackets,
            48 => KeyboardInputs.OemCloseBrackets,
            49 or 50 => KeyboardInputs.OemBackSlash,
            100 => KeyboardInputs.Oem102,
            51 => KeyboardInputs.OemSemicolon,
            52 => KeyboardInputs.OemQuotes,
            53 => KeyboardInputs.OemBacktick,
            54 => KeyboardInputs.OemComma,
            55 => KeyboardInputs.OemPeriod,
            56 => KeyboardInputs.OemForwardSlash,
            57 => KeyboardInputs.Capital,
            70 => KeyboardInputs.PrintScreen,
            71 => KeyboardInputs.ScrollLock,
            72 => KeyboardInputs.Pause,
            73 => KeyboardInputs.Insert,
            74 => KeyboardInputs.Home,
            75 => KeyboardInputs.PageUp,
            76 => KeyboardInputs.Delete,
            77 => KeyboardInputs.End,
            78 => KeyboardInputs.PageDown,
            79 => KeyboardInputs.Right,
            80 => KeyboardInputs.Left,
            81 => KeyboardInputs.Down,
            82 => KeyboardInputs.Up,
            83 => KeyboardInputs.NumLock,
            84 => KeyboardInputs.NumpadDivide,
            85 => KeyboardInputs.NumpadMultiply,
            86 => KeyboardInputs.NumpadSubtract,
            87 => KeyboardInputs.NumpadAdd,
            98 => KeyboardInputs.Numpad0,
            99 => KeyboardInputs.NumpadDecimal,
            101 => KeyboardInputs.Apps,
            224 or 228 => KeyboardInputs.Control,
            225 or 229 => KeyboardInputs.Shift,
            226 or 230 => KeyboardInputs.Alt,
            227 => KeyboardInputs.LWin,
            231 => KeyboardInputs.RWin,
            117 => KeyboardInputs.Help,
            119 => KeyboardInputs.Select,
            127 => KeyboardInputs.VolumeMute,
            128 => KeyboardInputs.VolumeUp,
            129 => KeyboardInputs.VolumeDown,
            156 => KeyboardInputs.Clear,
            159 => KeyboardInputs.NumpadSeparator,
            258 => KeyboardInputs.Sleep,
            262 => KeyboardInputs.Play,
            267 => KeyboardInputs.MediaNextTrack,
            268 => KeyboardInputs.MediaPrevTrack,
            269 => KeyboardInputs.MediaStop,
            271 => KeyboardInputs.MediaPlayPause,
            272 => KeyboardInputs.LaunchMediaSelect,
            280 => KeyboardInputs.BrowserSearch,
            281 => KeyboardInputs.BrowserHome,
            282 => KeyboardInputs.BrowserBack,
            283 => KeyboardInputs.BrowserForward,
            284 => KeyboardInputs.BrowserStop,
            285 => KeyboardInputs.BrowserRefresh,
            286 => KeyboardInputs.BrowserFavorites,
            _ => null
        };
    }
}

internal sealed class SdlMouseDevice : SdlInputDevice
{
    private Vector2 _absolute;
    private Vector2 _relative;
    private Vector2 _wheel;

    public override string Name => "SDL Mouse";
    public override string DeviceId => "SDL_MOUSE";

    internal SdlMouseDevice(IInputDeviceClass deviceClass) : base(deviceClass) { }

    internal override void BeginFrame()
    {
        base.BeginFrame();
        if (_relative != Vector2.Zero)
            Changed.Add(MouseInputs.Position);
        if (_wheel.X != 0)
            Changed.Add(MouseInputs.HorizontalWheel);
        if (_wheel.Y != 0)
            Changed.Add(MouseInputs.VerticalWheel);
        _relative = Vector2.Zero;
        _wheel = Vector2.Zero;
        Active.Remove(MouseInputs.Position);
        Active.Remove(MouseInputs.HorizontalWheel);
        Active.Remove(MouseInputs.VerticalWheel);
    }

    internal void ApplyMotion(float x, float y, float deltaX, float deltaY)
    {
        _absolute = new Vector2(x, y);
        _relative += new Vector2(deltaX, deltaY);
        Changed.Add(MouseInputs.Position);
        if (_relative != Vector2.Zero)
            Active.Add(MouseInputs.Position);
    }

    internal void ApplyButton(int button, bool down)
    {
        InputId? input = button switch
        {
            1 => MouseInputs.Left,
            2 => MouseInputs.Middle,
            3 => MouseInputs.Right,
            4 => MouseInputs.Button4,
            5 => MouseInputs.Button5,
            _ => null
        };
        if (!input.HasValue || Active.Contains(input.Value) == down)
            return;
        Changed.Add(input.Value);
        if (down)
            Active.Add(input.Value);
        else
            Active.Remove(input.Value);
    }

    internal void ApplyWheel(float x, float y)
    {
        _wheel += new Vector2(x, y);
        if (x != 0)
        {
            Active.Add(MouseInputs.HorizontalWheel);
            Changed.Add(MouseInputs.HorizontalWheel);
        }
        if (y != 0)
        {
            Active.Add(MouseInputs.VerticalWheel);
            Changed.Add(MouseInputs.VerticalWheel);
        }
    }

    internal override void Clear()
    {
        base.Clear();
        _relative = Vector2.Zero;
        _wheel = Vector2.Zero;
    }

    public override float GetAnalogState(InputId input) => input == MouseInputs.VerticalWheel
        ? _wheel.Y
        : input == MouseInputs.HorizontalWheel ? _wheel.X : 0;

    public override Vector2 GetPointerState(InputId input, PointerStateKind kind) => input == MouseInputs.Position
        ? kind == PointerStateKind.Relative ? _relative : _absolute
        : Vector2.Zero;
}

internal sealed class SdlGamepadDevice : SdlInputDevice
{
    private float _leftX;
    private float _leftY;
    private float _rightX;
    private float _rightY;
    private float _leftTrigger;
    private float _rightTrigger;

    public override string Name { get; }
    public override string DeviceId { get; }

    internal SdlGamepadDevice(IInputDeviceClass deviceClass, string name, string deviceId) : base(deviceClass)
    {
        Name = name;
        DeviceId = deviceId;
    }

    internal void ApplyAxis(int axis, short raw)
    {
        switch (axis)
        {
            case 0: Change(ref _leftX, Normalize(raw, 7849, 32767), GameControllerInputs.LeftThumbstickX); break;
            case 1: Change(ref _leftY, Normalize(-(int)raw, 7849, 32767), GameControllerInputs.LeftThumbstickY); break;
            case 2: Change(ref _rightX, Normalize(raw, 8689, 32767), GameControllerInputs.RightThumbstickX); break;
            case 3: Change(ref _rightY, Normalize(-(int)raw, 8689, 32767), GameControllerInputs.RightThumbstickY); break;
            case 4: Change(ref _leftTrigger, Normalize(raw, 3855, 32767), GameControllerInputs.LeftTrigger); break;
            case 5: Change(ref _rightTrigger, Normalize(raw, 3855, 32767), GameControllerInputs.RightTrigger); break;
        }
    }

    internal void ApplyButton(int button, bool down)
    {
        InputId? input = button switch
        {
            0 => GameControllerInputs.A,
            1 => GameControllerInputs.B,
            2 => GameControllerInputs.X,
            3 => GameControllerInputs.Y,
            4 => GameControllerInputs.View,
            6 => GameControllerInputs.Menu,
            7 => GameControllerInputs.LeftThumbstickPress,
            8 => GameControllerInputs.RightThumbstickPress,
            9 => GameControllerInputs.LeftShoulder,
            10 => GameControllerInputs.RightShoulder,
            11 => GameControllerInputs.DPadUp,
            12 => GameControllerInputs.DPadDown,
            13 => GameControllerInputs.DPadLeft,
            14 => GameControllerInputs.DPadRight,
            _ => null
        };
        if (!input.HasValue || Active.Contains(input.Value) == down)
            return;
        Changed.Add(input.Value);
        if (down)
            Active.Add(input.Value);
        else
            Active.Remove(input.Value);
    }

    public override float GetAnalogState(InputId input) => input == GameControllerInputs.LeftThumbstickX ? _leftX
        : input == GameControllerInputs.LeftThumbstickY ? _leftY
        : input == GameControllerInputs.RightThumbstickX ? _rightX
        : input == GameControllerInputs.RightThumbstickY ? _rightY
        : input == GameControllerInputs.LeftTrigger ? _leftTrigger
        : input == GameControllerInputs.RightTrigger ? _rightTrigger
        : 0;

    internal override void Clear()
    {
        base.Clear();
        _leftX = _leftY = _rightX = _rightY = _leftTrigger = _rightTrigger = 0;
    }

    private void Change(ref float current, float value, InputId input)
    {
        if (current == value)
            return;
        current = value;
        Changed.Add(input);
        if (value == 0)
            Active.Remove(input);
        else
            Active.Add(input);
    }

    private static float Normalize(int value, int deadZone, int maximum)
    {
        float adjusted = value < 0 ? Math.Min(value + deadZone, 0) : Math.Max(value - deadZone, 0);
        return adjusted / (maximum - deadZone);
    }
}
