using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace FFmPlayer.Services;

public unsafe class FFmpegDecoder : IDisposable
{
    private AVFormatContext* _formatContext;
    private AVCodecContext* _videoCodecContext;
    private AVCodecContext* _audioCodecContext;
    private int _videoStreamIndex = -1;
    private int _audioStreamIndex = -1;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private SwsContext* _swsContext;
    private SwrContext* _swrContext;
    
    public double Duration { get; private set; }
    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }
    public double Framerate { get; private set; }
    
    public int AudioSampleRate { get; private set; }
    public int AudioChannels { get; private set; }
    
    public bool Initialize(string url)
    {
        try
        {
            _frame = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();

            var pFormatContext = ffmpeg.avformat_alloc_context();
            if (ffmpeg.avformat_open_input(&pFormatContext, url, null, null) != 0)
            {
                Logger.Error($"Cannot open: {url}");
                return false;
            }
            _formatContext = pFormatContext;

            if (ffmpeg.avformat_find_stream_info(_formatContext, null) < 0)
            {
                Logger.Error($"Cannot find stream info: {url}");
                return false;
            }

            Duration = _formatContext->duration / (double)ffmpeg.AV_TIME_BASE;

            // Find Video stream
            _videoStreamIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (_videoStreamIndex >= 0)
            {
                var videoStream = _formatContext->streams[_videoStreamIndex];
                var videoCodec = ffmpeg.avcodec_find_decoder(videoStream->codecpar->codec_id);
                _videoCodecContext = ffmpeg.avcodec_alloc_context3(videoCodec);
                ffmpeg.avcodec_parameters_to_context(_videoCodecContext, videoStream->codecpar);
                ffmpeg.avcodec_open2(_videoCodecContext, videoCodec, null);
                
                VideoWidth = _videoCodecContext->width;
                VideoHeight = _videoCodecContext->height;
                Framerate = ffmpeg.av_q2d(videoStream->avg_frame_rate);
            }

            // Find Audio stream
            _audioStreamIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, _videoStreamIndex, null, 0);
            if (_audioStreamIndex >= 0)
            {
                var audioStream = _formatContext->streams[_audioStreamIndex];
                var audioCodec = ffmpeg.avcodec_find_decoder(audioStream->codecpar->codec_id);
                _audioCodecContext = ffmpeg.avcodec_alloc_context3(audioCodec);
                ffmpeg.avcodec_parameters_to_context(_audioCodecContext, audioStream->codecpar);
                ffmpeg.avcodec_open2(_audioCodecContext, audioCodec, null);
                
                AudioSampleRate = _audioCodecContext->sample_rate;
                AudioChannels = _audioCodecContext->ch_layout.nb_channels;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception during initialize FFmpegDecoder", ex);
            return false;
        }
    }

    public void RequestSeek(double seconds)
    {
        // To be implemented
    }

    public void Dispose()
    {
        // To be implemented
    }
}
