namespace SmartMeterStudio.Protocol.Hdlc;

/// <summary>One decoded IEC 62056-46 style HDLC frame, excluding the 0x7E flags.</summary>
public sealed class HdlcFrame
{
    public HdlcFrame(uint destinationAddress, uint sourceAddress, byte control, ReadOnlySpan<byte> information = default)
    {
        DestinationAddress = destinationAddress;
        SourceAddress = sourceAddress;
        Control = control;
        Information = information.ToArray();
    }

    public uint DestinationAddress { get; }
    public uint SourceAddress { get; }
    public byte Control { get; }
    public byte[] Information { get; }
    public bool IsInformationFrame => (Control & 0x01) == 0;
    public bool IsSetNormalResponseMode => Control == 0x93;
    public bool IsUnnumberedAcknowledgement => Control == 0x73;
    public bool IsDisconnect => Control == 0x53;
    public bool IsDisconnectedMode => Control == 0x1F;
}

public sealed class HdlcProtocolException(string message) : Exception(message);
