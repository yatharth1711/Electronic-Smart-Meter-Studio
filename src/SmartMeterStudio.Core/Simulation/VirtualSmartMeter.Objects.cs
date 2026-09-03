using SmartMeterStudio.Core.Models;

namespace SmartMeterStudio.Core.Simulation;

public sealed partial class VirtualSmartMeter
{
    public IReadOnlyList<ObisValue> Objects()
    {
        lock (_gate)
        {
            var values = new List<ObisValue>();
            var reading = _readings.LastOrDefault();
            var source = Category switch
            {
                MeterCategory.D1 => "Part 2 Tables A1/A12/A13 + A1",
                MeterCategory.D2 => "Part 2 Tables A14/A26/A27 + A1",
                MeterCategory.D3 or MeterCategory.D4 => "Part 3 Tables 1/12/13/14/25/26 (subset)",
                _ => "Common demo subset; Part 1 category-specific mapping not yet verified"
            };
            void Add(string code, string name, int ic, string unit, object? value, int attribute = 2) =>
                values.Add(new(code, name, ic, attribute, unit, value, source, value is null ? "No sample yet" : "Simulated value"));
            Add("0.0.1.0.0.255", "Clock", 8, "date-time", SimulatedTime);
            Add("0.0.96.1.0.255", "Serial number", 1, "text", Definition.SerialNumber);
            Add("0.0.96.1.2.255", "Device ID (simulator identifier)", 1, "text", "SMS" + Definition.SerialNumber);
            Add("0.0.96.1.1.255", "Manufacturer", 1, "text", "SmartMeter Studio (virtual)");
            Add("1.0.0.2.0.255", "Communication firmware", 1, "text", _firmwareVersion);
            Add("0.0.94.91.11.255", "Category", 1, "text", Category.ToString());
            var single = Definition.PhaseMode == MeterPhaseMode.SinglePhase;
            if (single)
            {
                Add("1.0.12.7.0.255", "Voltage", 3, "V", reading?.VoltageL1);
                Add("1.0.11.7.0.255", "Phase current", 3, "A", reading?.CurrentL1);
                Add("1.0.91.7.0.255", "Neutral current (balanced model)", 3, "A", reading?.CurrentL1);
            }
            else
            {
                Add("1.0.32.7.0.255", "Voltage R-N", 3, "V", reading?.VoltageL1);
                Add("1.0.52.7.0.255", "Voltage Y-N", 3, "V", reading?.VoltageL2);
                Add("1.0.72.7.0.255", "Voltage B-N", 3, "V", reading?.VoltageL3);
                Add("1.0.31.7.0.255", "Current R", 3, "A", reading?.CurrentL1);
                Add("1.0.51.7.0.255", "Current Y", 3, "A", reading?.CurrentL2);
                Add("1.0.71.7.0.255", "Current B", 3, "A", reading?.CurrentL3);
                Add("1.0.33.7.0.255", "Power factor R (magnitude)", 3, "ratio", reading?.PowerFactor);
                Add("1.0.53.7.0.255", "Power factor Y (magnitude)", 3, "ratio", reading?.PowerFactor);
                Add("1.0.73.7.0.255", "Power factor B (magnitude)", 3, "ratio", reading?.PowerFactor);
            }
            Add("1.0.13.7.0.255", "Power factor (magnitude)", 3, "ratio", reading?.PowerFactor);
            Add("1.0.3.7.0.255", "Import reactive power", 3, "kvar", reading is null ? null : Math.Max(0, reading.ReactivePowerKvar));
            Add("1.0.4.7.0.255", "Export reactive power", 3, "kvar", reading is null ? null : Math.Max(0, -reading.ReactivePowerKvar));
            for (var quadrant = 0; quadrant < 4; quadrant++)
                values.Add(new($"1.0.{quadrant + 5}.8.0.255", $"Reactive energy Q{quadrant + 1}", 3, 2,
                    "kvarh", _quadrantEnergy[quadrant], "Blue Book Ed.17 Part 1 Table 13 / Figure 1", "Simulator extension; category applicability unverified"));
            Add("1.0.3.8.0.255", "Import reactive energy (QI + QII)", 3, "kvarh", _quadrantEnergy[0] + _quadrantEnergy[1]);
            Add("1.0.4.8.0.255", "Export reactive energy (QIII + QIV)", 3, "kvarh", _quadrantEnergy[2] + _quadrantEnergy[3]);
            Add("1.0.14.7.0.255", "Frequency", 3, "Hz", reading?.FrequencyHz);
            Add("1.0.1.7.0.255", "Active power", 3, "kW", reading?.ActivePowerKw);
            Add("1.0.9.7.0.255", "Apparent power", 3, "kVA", reading is null ? null : Math.Abs(reading.ActivePowerKw) / reading.PowerFactor);
            Add("1.0.1.8.0.255", "Import active energy", 3, "kWh", _importEnergyKwh);
            Add("1.0.2.8.0.255", "Export active energy", 3, "kWh", _exportEnergyKwh);
            Add("1.0.9.8.0.255", "Import apparent energy", 3, "kVAh", _importKvah);
            Add("1.0.10.8.0.255", "Export apparent energy", 3, "kVAh", _exportKvah);
            Add("1.0.1.6.0.255", "Integrated maximum demand", 4, "kW", _maximumDemandKw);
            Add("1.0.1.6.0.255", "Maximum demand timestamp", 4, "date-time", _demandTimestamp, 5);
            Add("1.0.9.6.0.255", "Integrated apparent maximum demand", 4, "kVA", _maximumDemandKva);
            Add("0.0.0.1.0.255", "Billing count", 1, "count", _billingCount);
            Add("0.0.96.2.0.255", "Programming count", 1, "count", _programmingCount);
            if (single) Add("0.0.94.91.14.255", "Power on duration", 3, "min", _powerOnMinutes);
            Add("1.0.0.8.0.255", "Demand integration period", 1, "s", _settings.DemandPeriodSeconds);
            Add("1.0.0.8.4.255", "Block capture period", 1, "s", _settings.CapturePeriodSeconds);
            Add("1.0.0.8.5.255", "Daily capture period", 1, "s", 86400);
            if (SupportsRelay)
            {
                Add("0.0.96.3.10.255", "Load switch connected", 70, "boolean", _connected);
                Add("0.0.17.0.0.255", "Load limit", 71, "kW", _settings.LoadLimitKw, 3);
            }
            if (SupportsRelay)
            {
                Add("0.0.94.96.20.255", "Payment mode", 1, "enum", _prepayment.Prepaid ? 1 : 0);
                Add("0.0.94.96.21.255", "Last token recharge amount", 1, "HES currency units", _prepayment.LastRechargeAmount);
                Add("0.0.94.96.22.255", "Last token recharge time", 1, "date-time", _prepayment.LastRechargeTime);
                Add("0.0.94.96.23.255", "Total amount at last recharge", 1, "HES currency units", _prepayment.TotalAtLastRecharge);
                Add("0.0.94.96.24.255", "Current balance", 1, "HES currency units", _prepayment.CurrentBalance);
                Add("0.0.94.96.25.255", "Current balance time", 1, "date-time", _prepayment.BalanceTime);
            }
            if (IsSmart)
            {
                Add("0.0.96.13.1.255", "Utility message", 1, "text", _utilityMessage);
                Add("0.0.96.13.2.255", "Consumer message", 1, "text", _consumerMessage);
            }
            return values;
        }
    }
}
