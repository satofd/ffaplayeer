using Avalonia.Controls;
using FFmPlayer.ViewModels;

namespace FFmPlayer;

public partial class MainWindow : Window
{
    private SettingsWindow? _settingsWindow;
    private PlaylistWindow? _playlistWindow;

    public MainWindow()
    {
        InitializeComponent();
        
        DataContextChanged += (s, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ShowSettingsWindowAction = () => 
                {
                    if (_settingsWindow == null || !_settingsWindow.IsVisible)
                    {
                        _settingsWindow = new SettingsWindow { DataContext = this.DataContext };
                        _settingsWindow.Show();
                    }
                    else
                    {
                        _settingsWindow.Activate();
                    }
                };
                
                vm.ShowPlaylistWindowAction = () =>
                {
                    if (_playlistWindow == null || !_playlistWindow.IsVisible)
                    {
                        _playlistWindow = new PlaylistWindow { DataContext = this.DataContext };
                        _playlistWindow.Show();
                    }
                    else
                    {
                        _playlistWindow.Activate();
                    }
                };

                vm.OpenFileAction = async () =>
                {
                    var options = new Avalonia.Platform.Storage.FilePickerOpenOptions
                    {
                        Title = "Open Media File",
                        AllowMultiple = false
                    };
                    var result = await StorageProvider.OpenFilePickerAsync(options);
                    if (result != null && result.Count > 0)
                    {
                        vm.LoadMedia(result[0].Path.LocalPath);
                    }
                };
            }
        };

        AddHandler(Avalonia.Input.DragDrop.DropEvent, OnDrop);
        KeyDown += OnKeyDown;
    }

    private void OnDrop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        // 11.xでのドラッグ＆ドロップAPIの仕様差(Dataプロパティエラー)回避のため一時スキップ
    }

    private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            e.Handled = vm.ProcessShortcut(e.Key, e.KeyModifiers);
        }
    }

    private void OnSliderPointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        if (sender is Slider slider && DataContext is MainViewModel vm)
        {
            vm.RequestSeek(slider.Value);
        }
    }
}