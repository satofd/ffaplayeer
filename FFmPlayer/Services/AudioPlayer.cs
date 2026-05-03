using System;
using System.Runtime.InteropServices;
using NAudio.Wave;
using SDL2;

namespace FFmPlayer.Services;

public class AudioPlayer : IDisposable
{
    private WaveOutEvent? _waveOut;
    private uint _deviceId = 0;
    private BufferedWaveProvider? _bufferedWaveProvider;
    private SDL.SDL_AudioCallback? _audioCallback;
    
    private long _totalBytesPlayed = 0;
    private long _bytesOffset = 0;
    private float _volume = 1.0f;

    public event EventHandler<EventArgs>? PlaybackStopped;

    public AudioPlayer()
    {
        if (!OperatingSystem.IsWindows())
        {
            if (SDL.SDL_Init(SDL.SDL_INIT_AUDIO) < 0)
            {
                throw new Exception("SDL Init Audio failed: " + SDL.SDL_GetError());
            }
        }
    }

    public void Init(int sampleRate, int channels)
    {
        DisposeDevice();

        _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, channels))
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5)
        };

        if (OperatingSystem.IsWindows())
        {
            _waveOut = new WaveOutEvent();
            _waveOut.PlaybackStopped += OnWaveOutPlaybackStopped;
            _waveOut.Init(_bufferedWaveProvider);
        }
        else
        {
            _audioCallback = new SDL.SDL_AudioCallback(AudioCallback);

            SDL.SDL_AudioSpec desired = new SDL.SDL_AudioSpec
            {
                freq = sampleRate,
                format = SDL.AUDIO_S16SYS,
                channels = (byte)channels,
                samples = 4096,
                callback = _audioCallback,
                userdata = IntPtr.Zero
            };

            _deviceId = SDL.SDL_OpenAudioDevice(null, 0, ref desired, out SDL.SDL_AudioSpec obtained, 0);
            
            if (_deviceId == 0)
            {
                throw new Exception("SDL OpenAudioDevice failed: " + SDL.SDL_GetError());
            }
        }
    }

    private void OnWaveOutPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    private void AudioCallback(IntPtr userdata, IntPtr stream, int len)
    {
        if (_bufferedWaveProvider == null)
        {
            unsafe
            {
                byte* ptr = (byte*)stream;
                for (int i = 0; i < len; i++)
                {
                    *ptr++ = 0;
                }
            }
            return;
        }
        
        byte[] buffer = new byte[len];
        int read = _bufferedWaveProvider.Read(buffer, 0, len);
        
        if (read > 0)
        {
            // Apply volume for 16-bit PCM
            if (Math.Abs(_volume - 1.0f) > 0.01f)
            {
                unsafe
                {
                    fixed (byte* pBuffer = buffer)
                    {
                        short* pSample = (short*)pBuffer;
                        int sampleCount = read / 2;
                        for (int i = 0; i < sampleCount; i++)
                        {
                            int sample = (int)(pSample[i] * _volume);
                            if (sample > short.MaxValue) sample = short.MaxValue;
                            if (sample < short.MinValue) sample = short.MinValue;
                            pSample[i] = (short)sample;
                        }
                    }
                }
            }

            Marshal.Copy(buffer, 0, stream, read);
            _totalBytesPlayed += read;
        }
        
        if (read < len)
        {
            unsafe
            {
                byte* ptr = (byte*)stream + read;
                for (int i = 0; i < len - read; i++)
                {
                    *ptr++ = 0;
                }
            }
        }
    }

    public void AddSamples(byte[] buffer, int offset, int count)
    {
        _bufferedWaveProvider?.AddSamples(buffer, offset, count);
    }

    public void Play()
    {
        if (OperatingSystem.IsWindows())
        {
            _waveOut?.Play();
        }
        else if (_deviceId != 0)
        {
            SDL.SDL_PauseAudioDevice(_deviceId, 0);
        }
    }

    public void Pause()
    {
        if (OperatingSystem.IsWindows())
        {
            _waveOut?.Pause();
        }
        else if (_deviceId != 0)
        {
            SDL.SDL_PauseAudioDevice(_deviceId, 1);
        }
    }

    public void Stop()
    {
        if (OperatingSystem.IsWindows())
        {
            _waveOut?.Stop();
        }
        else if (_deviceId != 0)
        {
            SDL.SDL_PauseAudioDevice(_deviceId, 1);
        }
        ClearBuffer();
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    public void ClearBuffer()
    {
        _bufferedWaveProvider?.ClearBuffer();
    }

    public void SetVolume(float volume)
    {
        _volume = Math.Max(0.0f, Math.Min(1.0f, volume));
        if (OperatingSystem.IsWindows() && _waveOut != null)
        {
            _waveOut.Volume = _volume;
        }
    }

    public double GetPlayedSeconds()
    {
        if (_bufferedWaveProvider == null) return 0;
        
        long positionBytes;
        if (OperatingSystem.IsWindows())
        {
            if (_waveOut == null) return 0;
            positionBytes = _waveOut.GetPosition() - _bytesOffset;
        }
        else
        {
            if (_deviceId == 0) return 0;
            positionBytes = _totalBytesPlayed - _bytesOffset;
        }
        return (double)positionBytes / _bufferedWaveProvider.WaveFormat.AverageBytesPerSecond;
    }

    public double GetBufferedSeconds()
    {
        if (_bufferedWaveProvider == null) return 0;
        return _bufferedWaveProvider.BufferedDuration.TotalSeconds;
    }

    public void ResetClock()
    {
        if (OperatingSystem.IsWindows() && _waveOut != null)
        {
            _bytesOffset = _waveOut.GetPosition();
        }
        else
        {
            _bytesOffset = _totalBytesPlayed;
        }
    }

    private void DisposeDevice()
    {
        if (OperatingSystem.IsWindows())
        {
            if (_waveOut != null)
            {
                _waveOut.PlaybackStopped -= OnWaveOutPlaybackStopped;
                _waveOut.Dispose();
                _waveOut = null;
            }
        }
        else
        {
            if (_deviceId != 0)
            {
                SDL.SDL_CloseAudioDevice(_deviceId);
                _deviceId = 0;
            }
        }
        
        _bufferedWaveProvider = null;
    }

    public void Dispose()
    {
        DisposeDevice();
        if (!OperatingSystem.IsWindows())
        {
            SDL.SDL_QuitSubSystem(SDL.SDL_INIT_AUDIO);
        }
    }
}
