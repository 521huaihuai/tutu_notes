using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace CustomStickyNote.Services;

/// <summary>
/// 系统托盘服务: 提供入口菜单 (新建便签 / 默认背景色 / 开机启动 / 退出)
/// 因为便签窗口不在任务栏显示, 托盘是唯一的用户入口
/// </summary>
public sealed class TrayService : IDisposable
{
    private TaskbarIcon? _icon;
    private readonly StickyNoteManager _manager;
    private MenuItem _autoStartItem = null!;
    private bool _disposed;

    public TrayService(StickyNoteManager manager)
    {
        _manager = manager;
    }

    public void Initialize()
    {
        _icon = new TaskbarIcon
        {
            Icon = LoadTrayIcon(),
            ToolTipText = "便利贴 (左键双击新建, 右键菜单)"
        };

        // 双击托盘 → 新建便签
        _icon.TrayMouseDoubleClick += (s, e) => _manager.CreateNote();

        BuildMenu();
    }

    /// <summary>
    /// 从嵌入资源 (Resources/tray.ico) 加载托盘图标.
    /// 失败时回退到系统 Information 图标.
    /// </summary>
    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/tray.ico");
            var info = Application.GetResourceStream(uri);
            if (info != null)
            {
                using var stream = info.Stream;
                return new System.Drawing.Icon(stream);
            }
        }
        catch { }
        return System.Drawing.SystemIcons.Information;
    }

    private void BuildMenu()
    {
        var menu = new ContextMenu();

        // 新建便签
        var miNew = new MenuItem { Header = "新建便签 (_N)" };
        miNew.Click += (s, e) => _manager.CreateNote();
        menu.Items.Add(miNew);

        menu.Items.Add(new Separator());

        // 默认背景色
        var miColor = new MenuItem { Header = "默认背景色 (_C)" };
        foreach (var c in _manager.Settings.ColorPalette)
        {
            var colorItem = new MenuItem
            {
                Header = c,
                IsCheckable = true,
                IsChecked = c == _manager.Settings.DefaultBgColor
            };
            var captured = c;
            colorItem.Click += (s, e) =>
            {
                _manager.Settings.DefaultBgColor = captured;
                _manager.SaveAll();
                // 刷新所有颜色项的勾选状态
                foreach (var item in miColor.Items)
                {
                    if (item is MenuItem mi)
                        mi.IsChecked = (string)mi.Header == captured;
                }
            };
            miColor.Items.Add(colorItem);
        }
        menu.Items.Add(miColor);

        // 开机启动
        _autoStartItem = new MenuItem
        {
            Header = "开机启动 (_A)",
            IsCheckable = true,
            IsChecked = AutoStartService.IsEnabled()
        };
        _autoStartItem.Click += (s, e) =>
        {
            var enabled = !AutoStartService.IsEnabled();
            AutoStartService.SetEnabled(enabled);
            _manager.Settings.AutoStart = enabled;
            _manager.SaveAll();
            _autoStartItem.IsChecked = enabled;
        };
        menu.Items.Add(_autoStartItem);

        menu.Items.Add(new Separator());

        // 退出
        var miExit = new MenuItem { Header = "退出 (_X)" };
        miExit.Click += (s, e) => Application.Current.Shutdown();
        menu.Items.Add(miExit);

        _icon!.ContextMenu = menu;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon?.Dispose();
    }
}
