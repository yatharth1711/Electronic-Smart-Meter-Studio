using SmartMeterStudio.Core.Models;

namespace SmartMeterStudio.Core.Persistence;

public interface IMeterDefinitionStore
{
    IReadOnlyList<MeterDefinition> Load();
    void Save(IEnumerable<MeterDefinition> definitions);
}
