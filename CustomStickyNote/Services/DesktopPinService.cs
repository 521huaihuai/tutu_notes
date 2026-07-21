using System;
using System.Windows;
using System.Windows.Interop;
using CustomStickyNote.Native;

namespace CustomStickyNote.Services;

/// <summary>
/// 桌面层级服务.
///
/// 便签作为顶层窗口 (WS_EX_TOOLWINDOW, 不在任务栏显示).
///
/// 注: SetParent 嵌入桌面层方案已尝试失败:
/// - SetParent 到 Progman: Progman 不渲染非 DefView 子窗口, 便签被吞
/// - SetParent 到壁纸层 WorkerW: Z order 在底部, 便签不可见
/// - SetParent 到 defViewHost (含图标 WorkerW) + SetWindowPos 提到 DefView 之上: 系统重排后仍不可见
/// 故回退为顶层窗口, 由调用方决定是否用 Topmost 保证"显示桌面"时可见.
/// </summary>
public static class DesktopPinService
{
    public static void PinToDesktop(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // WS_EX_TOOLWINDOW: 不在任务栏/Alt+Tab 显示
        IntPtr exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE,
            new IntPtr(exStyle.ToInt64() | Win32.WS_EX_TOOLWINDOW));
    }
}
