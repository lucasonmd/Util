using System.Runtime.InteropServices;

namespace HmiViewer.Services;

/// <summary>
/// Win32 SendInput 기반 키보드 이벤트를 외부 프로세스로 전달하는 서비스.
/// 대상 프로세스에 포커스를 전환 후 키를 발생시킨다.
/// </summary>
public static class InputService
{
    [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP   = 0x0002;
    private const uint INPUT_KEYBOARD    = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_UNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint       type;
        public INPUT_UNION u;
    }

    // 대상 창 핸들 (외부에서 설정)
    public static IntPtr TargetWindowHandle { get; set; } = IntPtr.Zero;

    public static void SendVirtualKey(int vkCode)
    {
        if (TargetWindowHandle != IntPtr.Zero)
            SetForegroundWindow(TargetWindowHandle);

        var inputs = new INPUT[]
        {
            MakeKeyInput((ushort)vkCode, KEYEVENTF_KEYDOWN),
            MakeKeyInput((ushort)vkCode, KEYEVENTF_KEYUP),
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void SendVirtualKeyToWindow(IntPtr hwnd, int vkCode)
    {
        if (hwnd != IntPtr.Zero)
            SetForegroundWindow(hwnd);

        var inputs = new INPUT[]
        {
            MakeKeyInput((ushort)vkCode, KEYEVENTF_KEYDOWN),
            MakeKeyInput((ushort)vkCode, KEYEVENTF_KEYUP),
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeKeyInput(ushort vk, uint flags) => new INPUT
    {
        type = INPUT_KEYBOARD,
        u    = new INPUT_UNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } }
    };
}
