namespace SmartMeterStudio.Core.Models;

public enum MeterPhaseMode { SinglePhase, ThreePhase }
public enum MeterOperatingState { Stopped, Running }
public enum MeterFaultType
{
    None,
    VoltageDip,
    VoltageSwell,
    PhaseLoss,
    LowPowerFactor,
    HarmonicBurst,
    ReverseEnergy,
    CommunicationDropout,
    ClockDrift
}

public sealed class MeterDefinition
{
    public MeterCategory Category { get; set; } = MeterCategory.Automatic;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = "SMS-Virtual-1";
    public string SerialNumber { get; set; } = string.Empty;
    public MeterPhaseMode PhaseMode { get; set; } = MeterPhaseMode.ThreePhase;
    public double NominalVoltage { get; set; } = 230;
    public double BaseLoadKw { get; set; } = 12;
    public double NominalPowerFactor { get; set; } = 0.94;
    public string TariffPlan { get; set; } = "TOU Residential";
}

public sealed class CreateMeterRequest
{
    public MeterCategory Category { get; set; } = MeterCategory.Automatic;
    public string Name { get; set; } = "New virtual meter";
    public string Model { get; set; } = "SMS-Virtual-1";
    public string? SerialNumber { get; set; }
    public MeterPhaseMode PhaseMode { get; set; } = MeterPhaseMode.ThreePhase;
    public double NominalVoltage { get; set; } = 230;
    public double BaseLoadKw { get; set; } = 12;
    public double NominalPowerFactor { get; set; } = 0.94;
    public string TariffPlan { get; set; } = "TOU Residential";
}

public sealed class FaultRequest
{
    public MeterFaultType Type { get; set; } = MeterFaultType.VoltageDip;
    public int DurationSeconds { get; set; } = 300;
}

public sealed class TimeScaleRequest
{
    public double Scale { get; set; } = 60;
}

public sealed record MeterReading
{
    public DateTimeOffset Timestamp { get; init; }
    public double VoltageL1 { get; init; }
    public double VoltageL2 { get; init; }
    public double VoltageL3 { get; init; }
    public double CurrentL1 { get; init; }
    public double CurrentL2 { get; init; }
    public double CurrentL3 { get; init; }
    public double FrequencyHz { get; init; }
    public double ActivePowerKw { get; init; }
    public double ReactivePowerKvar { get; init; }
    public double PowerFactor { get; init; }
    public double ImportEnergyKwh { get; init; }
    public double ExportEnergyKwh { get; init; }
    public double MaximumDemandKw { get; init; }
    public double VoltageThdPercent { get; init; }
    public int TariffRate { get; init; }
}
public sealed class MeterEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string MeterId { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public string Severity { get; init; } = "Info";
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class ActiveFault
{
    public MeterFaultType Type { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
}

public sealed class MeterSnapshot
{
    public required MeterDefinition Definition { get; init; }
    public MeterOperatingState State { get; init; }
    public DateTimeOffset SimulatedTime { get; init; }
    public double TimeScale { get; init; }
    public bool IsReachable { get; init; }
    public ActiveFault? ActiveFault { get; init; }
    public MeterReading? LatestReading { get; init; }
    public int ReadingCount { get; init; }
    public int EventCount { get; init; }
}
