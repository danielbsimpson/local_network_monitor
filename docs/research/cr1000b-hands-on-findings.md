# Hands-on verification: Verizon CR1000B gateway

Measured against the physical gateway on 2026-10-27, closing the open
questions left by [verizon-cr1000b-gateway.md](./verizon-cr1000b-gateway.md),
which was desk research only.

Evidence comes from two throwaway probes (`spike/cr1000b_cadence_probe.py`,
`spike/cr1000b_semantics_probe.py`) plus Windows adapter counters used as an
independent cross-check.

## Finding 1: the gateway has a fixed ~20.6s counter tick

`hosts_trafstat` updates on a fixed internal cycle, not on demand.

- Run 1: 96 polls at 1.5s intervals over 181.5s. Median gap between counter
  updates **20.8s**, minimum 18.9s, maximum 20.9s on actively transmitting
  device entries.
- Run 2: median gap **20.5s**, independently.

The tightness of that spread rules out jitter or load-dependence. Observed
larger gaps (39.7s, 58.6s, 60.5s) are whole multiples of the base tick: those
device entries had no traffic during a tick, so their counters held still.
They are not evidence of a slower cycle.

Polling faster than the tick returns identical values. Run 1 polled 96 times
to observe 8 updates.

**This is the binding constraint on the whole application.** No measurement
can be fresher than ~20.6s from this measurement source.

## Finding 2: bytes_rx is the download rate, bytes_tx is the upload rate

The counters are named from the **device's** perspective. The opposite
reading is a plausible misreading that would silently swap every download
rate and upload rate in the UI.

A controlled 100 MB download to one device entry produced:

| Tick | `bytes_rx` delta | `bytes_tx` delta |
|---|---|---|
| idle baseline | +1.1 MB | +25.3 MB |
| idle baseline | +1.3 MB | +17.6 MB |
| **during the download** | **+134.3 MB** | +20.3 MB |
| after | +1.2 MB | +21.1 MB |
| after | +1.3 MB | +23.5 MB |

`bytes_rx` rose to **109.7x its own idle baseline** on exactly the tick that
absorbed the download, while `bytes_tx` moved 1.1x and was visibly unaffected.

Confirmed independently by Windows adapter counters on the same machine over
a 25.2s idle window: **26.9 MB sent, 0.7 MB received** (~8.96 Mbps up,
~0.24 Mbps down). The gateway's `bytes_tx` for that device entry predicted
~24.9 MB sent over the same window — agreement within 8% from two unrelated
measurement sources, and in the same direction.

**Method note.** Comparing gross before/after totals gives the wrong answer
here and initially did: across five ticks, steady background upload
accumulated ~100 MB on `bytes_tx` and diluted the one-off `bytes_rx` spike to
a 2.1x ratio, which read as inconclusive. The correct test compares each
counter against *its own* idle baseline.

## Finding 3: measurement coverage is per-device only; there is no live network total

`get_history_rates` is **not** a live network total and cannot provide one.

- Changed twice in ~170s, median gap **64.9s** — slower than the 20.6s
  per-device tick, not faster.
- Shape is two series of 16 string-encoded values, consistent with a
  pre-binned chart history rather than a current reading.

A single clock therefore governs the application. Per the README, a sum of
measured device entries must be labeled as such and must not be presented as
total gateway usage.

## Finding 4: byte accuracy is approximate, attribution is sound

The gateway recorded **128.1 MB** for a transfer of exactly 104,857,600 bytes
(100.0 MB) — an over-count of **1.28x**. Some of that is Wi-Fi retransmits and
framing overhead.

Attribution to the correct device entry was unambiguous. Absolute byte totals
should be treated as indicative, not exact.

## Finding 5: the measurement source counts internet traffic, not local traffic

The device entry under test showed a constant ~8.3-9 Mbps on `bytes_tx` even
while idle, which raised the possibility that `hosts_trafstat` counts local
traffic as well as internet traffic — a distinction this project depends on.

Investigated and explained: the machine held **23 established internet
connections and zero true-LAN connections** (44 further connections were
loopback, which never reaches the network adapter). The sustained upload is
`OneDrive.Sync.Service` talking to Microsoft. It is genuine internet traffic.

**Scope of this finding:** it establishes that the observed traffic *was*
internet traffic, not that `hosts_trafstat` would *exclude* local traffic if
any existed. No local traffic was present to test against. See open questions.

## Finding 6: device entries appear and disappear mid-session

The device entry count moved 6 -> 8 -> 9 within the first 40s of run 1. The
set of entries is not stable across a monitoring session, which the UI must
handle without implying a device appeared or vanished physically.

## Finding 7: block/allow policy state is not exposed in the data layer

A local Block/Allow control does exist on the admin UI's Devices page and was
exercised against a test device entry (`6a:b6:9b:d1:09:bd`, a MacBook Pro at
192.168.1.169). Snapshots taken before and after the toggle showed **no change
to any field of that device entry**, across 6 discovered endpoints and 6,172
leaf values. `parentalrules` was an empty list both before and after.

