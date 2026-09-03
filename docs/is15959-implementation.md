# IS 15959 implementation ledger

## Status

**Partial simulator implementation. Not a DLMS/COSEM server, not a complete implementation
of IS 15959, and not a conformance or certification claim.**

The workspace at `/standards` and `GET /api/standards/coverage` expose the current
coverage ledger. Do not interpret an object shown in the inspector as a full
COSEM interface-class implementation. Values are engineering-domain values over REST,
not A-XDR data, wire APDUs, or an association object list.

## Supplied source baseline

The supplied PDFs were extracted locally. Relevant tables were checked against
their rendered pages where extraction layout was ambiguous. Source text has not
been copied into the repository. This is not yet a completed clause-by-clause audit.

| Source | Supplied amendments | Requirements used in this slice |
| --- | --- | --- |
| IS15959_Part1_2011.pdf | 15959A1_2014, 15959A2_2015, 15959A3_2016, 15959A4_2017, 15959_1A5_2021 | Category terminology; C3 addition; A5 B-3 clearing history when capture period changes. Full Part 1 capture/object lists remain unverified. |
| IS15959_Part2_2016.pdf | 15959_2A1_2017, 15959_2A2_2017 | Clauses 6, 9-20; Tables A1/A12/A13/A14/A26/A27; amended push IDs, payment data, image identifier and transaction IDs. |
| IS15959_Part3_2017.pdf | None supplied | Clause 10 exclusion of relay control; Tables 1/13/14/26; D3 capture choices and D4 interface demand period. |

Later BIS amendments identified during preliminary research are **not incorporated**.
The supplied baseline must not be represented as the latest consolidated standard.
Referenced IEC 62056/COSEM specifications, underlying metrology standards and a
protocol interoperability test suite are still needed for full protocol verification.

## Working features

- Categories A, B, C1, C2, C3, D1, D2, D3, D4 can be selected on creation.
  C3/D1 require single phase; other explicit categories require three phase.
  Automatic preserves existing meters by resolving single phase to D1 and three
  phase to D2. Category selection alone does not imply that category's full tables
  have been implemented. Three-wire wiring and transformer ratios remain pending.
- Live, searchable OBIS inspector with name, IC, attribute, unit and engineering
  value for a defined subset. Phase-specific current/voltage IDs, energy,
  integrated demand, settings and selected nameplate values are connected to state.
- Block captures align to local meter-clock interval ends, even with accelerated
  ticks. Block energy is interval consumption; voltage/current are time-weighted
  L1 averages. Incomplete startup/clock-change/configuration intervals are labelled.
- Daily cumulative captures occur at midnight. Block retention is 35 calendar-days
  worth of entries; daily retention is 35 entries. This does not implement all
  category-specific power-on-day retention requirements.
- Demand is the integrated import power over a completed interval, not a peak of
  instantaneous samples. Monthly or manual billing closes retain six snapshots
  and reset demand without resetting lifetime energy.
- Clock, capture period and demand period programming are validated and logged.
  D2/D3 capture permits 900/1800 seconds; D4 is currently modelled as a fixed
  900-second interface-meter profile. D1 permits 900/1800/3600 seconds. Other
  Part 1 category period rules remain a demo policy, not verified conformance.
- D1/D2 connect/disconnect changes load without stopping the simulation clock.
  D3/D4 reject relay actions and omit IC70/71 from the inspector. The optional
  load limiter uses an explicitly labelled instant-trip simulation policy.
- D1/D2 HES-written payment mode/recharge/balance fields do not cause local credit
  deduction. This is a local management operation, not an authenticated HES session.
- Utility/consumer message previews enforce a 128-byte UTF-8 limit. Amended push
  setup IDs are used for manual data/message previews. Queues are in memory;
  nothing is delivered externally.
- Communication-image transfer accepts sequential bounded blocks up to a total
  1 MiB. Activation requires a complete image with a matching SHA-256 digest.
  Activation changes the simulated version only; no executable code is loaded.
  SHA-256 does not establish publisher authenticity or implement DLMS security.
- Existing fault/engine controls remain available. Clock drift now advances through
  capture boundaries rather than skipping them during accelerated ticks.

## APIs

These are **local unauthenticated management APIs**, consistent with the existing
application. Keep the host bound to loopback. Do not expose this app on an untrusted
network. No association selector or access-right display is treated as authentication.

Base: `/api/meters/{id}/companion`

