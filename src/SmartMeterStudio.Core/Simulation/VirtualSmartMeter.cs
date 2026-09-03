using SmartMeterStudio.Core.Models;

namespace SmartMeterStudio.Core.Simulation;

public sealed partial class VirtualSmartMeter
{
    private const int ReadingCapacity = 360;
    private const int EventCapacity = 100;
    private readonly object _gate = new();
    private readonly Queue<MeterReading> _readings = new();
    private readonly Queue<MeterEvent> _events = new();
    private double _importEnergyKwh;
    private double _exportEnergyKwh;
    private double _maximumDemandKw;
    private ActiveFault? _activeFault;

    public VirtualSmartMeter(MeterDefinition definition, DateTimeOffset? initialTime = null)
    {
        Definition = definition;
        SimulatedTime = initialTime ?? DateTimeOffset.UtcNow;
        if (Category == MeterCategory.D4) _settings = new() { CapturePeriodSeconds = 900, DemandPeriodSeconds = 900 };
        _partialBlock = !AtBoundary(SimulatedTime.TimeOfDay.TotalSeconds, _settings.CapturePeriodSeconds);
        _partialDemand = !AtBoundary(SimulatedTime.TimeOfDay.TotalSeconds, _settings.DemandPeriodSeconds);
        State = MeterOperatingState.Running;
        TimeScale = 60;
        AddEvent("METER_CREATED", $"Virtual meter {definition.SerialNumber} is online.", "Info");
    }

