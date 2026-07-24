# 图图便利签 / Tutu Sticky Notes

[中文](#中文) · [English](#english)

---

<a id="english"></a>

## English

A lightweight Windows desktop sticky-notes app built with **.NET 8 WPF**. Stays resident in the system tray, supports multiple independent notes, rich-text editing, and — most importantly — **stays visible when you press Win+D or click "Show desktop"** without obstructing other application windows.

### Highlights

- **Smart "Show desktop" guard** — The trickiest part of any desktop-pin app. A dual Win32 event-hook state machine (foreground + minimize/restore) detects the difference between *Show desktop* (WorkerW covers everything) and *single-window minimize*, and toggles `WS_EX_TOPMOST` only when needed. Subclassed `WndProc` also intercepts `WM_WINDOWPOSCHANGING` and `WM_SYSCOMMAND/SC_MINIMIZE` as a fallback.
- **Doesn't block other windows** — When a normal app is brought to the foreground, notes are demoted from topmost *only if* they were topmost. New windows get raised above non-topmost notes naturally by Windows, so notes never cover windows you just activated.
- **Multi-note manager** — Each note is an independent `WS_EX_TOOLWINDOW` (no taskbar entry, no Alt+Tab). Tray double-click creates a new note.
- **Rich text** — `FlowDocument`-based editor with per-selection font family, size, bold, text color, line spacing. Content serialized as XAML.
- **Per-note customization** — 10 background colors, 12 text colors, background opacity slider (also `Ctrl+Mouse wheel`), resizable, draggable.
- **Persistent** — Notes and settings persisted to `%APPDATA%\CustomStickyNote\` as JSON. Auto-save is debounced (500 ms).
- **Auto-start** — Optional, via `HKCU\...\Run` registry key.
- **Single instance** — `Mutex`-guarded.
- **DPI-aware** — Work-area clamping uses logical pixels via `GetDpiForSystem`.

### Project Structure

```
CustomStickyNote/
├── Models/
│   ├── AppSettings.cs          # Settings model (colors, auto-start pref)
│   └── StickyNote.cs           # Note model (INotifyPropertyChanged)
├── Native/
│   └── Win32.cs                # P/Invoke declarations
├── Resources/
│   └── tray.ico                # Tray icon
├── Services/
│   ├── AutoStartService.cs     # HKCU Run-key management
│   ├── DesktopPinService.cs    # WS_EX_TOOLWINDOW assignment
│   ├── NoteStorageService.cs   # JSON persistence
│   ├── ShowDesktopGuardService.cs   # Core: Show-desktop detection & Z-order
│   ├── StickyNoteManager.cs    # Coordinator: lifecycle, persistence, tray
│   └── TrayService.cs          # System tray icon & menu
├── Views/
│   ├── StickyNoteWindow.xaml
│   └── StickyNoteWindow.xaml.cs
├── App.xaml / App.xaml.cs      # Entry point + single-instance guard
└── CustomStickyNote.csproj
```

### Requirements

- Windows 10 / 11
- .NET 8 SDK (`net8.0-windows`)
- WPF runtime

### Build & Run

```powershell
# Restore + build
dotnet build CustomStickyNote/CustomStickyNote.csproj -c Debug

# Or just use the launcher (auto-builds on first run)
./启动.bat
```

### Usage

- **Tray double-click** — Create a new note
- **Tray right-click** — Menu: New note / Default color / Auto-start / Exit
- **Drag title bar** — Move note (uses `WM_NCLBUTTONDOWN(HTCAPTION)` for smooth system-managed drag)
- **Bottom-right handle** — Resize
- **Eye icon (top-right)** — Toggle toolbar (title bar + format bar + resize handle)
- **`Ctrl` + Mouse wheel** — Adjust background opacity
- **Format bar** — Font family, size, bold, line spacing, text color, background color
- **`Enter`** in title edit — Save; **`Esc`** — Cancel

### Data Location

```
%APPDATA%\CustomStickyNote\
├── notes.json          # All notes (XAML-serialized FlowDocument content)
├── settings.json       # App settings
├── app.log             # Startup / shutdown log
└── window.log          # Z-order / drag / resize debug log
```

### Why a custom sticky-notes app?

The built-in Windows Sticky Notes has limitations: it can't stay visible during *Show desktop*, doesn't support background opacity, and ties notes to a single parent window. This project explores how to make notes behave like real desktop wallpaper overlays while still respecting normal window activation order.

---

<a id="中文"></a>

## 中文

一个基于 **.NET 8 WPF** 的轻量级 Windows 桌面便签应用。常驻系统托盘，支持多张独立便签、富文本编辑，**最重要的特性：按 Win+D 或点击"显示桌面"时便签依然可见**，且不会遮挡其他正常应用窗口。

### 核心亮点

- **智能"显示桌面"防护** — 桌面贴附类应用最难的部分。采用双 Win32 事件钩子状态机（前台变化 + 最小化/恢复）来区分"显示桌面"（WorkerW 覆盖所有窗口）与"单窗口最小化"，仅在需要时切换 `WS_EX_TOPMOST`。同时子类化 `WndProc` 拦截 `WM_WINDOWPOSCHANGING` 和 `WM_SYSCOMMAND/SC_MINIMIZE` 作为后备。
- **不遮挡其他窗口** — 当普通应用进入前台时，仅在便签本身处于置顶层时才取消置顶。新激活的窗口会被 Windows 自然地提到非置顶便签之上，便签永远不会盖住你刚激活的窗口。
- **多便签管理** — 每张便签都是独立的 `WS_EX_TOOLWINDOW`（不在任务栏、不在 Alt+Tab 显示）。双击托盘新建便签。
- **富文本编辑** — 基于 `FlowDocument` 的编辑器，支持按选区设置字体、字号、加粗、文字颜色、行间距。内容以 XAML 序列化。
- **便签级自定义** — 10 种背景色、12 种文字颜色、背景透明度滑块（也可用 `Ctrl+鼠标滚轮`）、可拖动、可缩放。
- **持久化** — 便签与配置以 JSON 持久化到 `%APPDATA%\CustomStickyNote\`。自动保存采用防抖策略（500ms）。
- **开机自启** — 可选，通过 `HKCU\...\Run` 注册表键实现。
- **单实例** — 基于 `Mutex` 守护。
- **DPI 感知** — 屏幕工作区限制使用 `GetDpiForSystem` 转换后的逻辑像素。

### 项目结构

```
CustomStickyNote/
├── Models/
│   ├── AppSettings.cs          # 配置模型（颜色、开机启动偏好）
│   └── StickyNote.cs           # 便签模型（INotifyPropertyChanged）
├── Native/
│   └── Win32.cs                # P/Invoke 声明
├── Resources/
│   └── tray.ico                # 托盘图标
├── Services/
│   ├── AutoStartService.cs     # HKCU Run 键管理
│   ├── DesktopPinService.cs    # WS_EX_TOOLWINDOW 设置
│   ├── NoteStorageService.cs   # JSON 持久化
│   ├── ShowDesktopGuardService.cs   # 核心：显示桌面检测与 Z 序管理
│   ├── StickyNoteManager.cs    # 协调器：生命周期、持久化、托盘
│   └── TrayService.cs          # 系统托盘图标与菜单
├── Views/
│   ├── StickyNoteWindow.xaml
│   └── StickyNoteWindow.xaml.cs
├── App.xaml / App.xaml.cs      # 入口 + 单实例守护
└── CustomStickyNote.csproj
```

### 环境要求

- Windows 10 / 11
- .NET 8 SDK（`net8.0-windows`）
- WPF 运行时

### 构建与运行

```powershell
# 还原 + 编译
dotnet build CustomStickyNote/CustomStickyNote.csproj -c Debug

# 或直接用启动脚本（首次运行会自动编译）
./启动.bat
```

### 使用方法

- **双击托盘图标** — 新建便签
- **右键托盘图标** — 菜单：新建便签 / 默认背景色 / 开机启动 / 退出
- **拖动标题栏** — 移动便签（用 `WM_NCLBUTTONDOWN(HTCAPTION)` 让系统接管拖拽，流畅且不阻塞 UI）
- **右下角手柄** — 缩放
- **右上角眼睛图标** — 切换工具栏（标题栏 + 格式栏 + 缩放手柄）的显示
- **`Ctrl` + 鼠标滚轮** — 调节背景透明度
- **格式栏** — 字体、字号、加粗、行间距、文字颜色、背景色
- **标题编辑框** — `Enter` 保存，`Esc` 取消

### 数据存放位置

```
%APPDATA%\CustomStickyNote\
├── notes.json          # 所有便签（XAML 序列化的 FlowDocument 内容）
├── settings.json       # 应用配置
├── app.log             # 启动 / 退出日志
└── window.log          # Z 序 / 拖拽 / 缩放调试日志
```

### 为什么自己写便签？

Windows 自带的"便笺"有几个限制：在"显示桌面"时无法保持可见、不支持背景透明度、所有便签都绑定在同一个父窗口上。本项目探索如何让便签像真正的桌面壁纸覆盖层一样工作，同时仍尊重正常的窗口激活顺序。

---

## License

MIT
