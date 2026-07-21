using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using CustomStickyNote.Native;

namespace CustomStickyNote.Services;

/// <summary>
/// "显示桌面"防护服务.
///
/// 关键发现 (网上调研 + 日志验证): Windows 10/11 的"显示桌面"按钮 (任务栏右下角) 和 Win+D
/// **不是最小化窗口**, 而是让 WorkerW 桌面窗口置顶, 覆盖所有非 Topmost 窗口. 窗口只是被
/// WorkerW 遮盖, 没有被最小化, 没有被移动.
///
/// 检测难点: 任务栏"显示桌面"按钮**不会触发** EVENT_SYSTEM_FOREGROUND 到 WorkerW
/// (日志验证: 点击按钮后无 WorkerW 前台事件, 只有后台 Chrome 窗口抢前台). 直接监听
/// WorkerW 前台不可靠.
///
/// 方案 (双 hook + 状态标志):
/// 1. Hook A: EVENT_SYSTEM_FOREGROUND (0x0003) — 前台窗口变化
/// 2. Hook B: EVENT_SYSTEM_MINIMIZEALL (0x0016) + RESTOREALL (0x0017) — 显示桌面/恢复
///
/// 状态机 (_isDesktopShown):
/// - MINIMIZEALL (显示桌面触发) → _isDesktopShown=true, 设所有便签 Topmost
/// - RESTOREALL (恢复) → _isDesktopShown=false, 取消所有便签 Topmost
/// - FOREGROUND:
///   * WorkerW/Progman 前台 (用户点击桌面) → 设 Topmost
///   * 普通应用窗口前台 → 仅当 _isDesktopShown=false 才取消 Topmost
///     (避免"显示桌面"状态下后台窗口抢前台导致误取消)
///   * 工具窗口 (WS_EX_TOOLWINDOW) 前台 → 不动 (保持当前状态)
/// </summary>
public static class ShowDesktopGuardService
{
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MINIMIZEALL = 0x0016;
    private const uint EVENT_SYSTEM_RESTOREALL = 0x0017;
    private const int OBJID_WINDOW = 0;
    private const string WorkerWClass = "WorkerW";
    private const string ProgmanClass = "Progman";

    private static readonly HashSet<Window> _windows = new();
    // 必须保持委托引用, 防止 GC 回收回调委托导致崩溃
    private static WinEventDelegate? _foregroundDelegate;
    private static WinEventDelegate? _minimizeRestoreDelegate;
    private static IntPtr _foregroundHook = IntPtr.Zero;
    private static IntPtr _minimizeRestoreHook = IntPtr.Zero;
    private static readonly object _lock = new();

