using System.Globalization;
using System.Text.Json;
using SmartMeterStudio.Core.Models;

namespace SmartMeterStudio.Core.Simulation;

public sealed partial class VirtualSmartMeter
{
    private readonly Dictionary<string, CustomCosemState> _customCosem = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProfileState> _cosemProfiles = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions CosemJson = new(JsonSerializerDefaults.Web);
    public const string DemoProfileLogicalName = "0.128.99.1.0.255";

    private sealed class CustomCosemState(CreateCosemObject request, JsonElement value, DateTimeOffset time)
    {
        public CreateCosemObject Definition { get; } = request;
        public JsonElement Value { get; set; } = value;
        public DateTimeOffset CaptureTime { get; set; } = time;
    }
    private sealed class ProfileState(string name, ProfileConfiguration configuration)
    {
        public string Name { get; } = name;
        public ProfileConfiguration Configuration { get; set; } = configuration;
        public Queue<ProfileRow> Rows { get; } = new();
        public DateTimeOffset? NextCapture { get; set; }
        public long Sequence { get; set; }
    }

    private void InitializeCosem()
    {
        var profile = new ProfileState("Studio load capture (manufacturer-specific)", new([
            new(8, "0.0.1.0.0.255"), new(3, "1.0.1.8.0.255"), new(3, "1.0.1.7.0.255") ]));
        _cosemProfiles.Add(DemoProfileLogicalName, profile);
        ScheduleProfile(profile);
    }

    public IReadOnlyList<CosemObject> CosemObjects()
    {
        lock (_gate)
        {
            var result = Objects().GroupBy(x => x.Obis).Select(BuildLiveObject).ToList();
            result.AddRange(_customCosem.Select(pair => BuildCustomObject(pair.Key, pair.Value)));
            result.AddRange(_cosemProfiles.Select(pair => BuildProfileObject(pair.Key, pair.Value)));
            return result.OrderBy(x => x.ClassId).ThenBy(x => x.LogicalName, StringComparer.Ordinal).ToArray();
        }
    }

    public CosemObject CosemObject(string logicalName)
    {
        lock (_gate)
            return CosemObjects().FirstOrDefault(x => x.LogicalName == logicalName)
                ?? throw new KeyNotFoundException("COSEM object not found.");
    }

    public CosemAttribute ReadCosemAttribute(string ln, int index)
    {
        lock (_gate)
        {
            var obj = CosemObject(ln);
            var attribute = obj.Attributes.FirstOrDefault(x => x.Index == index) ?? throw new KeyNotFoundException("Attribute not found.");
            return obj.ClassId == 7 && index == 2
                ? attribute with { Value = GetCosemProfile(ln).Rows.Select(r => r.Values.ToArray()).ToArray(), Note = "Complete FIFO buffer. Use the profile endpoint for filtered reads with capture metadata." }
                : attribute;
        }
    }

