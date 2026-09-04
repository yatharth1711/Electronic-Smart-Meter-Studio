using System.Security.Cryptography;
using SmartMeterStudio.Core.Models;

namespace SmartMeterStudio.Core.Simulation;

// This is a simulator domain model, not a DLMS server or a conformance declaration.
public sealed partial class VirtualSmartMeter
{
    private CompanionSettings _settings = new();
    private readonly Queue<SurveyEntry> _blocks = new(), _days = new(), _bills = new();
    private readonly Queue<CompanionEvent> _transactions = new();
    private readonly Queue<PushNotification> _push = new();
    private bool _connected = true;
    private int _programmingCount, _billingCount;
    private double _importKvah, _exportKvah, _powerOnMinutes, _billPowerOnStart;
    private double _blockImport, _blockExport, _blockImportVa, _blockExportVa, _blockVoltage, _blockCurrent, _blockSeconds;
    private double _demandImport, _demandVa, _demandSeconds, _maximumDemandKva;
    private bool _partialBlock = true, _partialDemand = true;
    private DateTimeOffset? _demandTimestamp;
    private PrepaymentState _prepayment = new();
    private string _utilityMessage = "", _consumerMessage = "", _firmwareVersion = "SIM-COMM-1.0";
    private string _imageState = "Idle", _imageId = "", _expectedHash = "";
    private byte[]? _image;
    private int _received;

    public MeterCategory Category => Definition.Category == MeterCategory.Automatic
        ? Definition.PhaseMode == MeterPhaseMode.SinglePhase ? MeterCategory.D1 : MeterCategory.D2
        : Definition.Category;
    private bool IsSmart => Category is MeterCategory.D1 or MeterCategory.D2 or MeterCategory.D3 or MeterCategory.D4;
    private bool SupportsRelay => Category is MeterCategory.D1 or MeterCategory.D2;

    public CompanionSnapshot Companion()
    {
        lock (_gate) return new(Category, Category is MeterCategory.D3 or MeterCategory.D4 ? 3 : IsSmart ? 2 : 1,
            SupportsRelay, _connected, _settings, _programmingCount, _billingCount, _maximumDemandKw,
            _maximumDemandKva, _demandTimestamp, _blocks.Count, _days.Count, _bills.Count, _importKvah,
            _exportKvah, _powerOnMinutes, _prepayment, new(_firmwareVersion, _imageState, _imageId,
                _image?.Length ?? 0, _received, _expectedHash), _utilityMessage, _consumerMessage,
            _transactions.Reverse().ToArray(), _push.Reverse().ToArray());
    }

    private double NextCompanionStep()
    {
        var withinDay = SimulatedTime.TimeOfDay.Ticks;
        static double Until(long t, int period) => (period * TimeSpan.TicksPerSecond - t % (period * TimeSpan.TicksPerSecond)) / (double)TimeSpan.TicksPerSecond;
        return Math.Min(60, Math.Min(Until(withinDay, _settings.CapturePeriodSeconds),
            Math.Min(Until(withinDay, _settings.DemandPeriodSeconds), Until(withinDay, 86400))));
    }

    private void ObserveCompanion(MeterReading reading, double seconds)
    {
        var apparent = Math.Abs(reading.ActivePowerKw) / Math.Max(.01, reading.PowerFactor);
        var import = Math.Max(0, reading.ActivePowerKw) * seconds / 3600;
        var export = Math.Max(0, -reading.ActivePowerKw) * seconds / 3600;
        var importVa = reading.ActivePowerKw >= 0 ? apparent * seconds / 3600 : 0;
        var exportVa = reading.ActivePowerKw < 0 ? apparent * seconds / 3600 : 0;
        _importKvah += importVa; _exportKvah += exportVa;
        if (_electrical.SupplyAvailable) _powerOnMinutes += seconds / 60;
        _blockImport += import; _blockExport += export; _blockImportVa += importVa; _blockExportVa += exportVa;
        _blockVoltage += reading.VoltageL1 * seconds; _blockCurrent += reading.CurrentL1 * seconds; _blockSeconds += seconds;
        _demandImport += import; _demandVa += importVa; _demandSeconds += seconds;
        var t = SimulatedTime.TimeOfDay.TotalSeconds;
        if (AtBoundary(t, _settings.DemandPeriodSeconds))
        {
            if (!_partialDemand && _demandSeconds > 0)
            {
                var kw = _demandImport * 3600 / _demandSeconds;
                var kva = _demandVa * 3600 / _demandSeconds;
                if (kw > _maximumDemandKw) { _maximumDemandKw = kw; _demandTimestamp = SimulatedTime; }
                _maximumDemandKva = Math.Max(_maximumDemandKva, kva);
            }
            _demandImport = _demandVa = _demandSeconds = 0; _partialDemand = false;
        }
        if (AtBoundary(t, _settings.CapturePeriodSeconds))
        {
            Enqueue(_blocks, Entry(_blockImport, _blockExport, _blockImportVa, _blockExportVa,
                _partialBlock ? "Partial interval after start/configuration/clock change" : "Complete interval"),
                35 * 86400 / _settings.CapturePeriodSeconds);
            ResetBlock(); _partialBlock = false;
        }
        if (AtBoundary(t, 86400))
        {
            Enqueue(_days, Entry(_importEnergyKwh, _exportEnergyKwh, _importKvah, _exportKvah, "Cumulative at midnight"), 35);
            if (SimulatedTime.Day == 1) CloseBillingCore();
        }
        if (SupportsRelay && _settings.LoadLimitEnabled && _connected && Math.Abs(reading.ActivePowerKw) > _settings.LoadLimitKw)
        {
            _connected = false;
            Transaction(215, "Overload occurrence (instant-trip simulation policy)", false);
            Transaction(301, "Load switch disconnected by load limit", false);
        }
    }

