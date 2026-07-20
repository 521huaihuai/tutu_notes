using System;
using System.Collections.Generic;
using System.Linq;
using CustomStickyNote.Models;
using CustomStickyNote.Views;

namespace CustomStickyNote.Services;

/// <summary>
/// 便签协调器: 管理所有便签窗口的生命周期 + 数据持久化 + 托盘
/// (App 不直接持有窗口, 通过本类间接管理)
/// </summary>
public sealed class StickyNoteManager : IDisposable
{
    private List<StickyNote> _notes = new();
    private AppSettings _settings = new();
    private readonly Dictionary<Guid, StickyNoteWindow> _windows = new();
    private TrayService? _tray;
    private bool _disposed;

    public void Initialize()
    {
        _settings = NoteStorageService.LoadSettings();
        _notes = NoteStorageService.LoadNotes();

        // 应用开机启动状态
        if (_settings.AutoStart && !AutoStartService.IsEnabled())
            AutoStartService.SetEnabled(true);

        // 初始化托盘
        _tray = new TrayService(this);
        _tray.Initialize();

        // 显示已保存的便签
        if (_notes.Count == 0)
        {
            // 首次启动: 创建默认便签
            CreateNote();
        }
        else
        {
            foreach (var note in _notes)
                ShowNote(note);
        }
    }

    /// <summary>
    /// 创建新便签
    /// </summary>
    public StickyNote CreateNote()
    {
        var note = new StickyNote
        {
            Number = GetNextNumber(),
            BgColor = _settings.DefaultBgColor,
            X = 100 + (_notes.Count * 30) % 300,
            Y = 100 + (_notes.Count * 30) % 200,
            Width = 220,
            Height = 220
        };
        _notes.Add(note);
        SaveAll();
        ShowNote(note);
        return note;
    }

    private void ShowNote(StickyNote note)
    {
        if (_windows.ContainsKey(note.Id)) return;
        var window = new StickyNoteWindow(note, this);
        _windows[note.Id] = window;
        window.Show();
    }

    /// <summary>
    /// 删除便签
    /// </summary>
    public void DeleteNote(Guid id)
    {
        if (_windows.TryGetValue(id, out var window))
        {
            window.Close();
            _windows.Remove(id);
        }
        _notes.RemoveAll(n => n.Id == id);
        SaveAll();
    }

    /// <summary>
    /// 便签内容/位置变更时调用
    /// </summary>
    public void UpdateNote(StickyNote note)
    {
        note.UpdatedAt = DateTime.Now;
        SaveAll();
    }

    /// <summary>
    /// 设置便签背景色
    /// </summary>
    public void SetNoteColor(StickyNote note, string color)
    {
        note.BgColor = color;
        UpdateNote(note);
    }

    private int GetNextNumber()
    {
        return _notes.Count == 0 ? 1 : _notes.Max(n => n.Number) + 1;
    }

    public void SaveAll()
    {
        NoteStorageService.SaveNotes(_notes);
        NoteStorageService.SaveSettings(_settings);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SaveAll();
        _tray?.Dispose();
    }

    public AppSettings Settings => _settings;
    public IReadOnlyList<StickyNote> Notes => _notes;
}