    private CosemObject BuildLiveObject(IGrouping<string, ObisValue> items)
    {
        var first = items.First();
        var attributes = new List<CosemAttribute> { new(1, "logical_name", "octet-string (displayed as OBIS)", false, first.Obis) };
        var methods = new List<CosemMethod>();
        foreach (var item in items)
            attributes.Add(new(item.Attribute, item.Attribute == 5 ? "capture_time" : "value", "engineering value", false, item.Value, item.Unit));
        if (first.ClassId is 3 or 4)
        {
            attributes.Add(new(3, "scaler_unit", "structure {scaler, unit}", false, EngineeringUnit(first.Unit), "Engineering value × 10^scaler in the COSEM base unit."));
            methods.Add(new(1, "reset", false, "0", "Live measurement resets are disabled; use existing billing controls or a custom test register."));
            if (first.ClassId == 4)
            {
                attributes.Add(new(4, "status", "unsigned", false, 0, "Simulator instance policy: 0 = normal; not a complete quality model."));
                if (attributes.All(x => x.Index != 5)) attributes.Add(new(5, "capture_time", "date-time", false, null, "This live register does not yet track its own capture time."));
            }
        }
        if (first.ClassId == 8)
        {
            attributes.RemoveAll(x => x.Index == 2);
            attributes.AddRange([
                new(2, "time", "date-time (ISO 8601 with offset)", true, SimulatedTime),
                new(3, "time_zone", "long", true, -(int)SimulatedTime.Offset.TotalMinutes, "Local-to-UTC minutes (-840..720); UTC+05:30 = -330. Changes offset while preserving the instant."),
                new(4, "status", "unsigned", false, 0, "Software clock; fault-quality flags not modelled."),
                new(5, "daylight_savings_begin", "date-time", false, null, "DST scheduling not implemented."),
                new(6, "daylight_savings_end", "date-time", false, null, "DST scheduling not implemented."),
                new(7, "daylight_savings_deviation", "integer", false, 0, "DST disabled."),
                new(8, "daylight_savings_enabled", "boolean", false, false),
                new(9, "clock_base", "enum", false, 0, "Undefined: virtual software clock.") ]);
            methods.AddRange([
                new(1, "adjust_to_quarter", true, "0", "Nearest quarter; midpoint rounds forward."),
                new(2, "adjust_to_measuring_period", true, "0", "Nearest configured demand-period boundary."),
                new(3, "adjust_to_minute", true, "0", "Nearest minute; midpoint rounds forward."),
                new(4, "adjust_to_preset_time", true, "0", "Apply saved time only inside the preset validity window."),
                new(5, "preset_adjusting_time", true, "{presetTime, validityIntervalStart, validityIntervalEnd}", "ISO timestamps with explicit offsets; staging does not change the clock."),
                new(6, "shift_time", true, "integer seconds (-900 to 900)") ]);
        }
        var definition = CosemCatalog.Find(first.ClassId);
        return new(first.Obis, first.Name, first.ClassId, definition.Version, false, definition.Coverage,
            attributes.OrderBy(x => x.Index).ToArray(), methods);
    }

    private static ScalerUnit EngineeringUnit(string unit) => unit switch
    {
        "kW" => new(3, 27), "kVA" => new(3, 28), "kvar" => new(3, 29),
        "kWh" => new(3, 30), "kVAh" => new(3, 31), "kvarh" => new(3, 32),
        "A" => new(0, 33), "V" => new(0, 35), "Hz" => new(0, 44), "min" => new(0, 6),
        _ => new(0, 255)
    };

    private static CosemObject BuildCustomObject(string ln, CustomCosemState state)
    {
        var d = state.Definition;
        var attrs = new List<CosemAttribute> {
            new(1, "logical_name", "octet-string (displayed as OBIS)", false, ln),
            new(2, d.ClassId == 9 ? "scripts" : d.ClassId == 11 ? "entries" : "value",
                d.ClassId is 9 or 11 ? "array (JSON management form)" : d.ClassId == 1 ? $"JSON scalar ({state.Value.ValueKind})" : "float64", true, state.Value) };
        if (d.ClassId is 3 or 4) attrs.Add(new(3, "scaler_unit", "structure {scaler, unit}", false,
            new ScalerUnit(d.Scaler, d.Unit), "Engineering result = value × 10^scaler; configure scaler/unit at creation."));
        if (d.ClassId == 4) attrs.AddRange([
            new(4, "status", "unsigned", false, 0, "Instance policy: 0 = normal."),
            new(5, "capture_time", "date-time", false, state.CaptureTime, "Time of last write or reset.") ]);
        return new(ln, d.Name, d.ClassId, 0, true, CosemCatalog.Subset, attrs,
            d.ClassId switch {
                1 => [],
                9 => [new(1, "execute", true, "script ID (0 = no-op)", "Sequential local writes/actions; no nested scripts or external calls. Earlier actions remain applied if a later action fails.")],
                11 => [new(1, "insert", true, "{index, specialdayDate, dayId}", "Exact YYYY-MM-DD dates only; same index or date replaces the existing entry."), new(2, "delete", true, "entry index", "Table management only; Schedule/Activity Calendar integration pending.")],
                _ => [new(1, "reset", true, "0", "This test-register instance resets to zero.")]
            });
    }

