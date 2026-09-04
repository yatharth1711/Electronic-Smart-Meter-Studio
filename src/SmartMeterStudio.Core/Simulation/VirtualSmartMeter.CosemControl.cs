using System.Text.Json;
using SmartMeterStudio.Core.Models;

namespace SmartMeterStudio.Core.Simulation;

public sealed partial class VirtualSmartMeter
{
    private (DateTimeOffset Time, DateTimeOffset From, DateTimeOffset To)? _cosemClockPreset;

    private static T Decode<T>(JsonElement value) where T : class
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) throw new ArgumentException("A structured JSON parameter is required.");
        try { return value.Deserialize<T>(CosemJson) ?? throw new ArgumentException("A non-null JSON value is required."); }
        catch (JsonException e) { throw new ArgumentException("Invalid structured parameter: " + e.Message); }
    }
    private static int IntegerParameter(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number)) throw new ArgumentException("Expected an integer parameter.");
        return number;
    }
    private static DateTimeOffset ParseCosemClockTime(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || !HasOffset(value.GetString()!) || !value.TryGetDateTimeOffset(out var time))
            throw new ArgumentException("Use an ISO 8601 timestamp with Z or a ±HH:mm offset.");
        if (time.Year is < 2000 or > 2099 || time.Offset.TotalMinutes is < -720 or > 840)
            throw new ArgumentException("Clock time must be in 2000..2099, with a UTC offset between -12:00 and +14:00.");
        return time;
    }
    private void PresetCosemClock(JsonElement parameter)
    {
        var request = Decode<CosemClockPreset>(parameter);
        var time = ParseCosemClockTime(JsonSerializer.SerializeToElement(request.PresetTime));
        var from = ParseCosemClockTime(JsonSerializer.SerializeToElement(request.ValidityIntervalStart));
        var to = ParseCosemClockTime(JsonSerializer.SerializeToElement(request.ValidityIntervalEnd));
        if (from > to) throw new ArgumentException("Preset validity start must not be after its end.");
        _cosemClockPreset = (time, from, to);
    }
    private void ValidateStructuredObject(int classId, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetRawText().Length > 65536) throw new ArgumentException("Expected a JSON array up to 64 KiB.");
        if (classId == 11) { ValidateSpecialDays(Decode<CosemSpecialDay[]>(value)); return; }
        var scripts = Decode<CosemScript[]>(value);
        if (scripts.Length > 16) throw new ArgumentException("A test script table supports at most 16 scripts.");
        var ids = new HashSet<int>();
        foreach (var script in scripts)
        {
            if (script is null || script.ScriptIdentifier is < 1 or > 65535 || !ids.Add(script.ScriptIdentifier)) throw new ArgumentException("Script IDs must be unique, 1..65535 (0 is reserved for the null script).");
            if (script.Actions is null || script.Actions.Length > 16) throw new ArgumentException("Each script requires an action array of at most 16 actions.");
            foreach (var action in script.Actions)
            {
                if (action is null || action.ServiceId is not (1 or 2)) throw new ArgumentException("Script serviceId must be 1 (write) or 2 (method). Dummy actions are not supported.");
                var target = CosemObject(action.LogicalName);
                if (target.ClassId != action.ClassId) throw new ArgumentException("Script target class and logical name do not match.");
                if (action.ClassId == 9) throw new NotSupportedException("Nested scripts and script-table rewrites are disabled to prevent recursion.");
                if (action.ServiceId == 1 && !target.Attributes.Any(a => a.Index == action.Index && a.Writable)) throw new NotSupportedException("Script target attribute is not writable.");
                if (action.ServiceId == 2 && !target.Methods.Any(m => m.Index == action.Index && m.Enabled)) throw new NotSupportedException("Script target method is not implemented.");
                if (action.Parameter.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Every script action requires a parameter.");
            }
        }
    }
    private void ExecuteCosemScript(string ln, int id)
    {
        if (id is < 0 or > 65535) throw new ArgumentException("Script ID must be 0..65535.");
        if (id == 0) return;
        var scripts = Decode<CosemScript[]>(_customCosem[ln].Value);
        var script = scripts.FirstOrDefault(s => s.ScriptIdentifier == id) ?? throw new ArgumentException("Script ID not found.");
        var completed = 0;
        try
        {
            foreach (var action in script.Actions)
            {
                if (action.ServiceId == 1) WriteCosem(action.LogicalName, action.Index, action.Parameter);
                else InvokeCosem(action.LogicalName, action.Index, action.Parameter);
                completed++;
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or KeyNotFoundException)
        { throw new ArgumentException($"Script stopped after {completed} completed actions (not rolled back): {e.Message}"); }
    }
    private static void ValidateSpecialDays(CosemSpecialDay[] days)
    {
        if (days.Length > 100) throw new ArgumentException("At most 100 special days per test table.");
        var indices = new HashSet<int>(); var dates = new HashSet<DateOnly>();
        foreach (var day in days)
            if (day is null || day.Index is < 0 or > 65535 || day.DayId is < 0 or > 255 || day.SpecialdayDate.Year is < 2000 or > 2099 || !indices.Add(day.Index) || !dates.Add(day.SpecialdayDate))
                throw new ArgumentException("Special days require unique indices (0..65535) and exact dates in 2000..2099, with dayId 0..255. Date wildcards are not implemented.");
    }
    private void InsertSpecialDay(string ln, JsonElement parameter)
    {
        var entry = Decode<CosemSpecialDay>(parameter); ValidateSpecialDays([entry]);
        var state = _customCosem[ln];
        var days = Decode<CosemSpecialDay[]>(state.Value).Where(x => x.Index != entry.Index && x.SpecialdayDate != entry.SpecialdayDate).Append(entry).OrderBy(x => x.Index).ToArray();
        ValidateSpecialDays(days); state.Value = JsonSerializer.SerializeToElement(days, CosemJson);
    }
    private void DeleteSpecialDay(string ln, int index)
    {
        if (index is < 0 or > 65535) throw new ArgumentException("Special day index must be 0..65535.");
        var state = _customCosem[ln]; var days = Decode<CosemSpecialDay[]>(state.Value);
        if (!days.Any(x => x.Index == index)) throw new ArgumentException("Special day index not found.");
        state.Value = JsonSerializer.SerializeToElement(days.Where(x => x.Index != index).ToArray(), CosemJson);
    }
}
