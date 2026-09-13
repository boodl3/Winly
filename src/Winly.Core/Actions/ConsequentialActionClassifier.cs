namespace Winly.Core.Actions;

/// <summary>
/// Which actions Winly must ask about before carrying out (FR-011).
///
/// The rule deliberately errs toward confirming: a verb whose classification is debatable is
/// confirmed until someone decides otherwise, and an unrecognised verb is confirmed rather than
/// waved through. A false confirmation costs the user one spoken word; a false pass can cost them
/// unsaved work.
/// </summary>
public static class ConsequentialActionClassifier
{
    public static bool IsConsequential(DesktopAction action) => action.Kind switch
    {
        // Types into whatever holds focus, which is never fully knowable in advance.
        DesktopActionKind.Type => true,

        // Closing may discard unsaved work; every other window verb is reversible.
        DesktopActionKind.Window => action.Argument.Equals("close", StringComparison.OrdinalIgnoreCase),

        // Locking and sleeping end the session; the rest are reversible settings.
        DesktopActionKind.System => action.Target is "lock" or "sleep",

        // Pressing a link or a play button is ordinary and confirming every one of them would make
        // the verb unusable. The label is the only thing known about the control before it is
        // pressed, so the few words that mean money or destruction are checked against it — a
        // control the user has to be sure about says so in its own name.
        DesktopActionKind.Click => LooksIrreversible(action.Target),

        DesktopActionKind.Open
            or DesktopActionKind.OpenPath
            or DesktopActionKind.Play
            or DesktopActionKind.Queue
            or DesktopActionKind.Media
            or DesktopActionKind.Volume
            or DesktopActionKind.Clipboard
            or DesktopActionKind.Timer => false,

        // A verb added without being classified lands here and is confirmed.
        _ => true,
    };

    /// <summary>
    /// Words that make a button worth asking about. Substring, not whole-word: "Unsubscribe" and
    /// "Deleting" both matter, and a false confirmation costs one spoken sentence.
    /// </summary>
    private static readonly string[] IrreversibleLabelWords =
    [
        "delete", "remove", "uninstall", "discard", "erase", "empty", "format", "wipe",
        "buy", "purchase", "pay", "order", "checkout", "subscribe", "send", "publish", "post",
        "confirm", "unfriend", "unfollow", "block", "leave", "sign out", "log out",
    ];

    private static bool LooksIrreversible(string label) =>
        IrreversibleLabelWords.Any(word => label.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>What Winly says out loud before doing it — plain language, and a question.</summary>
    public static string Ask(DesktopAction action) => action.Kind switch
    {
        DesktopActionKind.Type => "I'm about to type that into whatever you have open. Should I go ahead?",
        DesktopActionKind.Window => $"I'm about to close {action.Target}. Should I go ahead?",
        DesktopActionKind.System when action.Target == "lock" => "I'm about to lock your PC. Should I go ahead?",
        DesktopActionKind.System when action.Target == "sleep" => "I'm about to put your PC to sleep. Should I go ahead?",
        DesktopActionKind.Click => $"I'm about to press {action.Target}, which might not be undoable. Should I go ahead?",
        _ => "I'm about to do something I can't undo. Should I go ahead?",
    };

    /// <summary>
    /// The same action as a phrase that fits inside "you said no to …", for the report and the
    /// action record.
    /// </summary>
    public static string Describe(DesktopAction action) => action.Kind switch
    {
        DesktopActionKind.Type => "typing that out",
        DesktopActionKind.Window => $"closing {action.Target}",
        DesktopActionKind.System when action.Target == "lock" => "locking your PC",
        DesktopActionKind.System when action.Target == "sleep" => "putting your PC to sleep",
        DesktopActionKind.Click => $"pressing {action.Target}",
        _ => "that",
    };
}
