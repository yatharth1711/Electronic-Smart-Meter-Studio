namespace SmartMeterStudio.Protocol.Hdlc;

/// <summary>Reassembles complete flag-delimited HDLC frames from arbitrary TCP or serial byte chunks.</summary>
public sealed class HdlcFrameStreamDecoder
{
    private const int MaximumWireLength = 2050;
    private readonly List<byte> _pending = [];
    private bool _insideFrame;

    public IReadOnlyList<HdlcFrame> Feed(ReadOnlySpan<byte> bytes)
    {
        var frames = new List<HdlcFrame>();
        foreach (var item in bytes)
        {
            if (item == HdlcFrameCodec.Flag)
            {
                if (!_insideFrame)
                {
                    _insideFrame = true;
                    _pending.Clear();
                    _pending.Add(item);
                    continue;
                }

                _pending.Add(item);
                if (_pending.Count > 2 && HdlcFrameCodec.TryDecode(_pending.ToArray(), out var frame, out _))
                    frames.Add(frame!);
                _pending.Clear();
                _pending.Add(item); // A closing flag can also begin the next frame.
                continue;
            }

            if (_insideFrame)
            {
                _pending.Add(item);
                if (_pending.Count > MaximumWireLength)
                {
                    _pending.Clear();
                    _insideFrame = false;
                }
            }
        }
        return frames;
    }

    public void Reset() { _pending.Clear(); _insideFrame = false; }
}