    public MeterDefinition Definition { get; }
    public MeterOperatingState State { get; private set; }
    public DateTimeOffset SimulatedTime { get; private set; }
    public double TimeScale { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (State == MeterOperatingState.Running) return;
            State = MeterOperatingState.Running;
            AddEvent("SIMULATION_STARTED", "Meter simulation started.", "Info");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (State == MeterOperatingState.Stopped) return;
            State = MeterOperatingState.Stopped;
            AddEvent("SIMULATION_STOPPED", "Meter simulation paused.", "Info");
        }
    }

    public void SetTimeScale(double scale)
    {
        if (!double.IsFinite(scale) || scale is < 1 or > 86_400)
            throw new ArgumentOutOfRangeException(nameof(scale), "Time scale must be between 1 and 86,400.");

        lock (_gate)
        {
            TimeScale = scale;
            AddEvent("TIME_SCALE_CHANGED", $"Simulation speed changed to {scale:0}x.", "Info");
        }
    }

    public void InjectFault(MeterFaultType type, int durationSeconds)
    {
        if (type == MeterFaultType.None)
            throw new ArgumentException("Select a fault type.", nameof(type));
        if (durationSeconds is < 1 or > 86_400)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be between 1 and 86,400 simulated seconds.");

        lock (_gate)
        {
            _activeFault = new ActiveFault
            {
                Type = type,
                StartedAt = SimulatedTime,
                EndsAt = SimulatedTime.AddSeconds(durationSeconds)
            };
            AddEvent("FAULT_INJECTED", $"{Humanize(type)} injected for {durationSeconds} simulated seconds.", "Warning");
        }
    }

    public void ClearFault()
    {
        lock (_gate)
        {
            if (_activeFault is null) return;
            var cleared = _activeFault.Type;
            _activeFault = null;
            AddEvent("FAULT_CLEARED", $"{Humanize(cleared)} cleared.", "Info");
        }
    }

    public void Tick(double realSeconds)
    {
        if (!double.IsFinite(realSeconds)) throw new ArgumentOutOfRangeException(nameof(realSeconds));
        if (realSeconds <= 0) return;

        lock (_gate)
        {
            if (State != MeterOperatingState.Running) return;

            var remaining = realSeconds * TimeScale;
            if (remaining > 31 * 86400d) throw new ArgumentOutOfRangeException(nameof(realSeconds), "Advance at most 31 simulated days per call.");
            while (remaining > 0.000001)
            {
                var clockRate = _activeFault?.Type == MeterFaultType.ClockDrift ? 1.08 : 1;
                var seconds = Math.Min(remaining, NextCompanionStep() / clockRate);
                if (_activeFault is not null) seconds = Math.Min(seconds, Math.Max(.000001, (_activeFault.EndsAt - SimulatedTime).TotalSeconds / clockRate));
                SimulatedTime = SimulatedTime.AddTicks((long)Math.Round(seconds * clockRate * TimeSpan.TicksPerSecond));
                var reading = GenerateReading(seconds);
                ObserveCompanion(reading, seconds);
                Enqueue(_readings, reading with { MaximumDemandKw = _maximumDemandKw }, ReadingCapacity);
                if (_activeFault is not null && SimulatedTime >= _activeFault.EndsAt)
                {
                    var ended = _activeFault.Type;
                    _activeFault = null;
                    AddEvent("FAULT_ENDED", $"{Humanize(ended)} ended automatically.", "Info");
                }
                remaining -= seconds;
            }
        }
    }

    public MeterSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new MeterSnapshot
            {
                Definition = CloneDefinition(Definition),
                State = State,
                SimulatedTime = SimulatedTime,
                TimeScale = TimeScale,
                IsReachable = _activeFault?.Type != MeterFaultType.CommunicationDropout,
                ActiveFault = _activeFault,
                LatestReading = _readings.LastOrDefault() is { } latest ? latest with { MaximumDemandKw = _maximumDemandKw } : null,
                ReadingCount = _readings.Count,
                EventCount = _events.Count
            };
        }
    }

    public IReadOnlyList<MeterReading> Readings(int limit = 120)
    {
        lock (_gate)
        {
            return _readings.TakeLast(Math.Clamp(limit, 1, ReadingCapacity)).ToArray();
        }
    }

    public IReadOnlyList<MeterEvent> Events(int limit = 50)
    {
        lock (_gate)
        {
            return _events.Reverse().Take(Math.Clamp(limit, 1, EventCapacity)).ToArray();
        }
    }

    private MeterReading GenerateReading(double elapsedSeconds)
    {
        var hour = SimulatedTime.TimeOfDay.TotalHours;
        var timeWave = Math.Sin(SimulatedTime.ToUnixTimeSeconds() / 187d + StableSeed() * 0.07);
        var morningPeak = Gaussian(hour, 8.0, 1.8);
        var eveningPeak = Gaussian(hour, 19.5, 2.4);
        var daytimeLoad = hour is >= 9 and <= 17 ? 0.28 : 0;
        var loadFactor = 0.42 + morningPeak * 0.35 + eveningPeak * 0.65 + daytimeLoad + timeWave * 0.025;
        var activePower = Math.Max(0.15, Definition.BaseLoadKw * loadFactor);
        var powerFactor = Math.Clamp(Definition.NominalPowerFactor + timeWave * 0.012, 0.7, 0.999);
        var voltage1 = Definition.NominalVoltage * (1 + timeWave * 0.006);
        var voltage2 = Definition.PhaseMode == MeterPhaseMode.ThreePhase
            ? Definition.NominalVoltage * (1 + Math.Sin(timeWave + 2.1) * 0.005)
            : 0;
        var voltage3 = Definition.PhaseMode == MeterPhaseMode.ThreePhase
            ? Definition.NominalVoltage * (1 + Math.Sin(timeWave + 4.2) * 0.005)
            : 0;
        var frequency = 50 + Math.Sin(SimulatedTime.ToUnixTimeSeconds() / 71d) * 0.025;
        var thd = 1.7 + Math.Abs(timeWave) * 0.8;

        switch (_activeFault?.Type)
        {
            case MeterFaultType.VoltageDip:
                voltage1 *= 0.68; voltage2 *= 0.72; voltage3 *= 0.70;
                break;
            case MeterFaultType.VoltageSwell:
                voltage1 *= 1.19; voltage2 *= 1.17; voltage3 *= 1.18;
                break;
            case MeterFaultType.PhaseLoss:
                voltage2 = 0;
                break;
            case MeterFaultType.LowPowerFactor:
                powerFactor = 0.62;
                break;
            case MeterFaultType.HarmonicBurst:
                thd = 14.5 + Math.Abs(timeWave) * 3;
                break;
            case MeterFaultType.ReverseEnergy:
                activePower *= -0.72;
                break;
        }

        // Reverse-energy fault takes precedence over the configured active direction.
        if (_activeFault?.Type != MeterFaultType.ReverseEnergy && _electrical.ExportActive) activePower = -activePower;
        if (!_electrical.SupplyAvailable)
        {
            voltage1 = voltage2 = voltage3 = frequency = thd = activePower = 0;
            _powerOffMinutes += elapsedSeconds / 60;
        }
        var phaseDivisor = Definition.PhaseMode == MeterPhaseMode.ThreePhase ? 3d : 1d;
        if (!_connected) activePower = 0;
        var current = Math.Abs(activePower) * 1000 / Math.Max(1, phaseDivisor * Definition.NominalVoltage * powerFactor);
        var current2 = Definition.PhaseMode == MeterPhaseMode.ThreePhase && voltage2 > 0 ? current * 1.025 : 0;
        var current3 = Definition.PhaseMode == MeterPhaseMode.ThreePhase ? current * 0.982 : 0;
        var reactivePower = Math.Abs(activePower) * Math.Tan(Math.Acos(powerFactor));
        if (_electrical.ExportReactive) reactivePower = -reactivePower;
        // Blue Book Ed.17 Part 1 Figure 1: QI(+,+), QII(-,+), QIII(-,-), QIV(+,-).
        var quadrant = activePower >= 0 ? reactivePower >= 0 ? 0 : 3 : reactivePower >= 0 ? 1 : 2;
        _quadrantEnergy[quadrant] += Math.Abs(reactivePower) * elapsedSeconds / 3600;
        var energyDelta = activePower * elapsedSeconds / 3600d;
        if (energyDelta >= 0) _importEnergyKwh += energyDelta;
        else _exportEnergyKwh += Math.Abs(energyDelta);

        return new MeterReading
        {
            Timestamp = SimulatedTime,
            VoltageL1 = Round(voltage1),
            VoltageL2 = Round(voltage2),
            VoltageL3 = Round(voltage3),
            CurrentL1 = Round(current),
            CurrentL2 = Round(current2),
            CurrentL3 = Round(current3),
            FrequencyHz = Round(frequency, 3),
            ActivePowerKw = Round(activePower, 3),
            ReactivePowerKvar = Round(reactivePower, 3),
            PowerFactor = Round(powerFactor, 3),
            ImportEnergyKwh = Round(_importEnergyKwh, 4),
            ExportEnergyKwh = Round(_exportEnergyKwh, 4),
            MaximumDemandKw = Round(_maximumDemandKw, 3),
            VoltageThdPercent = Round(thd, 2),
            TariffRate = GetTariffRate(hour)
        };
    }

    private void AddEvent(string code, string message, string severity)
    {
        Enqueue(_events, new MeterEvent
        {
            MeterId = Definition.Id,
            Timestamp = SimulatedTime,
            Severity = severity,
            Code = code,
            Message = message
        }, EventCapacity);
    }

    private static void Enqueue<T>(Queue<T> queue, T item, int capacity)
    {
        queue.Enqueue(item);
        while (queue.Count > capacity) queue.Dequeue();
    }

    private int StableSeed() => Definition.SerialNumber.Aggregate(17, (value, ch) => unchecked(value * 31 + ch));
    private static double Gaussian(double value, double center, double width) => Math.Exp(-Math.Pow(value - center, 2) / (2 * width * width));
    private static int GetTariffRate(double hour) => hour is >= 18 and < 22 ? 3 : hour is >= 6 and < 18 ? 2 : 1;
    private static double Round(double value, int decimals = 2) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);
    private static string Humanize(MeterFaultType type) => string.Concat(type.ToString().Select((ch, index) => index > 0 && char.IsUpper(ch) ? " " + ch : ch.ToString()));

    private static MeterDefinition CloneDefinition(MeterDefinition source) => new()
    {
        Category = source.Category,
        Id = source.Id,
        Name = source.Name,
        Model = source.Model,
        SerialNumber = source.SerialNumber,
        PhaseMode = source.PhaseMode,
        NominalVoltage = source.NominalVoltage,
        BaseLoadKw = source.BaseLoadKw,
        NominalPowerFactor = source.NominalPowerFactor,
        TariffPlan = source.TariffPlan
    };
}
