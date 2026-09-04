# DLMS/COSEM protocol layer

`SmartMeterStudio.Protocol` is a transport- and UI-independent server-side layer. It references `SmartMeterStudio.Core` only; the Blazor project has no dependency on it.

```
TCP or serial host (next) -> HDLC frame codec -> xDLMS LN APDU codec -> COSEM service router -> virtual meter core
Blazor UI -------------------------------------------------------------> virtual meter core
```

## Available now

- Bounded HDLC frame encode/decode with destination/source addresses, frame length, HCS and FCS validation.
- Bounded A-XDR values used by the supported services: scalar values, octet/visible/UTF-8 strings, arrays, structures and date-time.
- Logical-name normal-form `GET`, `SET` and `ACTION` APDUs, including normal responses.
- A COSEM router which resolves class ID, logical name and attribute/method against one `SmartMeterFleet` meter. Association policy permits read-only or write/action access.
- `SmartMeterStudio.Protocol.Transport` provides a TCP listener whose connection handler feeds the HDLC stream decoder and session. It does not depend on Blazor.
- HDLC link management supports SNRM/UA, DISC/UA, I-frame sequence acknowledgement and REJ for invalid sequence or LLC data.
- The Green Book Edition 11 no-ciphering, no-authentication LN AARQ/AARE profile is accepted. It creates a **read-only** association only and includes the Edition 11 InitiateResponse parameters from Table 130.

This is an in-memory protocol engine: supply it bytes from a future serial/TCP host and return its bytes to that host. The router never knows about Blazor, HTTP, or a browser.

## Deliberately not yet claimed

No serial-port adapter, LLS/HLS authentication, ciphering/security suites, release APDUs, selective access, data-block transfer, or full type coverage is included. Secure associations require persisted invocation counters, key lifecycle and the Green Book-defined security setup before they can be exposed beyond loopback. The TCP listener therefore must begin on `IPAddress.Loopback`.

Firmware remains a safe simulator feature: a future Image Transfer (class 18) adapter may store and verify virtual image blocks, but it must never execute an uploaded file on the Windows host.
