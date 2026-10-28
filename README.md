# Local Network Monitor

A personal project built with coding agents: a local Windows app that helps
identify likely internet bandwidth consumers and safely pause or resume a
selected device's internet access through a supported gateway.

The application itself will be deterministic and user-controlled, with no runtime
AI. It is intended only for a network the user owns or administers.

## Status and feasibility gate

The initial implementation is a Windows-only **C#/.NET 10 + WPF monitoring app**
for the Verizon CR1000B. The [hands-on findings](./docs/research/cr1000b-hands-on-findings.md)
establish an approximately **20.6-second source counter cycle**, not live
2-5-second measurements. On 2026-10-28, the user explicitly accepted this limit,
Windows-only WPF, and the credential/freshness policies below.

The app implements the documented challenge-response login and reads the
one-hour `hosts_trafstat` bucket from `/cgi/cgi_bandwith.js`. The parser accepts
JSON data within `addROD`, never executes gateway JavaScript, and rejects missing,
invalid, or unsupported measurement shapes instead of substituting zeroes.
End-to-end compatibility with the actual gateway is **not yet certified**.

**Pause/resume remains unavailable.** Machine-readable policy state, enforcement,
management-path protections, and recovery must be verified before enabling
control commands. No router control requests are implemented.

Do not substitute simulated data or host-only measurements for network-wide
capabilities. Local-traffic exclusion and extender coverage are still unverified.

## Build, test, and run

Requires Windows and a .NET 10 SDK with Windows desktop support:

```powershell
dotnet build .\LocalNetworkMonitor.sln
dotnet test .\LocalNetworkMonitor.sln
dotnet run --project .\src\LocalNetworkMonitor.App\LocalNetworkMonitor.App.csproj
```

If using the user-local SDK installed during development rather than an SDK on
`PATH`, replace `dotnet` with:

```powershell
& "$env:LOCALAPPDATA\LocalNetworkMonitor\dotnet\dotnet.exe"
```

Enter an HTTPS gateway origin, normally `https://mynetworksettings.com`, and its
administrator password. Normal HTTPS certificate and hostname validation must
succeed **before credentials are submitted**. The research records browser trust
warnings on this gateway; if Windows does not trust its certificate, login is
blocked visibly. No certificate bypass, automatic trust installation, or HTTP
fallback exists. Any gateway-specific trust exception requires a separate
explicit decision after inspecting the actual certificate.

Tests use synthetic HTTP responses, plus real Windows Credential Manager
round-trip/deletion under a unique synthetic target and a WPF window smoke test.
Offline reconnect tests verify that history is isolated by gateway origin.
They do not log in to the physical gateway, establish measurement coverage, or
certify enforcement. Production does not offer a simulated-data mode.

### Credential lifecycle

- Session-only by default. "Remember on this Windows account" is unchecked.
- Opting in stores the password in Windows Credential Manager, scoped to the
  exact gateway origin, only after login and a valid counter response succeed.
- "Load saved password" explicitly retrieves an existing credential. Connecting
  without Remember does not overwrite or delete an existing saved credential.
- "Forget saved password" deletes persistence immediately, without ending an
  active in-memory session. Disconnect/close releases app-held session references.
- Passwords and session cookies may remain in process memory until disconnect or
  close; no authentication material is logged or written to application settings.
  Managed strings cannot guarantee immediate zeroization. Windows storage is not
  protection against malicious processes running as the same Windows account.
- An expired session gets one automatic re-login attempt. A failed renewal stops
  authentication retries and requires explicit reconnection; saved credentials
  are not silently changed.

Raw router research captures can contain Wi-Fi passwords and identifying network
details. Keep them out of Git and redact sensitive fields before retaining or
sharing them; being ignored by Git does not make a local capture safe to share.

### Observation and chart semantics

- Poll after a five-second wait, with no overlapping or catch-up requests.
  Requests time out after ten seconds. Collection failures back off to
  10, 20, 40, then at most 60 seconds; success restores the five-second wait.
- Router reachability/last valid response and last observed per-entry counter
  change are separate. A successful response is not proof of a new source sample.
- Unchanged counters do not create chart points or prove zero traffic. Before a
  first change, the entry is awaiting an interval.
- A valid counter increase yields an approximate byte delta / elapsed observation
  interval average. Repeated polls do not shorten that interval. A long interval
  is a coarse average, not evidence of a burst at its end.
