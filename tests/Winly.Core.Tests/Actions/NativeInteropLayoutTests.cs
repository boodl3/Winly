using System.Runtime.InteropServices;
using Winly.Platform.Actions;

namespace Winly.Core.Tests.Actions;

/// <summary>
/// The sizes `SendInput` is handed as `cbSize`.
///
/// This is the whole of a bug that made `type`, `clipboard copy` and the `system` Win+key chords
/// fail for as long as they had existed: `SendInput` rejects a `cbSize` that is not exactly
/// `sizeof(INPUT)`, returns 0 and delivers nothing, and two of the three call sites discarded the
/// return value and logged success anyway. Nothing about it is visible at a call site or in a log —
/// only the arithmetic says it is wrong, so the arithmetic is asserted.
/// </summary>
public class NativeInteropLayoutTests
{
    /// <summary>
    /// `INPUT` is a DWORD type, padding to the union's alignment, and a union whose largest arm is
    /// `MOUSEINPUT`: 4 + 4 + 32 on x64, 4 + 24 on x86.
    /// </summary>
    [Fact]
    public void TheInputUnionIsExactlyTheSizeSendInputExpects() =>
        Assert.Equal(nint.Size == 8 ? 40 : 28, Marshal.SizeOf<NativeDesktopMethods.SyntheticInput>());

    /// <summary>
    /// `KEYBDINPUT` is wVk, wScan, dwFlags, time, dwExtraInfo — the last a ULONG_PTR, which is what
    /// makes it 24 rather than 20 on x64. It is the arm that must fit inside the union above.
    /// </summary>
    [Fact]
    public void TheKeyboardArmFitsInsideTheUnion()
    {
        var arm = Marshal.SizeOf<NativeDesktopMethods.KeyboardInput>();

        Assert.Equal(nint.Size == 8 ? 24 : 16, arm);
        Assert.True(arm <= Marshal.SizeOf<NativeDesktopMethods.SyntheticInput>() - (nint.Size == 8 ? 8 : 4));
    }
}
