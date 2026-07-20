using System;
using System.IO;
using System.Threading;
using System.Windows;
using CustomStickyNote.Services;

namespace CustomStickyNote;

/// <summary>
/// 应用入口: 单实例守护 + 启动便签管理器
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;
    private StickyNoteManager? _manager;
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CustomStickyNote", "app.log");

    private static void Log(string msg)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log($"OnStartup begin, args={e.Args.Length}");
        // 单实例守护
        _mutex = new Mutex(true, @"Global\CustomStickyNote_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Log("Another instance running, exiting");
            Shutdown();
            return;
        }

        base.OnStartup(e);

        try
        {
            _manager = new StickyNoteManager();
            _manager.Initialize();
            Log($"OnStartup done, notes count = {_manager.Notes.Count}");
        }
        catch (Exception ex)
        {
            Log($"FATAL: {ex}");
            MessageBox.Show(ex.ToString(), "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"OnExit, code={e.ApplicationExitCode}");
        _manager?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Log($"OnSessionEnding, reason={e.ReasonSessionEnding}");
        base.OnSessionEnding(e);
    }
}
