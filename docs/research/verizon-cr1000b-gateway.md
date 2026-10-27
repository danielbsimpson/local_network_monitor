# Hardware feasibility research: Verizon CR1000B gateway

Desk research only. No login, probing, or credential use against the live
router was attempted; all findings below come from Verizon's own published
documentation, Broadband Forum's published data-model spec, and the source
code of independently-maintained open-source gateway clients on GitHub. This
file answers the README's hardware feasibility gate before any framework or
implementation decision is made.

Gateway identified by the user via TLS certificate inspection (desk research,
no authentication attempted): default gateway 192.168.1.1, MAC
`20-37-F0-78-0F-85`; TLS cert `CN=mynetworksettings.com, OU=CR1000B,
O=Verizon`; issuer `CN=Verizon Fios CA`; HTTP title "Fios Router"; ports
80/443 open, 8080/8443 closed; firmware file `Last-Modified` 2026-06-01;
client PC on Wi-Fi via an Intel AX211 adapter.

Research was split across three focused passes (model identity + Verizon
constraints; per-device usage API; pause/resume + auth model), each
instructed to use only primary sources and to label anything resting on a
forum/Reddit post as unverified. That labeling is preserved below.

---

## 1. Model identity

**CR1000B is the current-generation main router/gateway ("Verizon Router"
family), not a Fios Extender.** The official CR1000B user guide PDF (cover
page: "Verizon Router USER GUIDE / Model CR1000B / ©2022 Verizon") describes a
full gateway: 10 Gigabit WAN, tri-band Wi-Fi 6E, MoCA 2.5, 3-port Ethernet
switch, USB-C, firewall/SPI, DMZ, VPN pass-through, and "Secure remote
management using HTTPS or Verizon app" —
https://www.verizon.com/content/dam/verizon/support/consumer/documents/internet/verizon-cr1000b-router-guide.pdf
(p.7 feature list).

