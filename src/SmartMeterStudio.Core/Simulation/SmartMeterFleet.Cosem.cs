using System.Text.Json;
using SmartMeterStudio.Core.Models;

namespace SmartMeterStudio.Core.Simulation;

public sealed partial class SmartMeterFleet
{
    private VirtualSmartMeter CosemMeter(string id) => TryGet(id, out var meter) ? meter : throw new KeyNotFoundException("Meter not found.");
    public IReadOnlyList<CosemObject> GetCosemObjects(string id) => CosemMeter(id).CosemObjects();
    public CosemObject GetCosemObject(string id, string ln) => CosemMeter(id).CosemObject(ln);
    public CosemAttribute ReadCosemAttribute(string id, string ln, int index) => CosemMeter(id).ReadCosemAttribute(ln, index);
    public void CreateCosem(string id, CreateCosemObject request) => CosemMeter(id).CreateCosem(request);
    public void DeleteCosem(string id, string ln) => CosemMeter(id).DeleteCosem(ln);
    public void WriteCosem(string id, string ln, int attribute, JsonElement value) => CosemMeter(id).WriteCosem(ln, attribute, value);
    public void InvokeCosem(string id, string ln, int method, JsonElement parameter = default) => CosemMeter(id).InvokeCosem(ln, method, parameter);
    public ProfileConfiguration GetCosemProfileConfiguration(string id, string ln) => CosemMeter(id).CosemProfileConfiguration(ln);
    public void ConfigureCosemProfile(string id, string ln, ProfileConfiguration configuration) => CosemMeter(id).ConfigureCosemProfile(ln, configuration);
    public IReadOnlyList<ProfileRow> GetCosemProfileRows(string id, string ln, DateTimeOffset? from = null, DateTimeOffset? to = null, int start = 1, int count = 100) =>
        CosemMeter(id).ReadCosemProfile(ln, from, to, start, count);
}
