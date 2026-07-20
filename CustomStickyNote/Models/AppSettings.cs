using System.Collections.Generic;

namespace CustomStickyNote.Models;

/// <summary>
/// 应用配置 (持久化到 %APPDATA%\CustomStickyNote\settings.json)
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 是否开机启动 (实际开关以注册表为准, 这里仅记录偏好)
    /// </summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>
    /// 新建便签默认背景色
    /// </summary>
    public string DefaultBgColor { get; set; } = "#FFF9C4";

    /// <summary>
    /// 可选背景色面板
    /// </summary>
    public List<string> ColorPalette { get; set; } = new()
    {
        "#FFF9C4", // 浅黄 (经典便利贴色)
        "#FFCDD2", // 浅红
        "#C8E6C9", // 浅绿
        "#BBDEFB", // 浅蓝
        "#E1BEE7", // 浅紫
        "#FFAB91", // 橙
        "#F8BBD0", // 粉
        "#FFFFFF", // 白
        "#B0BEC5", // 灰
        "#FFF59D"  // 金黄
    };
}
