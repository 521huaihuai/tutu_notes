using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CustomStickyNote.Models;
using CustomStickyNote.Native;
using CustomStickyNote.Services;

namespace CustomStickyNote.Views;

/// <summary>
/// 便签窗口 (单张便签)
/// - 拖动标题栏移动位置
/// - 右下角缩放手柄
/// - 底部颜色块切换背景色
/// - 自动持久化位置/尺寸/内容
/// </summary>
public partial class StickyNoteWindow : Window
{
    private readonly StickyNote _note;
    private readonly StickyNoteManager _manager;
    private DispatcherTimer? _saveTimer;
    private bool _pinned;
    private bool _isLoaded;
    private bool _isLoading;

    // 保存选区位置: 点击 ComboBox/ToggleButton 等格式控件时, RichTextBox 失焦可能导致选区丢失.
    // 在 PreviewMouseDown 时记录选区 TextPointer, 应用格式时用保存的选区恢复.
    private TextPointer? _savedSelStart;
    private TextPointer? _savedSelEnd;

    // 底部工具栏自然高度 (内容完全展开), 鼠标移入时动画到此高度, 移出时动画到 0.
    private double _toolbarNaturalHeight = 60;

    // 标题栏固定高度. 窗口 Top = 内容区 Top - HeaderHeight, 窗口 Height = 内容高度 + HeaderHeight.
    // 标题栏用 Opacity 动画控制可见性 (收起时透明+鼠标穿透), 窗口不移动, 内容区位置和大小始终不变.
    private const double HeaderTargetHeight = 26;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CustomStickyNote", "window.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
        catch { }
    }

    public StickyNoteWindow(StickyNote note, StickyNoteManager manager)
    {
        InitializeComponent();
        _note = note;
        _manager = manager;
        DataContext = _note;

        // 限制便签高度不超过屏幕工作区 (去除任务栏), 避免覆盖任务栏
        var workArea = SystemParameters.WorkArea;
        if (_note.Height > workArea.Height) _note.Height = workArea.Height - 20;

        Left = _note.X;
        // 窗口顶部在内容区上方 HeaderTargetHeight 像素, 内容区屏幕位置 = _note.Y (不变)
        Top = _note.Y - HeaderTargetHeight;
        Width = _note.Width;
        // 窗口高度 = 内容高度 + 标题栏高度, 内容区高度 = _note.Height (不变)
        Height = _note.Height + HeaderTargetHeight;

        BuildColorPalette();
        BuildFontSettings();

        Loaded += StickyNoteWindow_Loaded;
        LocationChanged += OnBoundsChanged;
        SizeChanged += OnBoundsChanged;
        MouseEnter += StickyNoteWindow_MouseEnter;
        MouseLeave += StickyNoteWindow_MouseLeave;
        PreviewMouseWheel += StickyNoteWindow_PreviewMouseWheel;
    }

    /// <summary>
    /// Ctrl+鼠标滚轮: 调整便签背景透明度 (0.2 - 1.0), 同步更新滑块.
    /// 只影响背景色 alpha, 不影响文字 (BgBrush getter 根据 Opacity 计算 alpha).
    /// </summary>
    private void StickyNoteWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;
        double step = 0.05;
        double newOpacity = _note.Opacity + (e.Delta > 0 ? step : -step);
        _note.Opacity = newOpacity;
        OpacitySlider.Value = _note.Opacity;
        _manager.UpdateNote(_note);
        Log($"BgOpacity changed to {_note.Opacity:F2}");
    }

    /// <summary>
    /// 鼠标移入便签: 标题栏淡入显示(Opacity 动画), 底部工具栏展开, 淡入 resize 手柄.
    /// 窗口不移动, 内容区位置和大小始终不变.
    /// </summary>
     private void StickyNoteWindow_MouseEnter(object sender, MouseEventArgs e)
    {
        var rootGrid = Content as System.Windows.Controls.Grid;
        var row0H = rootGrid?.RowDefinitions.Count > 0 ? rootGrid.RowDefinitions[0].ActualHeight : -1;
        var row1H = rootGrid?.RowDefinitions.Count > 1 ? rootGrid.RowDefinitions[1].ActualHeight : -1;
        Log($"MouseEnter: Window={ActualWidth}x{ActualHeight}, HeaderGrid.ActualHeight={HeaderGrid.ActualHeight}, Row0={row0H}, Row1={row1H}, NoteBorder.ActualHeight={NoteBorder.ActualHeight}");
        AnimateOpacity(HeaderGrid, 1);
        HeaderGrid.IsHitTestVisible = true;
        AnimateHeight(ToolbarPanel, _toolbarNaturalHeight);
        AnimateOpacity(ResizeThumb, 1);
        ResizeThumb.IsHitTestVisible = true;
    }

    /// <summary>
    /// 鼠标移出便签: 标题栏淡出(Opacity 动画), 底部工具栏收起, 淡出 resize 手柄.
    /// 若 TextBox/TitleEditBox 正在输入 (有键盘焦点), 则保持显示.
    /// 鼠标离开便签后恢复桌面固定 (SwitchToPinned), Win+D 仍不隐藏.
    /// </summary>
    private void StickyNoteWindow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (ContentBox.IsKeyboardFocused || TitleEditBox.IsKeyboardFocused) return;
        AnimateOpacity(HeaderGrid, 0);
        HeaderGrid.IsHitTestVisible = false;
        AnimateHeight(ToolbarPanel, 0);
        AnimateOpacity(ResizeThumb, 0);
        ResizeThumb.IsHitTestVisible = false;
        // 鼠标离开便签, 恢复桌面固定 (SetParent 回 WorkerW)
        DesktopPinService.SwitchToPinned();
    }

    private static void AnimateHeight(FrameworkElement element, double targetHeight)
    {
        var animation = new DoubleAnimation(targetHeight, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        element.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }

    private static void AnimateOpacity(FrameworkElement element, double targetOpacity)
    {
        var animation = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(150));
        element.BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>
    /// 右下角 resize 手柄拖拽: 调整窗口大小.
    /// </summary>
    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        // 限制最大高度不超过屏幕工作区, 避免覆盖任务栏
        var maxH = SystemParameters.WorkArea.Height;
        Height = Math.Clamp(Height + e.VerticalChange, MinHeight, maxH);
    }

    /// <summary>
    /// 鼠标移入标题区域: 显示编辑图标.
    /// </summary>
    private void TitleArea_MouseEnter(object sender, MouseEventArgs e)
    {
        if (TitleEditBox.Visibility == Visibility.Visible) return;
        EditTitleButton.Opacity = 1;
    }

    /// <summary>
    /// 鼠标移出标题区域: 隐藏编辑图标.
    /// </summary>
    private void TitleArea_MouseLeave(object sender, MouseEventArgs e)
    {
        EditTitleButton.Opacity = 0;
    }

    /// <summary>
    /// 点击编辑图标: 进入标题编辑模式.
    /// </summary>
    private void EditTitle_Click(object sender, RoutedEventArgs e)
    {
        TitleText.Visibility = Visibility.Collapsed;
        TitleEditBox.Visibility = Visibility.Visible;
        TitleEditBox.Text = _note.Title;
        TitleEditBox.Focus();
        TitleEditBox.SelectAll();
    }

    /// <summary>
    /// 标题编辑框按键: Enter 保存, Escape 取消.
    /// </summary>
    private void TitleEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTitleEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelTitleEdit();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 标题编辑框失去焦点: 保存修改.
    /// </summary>
    private void TitleEditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitTitleEdit();
    }

    private void CommitTitleEdit()
    {
        if (TitleEditBox.Visibility != Visibility.Visible) return;
        _note.Title = TitleEditBox.Text;
        _manager.UpdateNote(_note);
        TitleEditBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        Log($"Title committed: {_note.Title}");
    }

    private void CancelTitleEdit()
    {
        TitleEditBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
    }

    private void StickyNoteWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 加载文档 (在 _isLoaded=true 前, 避免触发 SelectionChanged 时误更新控件)
        LoadDocument();

        // 初始化透明度滑块值 (在 ValueChanged 处理激活前设置)
        // 背景透明度由 BgBrush getter 根据 Opacity 计算 alpha, binding 自动更新, 无需手动调用
        OpacitySlider.Value = _note.Opacity;
        _isLoaded = true;

        // 布局完成后计算底部工具栏自然高度 (供 MouseEnter 动画使用)
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var width = ActualWidth > 0 ? ActualWidth : Width;
            ToolbarPanel.Height = double.NaN;
            ToolbarPanel.Measure(new Size(width, double.PositiveInfinity));
            _toolbarNaturalHeight = ToolbarPanel.DesiredSize.Height;
            ToolbarPanel.Height = 0;

            // 强制重置标题栏/工具栏初始状态 (防止启动时 MouseEnter 误触发导致标题栏可见)
            HeaderGrid.Opacity = 0;
            HeaderGrid.IsHitTestVisible = false;
            ToolbarPanel.Height = 0;
            ResizeThumb.Opacity = 0;
            ResizeThumb.IsHitTestVisible = false;
            Log($"Loaded init: HeaderGrid.Opacity={HeaderGrid.Opacity}, ToolbarPanel.Height={ToolbarPanel.Height}, MouseOver={IsMouseOver}");
        });

        if (_pinned) return;
        // 将窗口贴到桌面层 (WorkerW 子窗口)
        DesktopPinService.PinToDesktop(this);
        _pinned = true;
    }

    /// <summary>
    /// 从 _note.Content 加载 FlowDocument.
    /// 兼容旧纯文本数据: 若 Content 不以 "&lt;" 开头, 当作纯文本加载.
    /// </summary>
    private void LoadDocument()
    {
        _isLoading = true;
        try
        {
            FlowDocument doc;
            if (!string.IsNullOrEmpty(_note.Content))
            {
                var trimmed = _note.Content.TrimStart();
                if (trimmed.StartsWith("<"))
                {
                    try
                    {
                        doc = (FlowDocument)XamlReader.Parse(_note.Content);
                    }
                    catch
                    {
                        doc = new FlowDocument(new Paragraph(new Run(_note.Content)));
                    }
                }
                else
                {
                    doc = new FlowDocument(new Paragraph(new Run(_note.Content)));
                }
            }
            else
            {
                doc = new FlowDocument();
            }

            // 默认字体样式 (未单独设置样式的文字会继承这些值)
            doc.FontFamily = new FontFamily("Microsoft YaHei");
            doc.FontSize = 13;
            doc.Foreground = new SolidColorBrush(ParseColor(_note.TextColor));

            // 统一段落间距: Paragraph 默认 Margin 不为 0, 导致 Enter 换行(新段落)与
            // Shift+Enter(LineBreak) 间距不一致. 设为 0 让所有换行间距统一.
            var paraStyle = new Style(typeof(Paragraph));
            paraStyle.Setters.Add(new Setter(Paragraph.MarginProperty, new Thickness(0)));
            paraStyle.Setters.Add(new Setter(Paragraph.PaddingProperty, new Thickness(0)));
            doc.Resources ??= new ResourceDictionary();
            doc.Resources[typeof(Paragraph)] = paraStyle;

            // 行间距: LineHeight = 字号 * 倍数. Double.NaN 表示自动.
            doc.LineHeight = doc.FontSize * _note.LineSpacing;

            ContentBox.Document = doc;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// 序列化 FlowDocument 为 XAML 字串, 保存到 _note.Content.
    /// </summary>
    private void SaveDocument()
    {
        var doc = ContentBox.Document;
        using (var ms = new MemoryStream())
        {
            XamlWriter.Save(doc, ms);
            _note.Content = Encoding.UTF8.GetString(ms.ToArray());
        }
        _manager.UpdateNote(_note);
    }

    private void BuildColorPalette()
    {
        ColorPanel.Children.Clear();
        foreach (var colorHex in _manager.Settings.ColorPalette)
        {
            var rect = new Border
            {
                Width = 12,
                Height = 12,
                Margin = new Thickness(1, 2, 1, 2),
                Background = new SolidColorBrush(ParseColor(colorHex)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                BorderThickness = new Thickness(0.5),
                Cursor = Cursors.Hand,
                Tag = colorHex,
                ToolTip = colorHex
            };
            var captured = colorHex;
            rect.MouseLeftButtonDown += (s, e) =>
            {
                _manager.SetNoteColor(_note, captured);
            };
            ColorPanel.Children.Add(rect);
        }
    }

    /// <summary>
    /// 初始化底部工具栏的字体设置控件: 系统字体下拉框、预设字号、加粗按钮、文字颜色栏.
    /// 在构造函数中调用一次, 之后由数据绑定驱动 UI 更新.
    /// </summary>
    private void BuildFontSettings()
    {
        // 字体下拉框: 系统字体
        FontFamilyCombo.ItemsSource = Fonts.SystemFontFamilies;
        FontFamilyCombo.SelectedItem = _note.FontFamilyValue;

        // 字号下拉框: 预设值 (IsEditable=False, 用 SelectedItem 驱动)
        var sizes = new double[] { 9, 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32 };
        FontSizeCombo.ItemsSource = sizes;
        FontSizeCombo.SelectedItem = _note.FontSize;

        // 加粗按钮状态
        BoldToggle.IsChecked = _note.FontBold;

        // 行间距下拉框: 倍数预设
        var spacings = new double[] { 1.0, 1.15, 1.5, 1.75, 2.0, 2.5, 3.0 };
        LineSpacingCombo.ItemsSource = spacings;
        LineSpacingCombo.SelectedItem = _note.LineSpacing;

        // 文字颜色选择栏: 独立色板 (深色为主, 适合文字), 与背景色板区分, 避免浅色看不清.
        // 深色边框确保在浅色/深色背景下都清晰可见.
        var textColors = new string[]
        {
            "#000000", "#333333", "#666666", "#999999",
            "#FFFFFF", "#CC0000", "#FF0000", "#FF6600",
            "#008000", "#0066CC", "#0000FF", "#9933CC"
        };
        TextColorPanel.Children.Clear();
        foreach (var colorHex in textColors)
        {
            var rect = new Border
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(1, 1, 1, 1),
                Background = new SolidColorBrush(ParseColor(colorHex)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = colorHex,
                ToolTip = colorHex
            };
            var captured = colorHex;
            rect.MouseLeftButtonDown += (s, e) =>
            {
                if (_isLoaded)
                {
                    SaveCurrentSelection();
                    GetTargetSelection().ApplyPropertyValue(TextElement.ForegroundProperty,
                        new SolidColorBrush(ParseColor(captured)));
                    SaveDocument();
                }
            };
            TextColorPanel.Children.Add(rect);
        }
    }

    /// <summary>
    /// 字体选择变化: 对选中文字应用 FontFamily.
    /// </summary>
    private void FontFamily_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || !_isLoaded) return;
        if (FontFamilyCombo.SelectedItem is FontFamily ff)
        {
            GetTargetSelection().ApplyPropertyValue(TextElement.FontFamilyProperty, ff);
            SaveDocument();
        }
    }

    /// <summary>
    /// 字号选择变化: 对选中文字应用 FontSize.
    /// </summary>
    private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || !_isLoaded) return;
        if (FontSizeCombo.SelectedItem is double size && size >= 6 && size <= 100)
        {
            GetTargetSelection().ApplyPropertyValue(TextElement.FontSizeProperty, size);
            SaveDocument();
        }
    }

    /// <summary>
    /// 加粗按钮切换: 对选中文字应用 FontWeight (Bold/Normal).
    /// </summary>
    private void BoldToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading || !_isLoaded) return;
        GetTargetSelection().ApplyPropertyValue(TextElement.FontWeightProperty,
            BoldToggle.IsChecked == true ? FontWeights.Bold : FontWeights.Normal);
        SaveDocument();
    }

    /// <summary>
    /// 行间距变化: 更新文档 LineHeight 并持久化.
    /// 行间距为文档级设置 (非选区), LineHeight = FontSize * 倍数.
    /// </summary>
    private void LineSpacing_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || !_isLoaded) return;
        if (LineSpacingCombo.SelectedItem is double spacing)
        {
            _note.LineSpacing = spacing;
            ContentBox.Document.LineHeight = ContentBox.Document.FontSize * spacing;
            SaveDocument();
        }
    }

    /// <summary>
    /// 格式控件 (ComboBox/ToggleButton) 点击前保存选区.
    /// PreviewMouseDown 是隧道事件, 在 RichTextBox 失去焦点前触发, 此刻选区还在.
    /// </summary>
    private void FormatControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        SaveCurrentSelection();
    }

    /// <summary>
    /// 保存 RichTextBox 当前选区位置 (TextPointer).
    /// </summary>
    private void SaveCurrentSelection()
    {
        var sel = ContentBox.Selection;
        if (sel != null && !sel.IsEmpty)
        {
            _savedSelStart = sel.Start;
            _savedSelEnd = sel.End;
        }
        else
        {
            _savedSelStart = null;
            _savedSelEnd = null;
        }
    }

    /// <summary>
    /// 获取要应用格式的选区: 优先当前选区, 当前为空时用保存的选区.
    /// 解决点击 ComboBox/ToggleButton 导致 RichTextBox 失焦、选区丢失的问题.
    /// </summary>
    private TextRange GetTargetSelection()
    {
        var sel = ContentBox.Selection;
        if (sel != null && !sel.IsEmpty)
            return sel;
        if (_savedSelStart != null && _savedSelEnd != null)
            return new TextRange(_savedSelStart, _savedSelEnd);
        return sel!;
    }

    /// <summary>
    /// 选区变化: 同步底部工具栏控件状态 (加粗/字体/字号) 反映当前选区样式.
    /// </summary>
    private void ContentBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || !_isLoaded) return;
        var selection = ContentBox.Selection;

        // 加粗状态
        var fw = selection.GetPropertyValue(TextElement.FontWeightProperty);
        if (fw != DependencyProperty.UnsetValue && fw is FontWeight fontWeight)
        {
            BoldToggle.IsChecked = fontWeight == FontWeights.Bold;
        }

        // 字体
        var ff = selection.GetPropertyValue(TextElement.FontFamilyProperty);
        if (ff != DependencyProperty.UnsetValue && ff is FontFamily fontFamily)
        {
            _isLoading = true;
            try { FontFamilyCombo.SelectedItem = fontFamily; }
            finally { _isLoading = false; }
        }

        // 字号 (IsEditable=False, 用 SelectedItem 同步; 不在预设里则为 null)
        var fs = selection.GetPropertyValue(TextElement.FontSizeProperty);
        if (fs != DependencyProperty.UnsetValue && fs is double fontSize)
        {
            _isLoading = true;
            try { FontSizeCombo.SelectedItem = fontSize; }
            finally { _isLoading = false; }
        }
    }

    /// <summary>
    /// 透明度滑块变化: 更新背景透明度并持久化. 只影响背景色 alpha, 不影响文字.
    /// </summary>
    private void OpacitySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded) return;
        _note.Opacity = OpacitySlider.Value;
        _manager.UpdateNote(_note);
    }

    private static Color ParseColor(string hex)
    {
        if (hex.StartsWith("#")) hex = hex.Substring(1);
        if (hex.Length == 6)
            return Color.FromRgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        if (hex.Length == 8)
            return Color.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16));
        return Colors.Yellow;
    }

    /// <summary>
    /// 拖拽便签: 用 Win32 API 发送 WM_NCLBUTTONDOWN(HTCAPTION), 让系统接管标题栏拖动.
    /// 优势: 不阻塞 UI 线程, 不与子控件(Button/TextBox)的鼠标事件冲突,
    ///       不需要 CaptureMouse (后者会让 Button 收不到 Click 事件).
    /// 使用 PreviewMouseLeftButtonDown (隧道事件): 父 Border 先收到, 子控件 mark Handled 也不影响.
    /// </summary>
    private void Note_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 点击交互元素时不拦截, 让事件继续传递给它们 (TextBox 输入 / Button 点击 / 颜色块切换)
        // 注意: Button.Content="×" 是字符串, WPF 用 TextBlock 渲染,
        //       点击 × 时 e.OriginalSource 是 TextBlock 而非 Button,
        //       所以必须遍历视觉树向上查找 Button/TextBoxBase 祖先.
        var src = e.OriginalSource as DependencyObject;
        if (IsDescendantOf<TextBoxBase>(src))
        {
            // WindowStyle=None + WS_EX_TOOLWINDOW 窗口点击客户区时, Window 可能不会被自动激活,
            // 导致 TextBox 无法获得焦点. 这里强制激活 Window.
            if (!IsActive) Activate();
            Log($"TextBox clicked, Activate={IsActive}, Left={Left}, Top={Top}");
            return;
        }
        if (IsDescendantOf<ButtonBase>(src))
        {
            Log($"Button clicked");
            return;
        }
        if (IsDescendantOf<Thumb>(src))
        {
            Log($"Thumb clicked");
            return;
        }
        if (e.OriginalSource is Border b && b.Tag is string)
            return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // 标记已处理, 阻止事件继续传递给子控件 (避免 TextBox 抢焦点等副作用)
        e.Handled = true;

        Log($"Drag start: Left={Left}, Top={Top}, note.X={_note.X}, note.Y={_note.Y}");

        // 1. 释放当前鼠标捕获 (清除可能存在的捕获状态)
        Win32.ReleaseCapture();
        // 2. 发送非客户区左键按下消息, 模拟点击标题栏, 系统接管后续拖拽
        Win32.SendMessage(hwnd, Win32.WM_NCLBUTTONDOWN, Win32.HTCAPTION, IntPtr.Zero);

        Log($"Drag end: Left={Left}, Top={Top}, note.X={_note.X}, note.Y={_note.Y}");

        // SendMessage 同步返回时拖拽已结束, 保存最终位置
        // 用 BeginInvoke 确保在 WPF 处理完 WM_MOVE 消息后再读取 Left/Top
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Log($"BeginInvoke: Left={Left}, Top={Top}, note.X={_note.X}, note.Y={_note.Y}");
            _note.X = Left;
            _note.Y = Top;
            _manager.UpdateNote(_note);
            Log($"After save: note.X={_note.X}, note.Y={_note.Y}");
        }));
    }

    /// <summary>
    /// 遍历视觉树/逻辑树向上查找指定类型的祖先 (用于判断点击是否落在交互控件内).
    /// 注意: RichTextBox 内部点击文字时 e.OriginalSource 是 Run/Paragraph 等 ContentElement,
    /// 它们不在视觉树中 (VisualTreeHelper.GetParent 返回 null), 必须用逻辑树遍历.
    /// </summary>
    private static bool IsDescendantOf<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T) return true;
            DependencyObject? parent;
            if (element is ContentElement ce)
                parent = ContentOperations.GetParent(ce) ?? LogicalTreeHelper.GetParent(ce);
            else
                parent = VisualTreeHelper.GetParent(element);
            if (parent == element) break;
            element = parent;
        }
        return false;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _manager.DeleteNote(_note.Id);
    }

    private void Content_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoaded) SaveDocument();
    }

    private void Content_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded) return;
        // 防抖保存: 用户输入时, 500ms 无变化才落盘
        _saveTimer?.Stop();
        _saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick -= SaveTimer_Tick;
        _saveTimer.Tick += SaveTimer_Tick;
        _saveTimer.Start();
    }

    private void SaveTimer_Tick(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        if (_isLoaded) SaveDocument();
    }

    private void OnBoundsChanged(object? sender, EventArgs e)
    {
        Log($"OnBoundsChanged: Left={Left}, Top={Top}, W={Width}, H={Height}");
        _note.X = Left;
        // 窗口 Top 包含标题栏偏移, 扣除得到内容区位置; Height 扣除标题栏得到内容高度
        _note.Y = Top + HeaderTargetHeight;
        _note.Width = Width;
        _note.Height = Height - HeaderTargetHeight;
        // 防抖保存
        _saveTimer?.Stop();
        _saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick -= SaveTimer_Tick;
        _saveTimer.Tick += SaveTimer_Tick;
        _saveTimer.Start();
    }
}
