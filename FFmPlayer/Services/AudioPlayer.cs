using System;
using NAudio.Wave;

namespace FFmPlayer.Services;

public class AudioPlayer : IDisposable
{
    // NAudio で実際に音声をスピーカーから出力するためのデバイスオブジェクト
    private WaveOutEvent? _waveOut;
    // デコードされた音声データ（バイト配列）を一時的に蓄え、再生に合わせて供給するバッファ
    private BufferedWaveProvider? _bufferedWaveProvider;
    
    // NAudio が独自のバックグラウンドスレッドで「再生が最後まで到達した（または停止された）」ことを通知するためのイベント
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    /// <summary>
    /// オーディオプレイヤーを初期化します。指定されたサンプルレートとチャンネル数で再生設定を構築します。
    /// </summary>
    public void Init(int sampleRate, int channels)
    {
        // 既に初期化されていた場合は古いリソースを安全に破棄する
        Dispose();

        _waveOut = new WaveOutEvent();
        _waveOut.PlaybackStopped += OnPlaybackStopped;
        
        // 16bit（2バイト）のPCM音声データとしてフォーマットを指定する
        _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, channels))
        {
            // バッファが溢れた（読み込みに対して再生が遅すぎる）場合は、古いデータを破弃して例外を防ぐ
            DiscardOnBufferOverflow = true,
            // 再生やシークの遅延・リードを許容するため、最大5秒分の音声を溜め込めるようにする
            BufferDuration = TimeSpan.FromSeconds(5)
        };

        // デバイスにバッファプロバイダを紐付けて準備完了とする
        _waveOut.Init(_bufferedWaveProvider);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // 外部（MainViewModelなど）へ停止イベントを中継する
        PlaybackStopped?.Invoke(this, e);
    }

    /// <summary>
    /// デコードされた生音声のバイトデータをバッファに追加します。
    /// </summary>
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

    /// <summary>
    /// バッファに溜まっている未再生の音声データを全て破棄します（シーク時などに使用）。
    /// </summary>
    public void ClearBuffer()
    {
        _bufferedWaveProvider?.ClearBuffer();
    }

    /// <summary>
    /// 音量を設定します（0.0 〜 1.0）。
    /// </summary>
    public void SetVolume(float volume)
    {
        if (_waveOut != null)
        {
            _waveOut.Volume = volume;
        }
    }

    // シークなどで「基準となる再生開始位置」が変更された際の、WaveOut デバイスのバイト数オフセット値
    private long _bytesOffset = 0;

    /// <summary>
    /// 再生開始（または最後の同期リセット）から、実際にスピーカーへ送られた音声データ量から「経過時間（秒）」を算出します。
    /// </summary>
    public double GetPlayedSeconds()
    {
        if (_waveOut == null || _bufferedWaveProvider == null) return 0;
        
        // デバイス出力の総バイト数から、シーク等でリセットした時点のバイト数を引いて「純粋な再生済みバイト数」を求める
        long positionBytes = _waveOut.GetPosition() - _bytesOffset;
        
        // 1秒あたりの平均バイト数（サンプルレート × ビット深度/8 × チャンネル数）で割り、秒数に変換する
        return (double)positionBytes / _bufferedWaveProvider.WaveFormat.AverageBytesPerSecond;
    }

    /// <summary>
    /// 現在バッファに先読みされて溜まっている未再生音声の時間（秒）を返します。
    /// </summary>
    public double GetBufferedSeconds()
    {
        if (_bufferedWaveProvider == null) return 0;
        return _bufferedWaveProvider.BufferedDuration.TotalSeconds;
    }

    /// <summary>
    /// 音声の再生経過時間（GetPlayedSeconds）を0秒にリセットします。シークした直後などに呼ばれます。
    /// </summary>
    public void ResetClock()
    {
        // 現在までに消費した総バイト数をオフセットに記録することで、以降の GetPosition() - _bytesOffset は 0 から開始される
        if (_waveOut != null)
        {
            _bytesOffset = _waveOut.GetPosition();
        }
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
