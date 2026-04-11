using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
    private CancellationTokenSource? _decodeCts;
    private Task? _decodeTask;

    private double _baseSeconds = 0;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    public bool IsStopped => !IsPlaying && !IsPaused;

    [ObservableProperty]
    private double _position;

    [ObservableProperty]
    private double _duration;

    [ObservableProperty]
    private string _osdMessage = string.Empty;

    [ObservableProperty]
    private double _volume = 1.0;

    [ObservableProperty]
    private bool _isMuted = false;

    [ObservableProperty]
    private double _playbackSpeed = 1.0;

    [ObservableProperty]
    private WriteableBitmap? _videoFrameBitmap;

    public Action? ShowSettingsWindowAction { get; set; }
    public Action? ShowPlaylistWindowAction { get; set; }
    public Action? OpenFileAction { get; set; }

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
        if (shortcut == _settings.ShortcutToggleFullscreen) { /* TODO */ return true; }
        if (shortcut == _settings.ShortcutExitFullscreen) { /* TODO */ return true; }
        if (shortcut == _settings.ShortcutOpenFile) { OpenFile(); return true; }
        if (shortcut == _settings.ShortcutOpenUrl) { /* TODO */ return true; }
        if (shortcut == _settings.ShortcutShowPlaylist) { OpenPlaylist(); return true; }
        if (shortcut == _settings.ShortcutShowMediaInfo) { /* TODO */ return true; }
        if (shortcut == _settings.ShortcutIncreaseSpeed) { IncreaseSpeed(); return true; }
        if (shortcut == _settings.ShortcutDecreaseSpeed) { DecreaseSpeed(); return true; }
        if (shortcut == _settings.ShortcutResetSpeed) { PlaybackSpeed = 1.0; return true; }

        if (key == Avalonia.Input.Key.Right && modifiers == Avalonia.Input.KeyModifiers.Alt) { StepForward(); return true; }
        if (key == Avalonia.Input.Key.Left && modifiers == Avalonia.Input.KeyModifiers.Alt) { StepBackward(); return true; }

        return false;
    }

    [RelayCommand]
    public void StepForward()
    {
        if (_decoder == null) return;
        IsPlaying = true;
        IsPaused = true;
        _audioPlayer?.Pause();
        
        // Synchronously read until we get a video frame
        while (true)
        {
            var type = _decoder.TryDecodeNextFrame(out double pts, out byte[] data, out _);
            if (type == FFmpegDecoder.FrameType.EndOfStream || type == FFmpegDecoder.FrameType.Error) break;
            if (type == FFmpegDecoder.FrameType.Video)
            {
                Position = pts;
                if (VideoFrameBitmap != null)
                {
                    using var fb = VideoFrameBitmap.Lock();
                    Marshal.Copy(data, 0, fb.Address, data.Length);
                }
                break;
            }
        }
    }

    [RelayCommand]
    public void StepBackward()
    {
        // For backwards step, generally need to seek a bit backwards and decode up to current frame - 1.
        // It's very complex with FFmpeg, so naive approach:
        RequestSeek(Math.Max(0, Position - 0.05)); 
        IsPaused = true;
    }

    public void LoadMedia(string url)
    {
        Stop();

        _decoder = new FFmpegDecoder();
        if (!_decoder.Initialize(url))
        {
            ShowOsd($"Cannot open: {url}");
            _decoder.Dispose();
            _decoder = null;
            return;
        }

        Duration = _decoder.Duration;
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
        
        _decodeCts = new CancellationTokenSource();
        _decodeTask = Task.Run(() => DecodeLoopAsync(_decodeCts.Token));
        
        _audioPlayer.Play();
        OnPropertyChanged(nameof(IsStopped));
    }

    private async Task DecodeLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (IsPaused || _decoder == null)
                {
                    await Task.Delay(50, token);
                    continue;
                }

                var type = _decoder.TryDecodeNextFrame(out double pts, out byte[] data, out int strideOrSize);

                if (type == FFmpegDecoder.FrameType.EndOfStream)
                {
                    Dispatcher.UIThread.Post(() => Stop());
                    break;
                }
                
                if (type == FFmpegDecoder.FrameType.Video)
                {
                    double audioMasterSec = _baseSeconds + _audioPlayer!.GetPlayedSeconds() * PlaybackSpeed;
                    double drift = pts - audioMasterSec;

                    if (drift > _settings.VideoLeadSleepThresholdSeconds)
                    {
                        await Task.Delay((int)(drift * 1000 / PlaybackSpeed), token);
                    }
                    else if (drift < -_settings.VideoDropLagThresholdSeconds)
                    {
                        continue; // Drop frame
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        Position = pts;
                        if (VideoFrameBitmap != null)
                        {
                            using var fb = VideoFrameBitmap.Lock();
                            Marshal.Copy(data, 0, fb.Address, data.Length);
                        }
                    });
                }
                else if (type == FFmpegDecoder.FrameType.Audio)
                {
                    // If mute is toggled, it's handled via SetVolume, but we still feed audio to keep clock moving
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

    private void ShowOsd(string msg)
    {
        OsdMessage = msg;
        // Ideally start a timer to clear after 3 seconds
        Task.Delay(3000).ContinueWith(_ => Dispatcher.UIThread.Post(() => OsdMessage = ""));
    }

    [RelayCommand]
    public void OpenSettings()
    {
        ShowSettingsWindowAction?.Invoke();
    }

    [RelayCommand]
    public void OpenPlaylist()
    {
        ShowPlaylistWindowAction?.Invoke();
    }

    public void RequestSeek(double seconds)
    {
        if (_decoder == null || !IsPlaying) return;

        bool wasPaused = IsPaused;
        IsPaused = true;
        
        // Wait for decoder to pause naturally or just force it
        _decoder.RequestSeek(seconds);
        _audioPlayer?.ClearBuffer();
        _baseSeconds = seconds;
        
        // In precise sync, we need to know how much audio we've pushed previously, so reset audio clock
        _audioPlayer?.ResetClock();
        Position = seconds;

        if (!wasPaused) IsPaused = false;
    }

    [RelayCommand]
    public void PlayPause()
    {
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

        OnPropertyChanged(nameof(IsStopped));
    }

    [RelayCommand]
    public void IncreaseSpeed() => PlaybackSpeed = Math.Clamp(PlaybackSpeed + 0.1, 0.1, 3.0);

    [RelayCommand]
    public void DecreaseSpeed() => PlaybackSpeed = Math.Clamp(PlaybackSpeed - 0.1, 0.1, 3.0);

    [RelayCommand]
    public void PlayListNext()
    {
        // TODO
    }
    
    [RelayCommand]
    public void PlayListPrev()
    {
        // TODO
    }

    partial void OnVolumeChanged(double value)
    {
        if (!IsMuted) _audioPlayer?.SetVolume((float)value);
    }

    partial void OnIsMutedChanged(bool value)
    {
        _audioPlayer?.SetVolume(value ? 0 : (float)Volume);
    }

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
