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
    
    // For Video Conversion
    private AVFrame* _dstVideoFrame;
    private byte* _dstVideoData;
    
    // For Audio Conversion
    private byte* _dstAudioData;
    private int _dstAudioSamples;
    
    public double Duration { get; private set; }
    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }
    public double Framerate { get; private set; }
    public AVRational VideoTimeBase { get; private set; }
    
    public int AudioSampleRate { get; private set; } = 44100;
    public int AudioChannels { get; private set; } = 2;
    public AVRational AudioTimeBase { get; private set; }
    
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
                Logger.Error("Cannot find stream info");
                return false;
            }

            Duration = _formatContext->duration / (double)ffmpeg.AV_TIME_BASE;

            // Find Video stream
            _videoStreamIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (_videoStreamIndex >= 0)
            {
                var videoStream = _formatContext->streams[_videoStreamIndex];
                VideoTimeBase = videoStream->time_base;
                var videoCodec = ffmpeg.avcodec_find_decoder(videoStream->codecpar->codec_id);
                _videoCodecContext = ffmpeg.avcodec_alloc_context3(videoCodec);
                ffmpeg.avcodec_parameters_to_context(_videoCodecContext, videoStream->codecpar);
                ffmpeg.avcodec_open2(_videoCodecContext, videoCodec, null);
                
                VideoWidth = _videoCodecContext->width;
                VideoHeight = _videoCodecContext->height;
                Framerate = ffmpeg.av_q2d(videoStream->avg_frame_rate);
                
                _swsContext = ffmpeg.sws_getContext(
                    VideoWidth, VideoHeight, _videoCodecContext->pix_fmt,
                    VideoWidth, VideoHeight, AVPixelFormat.AV_PIX_FMT_BGRA,
                    1, null, null, null); // 1 = SWS_FAST_BILINEAR
                    
                _dstVideoFrame = ffmpeg.av_frame_alloc();
                int numBytes = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGRA, VideoWidth, VideoHeight, 1);
                _dstVideoData = (byte*)ffmpeg.av_malloc((ulong)numBytes);
                _dstVideoFrame->data[0] = _dstVideoData;
                _dstVideoFrame->linesize[0] = VideoWidth * 4;
            }

            // Find Audio stream
            _audioStreamIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, _videoStreamIndex, null, 0);
            if (_audioStreamIndex >= 0)
            {
                var audioStream = _formatContext->streams[_audioStreamIndex];
                AudioTimeBase = audioStream->time_base;
                var audioCodec = ffmpeg.avcodec_find_decoder(audioStream->codecpar->codec_id);
                _audioCodecContext = ffmpeg.avcodec_alloc_context3(audioCodec);
                ffmpeg.avcodec_parameters_to_context(_audioCodecContext, audioStream->codecpar);
                ffmpeg.avcodec_open2(_audioCodecContext, audioCodec, null);
                
                _dstAudioSamples = 192000;
                _dstAudioData = (byte*)ffmpeg.av_malloc((ulong)(_dstAudioSamples * 2 * AudioChannels));
                AVChannelLayout outLayout;
                ffmpeg.av_channel_layout_default(&outLayout, AudioChannels);
                
                SwrContext* swrCtx = ffmpeg.swr_alloc();
                ffmpeg.swr_alloc_set_opts2(&swrCtx, 
                    &outLayout, AVSampleFormat.AV_SAMPLE_FMT_S16, AudioSampleRate,
                    &_audioCodecContext->ch_layout, _audioCodecContext->sample_fmt, _audioCodecContext->sample_rate, 
                    0, null);
                _swrContext = swrCtx;
                
                ffmpeg.swr_init(_swrContext);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Exception during initialize FFmpegDecoder", ex);
            return false;
        }
    }

    public void RequestSeek(double seconds)
    {
        if (_formatContext == null) return;
        long targetPts = (long)(seconds * ffmpeg.AV_TIME_BASE);
        ffmpeg.avformat_seek_file(_formatContext, -1, long.MinValue, targetPts, long.MaxValue, ffmpeg.AVSEEK_FLAG_BACKWARD);
        if (_videoCodecContext != null) ffmpeg.avcodec_flush_buffers(_videoCodecContext);
        if (_audioCodecContext != null) ffmpeg.avcodec_flush_buffers(_audioCodecContext);
    }

    public enum FrameType { None, Video, Audio, Error, EndOfStream }

    // Returns frame data and Type
    public FrameType TryDecodeNextFrame(out double pts, out byte[] data, out int strideOrCount)
    {
        pts = 0;
        data = Array.Empty<byte>();
        strideOrCount = 0;
        
        if (_formatContext == null) return FrameType.None;

        while (true)
        {
            int ret = ffmpeg.av_read_frame(_formatContext, _packet);
            if (ret == ffmpeg.AVERROR_EOF) return FrameType.EndOfStream;
            if (ret < 0) return FrameType.Error;

            if (_packet->stream_index == _videoStreamIndex)
            {
                ret = ffmpeg.avcodec_send_packet(_videoCodecContext, _packet);
                ffmpeg.av_packet_unref(_packet);
                if (ret < 0) continue;

                ret = ffmpeg.avcodec_receive_frame(_videoCodecContext, _frame);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF) continue;
                if (ret < 0) return FrameType.Error;

                pts = _frame->best_effort_timestamp * ffmpeg.av_q2d(VideoTimeBase);
                
                ffmpeg.sws_scale(_swsContext,
                    _frame->data, _frame->linesize, 0, _frame->height,
                    _dstVideoFrame->data, _dstVideoFrame->linesize);

                int size = VideoWidth * VideoHeight * 4;
                data = new byte[size];
                Marshal.Copy((IntPtr)_dstVideoData, data, 0, size);
                strideOrCount = VideoWidth * 4;
                
                ffmpeg.av_frame_unref(_frame);
                return FrameType.Video;
            }
            else if (_packet->stream_index == _audioStreamIndex)
            {
                ret = ffmpeg.avcodec_send_packet(_audioCodecContext, _packet);
                ffmpeg.av_packet_unref(_packet);
                if (ret < 0) continue;

                ret = ffmpeg.avcodec_receive_frame(_audioCodecContext, _frame);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF) continue;
                if (ret < 0) return FrameType.Error;

                pts = _frame->best_effort_timestamp * ffmpeg.av_q2d(AudioTimeBase);
                
                byte*[] outData = { _dstAudioData };
                int outSamples = 0;
                fixed (byte** pOutData = outData)
                {
                    byte** pInData = (byte**)&_frame->data;
                    outSamples = ffmpeg.swr_convert(_swrContext, 
                        pOutData, _dstAudioSamples,
                        pInData, _frame->nb_samples);
                }

                if (outSamples > 0)
                {
                    int size = outSamples * AudioChannels * 2; // 16-bit
                    data = new byte[size];
                    Marshal.Copy((IntPtr)_dstAudioData, data, 0, size);
                    strideOrCount = size;
                }
                
                ffmpeg.av_frame_unref(_frame);
                return FrameType.Audio;
            }
            else
            {
                ffmpeg.av_packet_unref(_packet);
            }
        }
    }

    public void Dispose()
    {
        if (_formatContext != null)
        {
            fixed (AVFormatContext** pFormatContext = &_formatContext)
            {
                ffmpeg.avformat_close_input(pFormatContext);
            }
            _formatContext = null;
        }

        if (_videoCodecContext != null)
        {
            fixed (AVCodecContext** ctx = &_videoCodecContext) ffmpeg.avcodec_free_context(ctx);
        }

        if (_audioCodecContext != null)
        {
            fixed (AVCodecContext** ctx = &_audioCodecContext) ffmpeg.avcodec_free_context(ctx);
        }

        if (_swsContext != null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }

        if (_swrContext != null)
        {
            fixed (SwrContext** ctx = &_swrContext) ffmpeg.swr_free(ctx);
        }

        if (_frame != null)
        {
            fixed (AVFrame** frame = &_frame) ffmpeg.av_frame_free(frame);
        }

        if (_packet != null)
        {
            fixed (AVPacket** packet = &_packet) ffmpeg.av_packet_free(packet);
        }
        
        if (_dstVideoFrame != null)
        {
            fixed (AVFrame** frame = &_dstVideoFrame) ffmpeg.av_frame_free(frame);
        }
        if (_dstVideoData != null)
        {
            ffmpeg.av_free(_dstVideoData);
            _dstVideoData = null;
        }
        if (_dstAudioData != null)
        {
            ffmpeg.av_free(_dstAudioData);
            _dstAudioData = null;
        }
    }
}