    private static bool AtBoundary(double seconds, int period) => Math.Abs(seconds % period) < .00001;
    private void ResetBlock() => _blockImport = _blockExport = _blockImportVa = _blockExportVa = _blockVoltage = _blockCurrent = _blockSeconds = 0;
    private SurveyEntry Entry(double import, double export, double importVa, double exportVa, string quality) =>
        new(SimulatedTime, import, export, importVa, exportVa,
            _blockSeconds > 0 ? _blockVoltage / _blockSeconds : 0,
            _blockSeconds > 0 ? _blockCurrent / _blockSeconds : 0,
            _maximumDemandKw, _maximumDemandKva, _demandTimestamp, _powerOnMinutes, quality);

    public IReadOnlyList<SurveyEntry> Survey(SurveyKind kind, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown survey kind.");
        if (from > to) throw new ArgumentException("Start must be before end.");
        lock (_gate)
        {
            var source = kind switch { SurveyKind.Block => _blocks, SurveyKind.Daily => _days, _ => _bills };
            return source.Where(x => (!from.HasValue || x.Timestamp >= from) && (!to.HasValue || x.Timestamp <= to)).ToArray();
        }
    }

    public void ConfigureCompanion(CompanionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.CapturePeriodSeconds is not (900 or 1800 or 3600)) throw new ArgumentException("Capture period must be 900, 1800 or 3600 seconds.");
        if (settings.DemandPeriodSeconds is not (900 or 1800)) throw new ArgumentException("Demand integration must be 900 or 1800 seconds.");
        if (Category is MeterCategory.D2 or MeterCategory.D3 && settings.CapturePeriodSeconds == 3600) throw new ArgumentException("D2/D3 capture must be 900 or 1800 seconds under the supplied amendment baseline.");
        if (Category == MeterCategory.D4 && (settings.CapturePeriodSeconds != 900 || settings.DemandPeriodSeconds != 900)) throw new ArgumentException("D4 currently uses the fixed 15-minute interface-meter simulation profile.");
        if (!double.IsFinite(settings.LoadLimitKw) || settings.LoadLimitKw <= 0 || settings.LoadLimitKw > 10000) throw new ArgumentException("Load limit must be finite and between 0 and 10,000 kW.");
        if (!SupportsRelay && settings.LoadLimitEnabled) throw new ArgumentException("Load control is available only for D1/D2 meters.");
        lock (_gate)
        {
            if (settings.CapturePeriodSeconds != _settings.CapturePeriodSeconds)
            {
                _blocks.Clear(); ResetBlock(); _partialBlock = true;
                Transaction(153, "Profile capture period changed; previous blocks cleared");
            }
            if (settings.DemandPeriodSeconds != _settings.DemandPeriodSeconds)
            {
                _demandImport = _demandVa = _demandSeconds = 0; _partialDemand = true;
                Transaction(152, "Demand integration period changed");
            }
            if (SupportsRelay && settings.LoadLimitKw != _settings.LoadLimitKw) Transaction(158, "Load limit set");
            if (SupportsRelay && settings.LoadLimitEnabled != _settings.LoadLimitEnabled) Transaction(settings.LoadLimitEnabled ? 159 : 160, "Load limit enable state changed");
            _settings = settings;
        }
    }

    public void SetRelay(bool connected)
    {
        if (!SupportsRelay) throw new ArgumentException("Connect/disconnect is not applicable to this category (Part 3 clause 10). Choose D1 or D2.");
        lock (_gate)
        {
            if (_connected == connected) return;
            _connected = connected;
            Transaction(connected ? 302 : 301, connected ? "Load switch connected" : "Load switch disconnected", false);
        }
    }

    public void CloseBilling() { lock (_gate) { CloseBillingCore(); Transaction(166, "Manual maximum demand reset", false); } }
    private void CloseBillingCore()
    {
        Enqueue(_bills, Entry(_importEnergyKwh, _exportEnergyKwh, _importKvah, _exportKvah, "Closed billing cycle")
            with { PowerOnMinutes = _powerOnMinutes - _billPowerOnStart }, 6);
        _billingCount++; _billPowerOnStart = _powerOnMinutes;
        _maximumDemandKw = _maximumDemandKva = _demandImport = _demandVa = _demandSeconds = 0;
        _demandTimestamp = null; _partialDemand = !AtBoundary(SimulatedTime.TimeOfDay.TotalSeconds, _settings.DemandPeriodSeconds);
    }

    public void SetClock(DateTimeOffset time)
    {
        if (time.Year is < 2000 or > 2099) throw new ArgumentException("Simulation clock must be between 2000 and 2099.");
        if (time.Offset.TotalMinutes is < -720 or > 840) throw new ArgumentException("Clock UTC offset must be between -12:00 and +14:00.");
        lock (_gate)
        {
            SimulatedTime = time; _activeFault = null; _readings.Clear();
            ReanchorCosemProfiles();
            ResetBlock(); _demandImport = _demandVa = _demandSeconds = 0;
            _partialBlock = _partialDemand = true;
            Transaction(151, "Clock set; current partial integrations reset; historical profiles retained");
        }
    }

    public void SetPrepayment(PrepaymentState state)
    {
        if (!SupportsRelay) throw new ArgumentException("HES payment fields are implemented only for D1/D2 (Part 2 Amendment 1).");
        ArgumentNullException.ThrowIfNull(state);
        if (state.LastRechargeAmount < 0 || state.TotalAtLastRecharge < 0) throw new ArgumentException("Recharge amounts cannot be negative.");
        lock (_gate)
        {
            if (state.Prepaid != _prepayment.Prepaid) Transaction(state.Prepaid ? 212 : 211, "Payment mode changed by simulated HES");
            else _programmingCount++;
            _prepayment = state; // HES-owned fields: never decrement locally.
        }
    }

    public void SendMessage(bool fromUtility, string message)
    {
        RequireSmart();
        if (string.IsNullOrWhiteSpace(message) || System.Text.Encoding.UTF8.GetByteCount(message) > 128) throw new ArgumentException("Message must contain 1 to 128 UTF-8 bytes (Part 2 clause 6.1.6).");
        lock (_gate)
        {
            if (fromUtility) _utilityMessage = message; else _consumerMessage = message;
            Enqueue(_push, new(SimulatedTime, fromUtility ? "0.1.25.9.0.255" : "0.2.25.9.0.255",
                fromUtility ? "Utility to IHD" : "Consumer to HES", "Preview only; not transmitted",
                [new(fromUtility ? "0.0.96.13.1.255" : "0.0.96.13.2.255", "Special message", 1, 2, "text", message, "Part 2 clause 6 + A1")]), 50);
        }
    }

    public void PreviewPush()
    {
        RequireSmart();
        lock (_gate) Enqueue(_push, new(SimulatedTime, "0.0.25.9.0.255", "Manual data preview", "Preview only; not transmitted",
            Objects().Where(x => x.Obis is "0.0.96.1.2.255" or "0.0.1.0.0.255" or "1.0.1.7.0.255" or "1.0.1.8.0.255").ToArray()), 50);
    }

    private void Transaction(int id, string description, bool programming = true)
    {
        if (programming) _programmingCount++;
        Enqueue(_transactions, new(SimulatedTime, id, description), 50);
    }
    private void RequireSmart() { if (!IsSmart) throw new ArgumentException("This service requires category D1, D2, D3 or D4."); }

    public void BeginFirmware(string imageId, int size, string sha256)
    {
        RequireSmart();
        if (string.IsNullOrWhiteSpace(imageId) || imageId.Length > 64) throw new ArgumentException("Image identifier must be 1 to 64 characters.");
        if (size is < 1 or > 1048576) throw new ArgumentException("Simulated image size must be 1 byte to 1 MiB.");
        if (sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) throw new ArgumentException("Supply a 64-character SHA-256 digest.");
        lock (_gate) { _image = new byte[size]; _received = 0; _imageId = imageId; _expectedHash = sha256; _imageState = "Transfer initiated"; }
    }

    public void WriteFirmwareBlock(int offset, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        lock (_gate)
        {
            if (_image is null || _imageState != "Transfer initiated") throw new ArgumentException("Initiate a new transfer first.");
            if (offset != _received || bytes.Length is < 1 or > 4096 || bytes.Length > _image.Length - _received) throw new ArgumentException("Blocks must be sequential, at most 4096 bytes, and within the declared image length.");
            bytes.CopyTo(_image, _received); _received += bytes.Length;
        }
    }

    public void VerifyFirmware()
    {
        lock (_gate)
        {
            if (_image is null || _received != _image.Length) throw new ArgumentException("All image bytes must be received before verification.");
            if (!Convert.ToHexString(SHA256.HashData(_image)).Equals(_expectedHash, StringComparison.OrdinalIgnoreCase))
            { _imageState = "Verification failed"; throw new ArgumentException("Image checksum mismatch. Initiate a new transfer."); }
            _imageState = "Verified";
        }
    }

    public void ActivateFirmware()
    {
        lock (_gate)
        {
            if (_imageState != "Verified") throw new ArgumentException("Only a verified image can be activated.");
            _firmwareVersion = _imageId; _imageState = "Activated (simulation only)";
            _image = null; _received = 0;
            Transaction(157, "Communication firmware version activated (no executable code loaded)");
        }
    }
}
