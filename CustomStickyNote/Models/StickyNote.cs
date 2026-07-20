using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace CustomStickyNote.Models;

/// <summary>
/// 便签数据模型 (持久化到 JSON)
/// </summary>
public class StickyNote : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    private int _number;
    public int Number
    {
        get => _number;
        set { _number = value; OnPropertyChanged(); OnPropertyChanged(nameof(HeaderText)); }
    }

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); OnPropertyChanged(nameof(HeaderText)); }
    }

    private string _content = string.Empty;
    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }

    private string _bgColor = "#FFF9C4";
    public string BgColor
    {
        get => _bgColor;
        set { _bgColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(BgBrush)); }
    }

    private string _textColor = "#222222";
    public string TextColor
    {
        get => _textColor;
        set { _textColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(TextBrush)); }
    }

    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 220;
    public double Height { get; set; } = 220;

    private double _opacity = 1.0;
    /// <summary>
    /// 便签背景透明度 (0.2 - 1.0), 用 Ctrl+鼠标滚轮或滑块调整.
    /// 只影响背景色 alpha, 不影响文字.
    /// </summary>
    public double Opacity
    {
        get => _opacity;
        set { _opacity = Math.Clamp(value, 0.2, 1.0); OnPropertyChanged(); OnPropertyChanged(nameof(BgBrush)); }
    }

    private string _fontFamily = "Microsoft YaHei";
    public string FontFamily
    {
        get => _fontFamily;
        set { _fontFamily = value; OnPropertyChanged(); OnPropertyChanged(nameof(FontFamilyValue)); }
    }

    private double _fontSize = 13;
    public double FontSize
    {
        get => _fontSize;
        set { _fontSize = value; OnPropertyChanged(); }
    }

    private bool _fontBold = false;
    public bool FontBold
    {
        get => _fontBold;
        set { _fontBold = value; OnPropertyChanged(); OnPropertyChanged(nameof(FontWeightValue)); }
    }

    private double _lineSpacing = 1.5;
    /// <summary>
    /// 行间距倍数 (1.0 - 3.0), 默认 1.5.
    /// 应用到 FlowDocument.LineHeight = FontSize * LineSpacing.
    /// </summary>
    public double LineSpacing
    {
        get => _lineSpacing;
        set { _lineSpacing = Math.Clamp(value, 1.0, 3.0); OnPropertyChanged(); }
    }

    [JsonIgnore]
    public System.Windows.Media.FontFamily FontFamilyValue => new System.Windows.Media.FontFamily(FontFamily);

    [JsonIgnore]
    public System.Windows.FontWeight FontWeightValue => FontBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 标题栏显示: "#编号 [标题]"
    /// </summary>
    [JsonIgnore]
    public string HeaderText => string.IsNullOrWhiteSpace(Title) ? $"#{Number}" : $"#{Number}  {Title}";

    /// <summary>
    /// 背景色 brush: 根据 BgColor 和 Opacity 计算 alpha 通道.
    /// 只让背景半透明, 文字不受影响 (文字在 RichTextBox 中独立绘制).
    /// </summary>
    [JsonIgnore]
    public SolidColorBrush BgBrush
    {
        get
        {
            var c = ParseColor(BgColor);
            byte alpha = (byte)Math.Round(_opacity * 255);
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }
    }

    [JsonIgnore]
    public SolidColorBrush TextBrush => new SolidColorBrush(ParseColor(TextColor));

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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
