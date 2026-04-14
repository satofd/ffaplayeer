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
    private ControlPanelWindow? _controlPanelWindow;

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
                    if (_playlistWindow == null)
                    {
                        _playlistWindow = new PlaylistWindow { DataContext = this.DataContext };
                        _playlistWindow.Show(this);
                    }
                    else
                    {
                        if (_playlistWindow.IsVisible) 
                            _playlistWindow.Hide();
                        else 
                        {
                            _playlistWindow.Show(this);
                            _playlistWindow.Activate();
                        }
                    }
                };

                vm.ShowControlPanelAction = () =>
                {
                    if (_controlPanelWindow == null || !_controlPanelWindow.IsVisible)
                    {
                        _controlPanelWindow = new ControlPanelWindow { DataContext = this.DataContext };
                        // offset it relative to the main window
                        var x = this.Position.X;
                        var y = this.Position.Y + (int)this.Bounds.Height - 150;
                        _controlPanelWindow.Position = new Avalonia.PixelPoint(x, y);
                        _controlPanelWindow.Show(this);
                    }
                    else
                    {
                        if (_controlPanelWindow.IsVisible) _controlPanelWindow.Hide();
                        else _controlPanelWindow.Show(this);
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

                vm.ResizeWindowToVideoSizeAction = (scaleW, scaleH) =>
                {
                    if (vm.VideoFrameBitmap != null)
                    {
                        var targetWidth = vm.VideoFrameBitmap.PixelSize.Width * scaleW;
                        var targetHeight = vm.VideoFrameBitmap.PixelSize.Height * scaleH;
                        // Approximate chrome heights, but we have almost none. Let's just set the size
                        this.Width = targetWidth;
                        // Add height for the bottom controls + top bar manually
                        this.Height = targetHeight + 50;
                    }
                };

                vm.ShrinkWindowToFitVideoAction = () =>
                {
                    if (vm.VideoFrameBitmap != null)
                    {
                        var video_w = vm.VideoFrameBitmap.PixelSize.Width;
                        var video_h = vm.VideoFrameBitmap.PixelSize.Height;
                        if (video_w == 0 || video_h == 0) return;

                        var imageControl = this.FindControl<Image>("VideoImage");
                        if (imageControl != null)
                        {
                            var container_w = imageControl.Bounds.Width;
                            var container_h = imageControl.Bounds.Height;
                            
                            if (container_w > 0 && container_h > 0)
                            {
                                double scaleW = container_w / video_w;
                                double scaleH = container_h / video_h;
                                double minScale = System.Math.Min(scaleW, scaleH);

                                double rendered_w = video_w * minScale;
                                double rendered_h = video_h * minScale;

                                this.Width = this.Width - container_w + rendered_w;
                                this.Height = this.Height - container_h + rendered_h;
                            }
                        }
                    }
                };

                vm.SetWindowModeAction = (state, stretch) =>
                {
                    this.WindowState = state;
                    vm.VideoStretch = stretch;
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

    /// <summary>
    /// 映像上のマウスホイール操作。音量を調整します。
    /// </summary>
    private void OnVideoPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var delta = e.Delta.Y;
            if (delta > 0)
            {
                vm.Volume = System.Math.Min(1.0, vm.Volume + 0.05);
            }
            else if (delta < 0)
            {
                vm.Volume = System.Math.Max(0.0, vm.Volume - 0.05);
            }
        }
    }

    /// <summary>
    /// 映像上でのマウスボタン押下時の処理。ミドルクリックなどに割り当てられたアクションを実行します。
    /// </summary>
    private void OnVideoPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
            {
                vm.ExecuteVideoMouseAction(vm.Settings.MiddleClickAction);
            }
        }
    }

    /// <summary>
    /// 映像上でのダブルクリック時の処理。ダブルクリックに割り当てられたアクションを実行します。
    /// </summary>
    private void OnVideoDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ExecuteVideoMouseAction(vm.Settings.DoubleClickAction);
        }
    }

    /// <summary>
    /// ウィンドウ端の不可視グリップからのドラッグリサイズ開始。
    /// </summary>
    private void OnResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is string edgeString && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (System.Enum.TryParse<WindowEdge>(edgeString, out var edge))
            {
                BeginResizeDrag(edge, e);
            }
            e.Handled = true;
        }
    }
}