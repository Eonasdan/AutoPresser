using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using AutoPresser.Models;

namespace AutoPresser.Input;

public sealed class InputSimulator
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventXDown = 0x0080;
    private const uint MouseEventXUp = 0x0100;

    private const uint KeyEventKeyUp = 0x0002;

    public void Press(InputTarget target) => Send(target, down: true);

    public void Release(InputTarget target) => Send(target, down: false);

    public void Click(InputTarget target)
    {
        // If key-down succeeds but key-up fails, the caller's input-state
        // coordinator retains ownership and can retry release during cleanup.
        Press(target);
        Release(target);
    }

    private static void Send(InputTarget target, bool down)
    {
        if (target.Device == TargetDevice.Mouse)
        {
            SendMouse(target.MouseButton, down);
        }
        else
        {
            SendKey(target.Key, down);
        }
    }

    private static void SendMouse(MouseButton button, bool down)
    {
        var (flags, data) = button switch
        {
            MouseButton.Left =>
                (down ? MouseEventLeftDown : MouseEventLeftUp, 0u),

            MouseButton.Right =>
                (down ? MouseEventRightDown : MouseEventRightUp, 0u),

            MouseButton.Middle =>
                (down ? MouseEventMiddleDown : MouseEventMiddleUp, 0u),

            MouseButton.XButton1 =>
                (down ? MouseEventXDown : MouseEventXUp, 1u),

            MouseButton.XButton2 =>
                (down ? MouseEventXDown : MouseEventXUp, 2u),

            _ => throw new ArgumentOutOfRangeException(
                nameof(button), button, "Unsupported mouse button.")
        };

        var input = new NativeInput
        {
            Type = InputMouse,
            Data = new NativeInputUnion
            {
                Mouse = new NativeMouseInput
                {
                    Flags = flags,
                    MouseData = data
                }
            }
        };

        SendInputChecked(input);
    }

    private static void SendKey(Key key, bool down)
    {
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);

        if (virtualKey == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key), key, "Unsupported keyboard key.");
        }

        var input = new NativeInput
        {
            Type = InputKeyboard,
            Data = new NativeInputUnion
            {
                Keyboard = new NativeKeyboardInput
                {
                    VirtualKey = (ushort)virtualKey,
                    Flags = down ? 0u : KeyEventKeyUp
                }
            }
        };

        SendInputChecked(input);
    }

    private static void SendInputChecked(NativeInput input)
    {
        NativeInput[] inputs = [input];

        if (SendInput(
                1,
                inputs,
                Marshal.SizeOf<NativeInput>()) != 1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        public NativeMouseInput Mouse;

        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] NativeInput[] inputs,
        int inputSize);
}