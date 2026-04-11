using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFmPlayer.ViewModels;

namespace FFmPlayer;

public partial class MainWindow : Window
{
    // 各種ポップアップウィンドウのキャッシュ
    private SettingsWindow? _settingsWindow;
    private PlaylistWindow? _playlistWindow;

    public MainWindow()
    {
        InitializeComponent();
        
        // DataContext（MainViewModel）が設定された際に、ViewModel側からの要求をView（画面）側で処理するためのデリゲートを登録します。
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

                vm.OpenUrlAction = async () =>
                {
                    var dialog = new UrlInputDialog();
                    var result = await dialog.ShowDialog<string?>(this);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        vm.LoadMedia(result);
                    }
                };

                vm.ToggleFullscreenAction = () =>
                {
                    WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
                };

                vm.ExitFullscreenAction = () =>
                {
                    WindowState = WindowState.Normal;
                };

                vm.ShowMediaInfoAction = () =>
                {
                    var dialog = new MediaInfoWindow();
                    dialog.SetInfo(vm.GetMediaInfoString());
                    dialog.ShowDialog(this);
                };
            }
        };

        // ドラッグ＆ドロップおよびキーボードショートカット用のイベント登録
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        
        // シークバー（プログレスバー）に対するマウス操作イベントを登録します
        var slider = this.FindControl<Slider>("SeekSlider");
        if (slider != null)
        {
            // RoutingStrategies.Tunnel: 親から子へイベントが伝播する段階でキャッチし、内部のプロパティ変更より前にフラグを立てる
            slider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Tunnel);
        }
    }

    /// <summary>
    /// シークバー操作開始時：シークバー上のつまみをクリックした際に呼ばれます。
    /// （ドラッグ中にViewModel側からの描画更新によって位置が戻されるのを防ぎます）
    /// </summary>
    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsDraggingSlider = true;
        }
    }

    /// <summary>
    /// シークバー操作終了時：つまみを離した際に呼ばれ、シーク指示をViewModelへ送ります。
    /// </summary>
    private void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider slider && DataContext is MainViewModel vm)
        {
            vm.IsDraggingSlider = false;
            vm.RequestSeek(slider.Value);
        }
    }

    /// <summary>
    /// タイトルバー操作時：自作のタイトルバー（カスタムクロム）をドラッグしてウィンドウを移動させる処理です。
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>ウィンドウを最小化します。</summary>
    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>ウィンドウを最大化／元に戻すを切り替えます。</summary>
    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>ウィンドウを閉じてアプリを終了します。</summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>フルスクリーン（全画面表示）のトグル切り替えを行います。</summary>
    private void OnFullscreenToggleClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }

    /// <summary>
    /// 外部からファイルがドラッグ＆ドロップされた際に呼ばれ、ファイルをプレイリストに追加・再生します。
    /// </summary>
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

    /// <summary>
    /// ウィンドウ上でキーボード入力があった際に呼ばれ、ショートカット設定に合致するかViewModelで判定・実行します。
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            e.Handled = vm.ProcessShortcut(e.Key, e.KeyModifiers);
        }
    }
}