# COSEM object directory

Open `/objects` or **Object directory** in the navigation. Select a meter, then a class folder and an OBIS object. **Refresh values** takes a fresh snapshot without overwriting an unfinished edit. No changes to the LCD display are required.

## Coverage and source boundary

This is a local software test bench, **not a conformant DLMS/COSEM server**. JSON management endpoints are not GET/SET/ACTION services over an association, and do not encode A-XDR, access rights, authentication, encryption or transport frames. Deploy only on a trusted development network; the management API has no authentication.

Both supplied documents were used: *Blue Book Edition 17 Part 1 V1.0* for OBIS and *Part 2 V1.0 (28 February 2025)* for interface classes. The directory catalogs the non-reserved class IDs in Part 2 Table 4 (printed pp. 61–66), including Attestation 170. Each catalog entry identifies its governing clause. Catalog versions describe the current class definitions, not implemented protocol compatibility. Earlier versions in clause 5 are not separately modelled. For 91, 92, 161 and 162, the specific clause headings supersede stale version values in the summary table.

| Class | Working behavior | Explicit limitations |
| --- | --- | --- |
| 1 Data, v0 | Inspect existing data; create, read and write custom scalar values | Custom string/finite number/boolean only; type fixed on creation. Existing identity/companion data stays read-only here. |
| 3 Register, v0 | Inspect live readings and scaler/unit; custom numeric registers; write and reset to zero | Live measurements cannot be overwritten/reset through this panel. Custom units/scalers are fixed at creation. |
| 4 Extended register, v0 | Register behavior plus status and write/reset capture time for custom instances | Status is a simulator policy (0 = normal). Active maximum demand reuses its existing timestamp; apparent maximum demand's untracked timestamp is explicitly null. |
| 7 Profile generic, v1 | Configurable capture list, timed/manual capture, FIFO capacity, reset, entry/time filtering | FIFO only; whole attributes from non-profile objects, data_index 0; no wire selective-access encoding. |
| 8 Clock, v0 | Read/set meter time and timezone; nearest-quarter, measuring-period and minute adjustment; staged preset with validity window; shift by ±900 seconds | DST scheduling, partially specified date-time fields and encoded status flags are not modelled; clock base is undefined for the software clock. |
| 9 Script table, v0 | Editable action lists; execute a selected script to write attributes or call implemented methods | Local, sequential execution only; no nested scripts, script-table rewrites, dummy actions or automatic scheduling. Partial execution is reported and not rolled back. |
| 11 Special days table, v0 | Read/write table; insert with same-index/date replacement; delete by index | Exact dates only. Wildcards and Schedule/Activity Calendar linking are pending, so this table does not change existing tariffs. |
| 70 / 71 | Existing relay/threshold values visible | Full class attributes/methods remain pending; use existing IS 15959 controls for current relay functionality. |
| Other folders | Name, ID and pending coverage | No placeholder actions claim successful execution. Class creation is rejected. |

Primary requirements checked in the supplied Part 2: Data §4.3.1 (p. 67); Register §4.3.2 and unit table 5 (pp. 68–73); Extended Register §4.3.3 (pp. 73–75); Profile Generic §4.3.6 (pp. 80–86); Clock §4.5.1 (pp. 185–188); Script Table §4.5.2 (pp. 188–190); Special Days Table §4.5.4 (pp. 193–194). The Profile Generic zero-capacity case follows the explicit Edition 17 change in the revision summary, printed p. 17. Source PDFs are not redistributed with the project.

