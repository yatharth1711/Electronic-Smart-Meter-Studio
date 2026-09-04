namespace SmartMeterStudio.Protocol.Server;

/// <summary>Authorization supplied by a future ACSE/security host; this class does not authenticate a client.</summary>
public sealed record DlmsAssociationContext(string MeterId, string ClientName, bool MayWrite, bool MayAction)
{
    public static DlmsAssociationContext PublicReadOnly(string meterId) => new(meterId, "public", false, false);
}
