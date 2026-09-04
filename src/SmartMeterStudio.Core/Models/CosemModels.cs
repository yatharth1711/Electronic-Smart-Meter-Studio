using System.Text.Json;

namespace SmartMeterStudio.Core.Models;

// Management DTOs, not DLMS/A-XDR wire encodings.
public sealed record CosemClass(int Id, string Name, int? Version, string Coverage, string Reference = "");
public sealed record CosemAttribute(int Index, string Name, string DataType, bool Writable, object? Value, string Note = "");
public sealed record CosemMethod(int Index, string Name, bool Enabled, string Parameter, string Note = "");
public sealed record CosemObject(string LogicalName, string Name, int ClassId, int? Version, bool Custom,
    string Coverage, IReadOnlyList<CosemAttribute> Attributes, IReadOnlyList<CosemMethod> Methods);
public sealed record ScalerUnit(int Scaler = 0, int Unit = 255);
public sealed record CaptureReference(int ClassId, string LogicalName, int AttributeIndex = 2, int DataIndex = 0);
public sealed record ProfileConfiguration(CaptureReference[] CaptureObjects, int CapturePeriod = 900, int Capacity = 100);
public sealed record ProfileRow(long Sequence, DateTimeOffset CapturedAt, IReadOnlyList<JsonElement> Values);
public sealed record CreateCosemObject(int ClassId, string LogicalName, string Name,
    JsonElement Value = default, int Scaler = 0, int Unit = 255);
public sealed record CosemScript(int ScriptIdentifier, CosemScriptAction[] Actions);
public sealed record CosemScriptAction(int ServiceId, int ClassId, string LogicalName, int Index, JsonElement Parameter);
public sealed record CosemSpecialDay(int Index, DateOnly SpecialdayDate, int DayId);
public sealed record CosemClockPreset(string PresetTime, string ValidityIntervalStart, string ValidityIntervalEnd);