    private static CosemObject BuildProfileObject(string ln, ProfileState state) => new(ln, state.Name, 7, 1,
        ln != DemoProfileLogicalName, CosemCatalog.Subset,
        [new(1, "logical_name", "octet-string (displayed as OBIS)", false, ln),
         new(2, "buffer", "array of rows", false, new { entries = state.Rows.Count }, "Read rows through the buffer panel or profile endpoint."),
         new(3, "capture_objects", "array of capture references", true, state.Configuration.CaptureObjects.ToArray(), "Use profile configuration; changing columns clears the buffer."),
         new(4, "capture_period", "double-long-unsigned", true, state.Configuration.CapturePeriod, "Seconds; 0 = manual. Use profile configuration."),
         new(5, "sort_method", "enum", false, 1, "FIFO only."),
         new(6, "sort_object", "capture reference", false, new CaptureReference(0, "0.0.0.0.0.0", 0), "Unused for FIFO."),
         new(7, "entries_in_use", "double-long-unsigned", false, state.Rows.Count),
         new(8, "profile_entries", "double-long-unsigned", true, state.Configuration.Capacity, "Use profile configuration; capacity changes clear the buffer.")],
        [new(1, "reset", true, "0", "Clear captured rows."), new(2, "capture", true, "0", "Atomically sample configured attributes.")]);

