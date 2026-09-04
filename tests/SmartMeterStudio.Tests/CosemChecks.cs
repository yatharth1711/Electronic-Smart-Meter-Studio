using System.Text.Json;
using SmartMeterStudio.Core.Models;
using SmartMeterStudio.Core.Simulation;

namespace SmartMeterStudio.Tests;

internal static class CosemChecks
{
    private const string Data = "0.128.96.1.0.255", Register = "0.128.1.0.0.255", Clock = "0.0.1.0.0.255";
    private const string Profile = VirtualSmartMeter.DemoProfileLogicalName;
    public static (string Name, Action Run)[] All => [
        ("COSEM class/attribute directory", Directory), ("COSEM typed Data writes", DataWrites),
        ("COSEM Register scaling and reset", RegisterReset), ("COSEM Extended Register capture time", ExtendedRegister),
        ("COSEM built-ins reject mutation", ReadOnly), ("COSEM validates object identity", Identity),
        ("COSEM manual capture snapshots values", ManualCapture), ("COSEM interval captures and FIFO", AutomaticCapture),
        ("COSEM time and entry selection", Selection), ("COSEM profile configuration is atomic", Configuration),
        ("COSEM profile references prevent unsafe deletion", References), ("COSEM clock set and shift", ClockControl),
        ("COSEM clock jumps re-anchor captures", ClockJump), ("COSEM fault drift capture boundaries", ClockDrift),
        ("COSEM parallel read/capture safety", Concurrency), ("COSEM fleet isolation", FleetIsolation),
        ("COSEM Edition 17 zero-capacity profile", ZeroCapacity), ("COSEM Clock preset validity window", ClockPreset),
        ("COSEM Script Table execution and references", Scripts), ("COSEM script failure is explicit", ScriptFailure),
        ("COSEM Special Days insert, replace and delete", SpecialDays)
    ];
    private static JsonElement J<T>(T value) => JsonSerializer.SerializeToElement(value);
    private static void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidOperationException("Invalid operation unexpectedly succeeded.");
    }
    private static VirtualSmartMeter Meter() => new(new MeterDefinition
    {
        Id = "COSEM-TEST", Name = "COSEM test", SerialNumber = "COSEM01", PhaseMode = MeterPhaseMode.ThreePhase,
        BaseLoadKw = 12, NominalVoltage = 230, NominalPowerFactor = .95
    }, new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero));
    private static JsonElement Attr(VirtualSmartMeter meter, string ln, int index) => J(meter.CosemObject(ln).Attributes.Single(x => x.Index == index).Value);
    private static CaptureReference[] Captures() => [new(8, Clock), new(3, "1.0.1.8.0.255")];
    private static void Directory()
    {
        var m = Meter(); m.Tick(1);
        foreach (var obj in m.CosemObjects()) Check(obj.Attributes.Select(x => x.Index).Distinct().Count() == obj.Attributes.Count, "Duplicate attribute indices.");
        foreach (var (ln, count) in new[] { (Clock, 9), (Profile, 8), ("1.0.1.8.0.255", 3), ("1.0.1.6.0.255", 5), ("0.0.96.1.0.255", 2) })
            Check(m.CosemObject(ln).Attributes.Count == count, "Wrong attribute count.");
        Check(m.CosemObject(Clock).Methods.Count == 6 && m.CosemObject(Clock).Methods.All(x => x.Enabled), "Clock methods missing.");
        Check(CosemCatalog.Classes.Select(x => x.Id).Distinct().Count() == CosemCatalog.Classes.Count && CosemCatalog.Find(170).Version == 0, "Edition 17 catalog invalid.");
        Check(CosemCatalog.Find(64).Coverage.Contains("Catalog only"), "Security class incorrectly claims implementation.");
        Check(Attr(m, "1.0.1.8.0.255", 2).GetDouble() == (double)m.Objects().Single(x => x.Obis == "1.0.1.8.0.255").Value!, "Live registers diverged.");
        Check(Math.Abs(Attr(m, "1.0.1.8.0.255", 2).GetDouble() - m.Snapshot().LatestReading!.ImportEnergyKwh) <= .00005, "LCD rounding exceeded precision.");
    }
    private static void DataWrites()
    {
        var m = Meter(); m.CreateCosem(new(1, Data, "Message", J("hello")));
        m.WriteCosem(Data, 2, J("updated")); Check(Attr(m, Data, 2).GetString() == "updated", "String write failed.");
        Reject(() => m.WriteCosem(Data, 2, J(3))); Reject(() => m.WriteCosem(Data, 2, J(new[] { 1, 2 })));
        m.CreateCosem(new(1, Register, "Flag", J(true))); m.WriteCosem(Register, 2, J(false));
        Check(!Attr(m, Register, 2).GetBoolean(), "Boolean write failed.");
    }
    private static void RegisterReset()
    {
        var m = Meter(); m.CreateCosem(new(3, Register, "Voltage", J(23000), -2, 35));
        var unit = (ScalerUnit)m.CosemObject(Register).Attributes.Single(x => x.Index == 3).Value!;
        Check(Attr(m, Register, 2).GetDouble() * Math.Pow(10, unit.Scaler) == 230 && unit.Unit == 35, "Scaler/unit invalid.");
        m.InvokeCosem(Register, 1); Check(Attr(m, Register, 2).GetDouble() == 0, "Reset did not zero custom register.");
        Reject(() => m.WriteCosem(Register, 2, J("bad")));
    }
    private static void ExtendedRegister()
    {
        var m = Meter(); m.CreateCosem(new(4, Register, "Demand", J(4)));
        m.Tick(1); m.WriteCosem(Register, 2, J(5));
        Check(Attr(m, Register, 5).GetDateTimeOffset() == m.SimulatedTime, "Write timestamp wrong.");
        m.Tick(1); m.InvokeCosem(Register, 1);
        Check(Attr(m, Register, 5).GetDateTimeOffset() == m.SimulatedTime && Attr(m, Register, 2).GetDouble() == 0, "Reset timestamp wrong.");
    }
    private static void ReadOnly()
    {
        var m = Meter(); Reject(() => m.WriteCosem("0.0.96.1.0.255", 2, J("other")));
        Reject(() => m.WriteCosem("1.0.1.8.0.255", 2, J(999))); Reject(() => m.InvokeCosem("1.0.1.8.0.255", 1));
        Reject(() => m.DeleteCosem(Clock)); Reject(() => m.DeleteCosem(Profile)); Reject(() => m.InvokeCosem(Clock, 5));
    }
    private static void Identity()
    {
        var m = Meter(); m.CreateCosem(new(1, Data, "Data", J(1)));
        Reject(() => m.CreateCosem(new(1, Data, "Duplicate", J(1))));
        foreach (var ln in new[] { "0.01.2.3.4.255", "0.256.2.3.4.255", "0.0.0.0.0.0", "1.2" }) Reject(() => m.CreateCosem(new(1, ln, "Invalid")));
        Reject(() => m.CreateCosem(new(64, "0.128.0.0.0.255", "Unsupported")));
        Reject(() => m.CreateCosem(new(3, Register, "Invalid scaler", J(1), 13)));
    }
    private static void ManualCapture()
    {
        var m = Meter(); m.CreateCosem(new(1, Data, "Number", J(1)));
        m.ConfigureCosemProfile(Profile, new([new(1, Data)], 0, 3));
        m.Tick(100); Check(m.ReadCosemProfile(Profile).Count == 0, "Manual profile captured automatically.");
        m.InvokeCosem(Profile, 2); m.WriteCosem(Data, 2, J(2)); m.InvokeCosem(Profile, 2);
        var rows = m.ReadCosemProfile(Profile); Check(rows[0].Values[0].GetInt32() == 1 && rows[1].Values[0].GetInt32() == 2, "Rows are not immutable snapshots.");
        Check(J(m.ReadCosemAttribute(Profile, 2).Value).GetArrayLength() == 2, "Buffer attribute did not return captured rows.");
        m.InvokeCosem(Profile, 1); Check(m.ReadCosemProfile(Profile).Count == 0, "Profile reset failed.");
    }
    private static void AutomaticCapture()
    {
        var m = Meter(); m.ConfigureCosemProfile(Profile, new(Captures(), 60, 3)); m.Tick(5);
        var rows = m.ReadCosemProfile(Profile);
        Check(rows.Count == 3 && rows[0].Sequence == 3 && rows[2].Sequence == 5, "FIFO count/sequence wrong.");
        Check(rows[0].CapturedAt.Minute == 3 && rows[2].CapturedAt.Minute == 5, "Accelerated captures missed boundaries.");
        Check(rows[0].Values[1].GetDouble() < rows[2].Values[1].GetDouble(), "Captures duplicated final energy.");
        m.Stop(); m.Tick(100); Check(m.ReadCosemProfile(Profile).Last().Sequence == 5, "Paused profile captured.");
    }
    private static void Selection()
    {
        var m = Meter(); m.ConfigureCosemProfile(Profile, new(Captures(), 60, 10)); m.Tick(5);
        var all = m.ReadCosemProfile(Profile);
        Check(m.ReadCosemProfile(Profile, all[1].CapturedAt, all[3].CapturedAt).Count == 3, "Inclusive range wrong.");
        Check(m.ReadCosemProfile(Profile, start: 2, count: 1).Single().Sequence == 2, "Entry selection not 1-based.");
        Reject(() => m.ReadCosemProfile(Profile, start: 0)); Reject(() => m.ReadCosemProfile(Profile, count: 1001));
        Reject(() => m.ReadCosemProfile(Profile, all[3].CapturedAt, all[0].CapturedAt));
    }
    private static void Configuration()
    {
        var m = Meter(); m.Tick(15); var before = m.ReadCosemProfile(Profile).Count;
        foreach (var period in new[] { -1, 1, 86401 }) Reject(() => m.ConfigureCosemProfile(Profile, new(Captures(), period)));
        Reject(() => m.ConfigureCosemProfile(Profile, new([new(3, Clock)], 60)));
        Reject(() => m.ConfigureCosemProfile(Profile, new([new(7, Profile)], 60)));
        Reject(() => m.ConfigureCosemProfile(Profile, new([new(8, Clock, 2, 1)], 60)));
        Reject(() => m.ConfigureCosemProfile(Profile, new([new(8, Clock, 10)], 60)));
        Check(m.ReadCosemProfile(Profile).Count == before, "Invalid configuration destroyed rows.");
        var saved = m.CosemProfileConfiguration(Profile); saved.CaptureObjects[0] = new(64, "bad");
        Check(m.CosemProfileConfiguration(Profile).CaptureObjects[0].ClassId == 8, "Configuration escaped by reference.");
        var attrs = (CaptureReference[])m.CosemObject(Profile).Attributes.Single(x => x.Index == 3).Value!;
        attrs[0] = new(64, "bad"); Check(m.CosemProfileConfiguration(Profile).CaptureObjects[0].ClassId == 8, "Attribute escaped by reference.");
        m.ConfigureCosemProfile(Profile, m.CosemProfileConfiguration(Profile) with { CapturePeriod = 0 });
        Check(m.ReadCosemProfile(Profile).Count == before, "Changing period destroyed rows.");
        m.ConfigureCosemProfile(Profile, new(Captures(), 60, 2)); Check(m.ReadCosemProfile(Profile).Count == 0, "Column/capacity change retained incompatible rows.");
    }
    private static void References()
    {
        var m = Meter(); m.CreateCosem(new(1, Data, "Data", J(1))); m.ConfigureCosemProfile(Profile, new([new(1, Data)], 0));
        Reject(() => m.DeleteCosem(Data)); m.ConfigureCosemProfile(Profile, new(Captures(), 0)); m.DeleteCosem(Data);
        Reject(() => m.CosemObject(Data));
        m.CreateCosem(new(7, Data, "Custom profile")); Reject(() => m.InvokeCosem(Data, 2)); m.DeleteCosem(Data);
    }
    private static void ClockControl()
    {
        var m = Meter(); m.WriteCosem(Clock, 2, J("2026-09-04T12:00:20+05:30"));
        Check(Attr(m, Clock, 3).GetInt32() == -330, "Timezone sign incorrect.");
        m.InvokeCosem(Clock, 3); Check(m.SimulatedTime.Second == 0, "Minute adjustment failed.");
        m.InvokeCosem(Clock, 6, J(-30)); Check(m.SimulatedTime.Minute == 59 && m.SimulatedTime.Second == 30, "Shift failed.");
        Reject(() => m.InvokeCosem(Clock, 6, J(901))); Reject(() => m.InvokeCosem(Clock, 1, J(1)));
        Reject(() => m.WriteCosem(Clock, 2, J("2026-09-04T12:00:00"))); Reject(() => m.WriteCosem(Clock, 2, J("1999-01-01T00:00:00Z")));
    }
    private static void ClockJump()
    {
        var m = Meter(); m.ConfigureCosemProfile(Profile, new(Captures(), 60, 100)); m.Tick(2);
        m.SetClock(new(2026, 9, 5, 12, 0, 30, TimeSpan.Zero)); m.Tick(1);
        var rows = m.ReadCosemProfile(Profile); Check(rows.Count == 3 && rows.Last().CapturedAt.Hour == 12 && rows.Last().CapturedAt.Second == 0, "Clock jump backfilled or missed capture.");
        m.SetClock(new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero)); m.Tick(1);
        Check(m.ReadCosemProfile(Profile).Count == 4, "Backward clock jump did not resume capture.");
    }
    private static void ClockDrift()
    {
        var m = Meter(); m.ConfigureCosemProfile(Profile, new(Captures(), 60, 100)); m.InjectFault(MeterFaultType.ClockDrift, 600); m.Tick(5);
        Check(m.ReadCosemProfile(Profile).Count == 5 && m.ReadCosemProfile(Profile).All(x => x.CapturedAt.Second == 0), "Clock drift skipped profile boundaries.");
    }
    private static void Concurrency()
    {
        var m = Meter(); m.ConfigureCosemProfile(Profile, new(Captures(), 0, 100));
        Parallel.For(0, 50, _ => { m.Tick(.1); m.InvokeCosem(Profile, 2); _ = m.CosemObjects().Count; });
        Check(m.ReadCosemProfile(Profile).Count == 50, "Concurrent captures lost rows.");
    }
    private static void FleetIsolation()
    {
        var fleet = new SmartMeterFleet(); var meters = fleet.GetSnapshots();
        fleet.CreateCosem(meters[0].Definition.Id, new(1, Data, "Local", J(1)));
        Check(fleet.GetCosemObjects(meters[1].Definition.Id).All(x => x.LogicalName != Data), "Objects leaked between meters.");
        Reject(() => fleet.GetCosemObjects("missing"));
    }
    private static void ZeroCapacity()
    {
        var m = Meter(); m.ConfigureCosemProfile(Profile, new(Captures(), 60, 0)); m.Tick(3); m.InvokeCosem(Profile, 2);
        Check(m.ReadCosemProfile(Profile).Count == 0 && Attr(m, Profile, 7).GetInt32() == 0, "Zero-capacity buffer retained rows.");
        m.ConfigureCosemProfile(Profile, new(Captures(), 60, 1)); m.Tick(2);
        Check(m.ReadCosemProfile(Profile).Count == 1, "Capture did not resume after zero capacity.");
    }
    private static void ClockPreset()
    {
        var m = Meter(); var original = m.SimulatedTime;
        var preset = new CosemClockPreset(original.AddMinutes(5).ToString("O"), original.ToString("O"), original.AddMinutes(1).ToString("O"));
        m.InvokeCosem(Clock, 5, J(preset)); Check(m.SimulatedTime == original, "Staging preset changed current clock.");
        m.InvokeCosem(Clock, 4); Check(m.SimulatedTime == original.AddMinutes(5), "Valid preset not applied.");
        Reject(() => m.InvokeCosem(Clock, 4)); Reject(() => m.InvokeCosem(Clock, 5));
        Reject(() => m.InvokeCosem(Clock, 5, J(preset with { ValidityIntervalStart = original.AddDays(1).ToString("O") })));
        var instant = m.SimulatedTime; m.WriteCosem(Clock, 3, J(-330));
        Check(m.SimulatedTime == instant && m.SimulatedTime.Offset.TotalMinutes == 330, "Timezone write changed the UTC instant.");
        Reject(() => m.WriteCosem(Clock, 3, J(721)));
        Reject(() => m.WriteCosem(Clock, 2, J("2026-09-04T12:00:00-13:00")));
    }
    private static void Scripts()
    {
        var m = Meter(); m.CreateCosem(new(3, Register, "Test register", J(1))); m.CreateCosem(new(9, Data, "Scenario"));
        var actions = new CosemScriptAction[] { new(1, 3, Register, 2, J(42)), new(2, 7, Profile, 2, J(0)) };
        m.WriteCosem(Data, 2, J(new[] { new CosemScript(1, actions) })); m.InvokeCosem(Data, 1, J(1));
        Check(Attr(m, Register, 2).GetInt32() == 42 && m.ReadCosemProfile(Profile).Count == 1, "Script did not write and capture.");
        m.InvokeCosem(Data, 1, J(0)); Check(m.ReadCosemProfile(Profile).Count == 1, "Null script executed actions.");
        Reject(() => m.DeleteCosem(Register));
        Reject(() => m.WriteCosem(Data, 2, J(new[] { new CosemScript(1, [new(2, 9, Data, 1, J(1))]) })));
        Reject(() => m.WriteCosem(Data, 2, J(new[] { new CosemScript(1, [new(1, 1, "0.0.96.1.0.255", 2, J("overwrite"))]) })));
        m.WriteCosem(Data, 2, J(Array.Empty<CosemScript>())); m.DeleteCosem(Register);
    }
    private static void ScriptFailure()
    {
        var m = Meter(); m.CreateCosem(new(3, Register, "Register", J(1)));
        m.CreateCosem(new(9, Data, "Partial scenario", J(new[] { new CosemScript(1, [new(1, 3, Register, 2, J(2)), new(1, 3, Register, 2, J("wrong type"))]) })));
        try { m.InvokeCosem(Data, 1, J(1)); throw new InvalidOperationException("Script incorrectly succeeded."); }
        catch (ArgumentException e) { Check(e.Message.Contains("1 completed actions"), "Partial execution was not reported."); }
        Check(Attr(m, Register, 2).GetInt32() == 2, "Script failure policy changed.");
    }
    private static void SpecialDays()
    {
        var m = Meter(); m.CreateCosem(new(11, Data, "Holidays"));
        var day = new CosemSpecialDay(1, new(2026, 12, 25), 3);
        m.InvokeCosem(Data, 1, J(day)); m.InvokeCosem(Data, 1, J(day with { Index = 2, DayId = 4 }));
        var entries = Attr(m, Data, 2); Check(entries.GetArrayLength() == 1 && entries[0].GetProperty("dayId").GetInt32() == 4, "Same-date replacement failed.");
        Reject(() => m.InvokeCosem(Data, 1, J(day with { DayId = 256 })));
        Reject(() => m.WriteCosem(Data, 2, J(new[] { day, day })));
        m.InvokeCosem(Data, 2, J(2)); Check(Attr(m, Data, 2).GetArrayLength() == 0, "Special-day deletion failed.");
    }
}
