using System;
using System.IO;
using System.Text.Json;
using FFmPlayer.Models;

namespace FFmPlayer.Services;

public class SettingsService
{
    private readonly string _settingsFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    public SettingsService()
    {
        _settingsFilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    }

    /// <summary>
    /// settings.json ファイルからアプリ設定を読み込みます。
    /// ファイルが存在しない、またはエラー時はデフォルト値を持った設定を返します。
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load settings: {ex.Message}");
        }

        return new AppSettings();
    }

    /// <summary>
    /// アプリ設定を settings.json ファイルとして保存します。
    /// </summary>
    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }
}
