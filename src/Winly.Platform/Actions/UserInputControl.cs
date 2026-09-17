using System.Runtime.InteropServices;
using Serilog;
using Winly.Core.Actions;
using static Winly.Platform.Actions.NativeDesktopMethods;

namespace Winly.Platform.Actions;

/// <summary>
/// Typing and the clipboard.
///
/// Typing is deliberately plain text only, and it goes in as a paste rather than as keystrokes.
/// Synthesised keystrokes cannot carry a composed note: measured against a real Notepad, 532
/// characters sent as 1,064 SendInput events arrived as 87, with characters dropped and others
/// stuck repeating - the run of full stops a user sees. Slowing down fixes it only at about one
/// character per 15 ms, which is 8 seconds for a short note and past the per-action time bound
/// long before a real email. A paste is one keystroke and is exact.
///
/// The only chord pressed is a hardcoded Ctrl+V, exactly as Copy presses a hardcoded Ctrl+C. No
/// model text is ever turned into a key combination, which is the constraint that matters: it
/// keeps dictation from becoming "press ctrl+shift+whatever in whatever happens to be focused".
/// </summary>
internal static class UserInputControl
{
    private const uint UnicodeTextFormat = 13; // CF_UNICODETEXT
    private const int MaxTypedCharacters = 4000;

    /// <summary>How long the target gets to read the clipboard before it is put back.</summary>
    private const int PasteSettleMilliseconds = 250;

    /// <summary>Where the user was pointed when they asked. Zero means no target was recorded.</summary>
    public static void Type(string text, nint intendedWindow)
    {
        // The clipboard wants CRLF, and normalising first means the length checked below is the
        // length that actually lands.
        text = text.ReplaceLineEndings("\r\n");

        if (text.Length > MaxTypedCharacters)
        {
            throw new DesktopActionFailedException("That's more text than I'll type in one go.");
        }

        VerifyTarget(intendedWindow);
        PasteText(text);

        Log.Information("Typed {Count} characters", text.Length);
    }

    /// <summary>
    /// Puts the text on the clipboard, presses Ctrl+V, and puts back what was there.
    ///
    /// The restore is text-only: a clipboard holding an image or files reads as empty here and is
    /// cleared rather than preserved.
    /// ponytail: text-only restore. Capture the whole IDataObject if someone loses a copied image
    /// to this.
    /// </summary>
    private static void PasteText(string text)
    {
        var previous = ReadText();

        if (!WriteText(text))
        {
            throw new DesktopActionFailedException("I couldn't reach the clipboard, so I didn't type that.");
        }

        SyntheticInput[] sequence =
        [
            SystemControl.Key(VirtualKeyControl, up: false),
            SystemControl.Key(VirtualKeyV, up: false),
            SystemControl.Key(VirtualKeyV, up: true),
            SystemControl.Key(VirtualKeyControl, up: true),
        ];
        Send(sequence, "typing there");

        // The paste is asynchronous: the target reads the clipboard when it handles the keystroke,
        // so restoring immediately would hand it the old contents instead.
        Thread.Sleep(PasteSettleMilliseconds);
        WriteText(previous);
    }

    /// <summary>
    /// Checked immediately before the keystrokes go out, against the window that held focus when
    /// the user started speaking — the only moment that corresponds to what they meant by "here".
    /// Comparing against the foreground window at execution time would compare it to itself.
    ///
    /// A window can still take focus between this check and <c>SendInput</c>. Closing that window
    /// needs BlockInput, which needs elevation, which Principle VIII forbids; the race is
    /// microseconds wide and is accepted here rather than papered over.
    /// </summary>
    private static void VerifyTarget(nint intendedWindow)
    {
        if (intendedWindow == 0)
        {
            throw new DesktopActionFailedException("I lost track of where you wanted that typed, so I didn't type it.");
        }

        var focused = GetForegroundWindow();
        if (focused != intendedWindow)
        {
            Log.Information("Typing abandoned: focus moved from {Intended} to {Focused}", intendedWindow, focused);
            throw new DesktopActionFailedException("You moved to a different window, so I didn't type that anywhere.");
        }

        if (IsElevated(focused))
        {
            Log.Information("Typing abandoned: {Window} belongs to an elevated process", focused);
            throw new DesktopActionFailedException("Windows won't let me type into that window.");
        }
    }

    /// <summary>
    /// SendInput reports success when UIPI silently drops the keystrokes into an elevated window,
    /// so without this check a user typing into an elevated console would be told it worked.
    /// </summary>
    private static bool IsElevated(nint window)
    {
        if (GetWindowThreadProcessId(window, out var processId) == 0)
        {
            return true;
        }

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == 0)
        {
            return true;
        }

        CloseHandle(handle);
        return false;
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
        Send(sequence, "copying that");
        Log.Information("Sent copy to the focused window");
    }

    /// <summary>
    /// Sends one batch and insists it was accepted.
    ///
    /// The return value used to be discarded here and in the Win+key chords, so both logged success
    /// whatever happened — which is how a malformed cbSize went unnoticed for the whole life of
    /// three verbs. Nothing that sends input may assume it landed.
    /// </summary>
    internal static void Send(SyntheticInput[] sequence, string doingWhat)
    {
        var sent = SendInput((uint)sequence.Length, sequence, Marshal.SizeOf<SyntheticInput>());
        if (sent == sequence.Length)
        {
            return;
        }

        // Named, not swallowed: 87 is a malformed cbSize, 5 is UIPI refusing on integrity grounds,
        // and without the number the two are the same sentence to whoever reads the log next.
        Log.Warning(
            "SendInput accepted {Sent} of {Total} events, Win32 error {Error}",
            sent,
            sequence.Length,
            Marshal.GetLastPInvokeError());
        throw new DesktopActionFailedException($"Something blocked me from {doingWhat}.");
    }

    /// <summary>
    /// Replaces the clipboard's text. False when the clipboard could not be opened, which is the
    /// one case where pasting must not go ahead: the target would receive whatever was there
    /// before.
    /// </summary>
    private static bool WriteText(string text)
    {
        var written = false;
        var writer = new Thread(() => written = WriteTextOnThisThread(text));
        writer.SetApartmentState(ApartmentState.STA);
        writer.Start();
        writer.Join(TimeSpan.FromSeconds(2));
        return written;
    }

    private static bool WriteTextOnThisThread(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!OpenClipboard(0))
            {
                Thread.Sleep(60);
                continue;
            }

            try
            {
                EmptyClipboard();
                if (text.Length == 0)
                {
                    return true;
                }

                // Null-terminated UTF-16, in moveable memory the system takes ownership of.
                var bytes = (nuint)((text.Length + 1) * sizeof(char));
                var block = GlobalAlloc(GlobalMoveable, bytes);
                if (block == 0)
                {
                    return false;
                }

                var target = GlobalLock(block);
                if (target == 0)
                {
                    GlobalFree(block);
                    return false;
                }

                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                    Marshal.WriteInt16(target, text.Length * sizeof(char), 0);
                }
                finally
                {
                    GlobalUnlock(block);
                }

                if (SetClipboardData(UnicodeTextFormat, block) == 0)
                {
                    // Ownership only transfers on success, so a failed handover is ours to free.
                    GlobalFree(block);
                    return false;
                }

                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }

        return false;
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

}
