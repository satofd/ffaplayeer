using System;
using NAudio.Wave;

namespace FFmPlayer.Services;

public class AudioPlayer : IDisposable
{
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _bufferedWaveProvider;
    private long _bytesPlayed;
    
    // NAudio may call PlaybackStopped on its own thread
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public void Init(int sampleRate, int channels)
    {
        Dispose();

        _waveOut = new WaveOutEvent();
        _waveOut.PlaybackStopped += OnPlaybackStopped;
        
        _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, channels))
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5) // Enough for some lead/lag
        };

        _waveOut.Init(_bufferedWaveProvider);
        _bytesPlayed = 0;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        PlaybackStopped?.Invoke(this, e);
    }

    public void AddSamples(byte[] buffer, int offset, int count)
    {
        _bufferedWaveProvider?.AddSamples(buffer, offset, count);
    }

    public void Play()
    {
        _waveOut?.Play();
    }

    public void Pause()
    {
        _waveOut?.Pause();
    }

    public void Stop()
    {
        _waveOut?.Stop();
    }

    public void ClearBuffer()
    {
        _bufferedWaveProvider?.ClearBuffer();
        _bytesPlayed = 0; // We will track manually or reset our base
    }

    public void SetVolume(float volume)
    {
        if (_waveOut != null)
        {
            _waveOut.Volume = volume;
        }
    }

    // NAudio reports GetPosition as bytes played since Play started (or rather, bytes passed to driver)
    public double GetPlayedSeconds()
    {
        if (_waveOut == null || _bufferedWaveProvider == null) return 0;
        
        // This is safe to use for relative offsets
        long positionBytes = _waveOut.GetPosition();
        
        return (double)positionBytes / _bufferedWaveProvider.WaveFormat.AverageBytesPerSecond;
    }

    public void Dispose()
    {
        if (_waveOut != null)
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            _waveOut.Dispose();
            _waveOut = null;
        }
        _bufferedWaveProvider = null;
    }
}
