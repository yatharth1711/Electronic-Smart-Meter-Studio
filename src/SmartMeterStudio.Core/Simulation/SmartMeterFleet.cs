using System.Collections.Concurrent;
using SmartMeterStudio.Core.Models;
using SmartMeterStudio.Core.Persistence;

namespace SmartMeterStudio.Core.Simulation;

public sealed partial class SmartMeterFleet
{
    private readonly ConcurrentDictionary<string, VirtualSmartMeter> _meters = new(StringComparer.OrdinalIgnoreCase);
    private readonly IMeterDefinitionStore? _store;

    public SmartMeterFleet(IMeterDefinitionStore? store = null, bool seedDefaults = true)
    {
        _store = store;
        var saved = store?.Load() ?? [];
        if (saved.Count > 0)
        {
            foreach (var definition in saved) AddInternal(definition, warmUp: true);
        }
        else if (seedDefaults)
        {
            foreach (var definition in DefaultDefinitions()) AddInternal(definition, warmUp: true);
            Persist();
        }
    }

    public IReadOnlyList<MeterSnapshot> GetSnapshots() => _meters.Values
        .Select(meter => meter.Snapshot())
        .OrderBy(snapshot => snapshot.Definition.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public MeterSnapshot? GetSnapshot(string id) => TryGet(id, out var meter) ? meter.Snapshot() : null;
    public IReadOnlyList<MeterReading>? GetReadings(string id, int limit = 120) => TryGet(id, out var meter) ? meter.Readings(limit) : null;
    public IReadOnlyList<MeterEvent>? GetEvents(string id, int limit = 50) => TryGet(id, out var meter) ? meter.Events(limit) : null;

    public MeterSnapshot Create(CreateMeterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Category) || !Enum.IsDefined(request.PhaseMode)) throw new ArgumentException("Invalid meter category or phase mode.");
        if (request.Category != MeterCategory.Automatic &&
            ((request.Category is MeterCategory.C3 or MeterCategory.D1) != (request.PhaseMode == MeterPhaseMode.SinglePhase)))
            throw new ArgumentException("C3/D1 require single phase; all other explicit categories require three phase.");
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Meter name is required.", nameof(request));
        if (!double.IsFinite(request.NominalVoltage) || request.NominalVoltage is < 50 or > 500) throw new ArgumentOutOfRangeException(nameof(request), "Nominal voltage must be between 50 V and 500 V.");
        if (!double.IsFinite(request.BaseLoadKw) || request.BaseLoadKw is <= 0 or > 10_000) throw new ArgumentOutOfRangeException(nameof(request), "Base load must be between 0 and 10,000 kW.");
        if (!double.IsFinite(request.NominalPowerFactor) || request.NominalPowerFactor is < 0.1 or > 1) throw new ArgumentOutOfRangeException(nameof(request), "Power factor must be between 0.1 and 1.0.");

        var number = _meters.Count + 1;
        string id;
        do id = $"MTR-{number++:0000}"; while (_meters.ContainsKey(id));

        var definition = new MeterDefinition
        {
            Category = request.Category,
            Id = id,
            Name = request.Name.Trim(),
            Model = string.IsNullOrWhiteSpace(request.Model) ? "SMS-Virtual-1" : request.Model.Trim(),
            SerialNumber = string.IsNullOrWhiteSpace(request.SerialNumber) ? $"SIM{DateTime.UtcNow:yyMMddHHmmss}{number:00}" : request.SerialNumber.Trim(),
            PhaseMode = request.PhaseMode,
            NominalVoltage = request.NominalVoltage,
            BaseLoadKw = request.BaseLoadKw,
            NominalPowerFactor = request.NominalPowerFactor,
            TariffPlan = string.IsNullOrWhiteSpace(request.TariffPlan) ? "Flat" : request.TariffPlan.Trim()
        };