    public void CreateCosem(CreateCosemObject request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            ValidateLogicalName(request.LogicalName);
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100) throw new ArgumentException("Name must be 1–100 characters.");
            if (request.ClassId is not (1 or 3 or 4 or 7 or 9 or 11)) throw new NotSupportedException("Create supports classes 1, 3, 4, 7, 9 and 11. The meter already owns its Clock object.");
            if (_customCosem.Count + _cosemProfiles.Count >= 64) throw new ArgumentException("Maximum 64 local objects/profiles per meter.");
            if (CosemObjects().Any(x => x.LogicalName == request.LogicalName)) throw new ArgumentException("Logical name already exists on this meter.");
            if (request.Scaler is < -12 or > 12 || request.Unit is < 0 or > 255) throw new ArgumentException("Simulator scaler range is -12..12; unit is 0..255.");
            if (request.ClassId == 7)
                _cosemProfiles.Add(request.LogicalName, new(request.Name, new([], 0, 100)));
            else
            {
                var value = request.Value.ValueKind == JsonValueKind.Undefined ?
                    (request.ClassId is 9 or 11 ? JsonSerializer.SerializeToElement(Array.Empty<object>()) : JsonSerializer.SerializeToElement(0)) : request.Value.Clone();
                if (request.ClassId is 9 or 11) ValidateStructuredObject(request.ClassId, value);
                else ValidateScalar(value, request.ClassId);
                _customCosem.Add(request.LogicalName, new(request with { Value = value }, value, SimulatedTime));
            }
            AddEvent("COSEM_CREATED", $"Created class {request.ClassId} object {request.LogicalName}.", "Info");
        }
    }

    private static void ValidateLogicalName(string ln)
    {
        if (string.IsNullOrWhiteSpace(ln)) throw new ArgumentException("Logical name is required.");
        var parts = ln.Split('.');
        if (parts.Length != 6 || parts.Any(p => !byte.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || p != n.ToString(CultureInfo.InvariantCulture)))
            throw new ArgumentException("Use six decimal bytes (0..255), e.g. 0.128.96.1.0.255; no leading zeros.");
        if (ln == "0.0.0.0.0.0") throw new ArgumentException("The null logical name is reserved.");
    }

    private static void ValidateScalar(JsonElement value, int classId)
    {
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException("This simulator supports scalar strings, finite numbers and booleans only.");
        if (classId != 1 && value.ValueKind != JsonValueKind.Number) throw new ArgumentException("Registers require a numeric value.");
        if (value.ValueKind == JsonValueKind.Number && (!value.TryGetDouble(out var number) || !double.IsFinite(number))) throw new ArgumentException("Number must be finite.");
        if (value.GetRawText().Length > 4096) throw new ArgumentException("Value exceeds the simulator's 4096-character limit.");
    }

    public void DeleteCosem(string ln)
    {
        lock (_gate)
        {
            if (!CosemObject(ln).Custom) throw new NotSupportedException("Built-in meter objects cannot be deleted.");
            if (_cosemProfiles.Values.Any(p => p.Configuration.CaptureObjects.Any(c => c.LogicalName == ln)))
                throw new ArgumentException("Remove this object from profile capture lists before deleting it.");
            if (_customCosem.Values.Where(s => s.Definition.ClassId == 9).Any(s => Decode<CosemScript[]>(s.Value).Any(script => script.Actions.Any(a => a.LogicalName == ln))))
                throw new ArgumentException("Remove this object from script actions before deleting it.");
            _customCosem.Remove(ln); _cosemProfiles.Remove(ln);
            AddEvent("COSEM_DELETED", $"Deleted test object {ln}.", "Info");
        }
    }

    public void WriteCosem(string ln, int attribute, JsonElement value)
    {
        lock (_gate)
        {
            var obj = CosemObject(ln);
            var descriptor = obj.Attributes.FirstOrDefault(x => x.Index == attribute) ?? throw new ArgumentException("Attribute does not exist.");
            if (!descriptor.Writable) throw new NotSupportedException("This attribute is read-only in the simulator.");
            if (_customCosem.TryGetValue(ln, out var custom) && attribute == 2)
            {
                if (obj.ClassId is 9 or 11) ValidateStructuredObject(obj.ClassId, value);
                else ValidateScalar(value, obj.ClassId);
                static JsonValueKind Kind(JsonValueKind kind) => kind is JsonValueKind.True or JsonValueKind.False ? JsonValueKind.True : kind;
                if (Kind(value.ValueKind) != Kind(custom.Value.ValueKind)) throw new ArgumentException("Data type cannot change after object creation.");
                custom.Value = value.Clone(); custom.CaptureTime = SimulatedTime;
            }
            else if (obj.ClassId == 8 && attribute == 2)
            {
                SetClock(ParseCosemClockTime(value));
            }
            else if (obj.ClassId == 8 && attribute == 3)
            {
                var zone = IntegerParameter(value);
                if (zone is < -840 or > 720) throw new ArgumentException("Time zone must be between -840 and 720 local-to-UTC minutes.");
                SetClock(SimulatedTime.ToOffset(TimeSpan.FromMinutes(-zone)));
            }
            else if (obj.ClassId == 7)
            {
                var config = _cosemProfiles[ln].Configuration;
                if (attribute == 3)
                {
                    CaptureReference[] captures;
                    try { captures = value.Deserialize<CaptureReference[]>(CosemJson) ?? throw new ArgumentException("Capture list is required."); }
                    catch (JsonException) { throw new ArgumentException("Invalid capture reference array."); }
                    ConfigureCosemProfile(ln, config with { CaptureObjects = captures });
                }
                else
                {
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number)) throw new ArgumentException("Expected an integer.");
                    ConfigureCosemProfile(ln, attribute == 4 ? config with { CapturePeriod = number } : config with { Capacity = number });
                }
            }
            else throw new NotSupportedException("Write not implemented.");
            AddEvent("COSEM_WRITE", $"Updated {ln} attribute {attribute}.", "Info");
        }
    }

    private static bool HasOffset(string text) => text.EndsWith('Z') ||
        (text.Length >= 6 && text[^3] == ':' && text[^6] is '+' or '-');

    public void InvokeCosem(string ln, int method, JsonElement parameter = default)
    {
        lock (_gate)
        {
            var obj = CosemObject(ln);
            var action = obj.Methods.FirstOrDefault(x => x.Index == method) ?? throw new ArgumentException("Method does not exist.");
            if (!action.Enabled) throw new NotSupportedException(action.Note);
            if (obj.ClassId == 8 && method == 5)
            {
                PresetCosemClock(parameter);
                AddEvent("COSEM_CLOCK_PRESET", "Clock preset and validity window stored; current clock unchanged.", "Info");
                return;
            }
            if (obj.ClassId == 11 && method == 1)
            {
                InsertSpecialDay(ln, parameter);
                AddEvent("COSEM_SPECIAL_DAY", $"Inserted special day into {ln}.", "Info");
                return;
            }
            var argument = 0;
            if (parameter.ValueKind != JsonValueKind.Undefined && (parameter.ValueKind != JsonValueKind.Number || !parameter.TryGetInt32(out argument)))
                throw new ArgumentException("Method parameter must be an integer.");
            if (!(obj.ClassId == 8 && method == 6) && obj.ClassId is not (9 or 11) && argument != 0) throw new ArgumentException("This method takes the dummy parameter 0.");
            if (obj.ClassId == 9) ExecuteCosemScript(ln, argument);
            else if (obj.ClassId == 11) DeleteSpecialDay(ln, argument);
            else if (obj.ClassId == 7)
            {
                var profile = _cosemProfiles[ln];
                if (method == 1) { profile.Rows.Clear(); profile.Sequence = 0; }
                else CaptureProfile(profile);
            }
            else if (obj.ClassId is 3 or 4)
            {
                var custom = _customCosem[ln]; custom.Value = JsonSerializer.SerializeToElement(0); custom.CaptureTime = SimulatedTime;
            }
            else if (obj.ClassId == 8)
            {
                if (method == 4)
                {
                    if (_cosemClockPreset is not { } preset || SimulatedTime < preset.From || SimulatedTime > preset.To)
                        throw new ArgumentException("No clock preset is valid at the current simulated time.");
                    SetClock(preset.Time);
                }
                else if (method == 6)
                {
                    if (argument is < -900 or > 900) throw new ArgumentException("Clock shift must be between -900 and 900 seconds.");
                    SetClock(SimulatedTime.AddSeconds(argument));
                }
                else
                {
                    var period = method == 1 ? 900 : method == 2 ? _settings.DemandPeriodSeconds : 60;
                    var ticks = (long)(Math.Floor(SimulatedTime.TimeOfDay.TotalSeconds / period + .5) * period * TimeSpan.TicksPerSecond);
                    SetClock(new DateTimeOffset(SimulatedTime.Date, SimulatedTime.Offset).AddTicks(ticks));
                }
            }
            AddEvent("COSEM_ACTION", $"Invoked {ln} method {method}.", "Info");
        }
    }

    public ProfileConfiguration CosemProfileConfiguration(string ln)
    {
        lock (_gate) { var config = GetCosemProfile(ln).Configuration; return config with { CaptureObjects = config.CaptureObjects.ToArray() }; }
    }
    public void ConfigureCosemProfile(string ln, ProfileConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        lock (_gate)
        {
            var state = GetCosemProfile(ln);
            if (configuration.CapturePeriod is < 0 or > 86400 || (configuration.CapturePeriod is > 0 and < 10))
                throw new ArgumentException("Simulator capture period: 0 (manual), or 10..86400 seconds.");
            if (configuration.Capacity is < 0 or > 1000) throw new ArgumentException("Profile capacity must be 0..1000 rows; 0 retains no entries (Edition 17).");
            if (configuration.CaptureObjects is null || configuration.CaptureObjects.Length is < 1 or > 16)
                throw new ArgumentException("Select 1..16 capture attributes.");
            var references = configuration.CaptureObjects.ToArray();
            var objects = CosemObjects();
            foreach (var reference in references)
            {
                if (reference is null) throw new ArgumentException("Capture references cannot be null.");
                var obj = objects.FirstOrDefault(x => x.LogicalName == reference.LogicalName);
                if (obj is null || obj.ClassId != reference.ClassId) throw new ArgumentException("Capture class ID and logical name must match an existing object.");
                if (reference.DataIndex != 0 || obj.ClassId == 7) throw new NotSupportedException("Only whole attributes (data_index 0) from non-profile objects can be captured.");
                if (!obj.Attributes.Any(x => x.Index == reference.AttributeIndex)) throw new ArgumentException("Capture attribute does not exist.");
            }
            if (references.Distinct().Count() != references.Length) throw new ArgumentException("Duplicate capture references are not allowed.");
            if (!state.Configuration.CaptureObjects.SequenceEqual(references) || state.Configuration.Capacity != configuration.Capacity)
            { state.Rows.Clear(); state.Sequence = 0; }
            state.Configuration = configuration with { CaptureObjects = references };
            ScheduleProfile(state);
            AddEvent("COSEM_PROFILE_CONFIGURED", $"Configured {ln}.", "Info");
        }
    }
    public IReadOnlyList<ProfileRow> ReadCosemProfile(string ln, DateTimeOffset? from = null, DateTimeOffset? to = null, int start = 1, int count = 100)
    {
        if (start < 1 || count is < 1 or > 1000 || (from.HasValue && to.HasValue && from > to)) throw new ArgumentException("Use a valid time range, start >= 1 and count 1..1000.");
        lock (_gate)
            return GetCosemProfile(ln).Rows.Where(r => (!from.HasValue || r.CapturedAt >= from) && (!to.HasValue || r.CapturedAt <= to))
                .Skip(start - 1).Take(count).Select(r => r with { Values = Array.AsReadOnly(r.Values.ToArray()) }).ToArray();
    }
    private ProfileState GetCosemProfile(string ln) => _cosemProfiles.TryGetValue(ln, out var state) ? state : throw new KeyNotFoundException("Profile not found.");
    private void ScheduleProfile(ProfileState profile)
    {
        var period = profile.Configuration.CapturePeriod;
        profile.NextCapture = period == 0 ? null : SimulatedTime.AddSeconds(period - SimulatedTime.TimeOfDay.TotalSeconds % period);
    }
    private void ReanchorCosemProfiles() { foreach (var profile in _cosemProfiles.Values) ScheduleProfile(profile); }
    private double NextCosemStep()
    {
        var next = _cosemProfiles.Values.Where(p => p.NextCapture.HasValue).Select(p => (p.NextCapture!.Value - SimulatedTime).TotalSeconds);
        return Math.Max(.000001, next.DefaultIfEmpty(60).Min());
    }
    private void ObserveCosemProfiles()
    {
        foreach (var profile in _cosemProfiles.Values)
            if (profile.NextCapture is { } next && SimulatedTime >= next)
            {
                CaptureProfile(profile);
                profile.NextCapture = next.AddSeconds(profile.Configuration.CapturePeriod);
            }
    }
    private void CaptureProfile(ProfileState profile)
    {
        if (profile.Configuration.CaptureObjects.Length == 0) throw new ArgumentException("Configure capture objects before capturing.");
        var objects = CosemObjects();
        var cells = profile.Configuration.CaptureObjects.Select(reference =>
            JsonSerializer.SerializeToElement(objects.First(x => x.LogicalName == reference.LogicalName)
                .Attributes.First(x => x.Index == reference.AttributeIndex).Value, CosemJson)).ToArray();
        Enqueue(profile.Rows, new ProfileRow(++profile.Sequence, SimulatedTime, Array.AsReadOnly(cells)), profile.Configuration.Capacity);
    }
}