    // "显示桌面"状态标志: true 表示当前处于显示桌面状态 (便签应保持 Topmost)
    private static bool _isDesktopShown = false;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CustomStickyNote", "window.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [Guard] {msg}\n"); }
        catch { }
    }

    /// <summary>
    /// 注册窗口, 当"显示桌面"触发时自动设为 Topmost 浮在桌面之上.
    /// </summary>
    public static void Register(Window window)
    {
        lock (_lock)
        {
            _windows.Add(window);
            Log($"Register: window count={_windows.Count}");
            EnsureHook();
        }
    }

    /// <summary>
    /// 注销窗口.
    /// </summary>
    public static void Unregister(Window window)
    {
        lock (_lock)
        {
            _windows.Remove(window);
            Log($"Unregister: window count={_windows.Count}");
        }
    }

    private static void EnsureHook()
    {
        if (_foregroundHook == IntPtr.Zero)
        {
            _foregroundDelegate = new WinEventDelegate(OnForegroundChanged);
            _foregroundHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _foregroundDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            Log($"EnsureHook foreground: _foregroundHook={_foregroundHook}, lastError={Marshal.GetLastWin32Error()}");
        }
        if (_minimizeRestoreHook == IntPtr.Zero)
        {
            _minimizeRestoreDelegate = new WinEventDelegate(OnMinimizeRestore);
            _minimizeRestoreHook = SetWinEventHook(
                EVENT_SYSTEM_MINIMIZEALL, EVENT_SYSTEM_RESTOREALL,
                IntPtr.Zero, _minimizeRestoreDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            Log($"EnsureHook minimize/restore: _minimizeRestoreHook={_minimizeRestoreHook}, lastError={Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>
    /// Hook A 回调: 前台窗口变化.
    /// 用于取消 Topmost (普通应用窗口前台时), 但不触发设 Topmost (由 Hook B 负责).
    /// </summary>
    private static void OnForegroundChanged(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType != EVENT_SYSTEM_FOREGROUND) return;
        if (idObject != OBJID_WINDOW) return;

        var className = GetWindowClass(hwnd);
        var isDesktop = className == WorkerWClass || className == ProgmanClass;

        var exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        const long WS_EX_TOOLWINDOW = 0x00000080;
        var fgIsToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
        // 区分"用户主动激活的可见窗口"与"后台抢前台的隐藏窗口":
        // - 用户点击任务栏浏览器: 窗口可见 (IsWindowVisible=true) 且非最小化 (IsIconic=false)
        // - Chrome 后台进程抢前台: 窗口不可见 (IsWindowVisible=false)
        var fgIsVisible = Win32.IsWindowVisible(hwnd);
        var fgIsIconic = Win32.IsIconic(hwnd);
        var fgIsActive = fgIsVisible && !fgIsIconic;
        Log($"OnForegroundChanged: hwnd={hwnd}, class='{className}', isDesktop={isDesktop}, fgIsToolWindow={fgIsToolWindow}, fgIsVisible={fgIsVisible}, fgIsIconic={fgIsIconic}, fgIsActive={fgIsActive}, _isDesktopShown={_isDesktopShown}");

        lock (_lock)
        {
            foreach (var w in _windows)
            {
                var noteHwnd = new WindowInteropHelper(w).Handle;
                if (noteHwnd == IntPtr.Zero) continue;

                const uint flags = Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE;

                if (isDesktop)
                {
                    // WorkerW/Progman 前台 (用户点击桌面): 设 Topmost
                    var ok = Win32.SetWindowPos(noteHwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, flags);
                    Log($"  SetWindowPos(note={noteHwnd}, TOPMOST) ok={ok}, lastError={Marshal.GetLastWin32Error()}");
                }
                else if (fgIsToolWindow)
                {
                    // 工具窗口前台: 不动 (保持当前 Topmost 状态)
                    Log($"  keep: ToolWindow foreground, no Z order change for note={noteHwnd}");
                }
                else if (!fgIsActive)
                {
                    // 前台窗口不可见或最小化 (后台窗口抢前台): 不取消 Topmost
                    // 避免"显示桌面"状态下 Chrome 后台进程抢前台导致便签被 WorkerW 遮盖
                    Log($"  keep: foreground not active (visible={fgIsVisible}, iconic={fgIsIconic}), keep Z order for note={noteHwnd}");
                }
                else
                {
                    // 可见的普通应用窗口前台 (用户主动激活): 取消 Topmost, 退出"显示桌面"状态
                    _isDesktopShown = false;
                    var ok = Win32.SetWindowPos(noteHwnd, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
                    Log($"  SetWindowPos(note={noteHwnd}, NOTOPMOST) ok={ok}, lastError={Marshal.GetLastWin32Error()} (active foreground, exit desktop shown)");
                }
            }
        }
    }

    /// <summary>
    /// Hook B 回调: MINIMIZEALL (显示桌面触发) / RESTOREALL (恢复).
    /// 这两个事件可靠检测任务栏"显示桌面"按钮 (EVENT_SYSTEM_FOREGROUND 不可靠).
    /// </summary>
    private static void OnMinimizeRestore(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        Log($"OnMinimizeRestore: eventType=0x{eventType:X}, hwnd={hwnd}, idObject={idObject}");

        if (eventType == EVENT_SYSTEM_MINIMIZEALL)
        {
            // "显示桌面"触发: 设所有便签 Topmost
            _isDesktopShown = true;
            Log($"  MINIMIZEALL: _isDesktopShown=true, setting all notes TOPMOST");
            SetAllNotesTopmost(true);
        }
        else if (eventType == EVENT_SYSTEM_RESTOREALL)
        {
            // 恢复: 取消所有便签 Topmost, 并排在恢复窗口 (hwnd) 之后
            // 关键: 用 SetWindowPos(note, hwnd) 而非 HWND_NOTOPMOST —
            // HWND_NOTOPMOST 会把便签放在非 Topmost 窗口 Z order 顶部, 仍在恢复的浏览器之上,
            // 导致浏览器被便签遮挡. 排在恢复窗口之后, 确保浏览器在便签之上.
            _isDesktopShown = false;
            Log($"  RESTOREALL: _isDesktopShown=false, setting all notes after restored window (hwnd={hwnd})");
            SetAllNotesZOrder(hwnd);
        }
    }

    private static void SetAllNotesTopmost(bool topmost)
    {
        lock (_lock)
        {
            var insertAfter = topmost ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST;
            const uint flags = Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE;
            foreach (var w in _windows)
            {
                try
                {
                    var noteHwnd = new WindowInteropHelper(w).Handle;
                    if (noteHwnd == IntPtr.Zero) continue;
                    var ok = Win32.SetWindowPos(noteHwnd, insertAfter, 0, 0, 0, 0, flags);
                    Log($"  SetWindowPos(note={noteHwnd}, {(topmost ? "TOPMOST" : "NOTOPMOST")}) ok={ok}, lastError={Marshal.GetLastWin32Error()}");
                }
                catch (Exception ex) { Log($"  SetWindowPos failed: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// 把所有便签排在指定窗口之后 (Z order 在其下方).
    /// 用于 RESTOREALL: 排在恢复的浏览器之后, 确保浏览器在便签之上不被遮挡.
    /// </summary>
    private static void SetAllNotesZOrder(IntPtr afterHwnd)
    {
        lock (_lock)
        {
            const uint flags = Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE;
            foreach (var w in _windows)
            {
                try
                {
                    var noteHwnd = new WindowInteropHelper(w).Handle;
                    if (noteHwnd == IntPtr.Zero) continue;
                    // 排在恢复窗口之后; 若 afterHwnd 无效则 fallback 到 NOTOPMOST
                    IntPtr insertAfter = (afterHwnd != IntPtr.Zero && afterHwnd != noteHwnd)
                        ? afterHwnd
                        : Win32.HWND_NOTOPMOST;
                    var ok = Win32.SetWindowPos(noteHwnd, insertAfter, 0, 0, 0, 0, flags);
                    Log($"  SetWindowPos(note={noteHwnd}, after={insertAfter}) ok={ok}, lastError={Marshal.GetLastWin32Error()}");
                }
                catch (Exception ex) { Log($"  SetWindowPos failed: {ex.Message}"); }
            }
        }
    }

    private static string GetWindowClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        Win32.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    internal delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
}