        var meter = AddInternal(definition, warmUp: true);
        Persist();
        return meter.Snapshot();
    }

    public bool Remove(string id)
    {
        var removed = _meters.TryRemove(id, out _);
        if (removed) Persist();
        return removed;
    }

    public bool Start(string id) => Apply(id, meter => meter.Start());
    public bool Stop(string id) => Apply(id, meter => meter.Stop());
    public bool SetTimeScale(string id, double scale) => Apply(id, meter => meter.SetTimeScale(scale));
    public bool InjectFault(string id, MeterFaultType type, int durationSeconds) => Apply(id, meter => meter.InjectFault(type, durationSeconds));
    public bool ClearFault(string id) => Apply(id, meter => meter.ClearFault());
    public ElectricalSnapshot? GetElectrical(string id) => TryGet(id, out var meter) ? meter.Electrical() : null;
    public bool ConfigureElectrical(string id, ElectricalSettings settings) => Apply(id, meter => meter.ConfigureElectrical(settings));
    public CompanionSnapshot? GetCompanion(string id) => TryGet(id, out var meter) ? meter.Companion() : null;
    public IReadOnlyList<ObisValue>? GetObjects(string id) => TryGet(id, out var meter) ? meter.Objects() : null;
    public IReadOnlyList<SurveyEntry>? GetSurvey(string id, SurveyKind kind, DateTimeOffset? from = null, DateTimeOffset? to = null) => TryGet(id, out var meter) ? meter.Survey(kind, from, to) : null;
    public bool ConfigureCompanion(string id, CompanionSettings settings) => Apply(id, meter => meter.ConfigureCompanion(settings));
    public bool SetRelay(string id, bool connected) => Apply(id, meter => meter.SetRelay(connected));
    public bool CloseBilling(string id) => Apply(id, meter => meter.CloseBilling());
    public bool SetClock(string id, DateTimeOffset time) => Apply(id, meter => meter.SetClock(time));
    public bool SetPrepayment(string id, PrepaymentState state) => Apply(id, meter => meter.SetPrepayment(state));
    public bool SendMessage(string id, bool fromUtility, string message) => Apply(id, meter => meter.SendMessage(fromUtility, message));
    public bool PreviewPush(string id) => Apply(id, meter => meter.PreviewPush());
    public bool BeginFirmware(string id, string imageId, int size, string sha256) => Apply(id, meter => meter.BeginFirmware(imageId, size, sha256));
    public bool WriteFirmwareBlock(string id, int offset, byte[] bytes) => Apply(id, meter => meter.WriteFirmwareBlock(offset, bytes));
    public bool VerifyFirmware(string id) => Apply(id, meter => meter.VerifyFirmware());
    public bool ActivateFirmware(string id) => Apply(id, meter => meter.ActivateFirmware());
    public void Tick(double realSeconds)
    {
        foreach (var meter in _meters.Values) meter.Tick(realSeconds);
    }
    private VirtualSmartMeter AddInternal(MeterDefinition definition, bool warmUp)
    {
        var meter = new VirtualSmartMeter(definition);
        if (!_meters.TryAdd(definition.Id, meter))
            throw new InvalidOperationException($"A meter with ID '{definition.Id}' already exists.");
        if (warmUp)
        {
            for (var index = 0; index < 32; index++) meter.Tick(1);
        }
        return meter;
    }
    private bool Apply(string id, Action<VirtualSmartMeter> action)
    {
        if (!TryGet(id, out var meter)) return false;
        action(meter);
        return true;
    }
    private bool TryGet(string id, out VirtualSmartMeter meter) => _meters.TryGetValue(id, out meter!);
    private void Persist() => _store?.Save(_meters.Values.Select(meter => meter.Definition));
    private static IReadOnlyList<MeterDefinition> DefaultDefinitions() =>
    [
        new()
            {
                Id = "MTR-0001", Name = "Assembly Line A", Model = "G3-CT Smart Meter",
                SerialNumber = "SIM24001001", PhaseMode = MeterPhaseMode.ThreePhase,
                NominalVoltage = 230, BaseLoadKw = 82, NominalPowerFactor = 0.92, TariffPlan = "Industrial TOU"
            },
            new()
            {
                Id = "MTR-0002", Name = "Solar Incomer", Model = "Bi-directional LT Meter",
                SerialNumber = "SIM24001002", PhaseMode = MeterPhaseMode.ThreePhase,
                NominalVoltage = 230, BaseLoadKw = 36, NominalPowerFactor = 0.98, TariffPlan = "Net Metering"
            },
            new()
            {
                Id = "MTR-0003", Name = "Admin Block", Model = "Whole-current Smart Meter",
                SerialNumber = "SIM24001003", PhaseMode = MeterPhaseMode.SinglePhase,
                NominalVoltage = 230, BaseLoadKw = 8.5, NominalPowerFactor = 0.96, TariffPlan = "Commercial TOU"
            }
    ];
}
