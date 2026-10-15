using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AutoPresser.Models;
using System.Windows.Input;

namespace AutoPresser.Input;

public sealed partial class GlobalKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    // ReSharper disable once IdentifierTypo
    private const uint LlkhfExtended = 0x00000001;
    // ReSharper disable once IdentifierTypo
    private const uint LlkhfInjected = 0x00000010;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable DO NOT MOVE THIS
    private readonly LowLevelKeyboardProc _callback;
    private readonly nint _hook;
    private readonly HashSet<Key> _pressedKeys = [];
    private bool _disposed;

    public event EventHandler<GlobalKeyEventArgs>? KeyDown;

    public GlobalKeyboardHook()
    {
        _callback = HookCallback;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule
                           ?? throw new InvalidOperationException("Unable to access the current process module.");

        _hook = SetWindowsHookEx(
            WhKeyboardLl,
            _callback,
            GetModuleHandle(module.ModuleName),
            0);

        if (_hook == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0 || wParam is not (WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp))
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        if ((data.Flags & LlkhfInjected) != 0)
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        var key = GetKey(data);
        var isKeyDown = wParam is WmKeyDown or WmSysKeyDown;

        if (isKeyDown)
        {
            // The low-level hook reports repeated key-down messages while a key is
            // held. Treat a physical press as one trigger until its key-up arrives.
            var isNewPress = _pressedKeys.Add(key);
            if (isNewPress)
            {
                KeyDown?.Invoke(this, new GlobalKeyEventArgs(key, GetCurrentModifiers()));
            }
        }
        else
        {
            _pressedKeys.Remove(key);
        }

        // Trigger keys always pass through to the foreground application.
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static Key GetKey(KbdLlHookStruct data)
    {
        // Windows uses the generic VK_SHIFT/VK_CONTROL/VK_MENU codes for some
        // modifier events. Use the scan code / extended flag to track both sides.
        return data.VirtualKey switch
        {
            0x10 => data.ScanCode == 0x36 ? Key.RightShift : Key.LeftShift,
            0x11 => (data.Flags & LlkhfExtended) != 0 ? Key.RightCtrl : Key.LeftCtrl,
            0x12 => (data.Flags & LlkhfExtended) != 0 ? Key.RightAlt : Key.LeftAlt,
            _ => KeyInterop.KeyFromVirtualKey((int)data.VirtualKey)
        };
    }

    private TriggerModifiers GetCurrentModifiers()
    {
        var modifiers = TriggerModifiers.None;

        if (_pressedKeys.Contains(Key.LeftCtrl) || _pressedKeys.Contains(Key.RightCtrl))
        {
            modifiers |= TriggerModifiers.Control;
        }

        if (_pressedKeys.Contains(Key.LeftShift) || _pressedKeys.Contains(Key.RightShift))
        {
            modifiers |= TriggerModifiers.Shift;
        }

        if (_pressedKeys.Contains(Key.LeftAlt) || _pressedKeys.Contains(Key.RightAlt))
        {
            modifiers |= TriggerModifiers.Alt;
        }

        if (_pressedKeys.Contains(Key.LWin) || _pressedKeys.Contains(Key.RWin))
        {
            modifiers |= TriggerModifiers.Windows;
        }

        return modifiers;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
        }

        _pressedKeys.Clear();
        _disposed = true;
    }

    private delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial nint SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc callback,
        nint module,
        uint threadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? moduleName);
}

public sealed class GlobalKeyEventArgs(Key key, TriggerModifiers modifiers) : EventArgs
{
    public Key Key { get; } = key;
    public TriggerModifiers Modifiers { get; } = modifiers;
}
