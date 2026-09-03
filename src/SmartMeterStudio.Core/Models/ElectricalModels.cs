namespace SmartMeterStudio.Core.Models;

public sealed record ElectricalSettings(bool ExportActive = false, bool ExportReactive = false, bool SupplyAvailable = true);
public sealed record ElectricalSnapshot(ElectricalSettings Settings, double Quadrant1Kvarh,
    double Quadrant2Kvarh, double Quadrant3Kvarh, double Quadrant4Kvarh,
    double PowerOffMinutes, int SupplyInterruptions);