| Method | Suffix | Operation |
| --- | --- | --- |
| GET | `/` | Simulator companion state and local queues |
| GET | `/objects` | OBIS engineering-value subset |
| GET | `/profiles/Block`, `/profiles/Daily`, `/profiles/Billing` | Captures; optional inclusive `from`/`to` ISO timestamp filters |
| PUT | `/settings` | CapturePeriodSeconds, DemandPeriodSeconds, LoadLimitEnabled, LoadLimitKw |
| PUT | `/relay` | Connected boolean; D1/D2 only |
| PUT | `/clock` | Time as an ISO date-time with explicit offset |
| POST | `/billing/close` | Capture billing and reset MD |
| PUT | `/prepayment` | HES field bundle, D1/D2 only |
| POST | `/messages` | FromUtility boolean and Message string |
| POST | `/push-preview` | Capture local preview; no send |
| POST | `/firmware/initiate` | ImageId, Size, Sha256 |
| POST | `/firmware/block` | Offset and Bytes (JSON base64); at most 4096 bytes per block |
| POST | `/firmware/verify` | Verify completeness and digest |
| POST | `/firmware/activate` | Change simulated communication version |

Invalid domain requests return 400; absent meters return 404. Runtime companion
settings, profiles, balances and image state are not persisted across host restarts.
Only meter definitions use the existing definition store.

## Outstanding work before the original request is complete

1. Complete clause/annexure/amendment traceability for every category and optional
   feature. Incorporate later amendments into an explicitly versioned baseline.
2. Full category-specific capture lists and scalers/units, including three-wire
   wiring, CT/PT ratios, signed PF, category-specific reactive capture lists, TOD registers and
   complete nameplate properties. Implement native COSEM attributes and methods.
3. Active/passive season/week/day tariff calendars, scheduled activation, billing
   schedules and per-zone billing/demand; generate billing on calendar activation.
4. Nonvolatile state, standards-specific power-failure event capture/retention, same-day block
   reconstruction on capture-period changes and full profile selective access.
5. Full Indian occurrence/restoration events, grouped buffers, non-rollover events,
   tamper counters, event capture values, 128-bit ESW/ESWF and event-driven push.
6. Full IC70 state/mode behavior and IC71 delays/reconnection rules. This requires
   the underlying interface-class definition, not only the companion table.
7. Push scripts/schedules, all five setup instances, object selection, destination
   handling, retry windows and actual notification transport. IHD associations.
8. Full image-transfer class behavior, resumption, scheduled activation and
   authenticated firmware handling; independent expected-digest validation in UI.
9. HDLC and TCP/UDP wrapper, A-XDR, ACSE association negotiation, GET/SET/ACTION,
   block transfer, selective access and all association access rights.
10. LLS/HLS, AES-GCM security suite, key wrapping/rotation, persistent invocation
    counters, replay rejection, system-title identity and test-key provisioning.
11. Interoperability and negative tests with an independent DLMS client; standard
    conformance tests and any required accredited physical-meter testing.

## Verification

The console regression harness runs 22 checks: existing electrical/fault behavior,
aligned capture, integrated demand, period changes, midnight/monthly boundaries,
six-cycle retention, category restrictions, HES balances, invalid inputs, firmware
failure states, amended preview identifiers, date filtering, clock drift and
UTF-8 message size, independent four-quadrant energy, supply interruption/restoration,
and supply/relay independence. Passing these does **not** establish standards conformance.

## Electrical extension

The Electrical panel provides independent active/reactive export controls and supply
availability. Blue Book Ed.17 Part 1 Table 13 and Figure 1 define the implemented
quadrants: QI(+,+), QII(-,+), QIII(-,-), QIV(+,-). Reactive energy is integrated into
OBIS C=5 through 8 registers; C=3/4 totals combine QI+QII and QIII+QIV respectively.
Power factor remains a magnitude, not a signed COSEM encoding. Category-specific
mandatory/optional applicability and capture lists are not yet validated.

Supply failure zeros electrical measurements and consumption; backup RTC and captures
continue as an explicit simulator policy. Power-off duration and interruption count are
tracked. Restoring supply does not reconnect an open load relay. Events use simulator
names, not unverified Indian event IDs. All runtime state remains memory-only.

GET/PUT `/api/meters/{id}/companion/electrical` reads state or configures
`ExportActive`, `ExportReactive`, and `SupplyAvailable`. These are simulation inputs,
not a DLMS SET interface. ReverseEnergy fault overrides active direction to export.

The supplied Blue Book Edition 17 Part 1 contains OBIS definitions, not the full
interface-class definitions. Edition 17 Part 2 is still required for that baseline.

Run `dotnet run --project tests/SmartMeterStudio.Tests/SmartMeterStudio.Tests.csproj`.
If a running web host locks output DLLs, stop that host or use a separate
`--artifacts-path` for builds and tests; do not kill unrelated processes.
