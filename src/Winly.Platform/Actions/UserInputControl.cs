using System.Runtime.InteropServices;
using Serilog;
using Winly.Core.Actions;
using static Winly.Platform.Actions.NativeDesktopMethods;

namespace Winly.Platform.Actions;

/// <summary>
/// Typing and the clipboard.
///
/// Typing is deliberately plain text only: every character goes in as a Unicode keystroke, and no
/// path here can press a modifier combination. That keeps dictation from turning into "press
/// ctrl+shift+whatever in whatever happens to be focused".
/// </summary>
internal static class UserInputControl
{
    private const uint UnicodeTextFormat = 13; // CF_UNICODETEXT
    private const int MaxTypedCharacters = 4000;

    public static void Type(string text)
    {
        if (text.Length > MaxTypedCharacters)
        {
            throw new DesktopActionFailedException("That's more text than I'll type in one go.");
        }

        // One down/up pair per UTF-16 unit, so surrogate pairs (emoji) arrive intact.
        var sequence = new SyntheticInput[text.Length * 2];
        for (var index = 0; index < text.Length; index++)
        {
            sequence[index * 2] = UnicodeKey(text[index], up: false);
            sequence[(index * 2) + 1] = UnicodeKey(text[index], up: true);
        }

        var sent = SendInput((uint)sequence.Length, sequence, Marshal.SizeOf<SyntheticInput>());
        if (sent == 0)
        {
            throw new DesktopActionFailedException("Something blocked me from typing there.");
        }

        Log.Information("Typed {Count} characters", text.Length);
    }

    public static void Copy()
    {
        SyntheticInput[] sequence =
        [
            SystemControl.Key(VirtualKeyControl, up: false),
            SystemControl.Key(VirtualKeyC, up: false),
            SystemControl.Key(VirtualKeyC, up: true),
            SystemControl.Key(VirtualKeyControl, up: true),
        ];
        SendInput((uint)sequence.Length, sequence, Marshal.SizeOf<SyntheticInput>());
        Log.Information("Sent copy to the focused window");
    }

    /// <summary>The clipboard's text, or empty when it holds something that cannot be spoken.</summary>
    public static string ReadText()
    {
        // The clipboard APIs want an STA thread; the orchestrator runs on the pool, so borrow one.
        string result = string.Empty;
        var reader = new Thread(() => result = ReadTextOnThisThread());
        reader.SetApartmentState(ApartmentState.STA);
        reader.Start();
        reader.Join(TimeSpan.FromSeconds(2));
        return result;
    }

    private static string ReadTextOnThisThread()
    {
        // Another process can hold the clipboard open for a moment; a couple of tries is enough.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!OpenClipboard(0))
            {
                Thread.Sleep(60);
                continue;
            }

            try
            {
                var handle = GetClipboardData(UnicodeTextFormat);
                if (handle == 0)
                {
                    return string.Empty;
                }

                var block = GlobalLock(handle);
                if (block == 0)
                {
                    return string.Empty;
                }

                try
                {
                    return Marshal.PtrToStringUni(block) ?? string.Empty;
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        return string.Empty;
    }

    private static SyntheticInput UnicodeKey(char character, bool up) => new()
    {
        Type = InputKeyboard,
        Keyboard = new KeyboardInput
        {
            ScanCode = character,
            Flags = KeyEventUnicode | (up ? KeyEventKeyUp : 0),
        },
    };
}
