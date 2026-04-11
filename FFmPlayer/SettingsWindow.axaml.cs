using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FFmPlayer.ViewModels;

namespace FFmPlayer;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // ショートカットキーの重複チェック
            var s = vm.Settings;
            var list = new List<string> 
            { 
                s.ShortcutPlayPause, s.ShortcutStop, s.ShortcutOpenFile, s.ShortcutShowPlaylist,
                s.ShortcutSeekForward1s, s.ShortcutSeekBackward1s, s.ShortcutSeekForward10s, s.ShortcutSeekBackward10s,
                s.ShortcutSeekForward60s, s.ShortcutSeekBackward60s, s.ShortcutToggleFullscreen, s.ShortcutToggleMute,
                s.ShortcutIncreaseSpeed, s.ShortcutDecreaseSpeed, s.ShortcutResetSpeed
            };

            var hashSet = new HashSet<string>();
            foreach (var shortcut in list)
            {
                if (string.IsNullOrWhiteSpace(shortcut)) continue;
                if (!hashSet.Add(shortcut))
                {
                    var errorBlock = this.FindControl<TextBlock>("ErrorTextBlock");
                    if (errorBlock != null)
                    {
                        errorBlock.Text = $"エラー: ショートカット '{shortcut}' が重複しています。";
                    }
                    return;
                }
            }

            // 重複がない場合は保存して閉じる
            vm.SaveSettings();
            Close();
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
