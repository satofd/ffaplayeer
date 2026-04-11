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

    private class VideoFrameData
    {
        public double Pts { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public bool IsEndOfStream { get; set; }
    }
    
    // UIに即時表示できるよう、デコードされた映像フレームを一時的に溜めておくためのスレッドセーフなキュー
    private ConcurrentQueue<VideoFrameData> _videoFrames = new();

    // ユーザーがシークや再生操作を行った際、映像と音声を同期させるための「基本となる基準時間（秒）」
    private double _baseSeconds = 0;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isDraggingSlider;

    public bool IsStopped => !IsPlaying && !IsPaused;

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
    private double _volume = 1.0;

    [ObservableProperty]
    private bool _isMuted = false;

    [ObservableProperty]
    private double _playbackSpeed = 1.0;

    [ObservableProperty]
    private WriteableBitmap? _videoFrameBitmap;

    public System.Collections.ObjectModel.ObservableCollection<string> Playlist { get; } = new();

    [ObservableProperty]
    private string? _currentPlaylistItem;

    [ObservableProperty]
    private int _currentPlaylistIndex = -1;

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
    public Action? OpenFileAction { get; set; }
    public Action? OpenUrlAction { get; set; }
    public Action? ShowMediaInfoAction { get; set; }
    public Action? ToggleFullscreenAction { get; set; }
    public Action? ExitFullscreenAction { get; set; }
    
    public AppSettings Settings => _settings;

    public MainViewModel(SettingsService settingsService, AppSettings settings)
    {
        _settingsService = settingsService;
        _settings = settings;
        Volume = _settings.Volume;
        IsMuted = _settings.IsMuted;
        PlaybackSpeed = _settings.PlaybackSpeed;
    }

    [RelayCommand]
    public void OpenFile()
    {
        OpenFileAction?.Invoke();
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
        // Implement Seeking and Step
        if (shortcut == _settings.ShortcutSeekForward1s) { RequestSeek(Position + 1); return true; }
        if (shortcut == _settings.ShortcutSeekBackward1s) { RequestSeek(Position - 1); return true; }
        
        if (shortcut == _settings.ShortcutToggleMute) { IsMuted = !IsMuted; return true; }
        if (shortcut == _settings.ShortcutToggleFullscreen) { ToggleFullscreenAction?.Invoke(); return true; }
        if (shortcut == _settings.ShortcutExitFullscreen) { ExitFullscreenAction?.Invoke(); return true; }
        if (shortcut == _settings.ShortcutOpenFile) { OpenFile(); return true; }
        if (shortcut == _settings.ShortcutOpenUrl) { OpenUrlAction?.Invoke(); return true; }
        if (shortcut == _settings.ShortcutShowPlaylist) { OpenPlaylist(); return true; }
        if (shortcut == _settings.ShortcutShowMediaInfo) { ShowMediaInfoAction?.Invoke(); return true; }
        if (shortcut == _settings.ShortcutIncreaseSpeed) { IncreaseSpeed(); return true; }
        if (shortcut == _settings.ShortcutDecreaseSpeed) { DecreaseSpeed(); return true; }
        if (shortcut == _settings.ShortcutResetSpeed) { PlaybackSpeed = 1.0; return true; }

        if (key == Avalonia.Input.Key.Right && modifiers == Avalonia.Input.KeyModifiers.Alt) { StepForward(); return true; }
        if (key == Avalonia.Input.Key.Left && modifiers == Avalonia.Input.KeyModifiers.Alt) { StepBackward(); return true; }

        return false;
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
        
        if (_videoFrames.TryDequeue(out var cachedFrame)) {
            if (!cachedFrame.IsEndOfStream) {
                Position = cachedFrame.Pts;
                UpdateVideoBitmap(cachedFrame);
            }
        }
    }

    /// <summary>
    /// AvalonUIのWriteableBitmapのメモリ空間をロックし、バックグラウンドでデコードされた生の映像byte配列をコピーして画面を更新します。
    /// （UIスレッドでのみ実行される想定）
    /// </summary>
    private void UpdateVideoBitmap(VideoFrameData frame)
    {
        if (VideoFrameBitmap != null)
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
    /// 現在の再生位置からコマ戻し（少し前の時間にシーク）を行います。
    /// 現状は簡易的にわずかな時間（0.05秒）だけ巻き戻し、一時停止状態へ移行します。
    /// </summary>
    [RelayCommand]
    public void StepBackward()
    {
        // For backwards step, generally need to seek a bit backwards and decode up to current frame - 1.
        // It's very complex with FFmpeg, so naive approach:
        RequestSeek(Math.Max(0, Position - 0.05)); 
        IsPaused = true;
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
        
        _videoFrames = new ConcurrentQueue<VideoFrameData>();

        _decodeCts = new CancellationTokenSource();
        _decodeTask = Task.Run(() => DecodeLoopAsync(_decodeCts.Token));
        _renderTask = Task.Run(() => VideoRenderLoopAsync(_decodeCts.Token));
        
        _audioPlayer.Play();
        OnPropertyChanged(nameof(IsStopped));
    }

    private double _seekRequestTime = -1;
    private bool _needsPreviewFrame = false;

    /// <summary>
    /// バックグラウンドでFFmpegからメディアパケットを連続で読み込み、デコードするループ処理です。
    /// デコードした映像フレームは _videoFrames キューに一時保存され、音声フレームはそのまま NAudio に渡されます。
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
                    while (_videoFrames.TryDequeue(out _)) { } 
                    
                    // シーク処理直後であることを記録し、強制的に次のフレームを画面に描画させるフラグをオンにする
                    targetSeekTimeAfterFlush = target;
                    _needsPreviewFrame = true;
                    continue;
                }

                // デコーダが存在しないか、一時停止中かつ既に十分なフレーム（5枚以上）が溜まっているなら休止
                // (一時停止中も描画のために数枚だけは読み込んでおく設計)
                if (_decoder == null || (IsPaused && _videoFrames.Count >= 5))
                {
                    await Task.Delay(50, token);
                    continue;
                }

                // 再生中で、十分な映像フレーム（設定値分）または音声バッファ（4秒以上）が既に溜まっているなら休止
                if (!IsPaused && (_videoFrames.Count >= _settings.FrameBufferSize || 
                    (_audioPlayer != null && _audioPlayer.GetBufferedSeconds() > 4.0)))
                {
                    await Task.Delay(10, token);
                    continue;
                }

                // FFmpegに次のフレームのデコードを行わせる
                var type = _decoder.TryDecodeNextFrame(out double pts, out byte[] data, out int strideOrSize);

                if (type == FFmpegDecoder.FrameType.EndOfStream)
                {
                    targetSeekTimeAfterFlush = -1;
                    _videoFrames.Enqueue(new VideoFrameData { IsEndOfStream = true });
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
                        Dispatcher.UIThread.Post(() => {
                            if (!IsDraggingSlider) Position = pts;
                        });
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
                    _videoFrames.Enqueue(new VideoFrameData { Pts = pts, Data = data });
                    
                    if (_needsPreviewFrame)
                    {
                        var previewData = data;
                        _needsPreviewFrame = false;
                        Dispatcher.UIThread.Post(() => {
                            if (IsPaused) UpdateVideoBitmap(new VideoFrameData { Pts = pts, Data = previewData });
                        });
                    }
                }
                else if (type == FFmpegDecoder.FrameType.Audio)
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
    /// デコードされてキューに溜まった映像フレーム (`_videoFrames`) を取り出し、
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
                if (_videoFrames.TryPeek(out var frame))
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

                    // 描画タイミングが来たため、初めてキューからフレームを取り出す
                    _videoFrames.TryDequeue(out _);

                    // 映像が音声より遅れすぎている場合は、このフレームは描画（表示）せずに破棄してA/V同期を合わせる
                    if (drift < -_settings.VideoDropLagThresholdSeconds)
                    {
                        continue; // Drop frame
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
            OsdMessage = "";
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
    }

    partial void OnDurationChanged(double value)
    {
        TimeDuration = TimeSpan.FromSeconds(value);
        if (_decoder != null)
        {
            MaxFrame = (long)(value * _decoder.Framerate);
        }
    }

    [RelayCommand]
    public void OpenSettings()
    {
        ShowSettingsWindowAction?.Invoke();
    }

    [RelayCommand]
    public void OpenUrl()
    {
        OpenUrlAction?.Invoke();
    }

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

    [RelayCommand]
    public void ShowMediaInfo()
    {
        ShowMediaInfoAction?.Invoke();
    }

    [RelayCommand]
    public void OpenPlaylist()
    {
        ShowPlaylistWindowAction?.Invoke();
    }

    /// <summary>
    /// 動画のシーク要求を行います。UIなどから時間（秒）を受け取り、バックグラウンドのデコードループへ通知します。
    /// </summary>
    /// <param name="seconds">ジャンプ先の時間（秒）</param>
    public void RequestSeek(double seconds)
    {
        if (_decoder == null) return;
        Interlocked.Exchange(ref _seekRequestTime, seconds);
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
            return;
        }

        if (IsPlaying)
        {
            IsPlaying = false;
            IsPaused = true;
            _audioPlayer?.Pause();
        }
        else
        {
            IsPlaying = true;
            IsPaused = false;
            _audioPlayer?.Play();
        }
        OnPropertyChanged(nameof(IsStopped));
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
        
        _videoFrames = new ConcurrentQueue<VideoFrameData>();

        OnPropertyChanged(nameof(IsStopped));
    }

    /// <summary>
    /// コンストラクタで初期化された設定値などに基づき、再生速度を0.1倍ずつ上げます（最大3.0倍）。
    /// </summary>
    [RelayCommand]
    public void IncreaseSpeed() => PlaybackSpeed = Math.Clamp(PlaybackSpeed + 0.1, 0.1, 3.0);

    /// <summary>
    /// コンストラクタで初期化された設定値などに基づき、再生速度を0.1倍ずつ下げます（最低0.1倍）。
    /// </summary>
    [RelayCommand]
    public void DecreaseSpeed() => PlaybackSpeed = Math.Clamp(PlaybackSpeed - 0.1, 0.1, 3.0);

    /// <summary>
    /// プレイリストにおける次のメディア（曲や動画）へ手動でスキップします。
    /// </summary>
    [RelayCommand]
    public void PlayListNext()
    {
        AdvancePlaylist();
    }
    
    /// <summary>
    /// プレイリストにおける前のメディアへ戻ります。ランダム、ループなどの再生モード設定（PlaybackMode）を加味して対象を決定します。
    /// </summary>
    [RelayCommand]
    public void PlayListPrev()
    {
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
    }

    partial void OnIsMutedChanged(bool value)
    {
        _audioPlayer?.SetVolume(value ? 0 : (float)Volume);
    }

    /// <summary>
    /// 現在の音量、ミュート状態、再生オプションなどを AppSettings クラスのプロパティへ反映し、ディスク（設定ファイル）へ保存します。
    /// </summary>
    public void SaveSettings()
    {
        _settings.Volume = Volume;
        _settings.IsMuted = IsMuted;
        _settings.PlaybackSpeed = PlaybackSpeed;
        _settingsService.Save(_settings);
    }

    public void Dispose()
    {
        Stop();
    }
}
