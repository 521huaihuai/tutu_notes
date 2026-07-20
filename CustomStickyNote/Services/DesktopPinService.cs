using System;
using System.Windows;
using System.Windows.Interop;
using CustomStickyNote.Native;

namespace CustomStickyNote.Services;

/// <summary>
/// 桌面层级服务
/// 当前方案: 仅设置 WS_EX_TOOLWINDOW (不在任务栏/Alt+Tab 显示), 便签作为普通顶级窗口
/// (SetParent 到 WorkerW 的方案会导致鼠标事件被桌面图标层拦截, 无法交互)
/// </summary>
public static class DesktopPinService
{
    /// <summary>
    /// 让窗口不在任务栏/Alt+Tab 显示
    /// </summary>
    public static void PinToDesktop(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // 设置 WS_EX_TOOLWINDOW: 不在任务栏/Alt+Tab 显示
        IntPtr exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE,
            new IntPtr(exStyle.ToInt64() | Win32.WS_EX_TOOLWINDOW));
    }
}
