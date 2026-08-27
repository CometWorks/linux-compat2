using Avalonia;
using Avalonia.Input.TextInput;
using Keen.VRage.Core.Input;

namespace LinuxCompat.Platform;

internal sealed class SdlInputMethodEditor : IInputMethodEditor, ITextInputMethodImpl
{
    private TextInputMethodClient? _client;

    public string Name => "SDL";
    public event IMETextDelegate? TextEntered;

    public void SetClient(TextInputMethodClient? client)
    {
        if (ReferenceEquals(_client, client))
            return;
        _client?.SetPreeditText(null);
        _client = client;
        SdlPlatformWindow.SetTextInputActive(client != null);
    }

    public void SetCursorRect(Rect rect) =>
        SdlPlatformWindow.SetTextInputArea(rect.X, rect.Y, rect.Width, rect.Height);

    public void SetOptions(TextInputOptions options) { }

    public void Reset()
    {
        _client?.SetPreeditText(null);
        SdlPlatformWindow.ClearTextComposition();
    }

    internal void Commit(string text)
    {
        _client?.SetPreeditText(null);
        if (text.Length != 0)
            TextEntered?.Invoke(this, text.AsSpan());
    }

    internal void SetPreedit(string text, int scalarOffset)
    {
        if (_client?.SupportsPreedit == true)
            _client.SetPreeditText(text.Length == 0 ? null : text,
                scalarOffset < 0 ? null : ToUtf16Offset(text, scalarOffset));
    }

    internal static int ToUtf16Offset(string text, int scalarOffset)
    {
        int offset = 0;
        for (int scalar = 0; scalar < scalarOffset && offset < text.Length; scalar++)
            offset += char.IsSurrogatePair(text, offset) ? 2 : 1;
        return offset;
    }
}
