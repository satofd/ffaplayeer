using System;
using System.Collections.Generic;

namespace FFmPlayer.Models;

public class VideoFrameData
{
    public double Pts { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public bool IsEndOfStream { get; set; }
}

public class FrameRingBuffer
{
    private readonly LinkedList<VideoFrameData> _frames = new();
    private LinkedListNode<VideoFrameData>? _unreadNode;
    private readonly object _lock = new();

    public int MaxFrames { get; set; }
    public bool MemoryLimitEnabled { get; set; }
    public int MemoryLimitMB { get; set; }

    public int UnreadCount
    {
        get
        {
            lock (_lock)
            {
                if (_unreadNode == null) return 0;
                int count = 1;
                var curr = _unreadNode.Next;
                while (curr != null)
                {
                    count++;
                    curr = curr.Next;
                }
                return count;
            }
        }
    }

    public void Enqueue(VideoFrameData frame)
    {
        lock (_lock)
        {
            bool wasEmpty = _unreadNode == null;
            _frames.AddLast(frame);
            if (wasEmpty && !frame.IsEndOfStream)
            {
                _unreadNode = _frames.Last;
            }
            EnforceLimits();
        }
    }

    public bool TryDequeue(out VideoFrameData frame)
    {
        lock (_lock)
        {
            if (_unreadNode != null)
            {
                frame = _unreadNode.Value;
                _unreadNode = _unreadNode.Next;
                return true;
            }
        }
        frame = null!;
        return false;
    }

    public bool TryPeek(out VideoFrameData frame)
    {
        lock (_lock)
        {
            if (_unreadNode != null)
            {
                frame = _unreadNode.Value;
                return true;
            }
        }
        frame = null!;
        return false;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _frames.Clear();
            _unreadNode = null;
        }
    }

    public VideoFrameData? StepBackward(double targetPts)
    {
        lock (_lock)
        {
            var node = _unreadNode?.Previous ?? _frames.Last;
            VideoFrameData? best = null;

            while (node != null)
            {
                if (node.Value.Pts <= targetPts)
                {
                    if (best == null || node.Value.Pts > best.Pts)
                    {
                        best = node.Value;
                    }
                }
                node = node.Previous;
            }

            if (best != null)
            {
                var targetNode = _frames.Find(best);
                if (targetNode != null)
                {
                    _unreadNode = targetNode.Next; // The unread pointer moves past this so that StepBackward consumes it? No, if we stepped back to best, the best should be shown immediately. The "unread pointer" should point to the frame AFTER best, so that play resumes smoothly. Actually StepBackward will typically be followed by drawing `best`.
                    return best;
                }
            }
            return null; // Missed
        }
    }

    private void EnforceLimits()
    {
        while (_frames.Count > MaxFrames && _frames.Count > 0)
        {
            if (_frames.First == _unreadNode)
            {
                // Unread portion reached max capacity limit? Wait, shouldn't drop unread frames if playing normally, but if queue is stalled it will drop.
                _unreadNode = _unreadNode?.Next;
            }
            _frames.RemoveFirst();
        }

        if (MemoryLimitEnabled && MemoryLimitMB > 0)
        {
            long maxBytes = (long)MemoryLimitMB * 1024 * 1024;
            while (GetTotalBytes() > maxBytes && _frames.Count > 0)
            {
                if (_frames.First == _unreadNode)
                {
                    _unreadNode = _unreadNode?.Next;
                }
                _frames.RemoveFirst();
            }
        }
    }

    private long GetTotalBytes()
    {
        long total = 0;
        foreach (var f in _frames)
        {
            if (f.Data != null) total += f.Data.Length;
        }
        return total;
    }
}
