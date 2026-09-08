using System.Text;
using SmartMeterStudio.Core.Models;
using SmartMeterStudio.Core.Simulation;
using SmartMeterStudio.Protocol.Dlms;

namespace SmartMeterStudio.Protocol.Server;

/// <summary>DLMS Image transfer IC 18 adapter. The image is retained only as bounded simulator data and is never executed.</summary>
internal static class ImageTransferObject
{
    public const string LogicalName = "0.0.44.0.0.255";
    private const int BlockSize = 1024;

    public static DlmsDataValue Read(SmartMeterFleet fleet, DlmsAssociationContext association, byte attributeId)
    {
        var firmware = Require(fleet, association.MeterId);
        return attributeId switch
        {
            1 => DlmsDataValue.Octets(DlmsLogicalName.Parse(LogicalName).ToArray()),
            2 => new(DlmsDataType.DoubleLongUnsigned, (uint)BlockSize),
            3 => BlockStatus(firmware),
            4 => new(DlmsDataType.DoubleLongUnsigned, (uint)FirstMissingBlock(firmware)),
            5 => new(DlmsDataType.Boolean, true),
            6 => new(DlmsDataType.Enum, TransferState(firmware.State)),
            7 => ActivationInfo(firmware),
            _ => throw new KeyNotFoundException("Image transfer attribute does not exist.")
        };
    }

    public static void Invoke(SmartMeterFleet fleet, DlmsAssociationContext association, byte methodId, DlmsDataValue? parameter)
    {
        switch (methodId)
        {
            case 1: Initiate(fleet, association.MeterId, parameter); return;
            case 2: TransferBlock(fleet, association.MeterId, parameter); return;
            case 3: RequireZero(parameter); Ensure(fleet.VerifyFirmware(association.MeterId)); return;
            case 4: RequireZero(parameter); Ensure(fleet.ActivateFirmware(association.MeterId)); return;
            default: throw new KeyNotFoundException("Image transfer method does not exist.");
        }
    }

    public static CosemObject Definition() => new(LogicalName, "Image transfer", 18, 0, false, "DLMS Image transfer IC 18 adapter",
        [new(1, "logical_name", "octet-string", false, LogicalName), new(2, "image_block_size", "double-long-unsigned", false, BlockSize),
         new(3, "image_transferred_blocks_status", "bit-string", false, null), new(4, "image_first_not_transferred_block_number", "double-long-unsigned", false, null),
         new(5, "image_transfer_enabled", "boolean", false, true), new(6, "image_transfer_status", "enum", false, null), new(7, "image_to_activate_info", "array", false, null)],
        [new(1, "image_transfer_initiate", true, "structure", "Image ID must contain ;sha256=<64 hex digest> for this simulator profile."),
         new(2, "image_block_transfer", true, "structure", "Sequential 0-based blocks up to 1024 bytes."), new(3, "image_verify", true, "integer", "Use integer 0."), new(4, "image_activate", true, "integer", "Use integer 0; simulation only.")]);

    private static void Initiate(SmartMeterFleet fleet, string meterId, DlmsDataValue? parameter)
    {
        var values = Structure(parameter, 2); var identifier = Text(values[0]);
        if (values[1].Type != DlmsDataType.DoubleLongUnsigned) throw new ArgumentException("Image size must be double-long-unsigned.");
        var size = checked((int)(uint)values[1].Value!); const string marker = ";sha256=";
        var position = identifier.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (position < 1) throw new ArgumentException("Image identifier must include ;sha256=<64 hexadecimal digest> in this simulator profile.");
        Ensure(fleet.BeginFirmware(meterId, identifier[..position], size, identifier[(position + marker.Length)..]));
    }
    private static void TransferBlock(SmartMeterFleet fleet, string meterId, DlmsDataValue? parameter)
    {
        var values = Structure(parameter, 2);
        if (values[0].Type != DlmsDataType.DoubleLongUnsigned || values[1].Type != DlmsDataType.OctetString) throw new ArgumentException("Block transfer expects unsigned block number and octet string.");
        var offset = checked((int)((uint)values[0].Value! * BlockSize));
        Ensure(fleet.WriteFirmwareBlock(meterId, offset, (byte[])values[1].Value!));
    }
    private static FirmwareStatus Require(SmartMeterFleet fleet, string meterId) => fleet.GetCompanion(meterId)?.Firmware ?? throw new KeyNotFoundException("Meter not found.");
    private static void Ensure(bool success) { if (!success) throw new KeyNotFoundException("Meter not found."); }
    private static DlmsDataValue BlockStatus(FirmwareStatus firmware)
    {
        var blocks = firmware.ExpectedBytes == 0 ? 0 : (firmware.ExpectedBytes + BlockSize - 1) / BlockSize;
        var bytes = new byte[(blocks + 7) / 8]; var completed = firmware.ReceivedBytes / BlockSize;
        if (firmware.ReceivedBytes == firmware.ExpectedBytes && blocks > 0) completed = blocks;
        for (var block = 0; block < completed; block++) bytes[block / 8] |= (byte)(0x80 >> (block % 8));
        return DlmsDataValue.Bits(blocks, bytes);
    }
    private static int FirstMissingBlock(FirmwareStatus firmware) => firmware.ExpectedBytes == 0 ? 0 : (firmware.ReceivedBytes + BlockSize - 1) / BlockSize;
    private static byte TransferState(string state) => state switch { "Transfer initiated" => 1, "Verified" => 3, "Verification failed" => 4, "Activated (simulation only)" => 6, _ => 0 };
    private static DlmsDataValue ActivationInfo(FirmwareStatus firmware) => firmware.State == "Verified" ? DlmsDataValue.Array(DlmsDataValue.Structure(
        new(DlmsDataType.DoubleLongUnsigned, (uint)firmware.ExpectedBytes), DlmsDataValue.Octets(Encoding.UTF8.GetBytes(firmware.ImageId ?? string.Empty)),
        DlmsDataValue.Octets(Convert.FromHexString(firmware.ExpectedSha256 ?? string.Empty)))) : DlmsDataValue.Array();
    private static DlmsDataValue[] Structure(DlmsDataValue? value, int count) => value is { Type: DlmsDataType.Structure, Value: DlmsDataValue[] items } && items.Length == count ? items : throw new ArgumentException("Image transfer parameter has an invalid structure.");
    private static string Text(DlmsDataValue value) => value.Type switch { DlmsDataType.OctetString => Encoding.UTF8.GetString((byte[])value.Value!), DlmsDataType.VisibleString or DlmsDataType.Utf8String => (string)value.Value!, _ => throw new ArgumentException("Image identifier must be an octet string.") };
    private static void RequireZero(DlmsDataValue? value)
    {
        if (value is not { Type: DlmsDataType.Integer, Value: sbyte number } || number != 0)
            throw new ArgumentException("Image verify/activate requires integer 0.");
    }
}
