namespace SmartMeterStudio.Protocol.Server;

/// <summary>Authorization supplied by a future ACSE/security host; this class does not authenticate a client.</summary>
public enum DlmsAssociationAuthentication { None, LowLevelSecurity, HlsGmac }

public sealed record DlmsAssociationContext(string MeterId, string ClientName, bool MayWrite, bool MayAction,
    byte ClientSap = 16, ushort ServerSap = 1, DlmsAssociationAuthentication Authentication = DlmsAssociationAuthentication.None)
{
    public static DlmsAssociationContext PublicReadOnly(string meterId) => new(meterId, "public", false, false);
}
