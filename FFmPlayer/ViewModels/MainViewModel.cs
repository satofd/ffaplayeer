using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmPlayer.Models;
using FFmPlayer.Services;
using System.IO;
using System.Linq;

namespace FFmPlayer.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    
    private FFmpegDecoder? _decoder;
    private AudioPlayer? _audioPlayer;
    // デコードや描画ループなどのバックグラウンドタスクをキャンセル（停止）するためのトークン
    private CancellationTokenSource? _decodeCts;
    // データの読み込み・デコードを行うタスク
    private Task? _decodeTask;
    // デコード済み映像を指定時間に合わせて画面に描画するタスク
    private Task? _renderTask;

    // 音ズレ補正用ディレイ（秒）。必要に応じて変更可能。
    private const double AudioSyncDelaySeconds = 0.500;
    
    // UIに即時表示できるよう、デコード済みの過去・未来の映像フレームを保持するリングバッファ
    private FrameRingBuffer _frameBuffer = new();

    // ユーザーがシークや再生操作を行った際、映像と音声を同期させるための「基本となる基準時間（秒）」
    private double _baseSeconds = 0;
    
    // コマ戻しのバックフィルなどで再デコード要求を出す際の目標となるPTS
    private double _backfillTargetPts = -1;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isDraggingSlider;

    public bool IsStopped => !IsPlaying && !IsPaused;
    public bool IsMediaActive => !IsStopped;

    [ObservableProperty]
    private Avalonia.Media.Stretch _videoStretch = Avalonia.Media.Stretch.Uniform;

    [ObservableProperty]
    private double _position;

    [ObservableProperty]
    private double _duration;

    [ObservableProperty]
    private string _osdMessage = string.Empty;

    [ObservableProperty]
    private bool _osdVisible;

    [ObservableProperty]
    private TimeSpan _timePosition;

    [ObservableProperty]
    private TimeSpan _timeDuration;

    [ObservableProperty]
    private TimeSpan _timeRemaining;

    [ObservableProperty]
    private long _currentFrame;

    [ObservableProperty]
    private long _maxFrame;

    [ObservableProperty]
    private string _timeDisplayText = "";

    [ObservableProperty]
    private double _volume = 1.0;

    [ObservableProperty]
    private bool _isMuted = false;

    [ObservableProperty]
    private double _playbackSpeed = 1.0;

    [ObservableProperty]
    private WriteableBitmap? _videoFrameBitmap;

    [ObservableProperty]
    private double _abStart = -1;

    [ObservableProperty]
    private double _abEnd = -1;

    public System.Collections.ObjectModel.ObservableCollection<string> Playlist { get; } = new();

    [ObservableProperty]
    private string? _currentPlaylistItem;

    [ObservableProperty]
    private int _currentPlaylistIndex = -1;

    [ObservableProperty]
    private bool _alwaysOnTop;

    public void AddFilesToPlaylist(System.Collections.Generic.IEnumerable<string> files, bool clearExisting)
    {
        if (clearExisting)
        {
            Playlist.Clear();
            CurrentPlaylistItem = null;
            CurrentPlaylistIndex = -1;
            Stop();
        }

        bool playFirst = false;
        if (clearExisting || Playlist.Count == 0 || !IsPlaying)
        {
            playFirst = clearExisting; // usually true when dropping direct to player
        }

        string? firstFile = null;
        foreach (var file in files)
        {
            if (!Playlist.Contains(file))
            {
                Playlist.Add(file);
                firstFile ??= file;
            }
        }

        if (playFirst && firstFile != null)
        {
            LoadMedia(firstFile);
        }
    }

    public Action? ShowSettingsWindowAction { get; set; }
    public Action? ShowPlaylistWindowAction { get; set; }
    public Action? ShowControlPanelAction { get; set; }
    public Action? OpenFileAction { get; set; }
    public Action? OpenUrlAction { get; set; }
    public Func<Task<(bool IsConfirmed, string FolderPath, double Fps)>>? RequestImageSequenceSetupAsync { get; set; }
    public Action? ShowMediaInfoAction { get; set; }
    public Action? ToggleFullscreenAction { get; set; }
    public Action? ExitFullscreenAction { get; set; }
    public Action<double, double>? ResizeWindowToVideoSizeAction { get; set; }
    public Action<Avalonia.Controls.WindowState, Avalonia.Media.Stretch>? SetWindowModeAction { get; set; }
    public Action? ShrinkWindowToFitVideoAction { get; set; }
    public Action? ResizeToFitMaxAction { get; set; }
    
    public AppSettings Settings => _settings;

    public MainViewModel(SettingsService settingsService, AppSettings settings)
    {
        _settingsService = settingsService;
        _settings = settings;
        Volume = _settings.Volume;
        IsMuted = _settings.IsMuted;
        PlaybackSpeed = _settings.PlaybackSpeed;
        AlwaysOnTop = _settings.AlwaysOnTop;
    }

    [RelayCommand]
    public void OpenFile()
    {
        ShowOsd("Open File");
        OpenFileAction?.Invoke();
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    [RelayCommand]
    public async Task OpenImageSequence()
    {
        ShowOsd("Open Image Sequence");
        if (RequestImageSequenceSetupAsync != null)
        {
            var result = await RequestImageSequenceSetupAsync();
            if (result.IsConfirmed && !string.IsNullOrWhiteSpace(result.FolderPath) && Directory.Exists(result.FolderPath))
            {
                var files = Directory.GetFiles(result.FolderPath)
                                     .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                 f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                 f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                                 f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                                     .ToArray();
                                     
                if (files.Length == 0)
                {
                    ShowOsd("No images found in folder");
                    return;
                }

                if (OperatingSystem.IsWindows())
                {
                    Array.Sort(files, (a, b) => StrCmpLogicalW(a, b));
                }
                else
                {
                    Array.Sort(files);
                }

                double duration = 1.0 / result.Fps;
                string tempFile = Path.Combine(Path.GetTempPath(), $"ffconcat_fps{result.Fps}_{Guid.NewGuid():N}.txt");
                
                using (var writer = new StreamWriter(tempFile, false))
                {
                    writer.WriteLine("ffconcat version 1.0");
                    foreach (var file in files)
                    {
                        string safePath = file.Replace("'", @"\'" ); // simple escape
                        writer.WriteLine($"file '{safePath}'");
                        writer.WriteLine($"duration {duration.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                    }
                    // Requires the last file to be repeated without duration or with regular properties
                    string lastSafePath = files.Last().Replace("'", @"\'");
                    writer.WriteLine($"file '{lastSafePath}'");
                }

                LoadMedia(tempFile);
            }
        }
    }

    /// <summary>
    /// アプリケーション内で受け取ったキー入力をショートカット設定と照らし合わせ、合致する機能（再生、停止、音量調整など）を実行します。
    /// </summary>
    /// <param name="key">押されたキー</param>
    /// <param name="modifiers">同時押しされている修飾キー（Ctrl, Shift, Altなど）</param>
    /// <returns>ショートカットとして処理された場合はtrueを返します</returns>
    public bool ProcessShortcut(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        var mods = string.Empty;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)) mods += "Ctrl+";
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)) mods += "Shift+";
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt)) mods += "Alt+";
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta)) mods += "Meta+";

        string shortcut = mods + key.ToString();

        if (shortcut == _settings.ShortcutPlayPause) { PlayPause(); return true; }
        if (shortcut == _settings.ShortcutStop) { Stop(); return true; }
        
        if (shortcut == _settings.ShortcutSeekForward1s) { SeekForward1(); return true; }
        if (shortcut == _settings.ShortcutSeekBackward1s) { SeekBackward1(); return true; }
        if (shortcut == _settings.ShortcutSeekForward10s) { SeekForward10(); return true; }
        if (shortcut == _settings.ShortcutSeekBackward10s) { SeekBackward10(); return true; }
        if (shortcut == _settings.ShortcutSeekForward60s) { SeekForward60(); return true; }
        if (shortcut == _settings.ShortcutSeekBackward60s) { SeekBackward60(); return true; }
        
        if (shortcut == _settings.ShortcutToggleMute) { IsMuted = !IsMuted; return true; }
        if (shortcut == _settings.ShortcutToggleFullscreen) { ToggleFullscreenAction?.Invoke(); ShowOsd("Toggle Fullscreen"); return true; }
        if (shortcut == _settings.ShortcutExitFullscreen) { ExitFullscreenAction?.Invoke(); ShowOsd("Exit Fullscreen"); return true; }
        if (shortcut == _settings.ShortcutOpenFile) { OpenFile(); return true; }
        if (shortcut == _settings.ShortcutOpenImageSequence) { _ = OpenImageSequence(); return true; }
        if (shortcut == _settings.ShortcutOpenUrl) { OpenUrlAction?.Invoke(); return true; }
        if (shortcut == _settings.ShortcutShowPlaylist) { OpenPlaylist(); return true; }
        if (shortcut == _settings.ShortcutShowMediaInfo) { ShowMediaInfoAction?.Invoke(); return true; }
        if (shortcut == _settings.ShortcutIncreaseSpeed || shortcut == _settings.ShortcutIncreaseSpeedAlt) { IncreaseSpeed(); return true; }
        if (shortcut == _settings.ShortcutDecreaseSpeed || shortcut == _settings.ShortcutDecreaseSpeedAlt) { DecreaseSpeed(); return true; }
        if (shortcut == _settings.ShortcutResetSpeed) { PlaybackSpeed = 1.0; return true; }

        if (shortcut == _settings.ShortcutStepForward) { StepForward(); return true; }
        if (shortcut == _settings.ShortcutStepBackward) { StepBackward(); return true; }
        
        if (shortcut == _settings.ShortcutSetAbStart) { SetAbStart(); return true; }
        if (shortcut == _settings.ShortcutSetAbEnd) { SetAbEnd(); return true; }
        if (shortcut == _settings.ShortcutCycleTimeDisplay) { CycleTimeDisplay(); return true; }
        if (shortcut == _settings.ShortcutTakeScreenshot) { TakeScreenshot(); return true; }

        if (shortcut == _settings.ShortcutWindowSize50) { ApplyWindowSizeFromShortcut(_settings.ShortcutWindowSize50); return true; }
        if (shortcut == _settings.ShortcutWindowSize100) { ApplyWindowSizeFromShortcut(_settings.ShortcutWindowSize100); return true; }
        if (shortcut == _settings.ShortcutWindowSize150) { ApplyWindowSizeFromShortcut(_settings.ShortcutWindowSize150); return true; }
        if (shortcut == _settings.ShortcutWindowSize200) { ApplyWindowSizeFromShortcut(_settings.ShortcutWindowSize200); return true; }
        if (shortcut == _settings.ShortcutMaximizedNoMargin) { ApplyWindowSizeFromShortcut(_settings.ShortcutMaximizedNoMargin); return true; }
        if (shortcut == _settings.ShortcutMaximizedMargin) { ApplyWindowSizeFromShortcut(_settings.ShortcutMaximizedMargin); return true; }
        if (shortcut == _settings.ShortcutFitVideoNoMargin) { ApplyWindowSizeFromShortcut(_settings.ShortcutFitVideoNoMargin); return true; }
        if (shortcut == _settings.ShortcutFullscreenNoMargin) { ApplyWindowSizeFromShortcut(_settings.ShortcutFullscreenNoMargin); return true; }
        if (shortcut == _settings.ShortcutFullscreenMargin) { ApplyWindowSizeFromShortcut(_settings.ShortcutFullscreenMargin); return true; }

        return false;
    }

    private void ApplyWindowSizeFromShortcut(string matchedShortcut)
    {
        if (_decoder == null) return;
        
        if (matchedShortcut == _settings.ShortcutWindowSize50) { SetWindowModeAction?.Invoke(Avalonia.Controls.WindowState.Normal, Avalonia.Media.Stretch.Uniform); ResizeWindowToVideoSizeAction?.Invoke(0.5, 0.5); ShowOsd("Size: 50%"); }
        else if (matchedShortcut == _settings.ShortcutWindowSize100) { SetWindowModeAction?.Invoke(Avalonia.Controls.WindowState.Normal, Avalonia.Media.Stretch.Uniform); ResizeWindowToVideoSizeAction?.Invoke(1.0, 1.0); ShowOsd("Size: 100%"); }
        else if (matchedShortcut == _settings.ShortcutWindowSize150) { SetWindowModeAction?.Invoke(Avalonia.Controls.WindowState.Normal, Avalonia.Media.Stretch.Uniform); ResizeWindowToVideoSizeAction?.Invoke(1.5, 1.5); ShowOsd("Size: 150%"); }
        else if (matchedShortcut == _settings.ShortcutWindowSize200) { SetWindowModeAction?.Invoke(Avalonia.Controls.WindowState.Normal, Avalonia.Media.Stretch.Uniform); ResizeWindowToVideoSizeAction?.Invoke(2.0, 2.0); ShowOsd("Size: 200%"); }
        else if (matchedShortcut == _settings.ShortcutMaximizedNoMargin) { ResizeToFitMaxAction?.Invoke(); ShowOsd("Maximized (Fit/No Margin)"); }
        else if (matchedShortcut == _settings.ShortcutMaximizedMargin) { SetWindowModeAction?.Invoke(Avalonia.Controls.WindowState.Maximized, Avalonia.Media.Stretch.Uniform); ShowOsd("Maximized (Keep Margin)"); }
        else if (matchedShortcut == _settings.ShortcutFitVideoNoMargin) 
        {
            SetWindowModeAction?.Invoke(Avalonia.Controls.WindowState.Normal, Avalonia.Media.Stretch.Uniform);
            ShrinkWindowToFitVideoAction?.Invoke();
            ShowOsd("Fit Video Window");
        }
        else if (matchedShortcut == _settings.ShortcutFullscreenNoMargin) { SetWindowModeAction?.Invoke(Avalonia.Controls.WindowState.FullScreen, Avalonia.Media.Stretch.Uniform); ShowOsd("Fullscreen (Fit)"); }
        else if (matchedShortcut == _settings.ShortcutFullscreenMargin) { SetWindowModeAction?.Invoke(Avalonia.Controls.WindowState.FullScreen, Avalonia.Media.Stretch.Uniform); ShowOsd("Fullscreen (Keep Margin)"); }
    }

    public void ExecuteVideoMouseAction(VideoMouseAction action)
    {
        switch (action)
        {
            case VideoMouseAction.TogglePlaylist:
                OpenPlaylist();
                break;
            case VideoMouseAction.ToggleControlPanel:
                OpenControlPanel();
                break;
            case VideoMouseAction.ToggleSettings:
                ShowSettingsWindowAction?.Invoke();
                break;
            case VideoMouseAction.FitWindowToVideo:
                ShrinkWindowToFitVideoAction?.Invoke();
                // Ensure Stretch is Uniform so it doesn't crop
                VideoStretch = Avalonia.Media.Stretch.Uniform;
                ShowOsd("Fit Window to Video");
                break;
            case VideoMouseAction.ToggleFullscreen:
                ToggleFullscreenAction?.Invoke();
                ShowOsd("Toggle Fullscreen");
                break;
            case VideoMouseAction.PlayPause:
                PlayPause(); // PlayPause already has ShowOsd
                break;
            case VideoMouseAction.None:
            default:
                break;
        }
    }

    /// <summary>
    /// 現在の再生位置からコマ送り（1フレーム進む）を行います。
    /// 音声と映像の再生ストリームを一時停止状態にし、次の映像フレームのみを即時デコードしてUIへ強制描画します。
    /// </summary>
    [RelayCommand]
    public void StepForward()
    {
        if (_decoder == null) return;
        IsPlaying = true;
        IsPaused = true;
        _audioPlayer?.Pause();
        
        if (_frameBuffer.TryDequeue(out var frame))
        {
            if (!frame.IsEndOfStream)
            {
                Position = frame.Pts;
                UpdateVideoBitmap(frame);
                ShowOsd("Step Forward");
            }
        }
    }

    /// <summary>
    /// AvalonUIのWriteableBitmapのメモリ空間をロックし、バックグラウンドでデコードされた生の映像byte配列をコピーして画面を更新します。
    /// （UIスレッドでのみ実行される想定）
    /// </summary>
    private void UpdateVideoBitmap(VideoFrameData frame)
    {
        if (VideoFrameBitmap != null && frame.Data != null && frame.Data.Length > 0)
        {
            using var fb = VideoFrameBitmap.Lock();
            int width = VideoFrameBitmap.PixelSize.Width;
            int height = VideoFrameBitmap.PixelSize.Height;
            int srcStride = width * 4;
            int dstStride = fb.RowBytes;

            if (srcStride == dstStride)
                Marshal.Copy(frame.Data, 0, fb.Address, frame.Data.Length);
            else
                for (int y = 0; y < height; y++)
                    Marshal.Copy(frame.Data, y * srcStride, fb.Address + y * dstStride, srcStride);
                    
            var temp = VideoFrameBitmap;
            VideoFrameBitmap = null;
            VideoFrameBitmap = temp;
        }
    }

    /// <summary>
    /// 現在の再生位置からコマ戻し（1フレーム戻る）を行います。
    /// キャッシュから対象のフレームを検索し、なければ指定時間分巻き戻してキャッシュをバックフィルします。
    /// </summary>
    [RelayCommand]
    public void StepBackward()
    {
        if (_decoder == null) return;
        IsPlaying = true;
        IsPaused = true;
        _audioPlayer?.Pause();

        if (_backfillTargetPts >= 0)
        {
            // バックフィル（キャッシュ再構築）中は入力を完全に無視することで、
            // キー長押しによって一気に数秒分戻ってしまう（大きく前に戻る）現象を防ぎ、
            // 必ず再構築後の1コマずつを取得させるように強制する。
            return;
        }

        double targetPts = Math.Max(0, Position - 1.0 / (_decoder.Framerate > 0 ? _decoder.Framerate : 30.0));
        var cachedFrame = _frameBuffer.StepBackward(targetPts);
        if (cachedFrame != null)
        {
            Position = cachedFrame.Pts;
            UpdateVideoBitmap(cachedFrame);
            ShowOsd("Step Backward");
        }
        else
        {
            // Cache miss: backfill
            double seekTime = Math.Max(0, targetPts - _settings.StepScanWindowBackwardSeconds);
            _backfillTargetPts = targetPts;
            RequestSeek(seekTime); // DecodeLoop will rapidly decode up to targetPts
            ShowOsd("Step Backward");
        }
    }

    [RelayCommand] public void SeekForward1() => RequestSeek(Position + 1);
    [RelayCommand] public void SeekForward10() => RequestSeek(Position + 10);
    [RelayCommand] public void SeekForward60() => RequestSeek(Position + 60);
    [RelayCommand] public void SeekBackward1() => RequestSeek(Math.Max(0, Position - 1));
    [RelayCommand] public void SeekBackward10() => RequestSeek(Math.Max(0, Position - 10));
    [RelayCommand] public void SeekBackward60() => RequestSeek(Math.Max(0, Position - 60));

    /// <summary>
    /// プレイリスト設定（リピート、順次再生、ランダムなど）に基づき、次のメディアファイルを選択して自動的にロードします。
    /// </summary>
    public void AdvancePlaylist()
    {
        if (Playlist.Count == 0) return;

        int currentIndex = Playlist.IndexOf(CurrentPlaylistItem ?? "");
        string? nextUrl = null;

        switch (_settings.PlaybackMode)
        {
            case FFmPlayer.Models.PlaybackMode.Sequential:
                nextUrl = Playlist[(currentIndex + 1) % Playlist.Count];
                break;
            case FFmPlayer.Models.PlaybackMode.RepeatOne:
                nextUrl = CurrentPlaylistItem;
                break;
            case FFmPlayer.Models.PlaybackMode.Random:
                nextUrl = Playlist[new Random().Next(Playlist.Count)];
                break;
            case FFmPlayer.Models.PlaybackMode.Off:
                if (currentIndex >= 0 && currentIndex < Playlist.Count - 1)
                {
                    nextUrl = Playlist[currentIndex + 1];
                }
                break;
        }

        if (!string.IsNullOrEmpty(nextUrl))
        {
            LoadMedia(nextUrl);
            ShowOsd($"Next: {System.IO.Path.GetFileName(nextUrl)}");
        }
    }

    /// <summary>
    /// 指定されたURL（またはローカルパス）のメディアファイルを読み込み、FFmpegデコーダとオーディオプレーヤーを初期化して再生を開始します。
    /// </summary>
    /// <param name="url">再生するメディアのパス</param>
    public void LoadMedia(string url)
    {
        Stop();

        if (!Playlist.Contains(url))
        {
            Playlist.Add(url);
        }
        CurrentPlaylistItem = url;
        CurrentPlaylistIndex = Playlist.IndexOf(url) + 1;

        _decoder = new FFmpegDecoder();
        if (!_decoder.Initialize(url))
        {
            ShowOsd($"Cannot open: {url}");
            _decoder.Dispose();
            _decoder = null;
            return;
        }

        Duration = _decoder.Duration;
        if (_decoder.Framerate > 0)
        {
            MaxFrame = (long)(Duration * _decoder.Framerate);
        }

        VideoFrameBitmap = new WriteableBitmap(
            new PixelSize(_decoder.VideoWidth, _decoder.VideoHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        _audioPlayer = new AudioPlayer();
        _audioPlayer.Init(_decoder.AudioSampleRate, _decoder.AudioChannels);
        _audioPlayer.SetVolume(IsMuted ? 0 : (float)Volume);

        _baseSeconds = 0;
        IsPlaying = true;
        IsPaused = false;
        
        _frameBuffer = new FrameRingBuffer 
        { 
            MaxFrames = _settings.FrameBufferSize,
            MemoryLimitEnabled = _settings.MemoryLimitEnabled,
            MemoryLimitMB = _settings.MemoryLimitMB
        };
        _backfillTargetPts = -1;

        _decodeCts = new CancellationTokenSource();
        _decodeTask = Task.Run(() => DecodeLoopAsync(_decodeCts.Token));
        _renderTask = Task.Run(() => VideoRenderLoopAsync(_decodeCts.Token));
        
        _audioPlayer.Play();
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsMediaActive));
    }

    private double _seekRequestTime = -1;
    private bool _needsPreviewFrame = false;

    /// <summary>
    /// バックグラウンドでFFmpegからメディアパケットを連続で読み込み、デコードするループ処理です。
    /// デコードした映像フレームは _frameBuffer キューに一時保存され、音声フレームはそのまま NAudio に渡されます。
    /// （シーク要求があった場合は古いフレームを破棄し、新しい時間軸から読み込みを再開します）
    /// </summary>
    /// <param name="token">終了・キャンセル要求を受け取るためのトークン</param>
    private async Task DecodeLoopAsync(CancellationToken token)
    {
        double targetSeekTimeAfterFlush = -1;
        try
        {
            while (!token.IsCancellationRequested)
            {
                // UIスレッドなどからのシーク要求があるかチェックする
                // Interlocked.Exchangeを使ってスレッドセーフに読み取りとリセット（-1へ）を同時に実行
                double target = Interlocked.Exchange(ref _seekRequestTime, -1);
                
                // もしシーク要求（0秒以上）があった場合
                if (target >= 0)
                {
                    _decoder?.RequestSeek(target);
                    _audioPlayer?.ClearBuffer();
                    // キューにたまっている古いデコード済みフレームをすべて破棄
                    _frameBuffer.Clear();
                    
                    targetSeekTimeAfterFlush = target;
                    // もしバックフィル目的のシークでなければ、シーク直後のプレビュー表示をONにする
                    if (_backfillTargetPts < 0)
                    {
                        _needsPreviewFrame = true;
                    }
                    continue;
                }

                // デコーダが存在しないか、一時停止中かつ既に十分なフレーム（5枚以上）が溜まっているなら休止
                // (一時停止中も描画のために数枚だけは読み込んでおく設計。ただしバックフィル中は無視して全力でデコードする)
                if (_decoder == null || (IsPaused && _frameBuffer.UnreadCount >= 5 && _backfillTargetPts < 0))
                {
                    await Task.Delay(50, token);
                    continue;
                }

                // 再生中で、十分な映像フレーム（設定値分）または音声バッファ（4秒以上）が既に溜まっているなら休止
                if (!IsPaused && (_frameBuffer.UnreadCount >= _settings.FrameBufferSize || 
                    (_audioPlayer != null && _audioPlayer.GetBufferedSeconds() > 4.0)))
                {
                    await Task.Delay(10, token);
                    continue;
                }

                // FFmpegに次のフレームのデコードを行わせる
                var type = _decoder.TryDecodeNextFrame(out double pts, out byte[] data, out int strideOrSize, targetSeekTimeAfterFlush);

                if (type == FFmpegDecoder.FrameType.EndOfStream)
                {
                    targetSeekTimeAfterFlush = -1;
                    _backfillTargetPts = -1;
                    _frameBuffer.Enqueue(new VideoFrameData { IsEndOfStream = true });
                    // ファイルの終端に達した場合は無駄なCPU消費を避けるため長めに待機
                    await Task.Delay(500, token);
                    continue;
                }
                
                if (targetSeekTimeAfterFlush >= 0)
                {
                    if (type == FFmpegDecoder.FrameType.Video || type == FFmpegDecoder.FrameType.Audio)
                    {
                        if (pts < targetSeekTimeAfterFlush)
                        {
                            continue; // Discard pre-target frames decoded after seeking back to keyframe
                        }
                        
                        _baseSeconds = pts;
                        targetSeekTimeAfterFlush = -1;
                        _audioPlayer?.ResetClock();
                        if (_backfillTargetPts < 0)
                        {
                            Dispatcher.UIThread.Post(() => {
                                if (!IsDraggingSlider) Position = pts;
                            });
                        }
                    }
                    else if (type == FFmpegDecoder.FrameType.Error)
                    {
                        targetSeekTimeAfterFlush = -1;
                        continue;
                    }
                    else
                    {
                        continue;
                    }
                }

                if (type == FFmpegDecoder.FrameType.Video)
                {
                    var f = new VideoFrameData { Pts = pts, Data = data };
                    _frameBuffer.Enqueue(f);

                    // バックフィル中の場合、目標時刻に達したら描画させてバックフィル状態を解除
                    if (_backfillTargetPts >= 0)
                    {
                        if (pts >= _backfillTargetPts)
                        {
                            var targetFrame = _frameBuffer.StepBackward(_backfillTargetPts);
                            if (targetFrame != null)
                            {
                                Dispatcher.UIThread.Post(() => {
                                    Position = targetFrame.Pts;
                                    UpdateVideoBitmap(targetFrame);
                                });
                            }
                            _backfillTargetPts = -1;
                        }
                    }
                    // 普通のシークプレビュー処理の場合
                    else if (_needsPreviewFrame)
                    {
                        _needsPreviewFrame = false;
                        Dispatcher.UIThread.Post(() => {
                            if (IsPaused) UpdateVideoBitmap(new VideoFrameData { Pts = f.Pts, Data = f.Data });
                        });
                    }
                }
                else if (type == FFmpegDecoder.FrameType.Audio && _backfillTargetPts < 0)
                {
                    _audioPlayer!.AddSamples(data, 0, data.Length);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error("Decode loop exception", ex);
        }
    }

    /// <summary>
    /// デコードされてキューに溜まった映像フレーム (`_frameBuffer`) を取り出し、
    /// 現在の再生時間（音声基準時間など）と照らし合わせながら、AvaloniaのUIスレッドへ描画を依頼するループ処理です。
    /// （映像が早すぎる場合は待機し、遅すぎる場合はフレームをドロップしてA/V同期を保ちます）
    /// </summary>
    /// <param name="token">終了・キャンセル要求を受け取るためのトークン</param>
    private async Task VideoRenderLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                // 一時停止中は、画面を更新しないため待機（CPU消費を抑える）
                if (IsPaused)
                {
                    await Task.Delay(10, token);
                    continue;
                }

                // キューに映像フレームが存在するかチェックする（取り出しはまだ行わない）
                if (_frameBuffer.TryPeek(out var frame))
                {
                    // 動画が終わった場合（EndOfStreamフラグが立っている）
                    if (frame.IsEndOfStream)
                    {
                        Dispatcher.UIThread.Post(() => {
                            Stop(); // 再生を停止状態にする
                            AdvancePlaylist(); // リピートや次の動画等、プレイリストの設定に合わせて次へ進む
                        });
                        break;
                    }

                    // A-Bループの判定処理
                    if (AbStart >= 0 && AbEnd >= 0 && AbStart < AbEnd && frame.Pts >= AbEnd)
                    {
                        RequestSeek(AbStart);
                        continue;
                    }

                    // 音声を基準とした「現在の正確な再生時間（マスタークロック）」を計算する
                    double audioMasterSec = _baseSeconds + _audioPlayer!.GetPlayedSeconds() * PlaybackSpeed;
                    
                    // 音ズレ補正（動画の描画を意図的に遅らせるディレイ）
                    audioMasterSec -= AudioSyncDelaySeconds;

                    // 映像の表示時間と現在のマスタークロックとの差（ズレ）を計算
                    double drift = frame.Pts - audioMasterSec;

                    // 映像が音声よりも先行しすぎている場合は、描画タイミングが来るまでその時間分だけ待機する
                    if (drift > _settings.VideoLeadSleepThresholdSeconds)
                    {
                        await Task.Delay((int)(drift * 1000 / PlaybackSpeed), token);
                        continue;
                    }

                    // 描画タイミングが来たため、初めてキューから未読フレームを取り出す
                    _frameBuffer.TryDequeue(out _);

                    // 映像が音声より遅れすぎている場合は、このフレームは描画（表示）せずに破棄してA/V同期を合わせる
                    if (drift < -_settings.VideoDropLagThresholdSeconds)
                    {
                        continue; 
                    }

                    // UIスレッドを呼び出して、実際に画像を画面へ表示する
                    Dispatcher.UIThread.Post(() =>
                    {
                        // ユーザーがシークバーをドラッグ中でなければ、シークバーの位置（Position）を動画の現在位置（Pts）に同調させる
                        if (!IsDraggingSlider) Position = frame.Pts;
                        UpdateVideoBitmap(frame);
                    });
                }
                else
                {
                    // デコードが追いついていない（キューが空の）場合は少し待機
                    await Task.Delay(10, token);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error("Video render loop exception", ex);
        }
    }

    private void ShowOsd(string msg)
    {
        OsdMessage = msg;
        OsdVisible = true;
        // Ideally start a timer to clear after 3 seconds
        Task.Delay(3000).ContinueWith(_ => Dispatcher.UIThread.Post(() => {
            if (OsdMessage == msg) OsdVisible = false;
        }));
    }

    partial void OnPositionChanged(double value)
    {
        TimePosition = TimeSpan.FromSeconds(value);
        TimeRemaining = TimeSpan.FromSeconds(Math.Max(0, Duration - value));
        if (_decoder != null)
        {
            CurrentFrame = (long)(value * _decoder.Framerate);
        }
        UpdateTimeDisplay();
    }

    partial void OnDurationChanged(double value)
    {
        TimeDuration = TimeSpan.FromSeconds(value);
        if (_decoder != null)
        {
            MaxFrame = (long)(value * _decoder.Framerate);
        }
        UpdateTimeDisplay();
    }

    private void UpdateTimeDisplay()
    {
        if (IsStopped)
        {
            TimeDisplayText = "00:00:00 / 00:00:00";
            return;
        }

        switch (_settings.TimeDisplayMode)
        {
            case 0:
                TimeDisplayText = $"{TimePosition:hh\\:mm\\:ss\\.fff} / {TimeDuration:hh\\:mm\\:ss\\.fff}";
                break;
            case 1:
                TimeDisplayText = $"-{TimeRemaining:hh\\:mm\\:ss\\.fff} / {TimeDuration:hh\\:mm\\:ss\\.fff}";
                break;
            case 2:
                TimeDisplayText = $"Frame: {CurrentFrame} / {MaxFrame}";
                break;
            default:
                TimeDisplayText = $"{TimePosition:hh\\:mm\\:ss} / {TimeDuration:hh\\:mm\\:ss}";
                break;
        }
    }

    [RelayCommand] public void OpenSettings() { ShowOsd("Settings"); ShowSettingsWindowAction?.Invoke(); }
    [RelayCommand] public void OpenUrl() { ShowOsd("Open URL"); OpenUrlAction?.Invoke(); }

    public string GetMediaInfoString()
    {
        if (_decoder == null) return "メディアがロードされていません";
        return $"ファイル: {CurrentPlaylistItem}\n" +
               $"解像度: {_decoder.VideoWidth} x {_decoder.VideoHeight}\n" +
               $"フレームレート: {_decoder.Framerate} fps\n" +
               $"オーディオ: {_decoder.AudioSampleRate} Hz, {_decoder.AudioChannels} ch\n" +
               $"持続時間: {TimeSpan.FromSeconds(Duration):hh\\:mm\\:ss}\n" +
               $"総フレーム数: {MaxFrame}";
    }

    [RelayCommand] public void ShowMediaInfo() { ShowOsd("Media Info"); ShowMediaInfoAction?.Invoke(); }
    [RelayCommand] public void OpenPlaylist() { ShowOsd("Playlist"); ShowPlaylistWindowAction?.Invoke(); }
    [RelayCommand] public void OpenControlPanel() { ShowOsd("Control Panel"); ShowControlPanelAction?.Invoke(); }

    /// <summary>
    /// 動画のシーク要求を行います。UIなどから時間（秒）を受け取り、バックグラウンドのデコードループへ通知します。
    /// </summary>
    /// <param name="seconds">ジャンプ先の時間（秒）</param>
    public void RequestSeek(double seconds)
    {
        if (_decoder == null) return;
        double clamped = Math.Clamp(seconds, 0, Duration);
        Interlocked.Exchange(ref _seekRequestTime, clamped);
        ShowOsd($"Seek: {TimeSpan.FromSeconds(clamped):hh\\:mm\\:ss}");
    }

    /// <summary>
    /// 再生・一時停止のトグル処理を行います。オーディオの再生状態も同時に切り替わります。
    /// </summary>
    [RelayCommand]
    public void PlayPause()
    {
        if (_decoder == null && !string.IsNullOrEmpty(CurrentPlaylistItem))
        {
            LoadMedia(CurrentPlaylistItem);
            ShowOsd("Play");
            return;
        }

        if (IsPlaying)
        {
            IsPlaying = false;
            IsPaused = true;
            _audioPlayer?.Pause();
            ShowOsd("Pause");
        }
        else
        {
            IsPlaying = true;
            IsPaused = false;
            _audioPlayer?.Play();
            ShowOsd("Play");
        }
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsMediaActive));
        UpdateTimeDisplay();
    }

    /// <summary>
    /// メディアの再生を完全に停止し、デコーダーとオーディオプレイヤーのメモリやタスクを破棄して初期状態に戻します。
    /// </summary>
    [RelayCommand]
    public void Stop()
    {
        IsPlaying = false;
        IsPaused = false;
        Position = 0;
        
        _decodeCts?.Cancel();
        _decodeCts?.Dispose();
        _decodeCts = null;

        _audioPlayer?.Stop();
        _audioPlayer?.Dispose();
        _audioPlayer = null;

        _decoder?.Dispose();
        _decoder = null;
        
        VideoFrameBitmap = null;
        
        _frameBuffer.Clear();

        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsMediaActive));
        UpdateTimeDisplay();
        ShowOsd("Stop");
    }

    /// <summary>
    /// コンストラクタで初期化された設定値などに基づき、再生速度を0.1倍ずつ上げます（最大3.0倍）。
    /// </summary>
    [RelayCommand] public void IncreaseSpeed() => PlaybackSpeed = Math.Clamp(PlaybackSpeed + 0.1, 0.1, 3.0);
    /// <summary>
    /// コンストラクタで初期化された設定値などに基づき、再生速度を0.1倍ずつ下げます（最低0.1倍）。
    /// </summary>
    [RelayCommand] public void DecreaseSpeed() => PlaybackSpeed = Math.Clamp(PlaybackSpeed - 0.1, 0.1, 3.0);
    
    [RelayCommand] public void ResetSpeed() => PlaybackSpeed = 1.0;

    /// <summary>
    /// プレイリストにおける次のメディア（曲や動画）へ手動でスキップします。
    /// </summary>
    [RelayCommand] public void PlayListNext() => AdvancePlaylist();
    
    /// <summary>
    /// プレイリストにおける前のメディアへ戻ります。ランダム、ループなどの再生モード設定（PlaybackMode）を加味して対象を決定します。
    /// </summary>
    [RelayCommand]
    public void PlayListPrev()
    {
        if (Position > 3.0)
        {
            RequestSeek(0);
            return;
        }

        if (Playlist.Count == 0) return;

        int currentIndex = Playlist.IndexOf(CurrentPlaylistItem ?? "");
        string? prevUrl = null;

        switch (_settings.PlaybackMode)
        {
            case FFmPlayer.Models.PlaybackMode.Sequential:
            case FFmPlayer.Models.PlaybackMode.Off:
                if (currentIndex > 0)
                {
                    prevUrl = Playlist[currentIndex - 1];
                }
                else
                {
                    prevUrl = Playlist[Playlist.Count - 1];
                }
                break;
            case FFmPlayer.Models.PlaybackMode.RepeatOne:
                prevUrl = CurrentPlaylistItem;
                break;
            case FFmPlayer.Models.PlaybackMode.Random:
                prevUrl = Playlist[new Random().Next(Playlist.Count)];
                break;
        }

        if (!string.IsNullOrEmpty(prevUrl))
        {
            LoadMedia(prevUrl);
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (!IsMuted) _audioPlayer?.SetVolume((float)value);
        ShowOsd($"Volume: {value:P0}");
    }

    partial void OnIsMutedChanged(bool value)
    {
        _audioPlayer?.SetVolume(value ? 0 : (float)Volume);
        ShowOsd(value ? "Muted" : "Unmuted");
    }

    partial void OnPlaybackSpeedChanged(double value)
    {
        ShowOsd($"Speed: {value:F2}x");
    }

    partial void OnAlwaysOnTopChanged(bool value)
    {
        _settings.AlwaysOnTop = value;
    }

    /// <summary>
    /// 現在の音量、ミュート状態、再生オプションなどを AppSettings クラスのプロパティへ反映し、ディスク（設定ファイル）へ保存します。
    /// </summary>
    public void SaveSettings()
    {
        _settings.Volume = Volume;
        _settings.IsMuted = IsMuted;
        _settings.PlaybackSpeed = PlaybackSpeed;
        _settings.AlwaysOnTop = AlwaysOnTop;
        _settingsService.Save(_settings);
    }

    public bool IsAbLoopActive => AbStart >= 0 && AbEnd >= 0 && AbStart < AbEnd;

    private void SetAbStart()
    {
        AbStart = Position;
        if (AbEnd >= 0 && AbStart >= AbEnd) AbEnd = -1; // Reset End if invalid
        ShowOsd($"A-B Start: {TimeSpan.FromSeconds(AbStart):hh\\:mm\\:ss}");
        OnPropertyChanged(nameof(IsAbLoopActive));
    }

    private void SetAbEnd()
    {
        if (AbStart < 0 || Position <= AbStart)
        {
            ShowOsd("A-B Error: Invalid End Position");
            return;
        }
        AbEnd = Position;
        ShowOsd($"A-B End: {TimeSpan.FromSeconds(AbEnd):hh\\:mm\\:ss}\nA-B Loop Enabled");
        OnPropertyChanged(nameof(IsAbLoopActive));
    }

    private void CycleTimeDisplay()
    {
        _settings.TimeDisplayMode = (_settings.TimeDisplayMode + 1) % 3;
        UpdateTimeDisplay();
        ShowOsd($"Time Display Mode: {_settings.TimeDisplayMode}");
        OnPropertyChanged(nameof(TimeDisplayMode));
    }

    public int TimeDisplayMode => _settings.TimeDisplayMode;

    private void TakeScreenshot()
    {
        if (VideoFrameBitmap == null) return;
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "FFmPlayer");
            System.IO.Directory.CreateDirectory(dir);
            var filename = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var path = System.IO.Path.Combine(dir, filename);
            VideoFrameBitmap.Save(path);
            ShowOsd($"Screenshot Saved:\n{filename}");
        }
        catch (Exception ex)
        {
            Logger.Error("Screenshot Error", ex);
            ShowOsd("Screenshot Capture Failed");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