The manual explicitly distinguishes the router from a separately paired
extender unit in its System Status section: "This section displays the
status of your router's local network (LAN) and internet connection (WAN) ...
of **Verizon Router and Extender Mini(s) (if connected)**" — same PDF, p.38
and p.155. Verizon maintains a wholly separate support page for that
companion product, confirming it is a distinct device family ("Wi-Fi
Extender Mini"), not an alternate name for the CR1000B itself:
https://www.verizon.com/support/residential/internet/equipment/network-extender/wi-fi-extender-mini.

**Relationship to CR1000A and G3100.** Verizon's own parental-controls
support page lists all three generations side by side and treats them as
distinct product lines:

> "If you have a Fios Quantum Gateway or a Fios Advanced Wi-Fi Router, sign
> into [My Verizon]… If you have the Fios Router (G3100 model) or a Verizon
> Router (CR1000A and CR1000B models) see our Fios Parental Controls page."
> — https://www.verizon.com/support/residential/internet/security/parental-controls

This confirms G3100 ("Fios Router") and CR1000A/CR1000B ("Verizon Router")
are **separate, sequential hardware generations**, not the same OEM platform;
CR1000A and CR1000B are grouped together as the newer "Verizon Router"
family, with CR1000B as a refresh of CR1000A. A distinct official CR1000A
overview page exists (confirmed via page `<title>`:
https://www.verizon.com/support/verizon-router-cr1000a/) but a full CR1000A
PDF manual could not be located/fetched to do a feature-by-feature diff
against CR1000B — **gap**. A separate, closely related document, the
"Verizon Router User Guide" (©2021, cover reads "CR1000A"), was fetched in
full and used below for pause/resume and auth detail, on the assumption that
CR1000A/CR1000B share the same admin-UI firmware lineage; this assumption is
reasonable (same product family, same manufacturer naming conventions
`cgi_bandwith.js`/`login.cgi` style endpoints below) but is **not itself
independently confirmed for CR1000B** — flagged as a gap.

**Mesh topology implication.** The CR1000B manual documents a
Self-Organizing Network (SON) feature that shares one SSID across the router
and any paired Extender Mini(s), with the system choosing which radio
(router vs. extender) serves a client based on signal/location — PDF p.23,
and https://www.verizon.com/support/residential/internet/equipment/network-extender/wi-fi-extender-mini.
This means a Wi-Fi client's traffic may in fact be served by an extender's
radio rather than the CR1000B's own radio in a mesh deployment, which is
directly relevant to measurement coverage (section 2). No primary source
quantifies how common extender deployments are; that is an architectural
inference from the SON/extender documentation, not a direct Verizon
prevalence claim.

---

## 2. Per-device internet usage

**Mechanism found: a proprietary, undocumented local JS/JSON-like API behind
the authenticated admin web UI — not TR-069/TR-181, not SNMP, not UPnP/IGD.**
This is corroborated by two independently-authored, unrelated open-source
clients that both reverse-engineered the same wire format:

- `Brishen/verizon_router_client` (PyPI `verizon-router-client`), targeting
  "the CR1000A web UI endpoints" —
  https://github.com/Brishen/verizon_router_client
- `cisasteelersfan/quantum_gateway`, targeting the older G3100/G1100
  "Quantum Gateway" —
  https://github.com/cisasteelersfan/quantum_gateway

Both parse response bodies that are not JSON but JavaScript containing
`addROD("key", <JS literal>);` calls that the real web UI executes
client-side; `verizon_router_client` regex/brace-matches this, while
`quantum_gateway` uses a real JS parser (`esprima`) with an AST visitor
(`src/quantum_gateway.py`, class `Gateway3100.get_connected_devices` —
https://raw.githubusercontent.com/cisasteelersfan/quantum_gateway/master/src/quantum_gateway.py).
This cross-confirmation from two unrelated codebases is strong evidence the
admin interface really is this proprietary blob format rather than a clean
REST/JSON API.

**Per-device data available:**
- `GET /cgi/cgi_owl.js` → `known_device_list`: `{mac, hostname, ip,
  mac_vendor, device_os, activity}` — presence/metadata only, **no byte
  counters** —
  `Brishen/verizon_router_client:tests/test_parsing.py` (fixture) and
  `src/verizon_router_client/cr1000a.py` (`parse_known_devices`) —
  https://raw.githubusercontent.com/Brishen/verizon_router_client/master/tests/test_parsing.py
- `GET /cgi/cgi_bandwith.js` (endpoint name misspelled in the firmware
  itself) → `hosts_trafstat`: cumulative counters **keyed by MAC, nested
  under five fixed rolling windows (3600s/1h, 43200s/12h, 86400s/24h,
  604800s/7d, 2592000s/30d)**:
  ```
  {"3600": {"00:00:5e:00:53:01": {"mac": "...", "packets_tx": 126,
    "bytes_tx": 20486, "packets_rx": 35, "bytes_rx": 4561}}}
  ```
  — `Brishen/verizon_router_client:src/verizon_router_client/cr1000a.py`
  (`get_host_traffic_stats`) and
  `tests/test_parsing.py:7-21` —
  https://raw.githubusercontent.com/Brishen/verizon_router_client/master/src/verizon_router_client/cr1000a.py,
  https://raw.githubusercontent.com/Brishen/verizon_router_client/master/tests/test_parsing.py.
  These are **cumulative rolling-window totals, not instantaneous rates** —
  a download/upload rate would have to be derived client-side by polling the
  1-hour bucket repeatedly and differencing successive samples.
- The same endpoint also returns `get_history_rates`: three series of
  2-element `["bytes-ish", "bytes-ish"]` pairs with **no field names, no
  comment, and no documentation anywhere in the client source, tests, or
  README** as to whether this is a WAN-level cumulative or rate series —
  `tests/mock_router.py:27-29` —
  https://raw.githubusercontent.com/Brishen/verizon_router_client/master/tests/mock_router.py.
  This is the only candidate for a gateway/WAN-level "network total" found,
  and its semantics are an explicit, unresolved gap.

**Refresh-cadence feasibility (2–5s target): UNKNOWN, and this is the
single most important open question.** No source documents how frequently
the firmware itself refreshes the `hosts_trafstat` rolling buckets
internally. If the 1-hour bucket only updates at coarse intervals (minutes),
a 2–5s poll would often see repeated identical values and produce a stepped,
aliased rate rather than a smooth one — a real risk the project's 2-5s
target must account for. The only documented rate-limiting behavior is
**login throttling** ("Real routers throttle rapid logins; reuse the
session-wide login" — code comment in
`Brishen/verizon_router_client:tests/test_integration.py`, from a real
hardware-backed integration test), which argues for reusing a session cookie
rather than re-authenticating each poll, but says nothing about steady-state
GET polling limits on `/cgi/cgi_bandwith.js` itself at sub-5s intervals —
untested, unknown.

**TR-069/TR-181 cross-check.** Fetched directly from Broadband Forum's own
spec repository:
`BroadbandForum/cwmp-data-models:tr-181-2-21-0-hosts.xml` —
https://raw.githubusercontent.com/BroadbandForum/cwmp-data-models/master/tr-181-2-21-0-hosts.xml.
`Device.Hosts.Host.{i}.WANStats` (`BytesSent`/`BytesReceived`/
`PacketsSent`/`PacketsReceived`) are explicitly **cumulative monotonic
counters**, and the spec text states entries "SHOULD NOT be removed … if
there are WANStats statistics collected for the entry in the past seven
days" — i.e. this data model is designed for long-window usage accounting,
not sub-5-second polling, even where it exists. More importantly, **neither
GitHub client observed TR-181 paths on the wire** — the real local HTTP(S)
traffic from both clients is the proprietary `addROD`/`addCfg` blob format
above, not TR-069/USP parameter names. TR-181 is relevant only as the data
model Verizon's own remote-management (ACS) backend would likely use over
the WAN management plane, not as a locally-reachable API. No SNMP OIDs or
UPnP/IGD service/action names appear in either client's source, tests, or
README; their presence or absence on this gateway is an open gap, not a
primary-sourced "no."

**Measurement coverage limits.** TR-181's own spec text states that a host's
`AssociatedDevice`/`Layer1Interface` fields are `null` "if … the host isn't
directly connected to this device (e.g., the host is connected to a Wi-Fi
Access Point)" (same XML, lines ~282-330) — i.e. the standard does not
guarantee that a mesh satellite's clients are fully attributed in the main
gateway's host table; that depends on vendor-specific mesh/controller
behavior. No equivalent wired/wireless/interface-type field was observed in
the real `known_device_list`/`hosts_trafstat` fixtures used by either
GitHub client, so **whether the proprietary local API on this gateway
distinguishes wired from wireless, or flags extender-attached devices, is
unverified** — a gap, not an assumption either way. Verizon's own support
pages that should document the "Connected Devices" admin-UI page
(`knowledge-base-308501`, `knowledge-base-301481`, and the product pages for
`fios-quantum-gateway`/`verizon-router`) could not be fetched as usable
content — they returned only generic session-timeout or JS-rendered
placeholder HTML, not article text, each attempted directly:
https://www.verizon.com/support/knowledge-base-308501/,
https://www.verizon.com/support/knowledge-base-301481/,
https://www.verizon.com/support/residential/internet/equipment/routers/fios-quantum-gateway,
https://www.verizon.com/support/residential/internet/equipment/routers/verizon-router.
Any claim that the admin UI visually shows real-time per-device speed
columns is therefore **UNVERIFIED (no primary source loaded)**.

---

## 3. Internet access pause/resume

Verizon's own CR1000B manual confirms a native, local (non-cloud) per-device
pause control reachable at the same local admin URL identified by the user's
TLS fingerprinting:

> "Click the **Block/Allow** option to quickly disable/enable a device from
> having internet access." — CR1000B Router Guide, "Connected Devices"
> section, p.63 —
> https://www.verizon.com/content/dam/verizon/support/consumer/documents/internet/verizon-cr1000b-router-guide.pdf
>
> local URL confirmed in the same manual: "enter: mynetworksettings.com …
> Alternately, you can enter: https://192.168.1.1" (p.23) — matching the
> TLS cert/IP the user already found.

The sibling "Verizon Router User Guide" (©2021, CR1000A-family cover,
treated here as the closest documented relative of CR1000B) spells out the
same UI pattern in more detail plus two additional mechanisms, all
reversible from the admin UI:

1. **Devices page Block/Allow toggle** — "The Devices section allows you to
   view and manage all the devices … You can view device details, block
   internet service, and rename devices" (p.64); "Block/Allow - Click this
   option to quickly enable/disable a device from having internet access"
   (p.65).
2. **Parental Controls rules** — scheduled or immediate "All Internet
   OFF"/"All Internet ON" rules assignable to specific devices, removable
   from the Active Rules list (p.69-73).
3. **Access Control (Firewall)** — per-device, per-service ACL rules with an
   explicit non-destructive disable/re-enable checkbox: "To reinstate the
   restriction, select the check box … To remove an access restriction,
   select the service and click Remove" (p.82-85).
   — https://www.verizon.com/content/dam/verizon/support/consumer/documents/internet/verizon-router_user-guide.pdf

**Policy-state readback: partially supported, not confirmed as a
machine-readable field.** At the UI level, the Devices page and Access
Control page both visibly display current Block/Allow and enabled/disabled
state, so a human (or an HTML re-fetch) can read it back rather than assume
success (same PDF, p.64-65, p.84-85). However, **neither open-source GitHub
client exposes a documented "is this device blocked" JSON/JS field** — a
full-file keyword search of both `cr1000a.py` and `quantum_gateway.py` for
"block"/"deny"/"parental" found no functional matches tied to device state
(only an unrelated `urllib3` connection-pool parameter named `block` in
`cr1000a.py`). Whether the firmware's `cgi_owl.js`/`cgi_basic.js` payload
carries an explicit blocked/allowed boolean that a script could read without
screen-scraping the HTML Devices/Access Control page is an **open gap** that
only direct inspection of the real gateway's response can resolve.

**Established-connection enforcement: undocumented.** The user guide
describes general firewall/session semantics only for the inbound
NAT/security-level engine ("If the request is permitted to pass, all
subsequent data … is also allowed to pass", p.79-80) but never states
whether the Devices-page Block/Allow toggle or an Access Control rule tears
down an already-established outbound TCP session from the targeted device,
or only blocks new connections. A full-text search of the 199-page extracted
manual for "established," "existing session," "terminate" found no
statement addressing this for the block features specifically — flagged
explicitly as unknown, to be verified empirically, not assumed either way
(consistent with the README's own caution that "a confirmed gateway rule is
not proof that every existing connection has stopped").

**Router-UI recovery path: confirmed.** The same toggles are directly
reversible from the admin UI with no dependency on any external app: Devices
page Block/Allow is a one-click toggle (p.65); Access Control restrictions
can be unchecked or removed (p.85). This confirms that if the Windows app
crashes or is not running, a user can always log into
`https://192.168.1.1` / `mynetworksettings.com` directly and manually restore
access — no single point of failure in the recovery path.

---

## 4. Authentication model

Confirmed directly from the Verizon Router User Guide:

- **Default credential location**: "enter the password that is printed next
  to the Network Settings Password on the label on the rear of your router"
  (p.23-24), and the rear-panel label contains "the default settings for the
  Verizon Router's Wi-Fi name (SSID), Wi-Fi password (WPA2 key), local URL
  for accessing the router's network settings, and network settings
  password" (p.9) —
  https://www.verizon.com/content/dam/verizon/support/consumer/documents/internet/verizon-router_user-guide.pdf.
  The router presents a self-signed TLS certificate that browsers flag as
  untrusted ("Your connection is not private… Proceed to 192.168.1.1
  (unsafe)", p.23), consistent with the user's own observation of a
  cert bound to the hostname `mynetworksettings.com` rather than the IP.
- **Changing the password**: Advanced → Users → Edit (p.182).
- **Session controls documented by Verizon**: a configurable "Session
  Lifetime" (idle timeout) and a configurable number of concurrent logged-in
  users (p.172) — **Verizon documents a concurrent-session limit and an idle
  timeout, but no failed-login lockout/rate-limit setting** in the fetched
  manual text.
- **Actual login flow, verified from GitHub source** (two independent
  reverse-engineered clients): CR1000A/B-class gateways use a
  challenge-response scheme nicknamed "ArcMD5" in the client source —
  `md5(password_bytes)` hex-encoded, then `sha512` of that hex string,
  seeded by a `loginToken` fetched from `GET /loginStatus.cgi`, POSTed to
  `/login.cgi` as `luci_username`/`luci_password` —
  `Brishen/verizon_router_client:src/verizon_router_client/cr1000a.py`
  (functions `arc_md5`, `luci_username`, `luci_password`, `login`,
  `login_status`) —
  https://raw.githubusercontent.com/Brishen/verizon_router_client/master/src/verizon_router_client/cr1000a.py.
  A `sysauth` session cookie is returned and reused for subsequent requests
  (confirmed in `tests/mock_router.py`). The older G1100 model instead uses
  a cleaner `POST /api/login` with salt+SHA512 and a plain JSON `GET
  /api/devices` response (`cisasteelersfan/quantum_gateway:src/quantum_gateway.py`,
  class `Gateway1100`) — but that API carries presence only, no bandwidth
  fields, and is not the CR1000B's generation.

**Gaps (this pass's research output was truncated before completion):** the
research pass did not reach a documented answer on (a) whether a CSRF token
accompanies state-changing POSTs beyond the `sysauth` cookie (the summary of
this pass asserted a "CSRF-like apply token from loginStatus.cgi" but the
detailed citation for that claim was not reached before output was cut off —
treat as **unverified pending re-check**), and (b) any explicit rate-limiting
behavior beyond Verizon's documented concurrent-session/idle-timeout
settings and the GitHub test comment that "real routers throttle rapid
logins" (`Brishen/verizon_router_client:tests/test_integration.py`, cited
above). Credential storage implication: the credential is a **static admin
password** (not a one-time token), so a Windows implementation would need
to store it via a secure OS credential store (e.g. Windows Credential
Manager/DPAPI) rather than plaintext config — this is an inference from the
static-password login flow above, not a Verizon-documented recommendation.

---

## 5. Verizon-imposed constraints

- **Remote management channel confirmed, mechanics unconfirmed.** The
  CR1000B's own feature list states "Secure remote management using HTTPS
  or Verizon app" —
  https://www.verizon.com/content/dam/verizon/support/consumer/documents/internet/verizon-cr1000b-router-guide.pdf
  (p.7) — confirming a Verizon-controlled remote-management channel exists
  alongside the local admin UI. No primary Verizon page describing TR-069/
  CWMP mechanics or an automatic/silent firmware-push policy could be found
  or fetched; all such claims surfaced by general web search were
  third-party (not verizon.com) and are **explicitly unverified**. The
  194-page CR1000B manual itself contains no "check for firmware update"
  section (full-text searched), which is circumstantial, not proof, of a
  server-pushed update model.
- **Terms of Service / Acceptable Use Policy.** The full Acceptable Use
  Policy was fetched and read —
  https://www.verizon.com/about/terms/acceptable-use-policy. Its clauses
  target unauthorized access to *others'* systems and network-harmful
  conduct (e.g. "penetrate the security measures of Verizon or any other
  person's computer system"; "conduct harmful to the Verizon network, the
  Internet generally or other users"); **no clause in the fetched text
  explicitly addresses a customer automating their own owned/leased
  gateway's local admin UI with their own credentials on their own LAN.**
  This is a reasonable reading of the quoted clauses, not an explicit
  Verizon statement either way. The deeper Residential Terms and Conditions
  document rendered only a short stub page
  (https://www.verizon.com/about/terms/residential-terms-and-conditions) —
  the full legal text (which might contain a more specific
  no-automation/no-reverse-engineering clause) could not be retrieved as
  static text — **gap**.
- **My Fios / Verizon Home app pause path appears cloud/account-gated, but
  a separate local path exists.** Verizon's parental-controls landing page
  directs CR1000A/CR1000B/G3100 owners to manage pause/parental controls by
  "signing into the Verizon Home app / My Verizon account" —
  https://www.verizon.com/support/residential/internet/security/parental-controls.
  Deeper FAQ/instructional pages
  (https://www.verizon.com/support/residential/internet/essentials/fios-parental-controls-faqs/,
  https://www.verizon.com/support/knowledge-base-308506/) could not be
  fetched as usable content (JS-rendered FAQ fragments / an authenticated
  "your session will end due to inactivity" placeholder) — suggestive of an
  account-gated flow but **not an explicit confirming statement** that the
  mobile app's pause feature requires cloud/account connectivity.
  Independent of that open question, **the CR1000B's own local admin UI
  exposes the same Block/Allow control with no cloud dependency** (section 3
  above, p.63 of the CR1000B manual) — this is the more directly relevant,
  confirmed finding for a local-only app.

---

## Verdict against the gate

Evaluated against the README's acceptance criteria that depend on gateway
capability (criteria 3, 6 and 7 are pure app-side logic — chart retention,
protecting the management computer, discarding history on close — and are
not evaluable by desk research; they are excluded here).

| # | README acceptance criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | "Controlled download and upload traffic is attributed to the correct test-device entry." | **SUPPORTED, with caveats** | A per-device, per-MAC byte-counter table (`hosts_trafstat`) exists behind the local authenticated admin API and is independently confirmed by two unrelated open-source clients (section 2). Caveat: this evidence is drawn from the CR1000A-generation firmware/clients; CR1000B-specific confirmation of the identical endpoint shape is inferred from shared product lineage, not independently verified (section 1, gap). |
| 2 | "Measurements update within 5 seconds when the gateway supports that target." | **UNKNOWN — explicitly unresolved, flagged as the key open question** | The only per-device data found is cumulative counters in fixed rolling windows (1h/12h/24h/7d/30d); no primary source documents the firmware's internal refresh interval for these buckets. A 2-5s poll could see stale/identical values if the underlying bucket updates less often than that. **This must be measured empirically against the real device before committing to the 2-5s target**; if it cannot be met, the README itself directs revisiting the requirement before implementation. |
| 4 | "A noncritical test device can be paused and resumed, with gateway-confirmed policy state and observed internet behavior checked separately." | **PAUSE/RESUME: SUPPORTED. GATEWAY-CONFIRMED STATE: UNKNOWN** | Verizon's own CR1000B manual confirms a reversible, local Block/Allow toggle (section 3) — the pause/resume primitive itself is solidly supported. However, no primary source confirms a machine-readable field reporting block state (vs. needing to parse/screen-scrape the admin HTML); this must be confirmed by inspecting the real gateway's `cgi_owl.js`/`cgi_basic.js` responses. Enforcement limits on already-established connections are also undocumented (section 3). |
| 5 | "Lost gateway connectivity produces a visible stale/error state, and reconnection rereads policy state." | **UNKNOWN (depends on criterion 4)** | The login/session flow is well understood and reconnection is architecturally straightforward (section 4), but "rereading policy state" inherits the same unresolved question as criterion 4: whether block/allow state is available as a structured, re-readable field rather than only as rendered HTML. |

**Bottom line:** the gateway is very likely capable of supporting this
project's scope — a real local admin API with per-device counters and a
local, reversible pause control both exist in primary sources for this
product family — but the **2-5 second refresh target is not yet
confirmed achievable** and the **machine-readable policy-state readback**
needed for criteria 4 and 5 is not yet confirmed. Both are resolvable only
by hands-on testing against the actual CR1000B, not further desk research.

---

## Open questions for hands-on verification

In priority order, since only testing the physical device can settle these:

1. **Confirm the CR1000B exposes the same endpoints as the CR1000A-era
   clients.** Authenticate to `https://192.168.1.1` (or
   `mynetworksettings.com`) using the router-label password, and confirm
   `GET /cgi/cgi_owl.js` and `GET /cgi/cgi_bandwith.js` (or equivalent
   endpoint names) exist and return the same `addROD("known_device_list", …)`
   / `addROD("hosts_trafstat", …)` shapes documented in
   `Brishen/verizon_router_client`. If the CR1000B uses different endpoint
   names, capture the actual ones via browser dev tools network tab while
   loading the Devices and Connected Devices admin pages.
2. **Measure the real update cadence of `hosts_trafstat`.** Poll the 1-hour
   (3600s) bucket for a known MAC at 1-2 second intervals while generating a
   controlled, known amount of traffic from a test device, and observe how
   often the byte counters actually change. This single test determines
   whether the README's 2-5s refresh target is achievable via differencing,
   or whether the requirement must be revisited per the README's own
   escape hatch.
3. **Determine the semantics of `get_history_rates`.** Capture real values
   from this field alongside known WAN throughput (e.g. during a measured
   speed test) to determine whether it is a true WAN-level cumulative
   counter, an already-computed rate, or something else — and whether it
   can serve as the README's "network total" distinct from the sum of
   per-device entries.
4. **Check for an explicit block/allow state field.** Inspect the raw
   `cgi_owl.js`/`cgi_basic.js` (or equivalent) response for a device that
   has been Block/Allow-toggled via the admin UI, to determine whether
   block state is a structured field (ideal) or only reflected in rendered
   HTML on the Devices page (would require HTML parsing instead of a clean
   API read).
5. **Test established-connection enforcement.** Start a long-lived
   connection (e.g. a streaming download) from a test device, then toggle
   Block/Allow off, and observe whether the existing connection is torn
   down immediately or continues until it naturally ends — this directly
   affects what "paused" means to the user and whether the app's messaging
   needs to caveat it.
6. **Check wired vs. wireless vs. extender-attached device coverage.**
   With a device connected directly to the CR1000B's own radio/Ethernet
   port and another connected only through a paired Wi-Fi Extender Mini (if
   one is present on this network), confirm whether both appear in
   `known_device_list`/`hosts_trafstat`, and whether any field distinguishes
   them (interface type, parent AP, etc.).
7. **Confirm login throttling and steady-state polling limits.** Determine
   the actual lockout/backoff behavior after repeated failed logins (do
   this carefully and sparingly to avoid being locked out of the real
   device), and confirm that polling `cgi_bandwith.js` every 2-5 seconds
   with a reused session cookie does not trigger rate limiting or session
   invalidation over a sustained monitoring session.
8. **Confirm whether a CSRF token or anti-replay field is required on
   state-changing requests** (e.g. the Block/Allow toggle), beyond the
   `sysauth` session cookie, by inspecting the actual POST request the
   admin UI sends when toggling a device — this determines whether the
   Windows app's pause/resume implementation needs to fetch and resubmit a
   token per command.
9. **Decide the CR1000A-vs-CR1000B question definitively.** Everything
   above is sourced from CR1000A-generation documentation/clients; confirm
   directly against the live CR1000B (via browser dev tools, not
   unauthorized scripting) that the endpoint names, auth flow, and response
   shapes are identical before relying on them in the application.
