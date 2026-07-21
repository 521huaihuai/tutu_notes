using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CustomStickyNote.Native;

namespace CustomStickyNote.Services;

/// <summary>
/// 桌面层级服务 (两套机制).
///
/// 默认状态: SetParent 到 WorkerW (桌面层), Win+D 不隐藏, 类似 360 桌面工具.
/// 交互状态: SetParent 到 null (脱离桌面层), 鼠标可交互 (移动/编辑/拖拽).
///
/// 切换由 DispatcherTimer 轮询鼠标位置驱动:
/// - 鼠标进入便签屏幕矩形 → SwitchToInteractive (脱离桌面层)
/// - 鼠标离开便签 (MouseLeave 事件) → SwitchToPinned (恢复桌面层)
/// </summary>
public static class DesktopPinService
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CustomStickyNote", "pin.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
        catch { }
    }

    private static Window? _window;
    private static IntPtr _hwnd;
    private static IntPtr _workerW;
    private static DispatcherTimer? _timer;
    private static bool _isInteractive;
    private static bool _isPinned;

    // 鼠标进入便签时的屏幕坐标缓冲, 避免在切换瞬间丢失鼠标位置.
    private static int _savedLeft;
    private static int _savedTop;
    private static int _savedWidth;
    private static int _savedHeight;

    public static void PinToDesktop(Window window)
    {
        _window = window;
        _hwnd = new WindowInteropHelper(window).Handle;
        if (_hwnd == IntPtr.Zero)
        {
            Log("PinToDesktop: hwnd is Zero, abort");
            return;
        }

        // 1. WS_EX_TOOLWINDOW: 不在任务栏/Alt+Tab 显示
        IntPtr exStyle = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE,
            new IntPtr(exStyle.ToInt64() | Win32.WS_EX_TOOLWINDOW));

        // 2. 查找 WorkerW (桌面壁纸窗口)
        _workerW = FindWorkerW();
        Log($"PinToDesktop: hwnd=0x{_hwnd.ToInt64():X}, WorkerW=0x{_workerW.ToInt64():X}");

        if (_workerW == IntPtr.Zero)
        {
            Log("PinToDesktop: WorkerW not found, abort");
            return;
        }

        // 3. SetParent 到 WorkerW, 便签成为桌面壁纸的子窗口, Win+D 不会最小化
        SaveBounds();
        Log($"PinToDesktop: before SetParent, bounds={_savedLeft},{_savedTop},{_savedWidth}x{_savedHeight}, window ActualSize={window.ActualWidth}x{window.ActualHeight}");
        var prevParent = Win32.SetParent(_hwnd, _workerW);
        Log($"PinToDesktop: SetParent prevParent=0x{prevParent.ToInt64():X}");
        const uint SWP_NOCOPYBITS = 0x0100;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOP,
            _savedLeft, _savedTop, _savedWidth, _savedHeight,
            Win32.SWP_NOACTIVATE | SWP_NOCOPYBITS);

        _isPinned = true;
        _isInteractive = false;
        Log($"PinToDesktop: after SetParent, window ActualSize={window.ActualWidth}x{window.ActualHeight}");
        Log("PinToDesktop: pinned=true, interactive=false");

        // 4. 启动轮询 Timer: 检测鼠标是否进入便签区域
        if (_timer == null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _timer.Tick += Timer_Tick;
        }
        _timer.Start();
    }

    /// <summary>
    /// 切换到交互模式: SetParent 到 null, 脱离桌面层, 鼠标可交互.
    /// 由 Timer_Tick 检测到鼠标进入便签区域时调用.
    /// 不使用 ShowWindow (会丢失 WPF 鼠标事件), 改用 SWP_NOCOPYBITS 避免残影.
    /// </summary>
    public static void SwitchToInteractive()
    {
        if (!_isPinned || _isInteractive || _hwnd == IntPtr.Zero) return;
        _isInteractive = true;

        SaveBounds();
        Win32.SetParent(_hwnd, IntPtr.Zero);
        // SWP_NOCOPYBITS: 丢弃旧位置内容, 避免屏幕残留
        const uint SWP_NOCOPYBITS = 0x0100;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOP,
            _savedLeft, _savedTop, _savedWidth, _savedHeight,
            Win32.SWP_NOACTIVATE | SWP_NOCOPYBITS);

        // 切换父级后, WPF 的 IsMouseOver 可能未及时更新. 通知窗口手动触发 MouseEnter 逻辑.
        _window?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (_window == null) return;
            Log($"SwitchToInteractive: IsMouseOver={_window.IsMouseOver}, IsVisible={_window.IsVisible}");
            if (_window.IsMouseOver)
            {
                _window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                {
                    RoutedEvent = Mouse.MouseEnterEvent
                });
            }
        });
        Log($"SwitchToInteractive: done, bounds={_savedLeft},{_savedTop},{_savedWidth}x{_savedHeight}");
    }

    /// <summary>
    /// 切换回桌面固定: SetParent 到 WorkerW, 恢复桌面层, Win+D 不隐藏.
    /// 由便签 MouseLeave 事件调用.
    /// 不使用 ShowWindow (会丢失 WPF 鼠标事件), 改用 SWP_NOCOPYBITS 避免残影.
    /// </summary>
    public static void SwitchToPinned()
    {
        if (!_isPinned || !_isInteractive || _hwnd == IntPtr.Zero) return;
        _isInteractive = false;

        SaveBounds();
        Win32.SetParent(_hwnd, _workerW);
        const uint SWP_NOCOPYBITS = 0x0100;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOP,
            _savedLeft, _savedTop, _savedWidth, _savedHeight,
            Win32.SWP_NOACTIVATE | SWP_NOCOPYBITS);
        Log($"SwitchToPinned: done, bounds={_savedLeft},{_savedTop},{_savedWidth}x{_savedHeight}");
    }

    /// <summary>
    /// Timer Tick: 检测鼠标是否在便签屏幕矩形内, 是则切换到交互模式.
    /// 切换到交互模式后停止轮询, 由 MouseLeave 事件触发回切.
    /// </summary>
    private static void Timer_Tick(object? sender, EventArgs e)
    {
        if (_isInteractive || _hwnd == IntPtr.Zero) return;

        if (!Win32.GetCursorPos(out var pt)) return;
        if (!Win32.GetWindowRect(_hwnd, out var rect)) return;

        // 鼠标在便签矩形内 (含边界), 切换到交互模式
        if (pt.X >= rect.Left && pt.X <= rect.Right &&
            pt.Y >= rect.Top && pt.Y <= rect.Bottom)
        {
            SwitchToInteractive();
        }
    }

    /// <summary>
    /// 保存当前窗口屏幕坐标 (SetParent 前调用), 防止切换父级后窗口位置漂移.
    /// </summary>
    private static void SaveBounds()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (Win32.GetWindowRect(_hwnd, out var rect))
        {
            _savedLeft = rect.Left;
            _savedTop = rect.Top;
            _savedWidth = rect.Right - rect.Left;
            _savedHeight = rect.Bottom - rect.Top;
        }
    }

    /// <summary>
    /// 恢复窗口屏幕坐标 (SetParent 后调用), 让窗口在切换父级时位置/大小保持不变.
    /// </summary>
    private static void RestoreBounds()
    {
        if (_hwnd == IntPtr.Zero) return;
        Win32.SetWindowPos(_hwnd, IntPtr.Zero,
            _savedLeft, _savedTop, _savedWidth, _savedHeight,
            Win32.SWP_NOACTIVATE);
    }

    /// <summary>
    /// 查找壁纸 WorkerW 窗口 (标准方法).
    /// 1. 让 Progman 通过 WM_SPAWN_WORKER 创建 WorkerW
    /// 2. 找到 SHELLDLL_DefView (桌面图标容器) 的父窗口
    /// 3. 通过 GetWindow(GW_HWNDNEXT) 找该父窗口在 Z order 中的下一个兄弟
    /// 4. 该兄弟窗口类名应为 "WorkerW", 即壁纸层窗口
    /// </summary>
    private static IntPtr FindWorkerW()
    {
        var progman = Win32.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            Log("FindWorkerW: Progman not found");
            return IntPtr.Zero;
        }
        Log($"FindWorkerW: Progman=0x{progman.ToInt64():X}");

        // 1. 让 Progman 创建 WorkerW (发送未公开消息 0x052C)
        Win32.SendMessageTimeout(progman, Win32.WM_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero,
            Win32.SMTO_NORMAL, 1000, out _);

        // 2. 找到 SHELLDLL_DefView. 可能在 Progman 下, 也可能在某个 WorkerW 下.
        var defView = Win32.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        Log($"FindWorkerW: SHELLDLL_DefView under Progman=0x{defView.ToInt64():X}");

        if (defView == IntPtr.Zero)
        {
            // 在 Progman 下没找到, 用 EnumWindows 在所有顶层窗口下找
            Win32.EnumWindows((hwnd, _) =>
            {
                var def = Win32.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (def != IntPtr.Zero)
                {
                    defView = def;
                    Log($"FindWorkerW: SHELLDLL_DefView found under hwnd=0x{hwnd.ToInt64():X}, defView=0x{def.ToInt64():X}");
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        if (defView == IntPtr.Zero)
        {
            Log("FindWorkerW: SHELLDLL_DefView not found, abort");
            return IntPtr.Zero;
        }

        // 3. 获取 SHELLDLL_DefView 的父窗口 (通常是 Progman)
        var parent = Win32.GetParent(defView);
        Log($"FindWorkerW: parent of SHELLDLL_DefView=0x{parent.ToInt64():X}");

        if (parent == IntPtr.Zero)
        {
            Log("FindWorkerW: parent is Zero, abort");
            return IntPtr.Zero;
        }

        // 4. 从父窗口开始, 用 GetWindow(GW_HWNDNEXT) 找下一个兄弟, 直到找到 WorkerW 类
        var next = parent;
        int maxIter = 32; // 防止死循环
        while (maxIter-- > 0 && next != IntPtr.Zero)
        {
            next = Win32.GetWindow(next, Win32.GW_HWNDNEXT);
            if (next == IntPtr.Zero) break;

            var sb = new System.Text.StringBuilder(64);
            Win32.GetClassName(next, sb, sb.Capacity);
            var clsName = sb.ToString();
            Log($"FindWorkerW: sibling hwnd=0x{next.ToInt64():X}, class={clsName}");

            if (clsName == "WorkerW")
            {
                Log($"FindWorkerW: WorkerW found at 0x{next.ToInt64():X}");
                return next;
            }
        }

        Log("FindWorkerW: WorkerW not found in siblings, abort");
        return IntPtr.Zero;
    }
}
