using System;
using System.Runtime.InteropServices;

namespace CustomStickyNote.Native;

/// <summary>
/// Win32 API 导入 (用于桌面层级固定 + 任务栏隐藏)
/// </summary>
internal static class Win32
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    public const uint SMTO_NORMAL = 0x0000;

    /// <summary>
    /// 让 Progman 创建 WorkerW 的未公开消息
    /// </summary>
    public const uint WM_SPAWN_WORKER = 0x052C;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    // 拖拽相关: 发送 WM_NCLBUTTONDOWN(HTCAPTION) 让系统接管标题栏拖动
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HTCAPTION = 0x2;

    // 64位系统必须用 GetWindowLongPtr / SetWindowLongPtr
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtr")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtr")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    // 子类化窗口过程: GWLP_WNDPROC 用于替换窗口的 WndProc.
    // HwndSource.AddHook 的 handled=true 只阻止 WPF 处理消息, 不阻止 Win32 DefWindowProc,
    // 所以拦截 WM_WINDOWPOSCHANGING 必须用 SetWindowLongPtr(GWLP_WNDPROC) 子类化.
    public const int GWLP_WNDPROC = -4;

    [DllImport("user32.dll")]
    public static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // SetWindowPos: 调整窗口 Z order / 位置 / 大小
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    // SWP_HIDEWINDOW / SWP_SHOWWINDOW: 隐藏/显示窗口. SetParent 切换前隐藏可让 DWM
    // 移除其在合成层的内容, 切换后再显示, 这是清除 SetParent 残影 (DWM 合成层残留) 的标准做法.
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_SHOWWINDOW = 0x0040;
    // SWP_NOCOPYBITS: 丢弃旧位置内容, 避免屏幕残留
    public const uint SWP_NOCOPYBITS = 0x0100;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    // GetWindowRect: 获取窗口屏幕坐标矩形 (用于鼠标命中检测)
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // GetCursorPos: 获取鼠标屏幕坐标 (用于轮询鼠标是否进入便签区域)
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    // GetWindow: 获取与指定窗口有特定关系的窗口 (Z order 中的下一个兄弟)
    public const uint GW_HWNDNEXT = 2;
    public const uint GW_HWNDPREV = 3;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    // GetParent: 获取指定窗口的父窗口句柄
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetParent(IntPtr hWnd);

    // GetClassName: 获取窗口类名
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    // IsWindowVisible: 窗口是否可见 (WS_VISIBLE 标志). 用于区分"用户主动激活的可见窗口"
    // 与"后台抢前台的隐藏窗口" (如 Chrome 后台进程窗口).
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    // IsIconic: 窗口是否最小化. 最小化窗口不应导致便签取消 Topmost.
    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    // GetForegroundWindow: 获取当前前台窗口. 用于延迟检查窗口恢复可见后取消 Topmost.
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    // ShowWindow: 控制窗口可见性 (SetParent 切换时隐藏/显示避免残影)
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_RESTORE = 9; // 恢复 minimized/maximized 窗口到原始大小/位置

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // WM_SETREDRAW: 禁止/允许窗口重绘 (SetParent 切换时避免残影, 比 ShowWindow 更可靠)
    public const int WM_SETREDRAW = 0x000B;

    // RedrawWindow: 强制刷新窗口区域 (WM_SETREDRAW 允许重绘后强制刷新)
    [DllImport("user32.dll")]
    public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    public const uint RDW_INVALIDATE = 0x0001;
    public const uint RDW_ALLCHILDREN = 0x0080;
    public const uint RDW_UPDATENOW = 0x0100;
    public const uint RDW_ERASE = 0x0004;

    // GetDpiForSystem: 获取系统 DPI (用于 WPF 逻辑像素与物理像素转换)
    // WPF 窗口大小是逻辑像素 (DIP), SystemParameters.WorkArea 在非 Per-Monitor DPI 感知时
    // 可能返回物理像素. 用 GetDpiForSystem / 96 得到 DPI 缩放比例, 转换为逻辑像素.
    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    // DwmFlush: 强制 DWM 立即合成新帧. SetParent 切换后调用, 让 DWM 重新合成屏幕,
    // 清除旧位置的合成层残留 (残影). GDI 方法 (RedrawWindow) 对 DWM 合成层无效.
    [DllImport("dwmapi.dll")]
    public static extern void DwmFlush();
}
