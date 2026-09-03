using SmartMeterStudio.Core.Models;
using SmartMeterStudio.Core.Simulation;

namespace SmartMeterStudio.Tests;

internal static class Program
{
    private static int Main()
    {
        var checks = new (string Name, Action Run)[]
        {
            ("Tick produces electrical data", TickProducesElectricalReadingAndAccumulatesEnergy),
            ("Voltage dip modifies output", VoltageDipFaultReducesVoltageAndCreatesEvent),
            ("Stopped clock stays paused", StoppedMeterDoesNotAdvanceSimulationClock),
            ("Fleet creates configured meter", FleetCreatesMeterFromConfiguration)
        };

        try
        {
            foreach (var check in checks)
            {
                check.Run();
                Console.WriteLine($"PASS  {check.Name}");
            }
            Console.WriteLine($"{checks.Length} simulation checks passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL  {error.Message}");
            return 1;
        }
    }

    private static void TickProducesElectricalReadingAndAccumulatesEnergy()
    {
        var meter = CreateMeter();

        meter.Tick(10);

        var snapshot = meter.Snapshot();
        Require(snapshot.LatestReading is not null, "No reading was generated.");
        Require(snapshot.LatestReading.VoltageL1 > 0, "Voltage was not generated.");
        Require(snapshot.LatestReading.ImportEnergyKwh > 0, "Energy did not accumulate.");
        Require(snapshot.LatestReading.TariffRate is >= 1 and <= 3, "Tariff register is invalid.");
    }

    private static void VoltageDipFaultReducesVoltageAndCreatesEvent()
    {
        var meter = CreateMeter();
        meter.Tick(1);
        var normalVoltage = meter.Snapshot().LatestReading!.VoltageL1;

        meter.InjectFault(MeterFaultType.VoltageDip, 600);
        meter.Tick(1);

        var dippedVoltage = meter.Snapshot().LatestReading!.VoltageL1;
        Require(dippedVoltage < normalVoltage * 0.8, "Voltage dip did not reduce the simulated voltage.");
        Require(meter.Events().Any(item => item.Code == "FAULT_INJECTED"), "Fault event was not recorded.");
    }

    private static void StoppedMeterDoesNotAdvanceSimulationClock()
    {
        var meter = CreateMeter();
        meter.Stop();
        var stoppedAt = meter.Snapshot().SimulatedTime;

        meter.Tick(30);

        Require(stoppedAt == meter.Snapshot().SimulatedTime, "Stopped meter advanced its clock.");
    }

    private static void FleetCreatesMeterFromConfiguration()
    {
        var fleet = new SmartMeterFleet(seedDefaults: false);

        var created = fleet.Create(new CreateMeterRequest
        {
            Name = "Test feeder",
            PhaseMode = MeterPhaseMode.SinglePhase,
            BaseLoadKw = 4.5
        });

        Require(created.Definition.Name == "Test feeder", "Meter name was not retained.");
        Require(created.Definition.PhaseMode == MeterPhaseMode.SinglePhase, "Phase mode was not retained.");
        Require(fleet.GetSnapshots().Count == 1, "Fleet count is incorrect.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static VirtualSmartMeter CreateMeter() => new(new MeterDefinition
    {
        Id = "TEST-01",
        Name = "Test meter",
        Model = "Virtual",
        SerialNumber = "TEST0001",
        PhaseMode = MeterPhaseMode.ThreePhase,
        NominalVoltage = 230,
        BaseLoadKw = 12,
        NominalPowerFactor = 0.95,
        TariffPlan = "Test"
    }, new DateTimeOffset(2026, 9, 3, 18, 0, 0, TimeSpan.Zero));
}