The instrument was validated first: a control pair of snapshots taken 2.5
minutes apart with no policy change produced 454 drifting leaves, of which 4
survived filtering — all of them `activity`/`activity_ip` flipping `0 -> 1` on
a *different* device entry that came online. So the probe does detect real
state changes; it found none for the blocked device entry.

**Conclusion:** policy state is not readable from the `cgi_*.js` data files the
admin SPA loads. Reading back whether a device entry is blocked would require
either `db.cgi` (a POST query endpoint, not examined) or scraping the rendered
admin UI.

This matters because README acceptance criterion 4 requires *gateway-confirmed*
policy state, and the README further requires that commands remain pending
until the gateway reports the requested state. Neither is satisfiable from the
data layer as it stands.

**Explicitly NOT established:**

- Whether the block was actually *enforced* — the test device was not checked
  for loss of internet access. The click was made; the effect was not observed.
- Whether the block persists across a page refresh.
- Whether already-established connections survive a block.
- Whether an undiscovered endpoint carries the state. The six endpoints were
  found by guessing page names; 332 further candidates derived from the SPA's
  route table have not been scanned.

**A related signal, unverified:** `cgi_parental.js` carries `HNP_Enable: 1`,
`HNP_Title: "Digital Secure Home and Parental Controls"` and `HNP_Applink:
https://home.verizon.com/ds?src=rgui` — a QR-code sign-up pointing at a Verizon
cloud service. Together with `parentalrules` being empty, this raises the
possibility that parental controls on this firmware are primarily a cloud
offering and that local rule storage is vestigial. Not established either way.

## Verdict against the README acceptance criteria

| # | Criterion | Verdict |
|---|---|---|
| 1 | Controlled traffic attributed to the correct test-device entry | **PASS** — 109.7x signal on the correct entry, direction confirmed, subject to the 1.28x accuracy caveat |
| 2 | Measurements update within 5 seconds | **FAIL** — the gateway ticks at ~20.6s. Not achievable from this measurement source at any poll rate |
| 3 | Rolling chart retains 10 minutes, gaps not zero | **ACHIEVABLE** — ~29 points per 10 minutes at the observed tick |
| 4 | Pause/resume with gateway-confirmed policy state | **PARTIAL** — a local Block/Allow control exists and is reachable, but policy state is not readable from the data layer (finding 7), and enforcement was not observed |
| 5 | Lost connectivity produces stale/error state | **NOT YET TESTED** — rereading policy state on reconnect inherits the problem in finding 7 |
| 6 | Cannot pause the management computer, gateway, or protected entries | **NOT YET TESTED** — note the management computer is device entry `bc:38:98:69:3e:08`, which must be ineligible |
| 7 | Traffic history discarded on close | Application behavior, not gateway-dependent |

## Decision required before implementation

Criterion 2 cannot be met as written. The README's own gate directs returning
to the user to decide explicitly between changing infrastructure and changing
requirements. The options:

1. **Relax the refresh target to ~21s** and keep the agreed scope. The product
   goal is identifying *likely* internet bandwidth consumers; a device entry
   saturating the connection is just as visible at 21s as at 2s.
2. **Change the measurement source** to something with finer granularity. No
   such source was found on this gateway.
3. **Change infrastructure.**

Option 1 preserves the scope. Whichever is chosen, repeated values must not be
presented as fresh measurements: at a 20.6s tick, a 2-5s poll yields identical
readings four times out of five, and per the glossary an unavailable
measurement is not zero.

## Remaining open questions

0. **Capture what the admin UI does when Block/Allow is clicked.** The highest
   value next step, and it avoids guessing at a write API. With browser dev
   tools open on the Devices page (Network tab, preserve log, Fetch/XHR
   filter), click Block/Allow and record the request URL, method and payload,
   plus which requests fire *afterward* — the latter reveal where the UI reads
   policy state from. Then confirm the device entry actually loses internet
   access, and that the state survives a page refresh.
1. **Pause/resume** (criteria 4, 5, 6) is partially tested — see finding 7.
   Specifically outstanding: enforcement, persistence, and whether already-
   established connections are torn down when a rule applies.
2. **Does `hosts_trafstat` count local traffic?** Untested — no local traffic
   existed during these runs. Settle with a deliberate LAN-only transfer
   between two devices while watching the counters.
3. **Do counters ever decrease?** None observed, but the window is a 1-hour
   rolling bucket and these runs lasted minutes. Traffic aging out of the
   window could make a counter fall and produce a negative derived rate.
   Defensive handling is warranted regardless.
4. **Coverage of device entries behind a Wi-Fi Extender Mini** is untested.
5. **Login throttling thresholds** remain unmeasured; sessions should be
   reused rather than re-authenticated per poll.
