using System.Text.Json;
using System.Text.Json.Serialization;
using SmartMeterStudio.Core.Models;
using SmartMeterStudio.Core.Persistence;

namespace SmartMeterStudio.Web.Infrastructure;

public sealed class JsonMeterDefinitionStore : IMeterDefinitionStore
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonMeterDefinitionStore(IConfiguration configuration)
    {
        var configuredPath = configuration["SmartMeterStudio:DataPath"];
        var dataDirectory = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartMeterStudio")
            : Path.GetFullPath(configuredPath);
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "meters.json");
    }

    public IReadOnlyList<MeterDefinition> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath)) return [];
            try
            {
                return JsonSerializer.Deserialize<List<MeterDefinition>>(File.ReadAllText(_filePath), JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                var backup = _filePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Copy(_filePath, backup, overwrite: false);
                return [];
            }
        }
    }

    public void Save(IEnumerable<MeterDefinition> definitions)
    {
        lock (_gate)
        {
            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(definitions.OrderBy(item => item.Id), JsonOptions));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
    }
}
