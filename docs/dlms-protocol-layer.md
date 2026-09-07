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
- LLS uses constant-time password comparison. HLS-GMAC (mechanism 5) validates the client proof and returns the server proof using GCM-AES-128, separate system titles, authentication keys and monotonic invocation counters.
- `FileInvocationCounterStore` persists counters across restarts. Store it in an ACL-protected application-data directory, never source control. `DlmsStreamSessionHost` runs the same session over any duplex `Stream`, so an application can pass `SerialPort.BaseStream` without duplicating protocol logic.
- `SerialDlmsHost` opens one configured COM port (including a virtual-pair endpoint) and connects it to the HDLC session. Its `SerialDlmsPortOptions` cover port, baud rate, parity, data bits, stop bits, handshake, DTR and RTS.

This is an in-memory protocol engine: supply it bytes from a future serial/TCP host and return its bytes to that host. The router never knows about Blazor, HTTP, or a browser.

## Deliberately not yet claimed

No `SerialPort` opener/configuration UI, general/global ciphered xDLMS service APDUs, key-agreement/certificate lifecycle, release APDUs, selective access, data-block transfer, or full type coverage is included. LLS/HLS secrets must be injected by the host from a protected secret store; never place them in source or browser configuration. The TCP listener therefore must begin on `IPAddress.Loopback`.

## Virtual COM test setup

1. Create a paired virtual COM connection with a Windows virtual-port driver. Configure one endpoint, for example `COM21`, for SmartMeter Studio and the other for the serial client.
2. Construct `SerialDlmsHost` with `new SerialDlmsPortOptions("COM21", 9600)` and the meter's `HdlcDlmsSession`, then call `StartAsync()`.
3. Configure the client endpoint with the same line settings. It can send SNRM, AARQ and xDLMS requests exactly as it would to a physical meter.

The virtual-port driver is external to SmartMeter Studio; the application never creates or installs a Windows driver.

Firmware remains a safe simulator feature: a future Image Transfer (class 18) adapter may store and verify virtual image blocks, but it must never execute an uploaded file on the Windows host.
