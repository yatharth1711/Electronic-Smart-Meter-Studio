using SmartMeterStudio.Core.Models;

namespace SmartMeterStudio.Core.Simulation;

public sealed partial class VirtualSmartMeter
{
    private ElectricalSettings _electrical = new();
    private readonly double[] _quadrantEnergy = new double[4];
    private double _powerOffMinutes;
    private int _supplyInterruptions;

    public ElectricalSnapshot Electrical()
    {
        lock (_gate) return new(_electrical, _quadrantEnergy[0], _quadrantEnergy[1],
            _quadrantEnergy[2], _quadrantEnergy[3], _powerOffMinutes, _supplyInterruptions);
    }

    public void ConfigureElectrical(ElectricalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            if (settings == _electrical) return;
            if (settings.SupplyAvailable != _electrical.SupplyAvailable)
            {
                if (!settings.SupplyAvailable) _supplyInterruptions++;
                AddEvent(settings.SupplyAvailable ? "SUPPLY_RESTORED" : "SUPPLY_INTERRUPTED",
                    settings.SupplyAvailable ? "Simulated supply restored." : "Simulated supply interrupted; RTC continues on backup.",
                    settings.SupplyAvailable ? "Info" : "Warning");
            }
            if (settings.ExportActive != _electrical.ExportActive || settings.ExportReactive != _electrical.ExportReactive)
                AddEvent("POWER_DIRECTION_CHANGED", "Active/reactive simulation directions changed.", "Info");
            _electrical = settings;
        }
    }
}
