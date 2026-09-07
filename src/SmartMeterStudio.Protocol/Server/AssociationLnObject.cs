using SmartMeterStudio.Core.Models;
using SmartMeterStudio.Core.Simulation;
using SmartMeterStudio.Protocol.Dlms;

namespace SmartMeterStudio.Protocol.Server;

/// <summary>Read model for the supported Association LN v3 instance (0.0.40.0.0.255).</summary>
internal static class AssociationLnObject
{
    public const string LogicalName = "0.0.40.0.0.255";

    public static DlmsDataValue Read(SmartMeterFleet fleet, DlmsAssociationContext association, byte attributeId)
    {
        return attributeId switch
        {
            1 => DlmsDataValue.Octets(DlmsLogicalName.Parse(LogicalName).ToArray()),
            2 => ObjectList(fleet, association),
            3 => DlmsDataValue.Structure(new(DlmsDataType.Integer, unchecked((sbyte)association.ClientSap)), new(DlmsDataType.LongUnsigned, association.ServerSap)),
            4 => DlmsDataValue.Octets([0x60, 0x85, 0x74, 0x05, 0x08, 0x01, 0x01]),
            5 => DlmsDataValue.Structure(new(DlmsDataType.Unsigned, (byte)6), new(DlmsDataType.LongUnsigned, (ushort)0x04B0)),
            6 => DlmsDataValue.Octets(MechanismOid(association.Authentication)),
            7 => throw new NotSupportedException("Association secrets are never readable over DLMS."),
            8 => new(DlmsDataType.Enum, (byte)2), // associated
            9 => DlmsDataValue.Octets(DlmsLogicalName.Parse("0.0.43.0.0.255").ToArray()),
            10 => DlmsDataValue.Array(),
            11 => DlmsDataValue.Structure(new(DlmsDataType.Unsigned, (byte)0), new(DlmsDataType.VisibleString, association.ClientName)),
            _ => throw new KeyNotFoundException("Association LN attribute does not exist.")
        };
    }

    private static DlmsDataValue ObjectList(SmartMeterFleet fleet, DlmsAssociationContext association)
    {
        var objects = fleet.GetCosemObjects(association.MeterId).Append(AssociationDefinition());
        return DlmsDataValue.Array(objects.OrderBy(item => item.ClassId).ThenBy(item => item.LogicalName, StringComparer.Ordinal)
            .Select(item => ObjectListElement(item, association)).ToArray());
    }

    private static DlmsDataValue ObjectListElement(CosemObject item, DlmsAssociationContext association)
    {
        var attributes = DlmsDataValue.Array(item.Attributes.Select(attribute => DlmsDataValue.Structure(
            new(DlmsDataType.Integer, unchecked((sbyte)attribute.Index)),
            new(DlmsDataType.Enum, AccessMode(attribute.Writable && association.MayWrite)))).ToArray());
        var methods = DlmsDataValue.Array(item.Methods.Select(method => DlmsDataValue.Structure(
            new(DlmsDataType.Integer, unchecked((sbyte)method.Index)),
            new(DlmsDataType.Enum, AccessMode(method.Enabled && association.MayAction)))).ToArray());
        var accessRights = DlmsDataValue.Structure(attributes, methods);
        return DlmsDataValue.Structure(new(DlmsDataType.LongUnsigned, (ushort)item.ClassId),
            new(DlmsDataType.Unsigned, (byte)(item.Version ?? 0)), DlmsDataValue.Octets(DlmsLogicalName.Parse(item.LogicalName).ToArray()), accessRights);
    }

    private static byte AccessMode(bool writeOrAction) => writeOrAction ? (byte)0x03 : (byte)0x01;
    private static byte[] MechanismOid(DlmsAssociationAuthentication authentication) => authentication switch
    {
        DlmsAssociationAuthentication.None => [0x60, 0x85, 0x74, 0x05, 0x08, 0x02, 0x00],
        DlmsAssociationAuthentication.LowLevelSecurity => [0x60, 0x85, 0x74, 0x05, 0x08, 0x02, 0x01],
        DlmsAssociationAuthentication.HlsGmac => [0x60, 0x85, 0x74, 0x05, 0x08, 0x02, 0x05],
        _ => throw new ArgumentOutOfRangeException(nameof(authentication))
    };
    private static CosemObject AssociationDefinition() => new(LogicalName, "Association LN", 15, 3, false, "Protocol Association LN v3",
        [new(1, "logical_name", "octet-string", false, LogicalName), new(2, "object_list", "array", false, null),
         new(3, "associated_partners_id", "structure", false, null), new(4, "application_context_name", "octet-string", false, null),
         new(5, "xDLMS_context_info", "structure", false, null), new(6, "authentication_mechanism_name", "octet-string", false, null),
         new(7, "secret", "octet-string", false, null), new(8, "association_status", "enum", false, null),
         new(9, "security_setup_reference", "octet-string", false, null), new(10, "user_list", "array", false, null), new(11, "current_user", "structure", false, null)],
        [new(1, "reply_to_HLS_authentication", true, "octet-string", "Handled only while HLS is pending.")]);
}
