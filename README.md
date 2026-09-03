# SmartMeter Studio

SmartMeter Studio is a software-defined laboratory for electronic smart meters. It lets development and QA teams create virtual meters, accelerate simulated time, generate realistic electrical data, inject controlled failures, and exercise client applications without physical meter hardware.

## Working MVP

- Single-phase and three-phase virtual meters
- Deterministic daily load curves
- Voltage, current, frequency, active/reactive power, power factor and THD
- Import/export energy, maximum demand and time-of-use tariff registers
- Time acceleration from real time to 3,600×
- Voltage dip/swell, phase loss, low power factor, harmonic burst, reverse energy, communication dropout and clock drift
- Browser-based fleet console with live trend visualization
- REST management API and SignalR live-update hub
- Meter-like endpoints suitable for EMMS integration
- JSON-backed meter-definition persistence under `%LocalAppData%\SmartMeterStudio`
- Automated simulation-engine tests

## Run locally

Requirements: .NET 10 SDK.

```powershell
dotnet restore .\SmartMeterStudio.slnx
dotnet run --project .\src\SmartMeterStudio.Web
```

Open `http://localhost:5182`.

Run the dependency-free simulation checks:

```powershell
dotnet run --project .\tests\SmartMeterStudio.Tests
```

## API examples

List the virtual fleet:

```http
GET /api/meters
```

Inject a five-minute simulated voltage dip:

```http
POST /api/meters/MTR-0001/faults
Content-Type: application/json

{ "type": "VoltageDip", "durationSeconds": 300 }
```

Read a virtual meter as a client application:

```http
GET /sim/MTR-0001/api/v1/read-instant
GET /sim/MTR-0001/api/v1/read-bill
GET /sim/MTR-0001/api/v1/read-tamper
GET /sim/MTR-0001/api/v1/status-datacollection
```

## Architecture

`SmartMeterStudio.Core` contains the UI-independent state machines, register calculations and fleet orchestration. `SmartMeterStudio.Web` hosts those state machines, exposes protocols and provides the Blazor control surface. Protocols should remain adapters around the core so future DLMS/COSEM, MQTT or Modbus support does not leak into simulation rules.

See [docs/architecture.md](docs/architecture.md) for the extension plan and boundaries.

## Scope

This product simulates meter data, behavior and communications for software development. It is not a calibrated source and does not replace metrology or certification equipment.
