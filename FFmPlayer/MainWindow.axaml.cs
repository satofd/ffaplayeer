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
            }
        };
    }
}