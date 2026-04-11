using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmPlayer.Models;
using FFmPlayer.Services;

namespace FFmPlayer.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;

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

    public Action? ShowSettingsWindowAction { get; set; }
    public Action? ShowPlaylistWindowAction { get; set; }

    public MainViewModel(SettingsService settingsService, AppSettings settings)
    {
        _settingsService = settingsService;
        _settings = settings;
        Volume = _settings.Volume;
        IsMuted = _settings.IsMuted;
        PlaybackSpeed = _settings.PlaybackSpeed;
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

    [RelayCommand]
    public void PlayPause()
    {
        if (IsPlaying)
        {
            IsPlaying = false;
            IsPaused = true;
        }
        else
        {
            IsPlaying = true;
            IsPaused = false;
        }
        OnPropertyChanged(nameof(IsStopped));
    }

    [RelayCommand]
    public void Stop()
    {
        IsPlaying = false;
        IsPaused = false;
        Position = 0;
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

    public void SaveSettings()
    {
        _settings.Volume = Volume;
        _settings.IsMuted = IsMuted;
        _settings.PlaybackSpeed = PlaybackSpeed;
        _settingsService.Save(_settings);
    }

    public void Dispose()
    {
        // Cleanup resources
    }
}