public static class CosemCatalog
{
    public const string Subset = "Implemented simulation subset";
    public const string Pending = "Catalog only · behavior not implemented";
    public static IReadOnlyList<CosemClass> Classes { get; } = Array.AsReadOnly(new CosemClass[]
    {
        Entry(1, "Data", 0, "4.3.1"), Entry(3, "Register", 0, "4.3.2"),
        Entry(4, "Extended register", 0, "4.3.3"), Entry(5, "Demand register", 0, "4.3.4"),
        Entry(6, "Register activation", 0, "4.3.5"), Entry(7, "Profile generic", 1, "4.3.6"),
        Entry(8, "Clock", 0, "4.5.1"), Entry(9, "Script table", 0, "4.5.2"),
        Entry(10, "Schedule", 0, "4.5.3"), Entry(11, "Special days table", 0, "4.5.4"),
        Entry(12, "Association SN", 4, "4.4.3"), Entry(15, "Association LN", 3, "4.4.4"),
        Entry(17, "SAP assignment", 0, "4.4.5"), Entry(18, "Image transfer", 0, "4.4.6"),
        Entry(19, "IEC local port setup", 1, "4.7.1"), Entry(20, "Activity calendar", 0, "4.5.5"),
        Entry(21, "Register monitor", 0, "4.5.6"), Entry(22, "Single action schedule", 0, "4.5.7"),
        Entry(23, "IEC HDLC setup", 1, "4.7.2"), Entry(24, "IEC twisted pair (1) setup", 1, "4.7.3"),
        Entry(25, "M-Bus slave port setup", 0, "4.8.2"), Entry(26, "Utility tables", 0, "4.3.7"),
        Entry(27, "Modem configuration", 1, "4.7.4"), Entry(28, "Auto answer", 2, "4.7.5"),
        Entry(29, "Auto connect", 2, "4.7.6"), Entry(30, "Data protection", 0, "4.4.9"),
        Entry(40, "Push setup", 3, "4.4.8.2"), Entry(41, "TCP-UDP setup", 0, "4.9.1"),
        Entry(42, "IPv4 setup", 0, "4.9.2"), Entry(43, "MAC address setup", 0, "4.9.4"),
        Entry(44, "PPP setup", 0, "4.9.5"), Entry(45, "GPRS modem setup", 0, "4.7.7"),
        Entry(46, "SMTP setup", 0, "4.9.6"), Entry(47, "GSM diagnostic", 2, "4.7.8"),
        Entry(48, "IPv6 setup", 0, "4.9.3"), Entry(50, "S-FSK Phy & MAC setup", 1, "4.10.3"),
        Entry(51, "S-FSK active initiator", 0, "4.10.4"), Entry(52, "S-FSK MAC synchronization timeouts", 0, "4.10.5"),
        Entry(53, "S-FSK MAC counters", 0, "4.10.6"), Entry(55, "IEC 61334-4-32 LLC setup", 1, "4.10.7"),
        Entry(56, "S-FSK reporting system list", 0, "4.10.8"), Entry(57, "ISO/IEC 8802-2 LLC Type 1 setup", 0, "4.11.2"),
        Entry(58, "ISO/IEC 8802-2 LLC Type 2 setup", 0, "4.11.3"), Entry(59, "ISO/IEC 8802-2 LLC Type 3 setup", 0, "4.11.4"),
        Entry(61, "Register table", 0, "4.3.8"), Entry(62, "Compact data", 1, "4.3.10"),
        Entry(63, "Status mapping", 0, "4.3.9"), Entry(64, "Security setup", 1, "4.4.7"),
        Entry(65, "Parameter monitor", 1, "4.5.10"), Entry(66, "Measurement data monitoring objects", 0, "4.3.11"),
        Entry(67, "Sensor manager", 0, "4.5.11"), Entry(68, "Arbitrator", 0, "4.5.12"),
        Entry(70, "Disconnect control", 2, "4.5.8"), Entry(71, "Limiter", 0, "4.5.9"),
        Entry(72, "M-Bus client", 2, "4.8.3"), Entry(73, "Wireless Mode Q channel", 1, "4.8.4"),
        Entry(74, "M-Bus master port setup", 0, "4.8.5"), Entry(76, "DLMS server M-Bus port setup", 0, "4.8.6"),
        Entry(77, "M-Bus diagnostic", 0, "4.8.7"), Entry(80, "61334-4-32 LLC SSCS setup", 0, "4.12.3"),
        Entry(81, "PRIME PLC physical layer counters", 0, "4.12.5"), Entry(82, "PRIME PLC MAC setup", 0, "4.12.6"),
        Entry(83, "PRIME PLC MAC functional parameters", 0, "4.12.7"), Entry(84, "PRIME PLC MAC counters", 0, "4.12.8"),
        Entry(85, "PRIME PLC MAC network administration data", 0, "4.12.9"), Entry(86, "PRIME PLC application identification", 0, "4.12.11"),
        Entry(90, "G3-PLC MAC layer counters", 1, "4.13.3"), Entry(91, "G3-PLC MAC setup", 4, "4.13.4"),
        Entry(92, "G3-PLC 6LoWPAN adaptation layer setup", 4, "4.13.5"),
        Entry(95, "Wi-SUN setup", 0, "4.18.1"), Entry(96, "Wi-SUN diagnostic", 0, "4.18.2"),
        Entry(97, "RPL diagnostic", 0, "4.18.3"), Entry(98, "MPL diagnostic", 0, "4.18.4"),
        Entry(100, "NTP setup", 0, "4.9.7"), Entry(101, "ZigBee SAS startup", 0, "4.15.2"),
        Entry(102, "ZigBee SAS join", 0, "4.15.3"), Entry(103, "ZigBee SAS APS fragmentation", 0, "4.15.4"),
        Entry(104, "ZigBee network control", 0, "4.15.5"), Entry(105, "ZigBee tunnel setup", 0, "4.15.6"),
        Entry(111, "Account", 0, "4.6.2"), Entry(112, "Credit", 0, "4.6.3"),
        Entry(113, "Charge", 0, "4.6.4"), Entry(115, "Token gateway", 0, "4.6.5"),
        Entry(116, "IEC 62055-41 attributes", 0, "4.6.6"), Entry(122, "Function control", 0, "4.4.10"),
        Entry(123, "Array manager", 0, "4.4.11"), Entry(124, "Communication port protection", 0, "4.4.12"),
        Entry(126, "SCHC-LPWAN setup", 0, "4.16.2.1"), Entry(127, "SCHC-LPWAN diagnostic", 0, "4.16.2.2"),
        Entry(128, "LoRaWAN setup", 0, "4.17.2.2"), Entry(129, "LoRaWAN diagnostic", 0, "4.17.2.3"),
        Entry(130, "ISO/IEC 14908 identification", 0, "4.19.2"), Entry(131, "ISO/IEC 14908 protocol setup", 0, "4.19.3"),
        Entry(132, "ISO/IEC 14908 protocol status", 1, "4.19.4"), Entry(133, "ISO/IEC 14908 diagnostic", 1, "4.19.5"),
        Entry(140, "HS-PLC ISO/IEC 12139-1 MAC setup", 0, "4.14.2"), Entry(141, "HS-PLC ISO/IEC 12139-1 CPAS setup", 0, "4.14.3"),
        Entry(142, "HS-PLC ISO/IEC 12139-1 IP SSAS setup", 0, "4.14.4"), Entry(143, "HS-PLC ISO/IEC 12139-1 HDLC SSAS setup", 0, "4.14.5"),
        Entry(151, "LTE monitoring", 1, "4.7.9"), Entry(152, "CoAP setup", 0, "4.9.8"),
        Entry(153, "CoAP diagnostic", 0, "4.9.9"), Entry(160, "G3-PLC Hybrid RF MAC layer counters", 0, "4.13.6"),
        Entry(161, "G3-PLC Hybrid RF MAC setup", 1, "4.13.7"), Entry(162, "G3-PLC Hybrid 6LoWPAN adaptation layer setup", 1, "4.13.8"),
        Entry(170, "Attestation", 0, "4.20.2")
    });
    public static CosemClass Find(int id) => Classes.FirstOrDefault(x => x.Id == id)
        ?? new(id, "Uncatalogued class", null, Pending);
    private static CosemClass Entry(int id, string name, int version, string clause) => new(id, name, version,
        id is 1 or 3 or 4 or 7 or 8 or 9 or 11 ? Subset : id is 70 or 71 ? "Existing companion value only; full class pending" : Pending,
        $"Blue Book Ed.17 Part 2 §{clause}");
}
