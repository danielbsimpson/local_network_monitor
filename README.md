# Local Network Monitor

A personal project built with coding agents: a local Windows app that helps
identify likely internet bandwidth consumers and safely pause or resume a
selected device's internet access through a supported gateway.

The application itself will be deterministic and user-controlled, with no runtime
AI. It is intended only for a network the user owns or administers.

## Status and feasibility gate

The project is at the concept stage; there is no application implementation yet.
The initial target is one network and one verified router/gateway integration,
not arbitrary consumer-router compatibility.

**The gateway model and its capabilities are currently unknown.** Before choosing
an application framework or building the dashboard:

1. Identify the target gateway and any separate mesh/access-point equipment.
2. Verify that an authorized integration can provide per-device internet usage,
   including its coverage and achievable refresh cadence.
3. Verify reversible internet access pause/resume, reported policy state,
   enforcement limitations, and a router-based recovery path.
4. Return to the user if the hardware cannot support the agreed scope or refresh
   target; explicitly decide whether to change infrastructure or requirements.

Do not substitute simulated data or host-only measurements for network-wide
capabilities. Integration details, credential storage, and framework selection
remain unresolved until this gate is passed.

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

- Separate download and upload rates per gateway-reported device entry, targeting
  updates every 2-5 seconds where supported.
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

After the hardware feasibility gate passes, demonstrate on the actual setup:

1. Controlled download and upload traffic is attributed to the correct test-device
   entry.
2. Measurements update within 5 seconds when the gateway supports that target.
   If it cannot, revisit the requirement before implementation.
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