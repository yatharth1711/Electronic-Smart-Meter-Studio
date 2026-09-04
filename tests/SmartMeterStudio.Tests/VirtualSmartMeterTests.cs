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
            ("Fleet creates configured meter", FleetCreatesMeterFromConfiguration),
            ("Aligned block profiles and integrated demand", ProfilesAndDemand),
            ("Capture changes clear old blocks", CaptureChanges),
            ("Daily captures occur at midnight", DailyCapture),
            ("Billing retains six cycles without resetting cumulative energy", BillingRetention),
            ("Relay is category restricted and stops load", RelayRules),
            ("HES balance is not locally decremented", PrepaymentFields),
            ("Firmware verification gates activation", FirmwareRules),
            ("Invalid inputs leave state unchanged", InvalidInputs),
            ("Amended OBIS IDs and preview-only push", ObjectsAndPush),
            ("Time range filtering", SelectiveProfileRead),
            ("Clock changes reset partial integrations", ClockChange),
            ("D4 and D2 amendment-specific capture rules", CategoryCaptureRules),
            ("Clock drift preserves capture boundaries", ClockDriftCapture),
            ("Messages enforce 128-byte limit", MessageByteLimit),
            ("Monthly boundary closes billing automatically", MonthlyBilling),
            ("Independent power directions accumulate four quadrants", FourQuadrants),
            ("Supply failure stops consumption but preserves the clock", SupplyFailure),
            ("Relay and supply failure remain independent", SupplyAndRelay)
        };

        checks = [.. checks, .. CosemChecks.All, .. ProtocolChecks.All];
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

    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ProfilesAndDemand()
    {
        var meter = CreateMeter();
        meter.Tick(60); // 3600 simulated seconds, two aligned half-hour captures.
        var blocks = meter.Survey(SurveyKind.Block);
        Require(blocks.Count == 2, "Missing block boundaries during an accelerated tick.");
        Require(blocks.All(x => x.Timestamp.Minute is 0 or 30), "Block timestamps are not aligned.");
        Require(blocks.All(x => x.CaptureQuality == "Complete interval"), "Aligned startup incorrectly marked partial.");
        Require(Math.Abs(blocks.Sum(x => x.ImportKwh) - meter.Snapshot().LatestReading!.ImportEnergyKwh) < .01, "Block energies do not reconcile.");
        Require(Math.Abs(meter.Companion().MaximumDemandKw - blocks.Max(x => x.ImportKwh * 2)) < 1e-9, "Demand is not integrated over its period.");
        var stepped = CreateMeter();
        for (var i = 0; i < 60; i++) stepped.Tick(1);
        Require(Math.Abs(stepped.Companion().MaximumDemandKw - meter.Companion().MaximumDemandKw) < 1e-9, "Accelerated boundary processing differs from stepped ticks.");
    }

    private static void CaptureChanges()
    {
        var meter = CreateMeter(); meter.Tick(60);
        meter.ConfigureCompanion(new() { CapturePeriodSeconds = 900 });
        Require(meter.Survey(SurveyKind.Block).Count == 0, "Old block data survived capture period change.");
        Require(meter.Companion().ProgrammingCount == 1, "Programming count did not increment exactly once.");
        meter.Tick(15);
        Require(meter.Survey(SurveyKind.Block).Count == 1, "New capture period was not applied.");
        Require(meter.Companion().Transactions.Any(x => x.EventId == 153), "Capture-period event missing.");
    }

    private static void DailyCapture()
    {
        var meter = CreateMeter(); meter.Tick(360); // 18:00 -> midnight
        var daily = meter.Survey(SurveyKind.Daily);
        Require(daily.Count == 1 && daily[0].Timestamp.TimeOfDay == TimeSpan.Zero, "Daily capture is not at midnight.");
        Require(Math.Abs(daily[0].ImportKwh - meter.Snapshot().LatestReading!.ImportEnergyKwh) < .001, "Daily energy is not cumulative.");
    }

    private static void BillingRetention()
    {
        var meter = CreateMeter(); meter.Tick(60);
        var cumulative = meter.Snapshot().LatestReading!.ImportEnergyKwh;
        for (var i = 0; i < 8; i++) meter.CloseBilling();
        Require(meter.Survey(SurveyKind.Billing).Count == 6 && meter.Companion().BillingCount == 8, "Billing retention/count incorrect.");
        Require(meter.Companion().MaximumDemandKw == 0, "Billing did not reset MD.");
        Require(meter.Snapshot().LatestReading!.ImportEnergyKwh == cumulative, "Billing reset cumulative energy.");
    }

    private static void RelayRules()
    {
        var meter = CreateMeter(); meter.Tick(1); var energy = meter.Snapshot().LatestReading!.ImportEnergyKwh;
        meter.SetRelay(false); meter.Tick(30);
        Require(meter.Snapshot().LatestReading!.ActivePowerKw == 0, "Disconnected meter still has load.");
        Require(meter.Snapshot().LatestReading!.ImportEnergyKwh == energy, "Disconnected meter consumed energy.");
        Require(meter.Companion().Transactions.Any(x => x.EventId == 301), "Disconnect event is missing.");
        var transformer = new VirtualSmartMeter(new() { Id = "CT", Category = MeterCategory.D3, PhaseMode = MeterPhaseMode.ThreePhase });
        Reject(() => transformer.SetRelay(false));
        Require(transformer.Objects().All(x => x.ClassId is not (70 or 71)), "Transformer meter exposes load control objects.");
    }

    private static void PrepaymentFields()
    {
        var meter = CreateMeter(); meter.SetPrepayment(new(true, 100, null, 120, 50)); meter.Tick(120);
        Require(meter.Companion().Prepayment.CurrentBalance == 50, "Meter locally debited HES balance.");
        Require(meter.Companion().Transactions.Any(x => x.EventId == 212), "Payment-mode event missing.");
    }

    private static void FirmwareRules()
    {
        var meter = CreateMeter(); byte[] data = [1, 2, 3, 4, 5];
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
        Reject(meter.ActivateFirmware);
        meter.BeginFirmware("TEST-V2", data.Length, hash);
        Reject(() => meter.WriteFirmwareBlock(1, [1]));
        meter.WriteFirmwareBlock(0, [1, 2]); Reject(meter.VerifyFirmware);
        meter.WriteFirmwareBlock(2, [3, 4, 5]); meter.VerifyFirmware(); meter.ActivateFirmware();
        Require(meter.Companion().Firmware.Version == "TEST-V2", "Verified firmware version not activated.");
        Require(meter.Companion().Transactions.Any(x => x.EventId == 157), "Firmware activation event missing.");
        meter.BeginFirmware("BAD", 1, new string('0', 64)); meter.WriteFirmwareBlock(0, [1]);
        Reject(meter.VerifyFirmware); Reject(meter.ActivateFirmware);
    }

    private static void InvalidInputs()
    {
        var meter = CreateMeter();
        Reject(() => meter.ConfigureCompanion(new() { CapturePeriodSeconds = 123 }));
        Reject(() => meter.ConfigureCompanion(new() { LoadLimitKw = double.NaN }));
        Reject(() => meter.SetTimeScale(double.NaN)); Reject(() => meter.Tick(double.PositiveInfinity));
        Require(meter.Companion().ProgrammingCount == 0, "Invalid settings changed the meter.");
        var fleet = new SmartMeterFleet(seedDefaults: false);
        Reject(() => fleet.Create(new() { BaseLoadKw = double.NaN }));
        Reject(() => fleet.Create(new() { Category = MeterCategory.D1, PhaseMode = MeterPhaseMode.ThreePhase }));
        Require(fleet.GetSnapshots().Count == 0, "Invalid creation modified the fleet.");
    }

    private static void ObjectsAndPush()
    {
        var meter = CreateMeter(); meter.Tick(1); meter.PreviewPush(); meter.SendMessage(true, "Test utility message");
        Require(meter.Companion().PushQueue.Any(x => x.SetupObis == "0.0.25.9.0.255"), "Amended HES push ID missing.");
        Require(meter.Companion().PushQueue.Any(x => x.SetupObis == "0.1.25.9.0.255"), "Amended IHD push ID missing.");
        Require(meter.Companion().PushQueue.All(x => x.Delivery.Contains("not transmitted")), "Preview claims external delivery.");
        Require(meter.Objects().Any(x => x.Obis == "1.0.31.7.0.255" && x.ClassId == 3), "Three phase current mapping missing.");
    }

    private static void SelectiveProfileRead()
    {
        var meter = CreateMeter(); meter.Tick(60); var time = meter.Survey(SurveyKind.Block)[0].Timestamp;
        Require(meter.Survey(SurveyKind.Block, time, time).Count == 1, "Inclusive selection failed.");
        Reject(() => meter.Survey(SurveyKind.Block, time.AddHours(1), time));
    }

    private static void ClockChange()
    {
        var meter = CreateMeter(); meter.Tick(1);
        meter.SetClock(new(2026, 9, 5, 0, 5, 0, TimeSpan.Zero)); meter.Tick(25);
        Require(meter.Survey(SurveyKind.Block).Last().CaptureQuality.StartsWith("Partial"), "Clock change falsely created a full interval.");
        Require(meter.Companion().Transactions.Any(x => x.EventId == 151), "Clock programming event missing.");
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected operation to be rejected.");
    }

    private static void CategoryCaptureRules()
    {
        var d4 = new VirtualSmartMeter(new() { Id = "D4", Category = MeterCategory.D4, PhaseMode = MeterPhaseMode.ThreePhase });
        Require(d4.Companion().Settings.DemandPeriodSeconds == 900, "D4 interface demand is not 15 minutes.");
        Reject(() => d4.ConfigureCompanion(new()));
        Reject(() => d4.SetPrepayment(new(true)));
        Reject(() => CreateMeter().ConfigureCompanion(new() { CapturePeriodSeconds = 3600 }));
    }

    private static void ClockDriftCapture()
    {
        var meter = CreateMeter(); meter.InjectFault(MeterFaultType.ClockDrift, 7200); meter.Tick(60);
        var blocks = meter.Survey(SurveyKind.Block);
        Require(blocks.Count == 2, "Clock drift skipped capture boundaries.");
        Require(blocks.All(x => x.Timestamp.Second == 0 && x.Timestamp.Minute is 0 or 30), "Drifted profiles are unaligned.");
    }

    private static void MessageByteLimit()
    {
        var meter = CreateMeter(); Reject(() => meter.SendMessage(true, new string('€', 50)));
        meter.SendMessage(true, new string('a', 128));
        Require(meter.Companion().UtilityMessage.Length == 128, "128-byte message rejected.");
    }

    private static void MonthlyBilling()
    {
        var meter = CreateMeter(); meter.SetClock(new(2026, 9, 30, 23, 45, 0, TimeSpan.Zero));
        meter.Tick(15);
        Require(meter.Companion().BillingCount == 1, "Month boundary did not close billing.");
        Require(meter.Survey(SurveyKind.Billing)[0].Timestamp == new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), "Monthly billing timestamp is wrong.");
    }

    private static void FourQuadrants()
    {
        foreach (var (active, reactive, quadrant) in new[] { (false, false, 0), (true, false, 1), (true, true, 2), (false, true, 3) })
        {
            var meter = CreateMeter();
            meter.ConfigureElectrical(new(active, reactive)); meter.Tick(10);
            var reading = meter.Snapshot().LatestReading!;
            Require((reading.ActivePowerKw < 0) == active, "Active direction wrong.");
            Require((reading.ReactivePowerKvar < 0) == reactive, "Reactive direction wrong.");
            var state = meter.Electrical();
            var registers = new[] { state.Quadrant1Kvarh, state.Quadrant2Kvarh, state.Quadrant3Kvarh, state.Quadrant4Kvarh };
            Require(registers[quadrant] > 0 && registers.Where((_, i) => i != quadrant).All(x => x == 0), "Energy leaked into another quadrant.");
            Require((active ? meter.Companion().ExportKvah : meter.Companion().ImportKvah) > 0, "Apparent energy direction wrong.");
            Require(meter.Objects().Any(x => x.Obis == $"1.0.{quadrant + 5}.8.0.255" && Equals(x.Value, registers[quadrant])), "Quadrant OBIS missing.");
        }
    }

    private static void SupplyFailure()
    {
        var meter = CreateMeter(); meter.Tick(1);
        var before = meter.Snapshot(); var powerOn = meter.Companion().PowerOnMinutes;
        meter.ConfigureElectrical(new(SupplyAvailable: false));
        meter.ConfigureElectrical(new(SupplyAvailable: false)); meter.Tick(30);
        var after = meter.Snapshot();
        Require(after.SimulatedTime > before.SimulatedTime, "Backup clock stopped.");
        Require(after.LatestReading!.VoltageL1 == 0 && after.LatestReading.CurrentL1 == 0 && after.LatestReading.FrequencyHz == 0, "Outage electrical values not zero.");
        Require(after.LatestReading.ImportEnergyKwh == before.LatestReading!.ImportEnergyKwh, "Outage consumed energy.");
        Require(meter.Companion().PowerOnMinutes == powerOn, "Outage counted as power-on.");
        Require(meter.Electrical().SupplyInterruptions == 1 && Math.Abs(meter.Electrical().PowerOffMinutes - 30) < .001, "Outage counters incorrect.");
        meter.ConfigureElectrical(new()); meter.Tick(1);
        Require(meter.Snapshot().LatestReading!.ImportEnergyKwh > after.LatestReading.ImportEnergyKwh, "Supply restoration failed.");
    }

    private static void SupplyAndRelay()
    {
        var meter = CreateMeter(); meter.SetRelay(false); meter.Tick(1);
        Require(meter.Snapshot().LatestReading!.VoltageL1 > 0 && meter.Companion().PowerOnMinutes > 0, "Relay wrongly removed supply.");
        meter.ConfigureElectrical(new(SupplyAvailable: false)); meter.Tick(1);
        meter.ConfigureElectrical(new()); meter.Tick(1);
        Require(meter.Snapshot().LatestReading!.ActivePowerKw == 0, "Restoring supply bypassed disconnected relay.");
        meter.SetRelay(true); meter.ConfigureElectrical(new(true, true)); meter.InjectFault(MeterFaultType.ReverseEnergy, 120); meter.Tick(1);
        Require(meter.Snapshot().LatestReading!.ActivePowerKw < 0, "Reverse fault inverted export back to import.");
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
