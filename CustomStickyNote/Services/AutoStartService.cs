using System;
using Microsoft.Win32;

namespace CustomStickyNote.Services;

/// <summary>
/// 开机启动管理 (通过 HKCU 注册表 Run 键)
/// </summary>
public static class AutoStartService
{
    private const string AppName = "CustomStickyNote";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// 当前是否已设置开机启动
    /// </summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(AppName) != null;
    }

    /// <summary>
    /// 开启/关闭开机启动
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key == null) return;

        if (enabled)
        {
            string exePath = Environment.ProcessPath ?? string.Empty;
            if (!string.IsNullOrEmpty(exePath))
                key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }
}
