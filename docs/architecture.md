# Architecture

## Runtime flow

```text
Blazor operator console
        │
ASP.NET Core management API ── SignalR telemetry
        │
SmartMeterFleet
        │
VirtualSmartMeter state machines
        │
Protocol adapters ── REST today; DLMS/COSEM, MQTT and Modbus next
```

## Design boundaries

- **Core** has no UI, web-server or persistence dependencies. It owns electrical calculations and state transitions.
- **Web** is the composition root. It runs the background clock, exposes APIs, streams fleet state and persists meter definitions.
- **Protocol endpoints** translate core snapshots into a client-specific shape. They do not calculate meter values.
- **Time acceleration** is applied by each state machine, so meters can run at different speeds in the same process.
- **Faults** modify state-machine outputs for a bounded simulated duration and always create auditable events.

## Recommended next slices

1. Scenario documents with scheduled load, tariff, relay and tamper actions.
2. DLMS/COSEM TCP adapter backed by the same virtual register state.
3. Trace capture and deterministic replay from real field data.
4. Multi-process fleet workers for tens of thousands of meters.
5. SQLite or PostgreSQL time-series persistence and export.
6. Authentication and role-based access before shared-network deployment.

## Safety boundary

The management API is intentionally unauthenticated for local development. Do not expose it to an untrusted network until authentication, authorization, TLS and network controls are configured.
