using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
                        _settingsWindow.ShowDialog(this);
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
                        AllowMultiple = true
                    };
                    var result = await StorageProvider.OpenFilePickerAsync(options);
                    if (result != null && result.Count > 0)
                    {
                        var paths = result.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>();
                        vm.AddFilesToPlaylist(paths, clearExisting: true); // Replace existing queue
                    }
                };
            }
        };

        AddHandler(DragDrop.DropEvent, OnDrop);
        KeyDown += OnKeyDown;
        
        var slider = this.FindControl<Slider>("SeekSlider");
        if (slider != null)
        {
            slider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Tunnel);
        }
    }

    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsDraggingSlider = true;
        }
    }

    private void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider slider && DataContext is MainViewModel vm)
        {
            vm.IsDraggingSlider = false;
            vm.RequestSeek(slider.Value);
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnFullscreenToggleClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var files = e.DataTransfer?.TryGetFiles();
            if (files != null)
            {
                var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>();
                vm.AddFilesToPlaylist(paths, clearExisting: true); // Replace playlist when dropped on main window
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            e.Handled = vm.ProcessShortcut(e.Key, e.KeyModifiers);
        }
    }
}