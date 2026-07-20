using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CustomStickyNote.Models;

namespace CustomStickyNote.Services;

/// <summary>
/// 数据持久化 (便签 + 配置, 存到 %APPDATA%\CustomStickyNote\)
/// </summary>
public static class NoteStorageService
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CustomStickyNote");

    private static readonly string NotesPath = Path.Combine(AppDataDir, "notes.json");
    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static void EnsureDir()
    {
        if (!Directory.Exists(AppDataDir))
            Directory.CreateDirectory(AppDataDir);
    }

    public static List<StickyNote> LoadNotes()
    {
        try
        {
            if (!File.Exists(NotesPath)) return new List<StickyNote>();
            var json = File.ReadAllText(NotesPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<StickyNote>>(json, JsonOptions) ?? new List<StickyNote>();
        }
        catch
        {
            return new List<StickyNote>();
        }
    }

    public static void SaveNotes(List<StickyNote> notes)
    {
        EnsureDir();
        var json = JsonSerializer.Serialize(notes, JsonOptions);
        File.WriteAllText(NotesPath, json, Encoding.UTF8);
    }

    public static AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        EnsureDir();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json, Encoding.UTF8);
    }
}
