using System;
using FFmpeg.AutoGen;
using System.Runtime.InteropServices;

class Program
{
    static unsafe void Main()
    {
        Console.WriteLine("Starting full FFmpeg test...");
        ffmpeg.RootPath = "/opt/homebrew/lib";
        string url = "/Users/isis/data/git/ffaplayeer/FFmPlayer/dummy.mp4";
        
        var pFormatContext = ffmpeg.avformat_alloc_context();
        AVDictionary* options = null;
        ffmpeg.avformat_open_input(&pFormatContext, url, null, &options);
        ffmpeg.avformat_find_stream_info(pFormatContext, null);
        
        int videoStreamIndex = ffmpeg.av_find_best_stream(pFormatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
        
        if (videoStreamIndex >= 0)
        {
            var videoStream = pFormatContext->streams[videoStreamIndex];
            var videoCodec = ffmpeg.avcodec_find_decoder(videoStream->codecpar->codec_id);
            var videoCodecContext = ffmpeg.avcodec_alloc_context3(videoCodec);
            ffmpeg.avcodec_parameters_to_context(videoCodecContext, videoStream->codecpar);
            ffmpeg.avcodec_open2(videoCodecContext, videoCodec, null);
            
            var swsContext = ffmpeg.sws_getContext(
                videoCodecContext->width, videoCodecContext->height, videoCodecContext->pix_fmt,
                videoCodecContext->width, videoCodecContext->height, AVPixelFormat.AV_PIX_FMT_BGRA,
                1, null, null, null);
                
            var dstVideoFrame = ffmpeg.av_frame_alloc();
            int numBytes = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGRA, videoCodecContext->width, videoCodecContext->height, 1);
            byte* dstVideoData = (byte*)ffmpeg.av_malloc((ulong)numBytes);
            dstVideoFrame->data[0] = dstVideoData;
            dstVideoFrame->linesize[0] = videoCodecContext->width * 4;
            
            var frame = ffmpeg.av_frame_alloc();
            var packet = ffmpeg.av_packet_alloc();
            
            Console.WriteLine("Reading frames...");
            while (ffmpeg.av_read_frame(pFormatContext, packet) >= 0)
            {
                if (packet->stream_index == videoStreamIndex)
                {
                    ffmpeg.avcodec_send_packet(videoCodecContext, packet);
                    if (ffmpeg.avcodec_receive_frame(videoCodecContext, frame) >= 0)
                    {
                        Console.WriteLine("Scaling frame...");
                        ffmpeg.sws_scale(swsContext,
                            frame->data, frame->linesize, 0, frame->height,
                            dstVideoFrame->data, dstVideoFrame->linesize);
                        Console.WriteLine("Scaled successfully!");
                        break;
                    }
                }
                ffmpeg.av_packet_unref(packet);
            }
        }
        Console.WriteLine("Test completed successfully.");
    }
}
