namespace SmartMeterStudio.Core.Models;

public enum MeterCategory { Automatic, A, B, C1, C2, C3, D1, D2, D3, D4 }
public enum SurveyKind { Block, Daily, Billing }
public enum SimulatedAssociation { Public, MeterReader, UtilitySettings, Push, Firmware, Ihd }

public sealed record CompanionSettings
{
    public int CapturePeriodSeconds { get; init; } = 1800;
    public int DemandPeriodSeconds { get; init; } = 1800;
    public bool LoadLimitEnabled { get; init; }
    public double LoadLimitKw { get; init; } = 100;
}

public sealed record SurveyEntry(DateTimeOffset Timestamp, double ImportKwh, double ExportKwh,
    double ImportKvah, double ExportKvah, double AverageVoltage, double AverageCurrent,
    double MaximumDemandKw, double MaximumDemandKva, DateTimeOffset? DemandTimestamp,
    double PowerOnMinutes, string CaptureQuality);

public sealed record ObisValue(string Obis, string Name, int ClassId, int Attribute,
    string Unit, object? Value, string Source, string Support = "Simulated value");

public sealed record CompanionEvent(DateTimeOffset Timestamp, int EventId, string Description);
public sealed record PushNotification(DateTimeOffset Timestamp, string SetupObis, string Trigger,
    string Delivery, IReadOnlyList<ObisValue> Values);
public sealed record PrepaymentState(bool Prepaid = false, decimal LastRechargeAmount = 0,
    DateTimeOffset? LastRechargeTime = null, decimal TotalAtLastRecharge = 0,
    decimal CurrentBalance = 0, DateTimeOffset? BalanceTime = null);
public sealed record FirmwareStatus(string Version, string State, string? ImageId,
    int ExpectedBytes, int ReceivedBytes, string? ExpectedSha256);
public sealed record CompanionSnapshot(MeterCategory Category, int Part, bool SupportsRelay,
    bool Connected, CompanionSettings Settings, int ProgrammingCount, int BillingCount,
    double MaximumDemandKw, double MaximumDemandKva, DateTimeOffset? DemandTimestamp,
    int BlockEntries, int DailyEntries, int BillingEntries, double ImportKvah, double ExportKvah,
    double PowerOnMinutes, PrepaymentState Prepayment, FirmwareStatus Firmware,
    string UtilityMessage, string ConsumerMessage, IReadOnlyList<CompanionEvent> Transactions,
    IReadOnlyList<PushNotification> PushQueue);

public sealed record CompanionRequirement(string Feature, string Source, string Status, string Detail);

public static class CompanionCoverage
{
    public static IReadOnlyList<CompanionRequirement> Requirements { get; } =
    [
        new("Meter categories", "Part 1 A2/A3; Part 2 clause 11; Part 3 clause 11", "Partial", "Category selection and D1/D2 relay applicability enforced. Full category-specific object lists remain pending."),
        new("Instantaneous / nameplate objects", "Part 2 Tables A1/A12/A14/A26; Part 3 Tables 1/12", "Partial", "Live OBIS inspection for a defined subset; no claim of a complete association object list."),
        new("Four-quadrant metering / supply", "Blue Book Ed.17 Part 1 Table 13 / Figure 1; simulator supply policy", "Partial", "Independent P/Q directions, four cumulative reactive-energy registers, import/export reactive totals, supply interruption/restoration and power-off accounting. Category applicability, signed PF and nonvolatile retention pending."),
        new("Block and daily profiles", "Part 2 clauses 12/13/18/19", "Partial", "Aligned capture, weighted voltage/current, interval energies, midnight cumulative snapshots, time filtering. Complete multi-phase capture lists and power-on retention remain pending."),
        new("Billing / maximum demand", "Part 2 clauses 14/20", "Partial", "Six closed billing snapshots, monthly/manual close, integrated demand. Per-zone billing, full capture lists and programmable billing schedules remain pending."),
        new("Programming", "Part 2 Tables A13/A27; Part 1 A5 B-3", "Partial", "Clock, integration and capture periods; change events and counter; capture-period change clears old blocks. Same-day reconstruction is not implemented."),
        new("Load control", "Part 2 clause 10; Part 3 clause 10", "Partial", "D1/D2 simulated disconnect/reconnect and load-limit trip. Full IC70 modes and IC71 persistence/reconnection policy pending; prohibited for D3/D4."),
        new("Prepayment", "Part 2 A1, Tables A13/A27 notes 6-9", "Simulated", "HES-written balance fields; no local credit deduction or automatic billing charges."),
        new("Push and IHD", "Part 2 clause 6 and A1", "Partial", "Amended push setup IDs and in-memory preview notifications/messages. No network delivery, ESW/ESWF, retry windows or scheduler conformance."),
        new("Firmware upgrade", "Part 2 clause 9 and A1", "Partial", "Bounded in-memory block transfer, SHA-256 integrity verification and simulated version activation; not actual firmware execution or authenticated image validation."),
        new("Associations and security", "Part 1 clauses 5/7; Part 2 clause 7 and A1/A2", "Not implemented", "No AARQ/AARE, LLS/HLS, ciphered APDUs, key wrapping or persistent invocation counters. UI association selection is not authentication."),
        new("DLMS transports and services", "Part 1 clauses 4/6; Part 2 clause 8", "Not implemented", "REST management only. HDLC, TCP/UDP wrapper, A-XDR, GET/SET/ACTION block transfer and wire selective access are pending."),
        new("Tariff calendar", "Part 1 clause 9; Part 2 Tables A13/A27", "Not implemented", "Existing fixed demo tariff is not a season/week/day activity calendar."),
        new("Indian event profiles", "Part 1 clause 8; Part 2 Tables A5-A11/A18-A25", "Partial", "Verified transaction/control event IDs only. Existing demo fault log is not a standards event profile; full occurrence/restoration, capture and retention pending."),
        new("Conformance testing", "Part 2 clauses 23/24; Part 3 clauses 27/28", "Not verified", "Automated software checks are not BIS certification, protocol conformance or physical meter tests."),
        new("Amendment baseline", "Supplied documents", "Partial", "Sources: Part 1 through A5 (2021), Part 2 through A2 (2017), Part 3 (2017). Later BIS amendments are not yet incorporated.")
    ];
}