- Chart segments cover only those observed intervals. No previous rate is
  extended into unconfirmed time. Numeric estimates identify their historical
  interval and age. Unknown intervals are blank, not zero.
- Timeouts, failed reads, session renewal, disappearance, and decreasing rolling
  counters break the rate baseline. Reappearance/reconnection first establishes
  a new baseline. A decrease is an observed counter change, not a negative rate.
- Reconnecting to the same HTTPS gateway origin retains its unexpired history.
  Attempting a connection to a different hostname or port clears the previous
  gateway's history, chart selection, and last-contact state before login, even
  if the new connection fails. Switching back does not restore cleared history.
- After 60 seconds without an observed counter change, show neutral inactivity
  messaging: traffic may be idle. Actual collection/authentication/data failures
  are visible immediately, independently of that threshold.
- History uses a monotonic session clock and expires after ten minutes. An
  interval crossing the left chart boundary is visually clipped without
  pretending its average was measured over a shorter interval.

## Agreed MVP

### Local window and data

- A dedicated Windows app window on the user's own PC.
- Collection only while the app runs; no always-on collector.
- Monitoring data stays on the user's machine, without cloud services or external
  AI processing.
- A rolling 10-minute, in-memory bandwidth chart, discarded when the app closes.
  Saved device names and connection settings are configuration, not traffic
  history.

### Internet bandwidth visibility

- Separate approximate download and upload interval averages per gateway-reported
  device entry, accepting the CR1000B's roughly 21-second source updates.
- Optional friendly names, without assuming that a network identity uniquely
  identifies a physical product or a person. One physical device may have several
  entries, including after a private Wi-Fi address changes.
- Explicit measurement coverage, missing data, stale measurements, and chart gaps.
  Unavailable measurements are not zero.
- A network total only when supported by the measurement source. A sum of measured
  entries is labeled as such and is not automatically total gateway usage.

Internet traffic means traffic crossing the gateway to or from the internet, not
all local device-to-device traffic or readable contents of encrypted connections.
High bandwidth usage identifies a likely consumer, not proof of the cause of a
slowdown.

### Safe internet access controls

- Pause/resume through an authorized gateway capability, only after a verified
  way to resume is available.
- Confirmation identifying the selected entry before pausing.
- The gateway and the app's own management computer are ineligible pause targets.
  Pause is disabled when their protection cannot be enforced confidently.
- Users can mark other important device entries as protected.
- Manual pauses may persist after the app closes. Explain this before
  confirmation and provide instructions for restoring access through the router
  when the app is unavailable.
- Commands remain pending until the gateway reports the requested policy state.
  Failures and unknown outcomes are explicit; uncertain control commands are not
  automatically retried.
- Read gateway state again on startup or reconnection rather than assuming a
  previous command succeeded. A confirmed gateway rule is not proof that every
  existing connection has stopped.

An internet access pause does not necessarily disconnect a device from Wi-Fi or
prevent it from reaching local printers, storage, or other devices.

## Outside the initial scope

- Wi-Fi disconnection or forcing devices off the local network.
- Bandwidth shaping or allocation.
- Local device-to-device traffic monitoring.
- Packet payload inspection or browsing history.
- Persistent traffic history or collection while the app is closed.
- Universal router support, additional operating systems, or runtime AI.
- Guaranteed diagnosis of connection slowdowns.

## Acceptance criteria

The automated tests cover application semantics, not the hardware gate. Before
claiming a verified MVP, demonstrate on the actual setup:

1. Controlled download and upload traffic is attributed to the correct test-device
   entry.
2. Detect changes at the gateway's roughly 21-second source cadence, normally
   within one additional five-second polling wait plus request time. Repeated
   replies must not look like fresh measurements.
3. The rolling chart retains up to 10 minutes of measurements during a monitoring
   session and represents missing measurements as gaps rather than zero usage.
4. A noncritical test device can be paused and resumed, with gateway-confirmed
   policy state and observed internet behavior checked separately.
5. Lost gateway connectivity produces a visible stale/error state, and
   reconnection rereads policy state.
6. The app cannot pause its management computer, the gateway, or user-protected
   entries.
7. Traffic history is discarded when the app closes.

Sample data alone does not establish successful monitoring or enforcement.

## Domain language

See [GLOSSARY.md](./GLOSSARY.md) for the agreed terminology.