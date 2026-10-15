using System.Runtime.InteropServices;

// 全部 P/Invoke 仅从 System32 解析目标 DLL，降低 DLL 劫持/侧载风险（CA5392）
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Numlock;

/// <summary>Win32 P/Invoke 现代化声明（采用 .NET 10 LibraryImport 源生成器）</summary>
internal static partial class Native
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const int VK_NUMLOCK = 0x90;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint LLKHF_INJECTED = 0x10;
    public const uint INPUT_KEYBOARD = 1;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMSBT_TRANSIENTWINDOW = 3; // 亚克力/瞬态悬浮材质 (Acrylic)
    public const int SM_CXSMICON = 49;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static partial IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll")]
    public static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandleW(string? lpModuleName);

    [LibraryImport("user32.dll")]
    public static partial short GetKeyState(int nVirtKey);

    // 注意：INPUT 的联合体必须带 MOUSEINPUT 才能凑满 x64 下 40 字节，
    // 否则 SendInput 返回 0（ERROR_INVALID_PARAMETER）。
    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    public static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr hIcon);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hWnd);

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint INPUT_MOUSE = 0;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static partial IntPtr WindowFromPointApi(POINT p);

    public static IntPtr WindowFromPoint(Point p) => WindowFromPointApi(new POINT { X = p.X, Y = p.Y });

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>屏幕点上是否为当前进程自己的窗口（自检注入点击前的安全校验：绝不点到别的窗口上）。</summary>
    public static bool PointIsOwnWindow(Point p)
    {
        try
        {
            IntPtr hwnd = WindowFromPoint(p);
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }
            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == (uint)Environment.ProcessId;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>合成一次鼠标左键点击（虚拟屏幕绝对坐标；用于自检时把前台交给本进程）。</summary>
    public static void MouseClick(int x, int y)
    {
        if (!TryToAbsolute(x, y, out int dx, out int dy))
        {
            return;
        }
        var inputs = new[]
        {
            MakeMouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, dx, dy),
            MakeMouse(MOUSEEVENTF_LEFTDOWN, 0, 0),
            MakeMouse(MOUSEEVENTF_LEFTUP, 0, 0),
        };
        _ = SendInput(3, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>合成一次鼠标移动（虚拟屏幕绝对坐标）。</summary>
    public static void MouseMove(int x, int y)
    {
        if (!TryToAbsolute(x, y, out int dx, out int dy))
        {
            return;
        }
        var inputs = new[] { MakeMouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, dx, dy) };
        _ = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>把物理屏幕坐标换算为 0–65535 归一化坐标（覆盖整个虚拟桌面，支持多显示器负坐标）。
    /// 用 64 位中间量：虚拟桌面宽/高超过 32767px（如 8K 多屏拼接）时 int 乘法会溢出。</summary>
    private static bool TryToAbsolute(int x, int y, out int dx, out int dy)
    {
        const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        dx = dy = 0;
        if (vw <= 1 || vh <= 1)
        {
            return false;
        }
        dx = (int)((long)(x - vx) * 65535 / (vw - 1));
        dy = (int)((long)(y - vy) * 65535 / (vh - 1));
        return true;
    }

    private static INPUT MakeMouse(uint flags, int dx, int dy) => new()
    {
        type = INPUT_MOUSE,
        u = new INPUTUNION { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } },
    };

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, in int value, int size);

    /// <summary>尝试为窗口启用 DWM 圆角（仅 Win11+ 生效）；返回是否成功。</summary>
    public static bool TryRoundCorners(IntPtr hwnd)
    {
        int v = DWMWCP_ROUND;
        return DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, in v, sizeof(int)) == 0;
    }

    /// <summary>关闭 DWM 圆角（子菜单用：左侧要与父面板拼成一条直边，圆角改由自绘 Region 只做右侧）。</summary>
    public static void TryDisableRoundCorners(IntPtr hwnd)
    {
        int v = DWMWCP_DONOTROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, in v, sizeof(int));
    }

    /// <summary>尝试为窗口开启亚克力/瞬态毛玻璃材质（Win11 22H2+）。</summary>
    public static bool TrySetTransientBackdrop(IntPtr hwnd)
    {
        int v = DWMSBT_TRANSIENTWINDOW;
        return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, in v, sizeof(int)) == 0;
    }

    /// <summary>取窗口所在显示器的 DPI（Win10 1607+；旧系统上会抛 EntryPointNotFoundException，调用方兜底）。</summary>
    public static uint GetDpiForWindow(IntPtr hwnd) => GetDpiForWindowApi(hwnd);

    /// <summary>按指定 DPI 取系统度量（Win10 1607+；用于“任务栏所在显示器”的小图标尺寸）。</summary>
    public static int GetSystemMetricsForDpi(int nIndex, uint dpi) => GetSystemMetricsForDpiApi(nIndex, dpi);

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static partial uint GetDpiForWindowApi(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetricsForDpi")]
    private static partial int GetSystemMetricsForDpiApi(int nIndex, uint dpi);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    /// <summary>设置窗口沉浸式深色模式（影响 DWM 绘制的边框/阴影色调）。</summary>
    public static void SetImmersiveDarkMode(IntPtr hwnd, bool dark)
    {
        int v = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, in v, sizeof(int));
    }
}
