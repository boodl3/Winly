using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Serilog;
using Winly.Core.Abstractions;
using static Winly.Platform.Input.NativeInputMethods;

namespace Winly.Platform.Input;

/// <summary>
/// A <c>WH_KEYBOARD_LL</c> hook on its own thread with its own message loop (research.md §3).
/// Observes the combination without consuming it (FR-003) and never blocks the hook thread —
/// Windows silently removes hooks that stall.
/// </summary>
public sealed unsafe class LowLevelKeyboardHookActivationMonitor : IActivationKeyMonitor
{
    // One low-level hook per process; the unmanaged callback has no instance context.
    private static LowLevelKeyboardHookActivationMonitor? _activeInstance;

    private readonly HashSet<uint> _heldKeys = [];
    private readonly object _gate = new();
    private uint[] _combinationKeys = [];
    private bool _combinationHeld;
    private nint _hookHandle;
    private Thread? _hookThread;
    private uint _hookThreadId;

    public event Action? KeyDown;

    public event Action? KeyUp;

    public Task Start(KeyCombination combination, CancellationToken cancellationToken)
    {
        if (_hookThread is not null)
        {
            throw new InvalidOperationException("The activation key monitor is already running.");
        }

        _combinationKeys = combination.KeyNames.Select(VirtualKeyNames.Resolve).Distinct().ToArray();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _hookThread = new Thread(() => RunMessageLoop(started)) { IsBackground = true, Name = "Winly activation key hook" };
        _hookThread.Start();
        return started.Task.WaitAsync(cancellationToken);
    }

    public Task Stop()
    {
        if (_hookThreadId != 0)
        {
            PostThreadMessageW(_hookThreadId, QuitMessage, 0, 0);
        }

        _hookThread?.Join(TimeSpan.FromSeconds(1));
        _hookThread = null;
        _hookThreadId = 0;
        _activeInstance = null;
        lock (_gate)
        {
            _heldKeys.Clear();
            _combinationHeld = false;
        }

        return Task.CompletedTask;
    }

    private void RunMessageLoop(TaskCompletionSource started)
    {
        _hookThreadId = GetCurrentThreadId();
        _activeInstance = this;
        _hookHandle = SetWindowsHookExW(LowLevelKeyboardHook, &HookCallback, GetModuleHandleW(null), 0);
        if (_hookHandle == 0)
        {
            started.SetException(new ActivationKeyUnavailableException("The keyboard hook could not be installed.", new Win32Exception()));
            return;
        }

        started.SetResult();
        while (GetMessageW(out _, 0, 0, 0) > 0)
        {
            // Nothing to dispatch: the hook itself is the only work this thread does.
        }

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            try
            {
                _activeInstance?.OnKeyboardEvent((int)wParam, ((KeyboardLowLevelHookData*)lParam)->VirtualKeyCode);
            }
            catch (Exception exception)
            {
                // An exception escaping an unmanaged callback would kill the process.
                Log.Error(exception, "Keyboard hook callback failed");
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private void OnKeyboardEvent(int message, uint virtualKey)
    {
        bool raiseDown = false, raiseUp = false;
        lock (_gate)
        {
            switch (message)
            {
                case KeyDownMessage or SystemKeyDownMessage:
                    _heldKeys.Add(virtualKey);
                    break;
                case KeyUpMessage or SystemKeyUpMessage:
                    _heldKeys.Remove(virtualKey);
                    break;
                default:
                    return;
            }

            var nowHeld = _combinationKeys.Length > 0 && _combinationKeys.All(_heldKeys.Contains);
            raiseDown = nowHeld && !_combinationHeld;
            raiseUp = !nowHeld && _combinationHeld;
            _combinationHeld = nowHeld;
        }

        if (raiseDown)
        {
            ThreadPool.QueueUserWorkItem(_ => KeyDown?.Invoke());
        }

        if (raiseUp)
        {
            ThreadPool.QueueUserWorkItem(_ => KeyUp?.Invoke());
        }
    }
}