Supplementary primary-implementer references used before Part 2 arrived: [Data](https://www.gurux.fi/Gurux.DLMS.Objects.GXDLMSData), [Register](https://www.gurux.fi/Gurux.DLMS.Objects.GXDLMSRegister), [Extended Register](https://www.gurux.fi/Gurux.DLMS.Objects.GXDLMSExtendedRegister), [Profile Generic](https://www.gurux.fi/Gurux.DLMS.Objects.GXDLMSProfileGeneric), and [Clock](https://www.gurux.fi/Gurux.DLMS.Objects.GXDLMSClock). This project contains no Gurux implementation code or package. Functional testing here is not a conformance test suite.

## Try it

1. Select a meter and expand **Create a test object**.
2. Choose class **3**, logical name `0.128.1.0.0.255`, value `23000`, scaler `-2`, unit `35` (V). This represents 230 V. Custom objects are independent test values, not inputs into the electrical simulation.
3. Select class **7** and the built-in **Studio load capture** (`0.128.99.1.0.255`). Its manufacturer-specific name avoids asserting a standard survey assignment. Existing IS 15959 block/daily/billing surveys remain separate and unchanged.
4. Add the test register to the capture list. Accept the buffer-clear warning and apply configuration. Set period to **0** for manual capture or **60** for one simulated minute.
5. Use method **2 · capture**. Change the register value, capture again, and read the buffer. Earlier cells remain snapshots, not references to the latest value.
6. Try a capacity of **3** and take five captures: only the newest three remain. Reset clears that profile's rows; it does not reset source registers.

New profiles start empty with period 0. Configure columns before capturing. All custom values, profile settings and buffers are **in memory only** and are lost when the host restarts. Meter-definition persistence is unchanged.

## Simulator policies

- Up to 64 local custom objects/profiles per meter; profiles have 1–16 columns and 0–1000 rows. Capacity 0 retains no rows. Capture periods are 0 or 10–86400 seconds. These bounds are simulator safeguards, not standard limits (the standard also allows 1-second captures).
- A timed profile initially schedules to the next period boundary relative to local midnight, then repeats at that interval. `Tick` splits accelerated time at each capture boundary. Pausing the meter pauses automatic capture; an explicit manual capture remains available.
- Changing columns or capacity clears that profile's buffer. Changing only the period retains rows and re-anchors its schedule. Invalid configurations are rejected before state changes. Referenced objects cannot be deleted.
- Clock writes require an ISO 8601 timestamp with an explicit offset or Z, year 2000–2099, and UTC offset -12:00..+14:00. `time_zone` uses local-to-UTC minutes (UTC+05:30 is -330), in -840..720. Writing it changes the displayed offset while preserving the instant. This is the DLMS sign convention, not a claim about a particular IS 15959 deviation encoding. DST is disabled. Midpoints for rounding methods go forward.
- Clock changes use the existing `SetClock`: partial survey/demand integration and current readings are cleared, faults cleared, history retained. Generic captures are re-anchored without backfilling skipped time. After a backward jump FIFO sequence remains capture order; timestamps need not be monotonically increasing.
- Register numeric value × 10^scaler yields the base-unit quantity. For example, a live value expressed in kWh uses scaler 3 and Wh unit 30. No additional scaling is silently applied to captured values.
- Time filters are inclusive on the capture metadata timestamp. Entry offset is 1-based **after** time filtering, in retained FIFO order. Sequence is diagnostic metadata and resets with the buffer. UI buffer reads default to 100 rows and can request up to 1000.

## Local management API

Base: `/api/meters/{meterId}/cosem`. Logical names are six decimal bytes separated by dots.

| Verb | Route | Purpose |
| --- | --- | --- |
| GET | `/api/cosem/classes` (absolute) | Catalog and honest coverage flags |
| GET | `/objects` or `/objects/{ln}` | Directory / object metadata and values; large buffer is deferred to rows endpoint |
| POST | `/objects` | Create class 1, 3, 4, 7, 9 or 11 |
| DELETE | `/objects/{ln}` | Delete unreferenced custom object (including its buffer) |
| GET / PUT | `/objects/{ln}/attributes/{index}` | Read descriptor and value (including complete profile buffer for attribute 2) / write `{ "value": ... }` |
| POST | `/objects/{ln}/methods/{index}` | Invoke with `{ "parameter": 0 }`; shift-time uses signed seconds |
| GET / PUT | `/profiles/{ln}/configuration` | Inspect / replace configuration |
| GET | `/profiles/{ln}/rows?start=1&count=100&from=...&to=...` | Read row snapshots; URL-encode `+` in offsets |

Create example:

```json
{ "classId": 3, "logicalName": "0.128.1.0.0.255", "name": "Test voltage", "value": 23000, "scaler": -2, "unit": 35 }
```

Profile configuration example:

```json
{
  "captureObjects": [
    { "classId": 8, "logicalName": "0.0.1.0.0.255", "attributeIndex": 2, "dataIndex": 0 },
    { "classId": 3, "logicalName": "0.128.1.0.0.255", "attributeIndex": 2, "dataIndex": 0 }
  ],
  "capturePeriod": 60,
  "capacity": 100
}
```

Validation returns 400; missing meter/object returns 404; disabled or unimplemented operations return 422. Writes through REST have the same domain validation as UI but no confirmation dialog, so clients must obtain their own user confirmation for destructive actions.

### Clock preset, scripts and special days

Clock method 5 accepts `{ "presetTime": "2026-09-04T12:01:00Z", "validityIntervalStart": "2026-09-04T12:00:00Z", "validityIntervalEnd": "2026-09-04T12:02:00Z" }` as the `parameter`. It stores but does not apply the time. Method 4 applies it only when the current simulated clock is inside that window; otherwise the simulator rejects the action without changing the clock.

Create class 9 with initial value `[]`, then write attribute 2, for example:

```json
[{"scriptIdentifier":1,"actions":[
  {"serviceId":1,"classId":3,"logicalName":"0.128.1.0.0.255","index":2,"parameter":24000},
  {"serviceId":2,"classId":7,"logicalName":"0.128.99.1.0.255","index":2,"parameter":0}
]}]
```

Method 1 with parameter `1` executes the script. Parameter `0` is a no-op. Script IDs are 1–65535, with up to 16 scripts and 16 actions each. Targets must exist and expose the requested operation; per-action parameter validation occurs at execution. An error stops later actions and reports how many have already completed. Objects referenced by scripts cannot be deleted until the references are removed.

Create class 11 with `[]`. Method 1 accepts `{ "index": 1, "specialdayDate": "2026-12-25", "dayId": 3 }`. Method 2 accepts the index to delete. Inserting an existing index or date replaces it; replacing the entire attribute rejects duplicate dates/indices. Tables allow 100 exact dates in 2000–2099. Entries alone do not cause tariff switching.

## Verification

```powershell
dotnet build SmartMeterStudio.slnx
dotnet run --project tests/SmartMeterStudio.Tests --no-build
```

The console test suite has 43 checks covering existing simulation behavior plus class metadata, scalar typing, scaling/reset, capture times, read-only access, OBIS validation, snapshots, FIFO boundaries, filters, atomic configuration, reference safety, clock changes/drift/presets, concurrent captures, meter isolation, scripts and special days.

For HTTP checks, start a development instance with an isolated `SmartMeterStudio:DataPath`, then run `tests/Smoke-Cosem.ps1 -BaseUrl http://127.0.0.1:5183`. It checks rendered page/CSS, the 106-class catalog, CRUD, buffers, access errors, scripts and special days. It creates and finally removes one uniquely named test meter. No existing meter is modified. These are server-rendering and HTTP checks, not an automated interactive-browser test.
