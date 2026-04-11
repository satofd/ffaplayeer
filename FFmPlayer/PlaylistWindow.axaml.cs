using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFmPlayer.ViewModels;

namespace FFmPlayer;

public partial class PlaylistWindow : Window
{
    public PlaylistWindow()
    {
        InitializeComponent();
        
        var listBox = this.FindControl<ListBox>("PlaylistListBox");
        if (listBox != null)
        {
            listBox.SelectionChanged += (s, e) =>
            {
                if (e.AddedItems.Count > 0 && e.AddedItems[0] is string url && DataContext is MainViewModel vm)
                {
                    if (vm.CurrentPlaylistItem != url)
                    {
                        vm.LoadMedia(url);
                    }
                }
            };
        }

        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var files = e.DataTransfer?.TryGetFiles();
            if (files != null)
            {
                var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>();
                vm.AddFilesToPlaylist(paths, clearExisting: false);
            }
        }
    }

    private async void OnAddFilesClick(object? sender, RoutedEventArgs e)
    {
        var options = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "プレイリストに追加",
            AllowMultiple = true
        };
        var result = await StorageProvider.OpenFilePickerAsync(options);
        if (result != null && result.Count > 0 && DataContext is MainViewModel vm)
        {
            var paths = result.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>();
            vm.AddFilesToPlaylist(paths, clearExisting: false);
        }
    }

    private void OnRemoveItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is string url && DataContext is MainViewModel vm)
        {
            if (vm.Playlist.Contains(url))
            {
                vm.Playlist.Remove(url);
                if (vm.CurrentPlaylistItem == url)
                {
                    vm.Stop();
                    vm.CurrentPlaylistItem = null;
                }
            }
        }
    }
}
