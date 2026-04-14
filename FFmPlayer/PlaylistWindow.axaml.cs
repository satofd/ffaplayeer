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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        e.Cancel = true;
        this.Hide();
    }

    /// <summary>
    /// プレイリスト画面上にメディアファイルがドロップされた際、リストへファイルを追加します。
    /// （既存のプレイリストを消去せず末尾に追加します）
    /// </summary>
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

    /// <summary>
    /// 「追加」ボタンがクリックされた際、OSのファイル選択ダイアログを開き、
    /// 選択されたファイルをプレイリストへ追加します。
    /// </summary>
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

    /// <summary>
    /// プレイリストの各項目の横にある「削除（Remove）」ボタンが押された際に、
    /// 対象のメディアをリストから取り除きます。再生中だった場合は再生も停止します。
    /// </summary>
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

    /// <summary>
    /// カスタムタイトルバーのドラッグによるウィンドウ移動処理です。
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>
    /// タイトルバーの閉じるボタン処理。Hideと同等の処理を行います。
    /// </summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        this.Close(); // Will be intercepted by OnClosing and Hidden instead.
    }
}
