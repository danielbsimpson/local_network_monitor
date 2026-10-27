"""THROWAWAY SPIKE - not application code.

Answers one question from the hardware feasibility gate in ../README.md:

    How often does the CR1000B actually update its per-device byte counters?

The gateway reports per-device internet usage as cumulative rolling-window
counters, not as download and upload rates. Rates have to be derived by
differencing successive samples, so the counters' real refresh interval sets
the floor on how fresh a measurement can ever be. No primary source documents
it (see docs/research/verizon-cr1000b-gateway.md), so measure it.

Usage:
    $env:CR1000B_PASSWORD = "<network settings password from router label>"
    python spike/cr1000b_cadence_probe.py --duration 120

While it runs, generate sustained traffic on a test device (a large download
or a video stream) so its counters are definitely moving.

The password is read from the environment, never echoed and never written to
disk. Sample data stays in memory unless --save is passed.
"""

from __future__ import annotations

import argparse
import ast
import hashlib
import http.cookiejar
import json
import os
import re
import ssl
import statistics
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

ADD_ROD_RE = re.compile(r'addROD\("(?P<key>[^"]+)",\s*(?P<val>.*?)\);\s*', re.DOTALL)
HOUR_BUCKET = "3600"


def arc_md5(s: str) -> str:
    """The firmware's ArcMD5: md5 of the JS 8-bit bytes, then sha512 of that hex."""
    js_bytes = bytes((ord(ch) & 0xFF) for ch in s)
    md5_hex = hashlib.md5(js_bytes).hexdigest()
    return hashlib.sha512(md5_hex.encode("ascii")).hexdigest()


def luci_password(password: str, login_token: str) -> str:
    if not login_token:
        return arc_md5(password)
    return hashlib.sha512((login_token + arc_md5(password)).encode("ascii")).hexdigest()


def parse_js_literal(raw: str):
    text = raw.strip()
    for parser in (json.loads, ast.literal_eval):
        try:
            return parser(text)
        except (ValueError, SyntaxError):
            continue
    return text


class Gateway:
    def __init__(self, host: str, timeout: float = 10.0):
        self.base = f"https://{host}"
        self.timeout = timeout
        ctx = ssl.create_default_context()
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPSHandler(context=ctx),
            urllib.request.HTTPCookieProcessor(self.jar),
        )

    def _request(self, path: str, data: bytes | None = None) -> str:
        req = urllib.request.Request(
            f"{self.base}{path}",
            data=data,
            headers={
                "Referer": f"{self.base}/",
                "User-Agent": "cr1000b-cadence-spike",
            },
        )
        with self.opener.open(req, timeout=self.timeout) as resp:
            return resp.read().decode("utf-8", errors="replace")

    def login_status(self) -> dict:
        return json.loads(self._request("/loginStatus.cgi"))

    @staticmethod
    def _is_logged_in(status: dict) -> bool:
        # The live CR1000B reports "islogin"; accept the camelCase spelling too
        # in case other firmware revisions differ.
        for key in ("islogin", "isLoggedIn", "isLogin"):
            if key in status:
                return str(status[key]).strip().lower() in {"1", "true", "yes"}
        return False

    def login(self, username: str, password: str) -> None:
        token = self.login_status().get("loginToken", "")
        if not isinstance(token, str) or len(token) != 32:
            raise RuntimeError(f"Unexpected loginToken shape: {token!r}")
        token = token.lower()
        payload = urllib.parse.urlencode(
            {
                "luci_username": arc_md5(username),
                "luci_password": luci_password(password, token),
                "luci_view": "Mobile",
                "luci_token": token,
                "luci_keep_login": "0",
            }
        ).encode("ascii")
        self._request("/login.cgi", data=payload)

        if not self._is_logged_in(self.login_status()):
            raise RuntimeError(
                "Login rejected. Check the Network Settings Password on the router "
                "label, and note the firmware throttles rapid repeated logins."
            )

    def bandwidth_rod(self) -> dict:
        text = self._request("/cgi/cgi_bandwith.js")
        rod = {m.group("key"): parse_js_literal(m.group("val")) for m in ADD_ROD_RE.finditer(text)}
        if not rod:
            raise RuntimeError(
                "No addROD() entries in /cgi/cgi_bandwith.js. The CR1000B may use a "
                "different endpoint than the CR1000A-era clients; capture the real "
                "one from browser dev tools on the Devices page."
            )
        return rod


def counters_for(rod: dict) -> dict[str, tuple[int, int]]:
    """Per-MAC (bytes_rx, bytes_tx) from the 1-hour rolling bucket."""
    trafstat = rod.get("hosts_trafstat")
    if not isinstance(trafstat, dict):
        raise RuntimeError(f"Unexpected hosts_trafstat payload: {type(trafstat)}")
    hosts = trafstat.get(HOUR_BUCKET) or trafstat.get(int(HOUR_BUCKET)) or {}
    out = {}
    for mac, stats in hosts.items():
        if isinstance(stats, dict):
            out[mac.lower()] = (int(stats.get("bytes_rx", 0)), int(stats.get("bytes_tx", 0)))
    return out


def report(change_times: dict[str, list[float]], samples: int, elapsed: float, interval: float) -> None:
    print("\n" + "=" * 72)
    print(f"Polled {samples} times over {elapsed:.1f}s at {interval}s intervals")
    print("=" * 72)

    movers = {mac: ts for mac, ts in change_times.items() if len(ts) >= 2}
    if not movers:
        print("\nNo device entry's counters changed more than once.")
        print("Either no traffic was flowing, or the bucket updates slower than this run.")
        print("Re-run with a longer --duration while downloading something large.")
        return

    print(f"\n{'device entry (MAC)':<20} {'changes':>8} {'min gap':>9} {'median':>9} {'max gap':>9}")
    print("-" * 72)
    all_gaps = []
    for mac, times in sorted(movers.items(), key=lambda kv: -len(kv[1])):
        gaps = [b - a for a, b in zip(times, times[1:])]
        all_gaps.extend(gaps)
        print(
            f"{mac:<20} {len(times):>8} {min(gaps):>8.1f}s "
            f"{statistics.median(gaps):>8.1f}s {max(gaps):>8.1f}s"
        )

    median_gap = statistics.median(all_gaps)
    print("\n" + "-" * 72)
    print(f"Median interval between counter updates: {median_gap:.1f}s")
    print("\nVERDICT on README acceptance criterion 2 (updates within 5 seconds):")
    if median_gap <= 5.0:
        print(f"  ACHIEVABLE. Counters move every ~{median_gap:.1f}s, within the 5s target.")
        print("  Derive rates by differencing successive samples.")
    else:
        print(f"  NOT ACHIEVABLE AS WRITTEN. Counters move only every ~{median_gap:.1f}s.")
        print("  Polling every 2-5s would repeat identical values and produce a stepped,")
        print("  aliased chart. Per the README, revisit the requirement before building:")
        print(f"  either relax the refresh target toward {median_gap:.0f}s, or change the")
        print("  measurement source. Do not present repeated values as fresh measurements.")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", default="192.168.1.1")
    ap.add_argument("--username", default="admin")
    ap.add_argument("--interval", type=float, default=1.5, help="seconds between polls")
    ap.add_argument("--duration", type=float, default=120.0, help="seconds to run")
    ap.add_argument("--mac", help="watch only this device entry")
    ap.add_argument("--save", help="write raw samples to this .jsonl path (gitignored)")
    args = ap.parse_args()

    password = os.environ.get("CR1000B_PASSWORD")
    if not password:
        print('Set the password first:  $env:CR1000B_PASSWORD = "<router label password>"', file=sys.stderr)
        return 2

    gw = Gateway(args.host)
    print(f"Authenticating to {args.host} ...")
    try:
        gw.login(args.username, password)
    except (urllib.error.URLError, RuntimeError) as exc:
        print(f"FAILED: {exc}", file=sys.stderr)
        return 1
    print("Logged in.\n")

    rod = gw.bandwidth_rod()
    print(f"addROD keys present: {', '.join(sorted(rod))}")
    print(f"  known_device_list : {'yes' if 'known_device_list' in rod else 'MISSING'}")
    print(f"  hosts_trafstat    : {'yes' if 'hosts_trafstat' in rod else 'MISSING'}")
    print(f"  get_history_rates : {'yes' if 'get_history_rates' in rod else 'MISSING'}"
          "   (candidate network total, semantics unknown)")
    buckets = rod.get("hosts_trafstat")
    if isinstance(buckets, dict):
        print(f"  rolling windows   : {', '.join(sorted(map(str, buckets)))}")
    print(f"\nPolling the {HOUR_BUCKET}s bucket every {args.interval}s for {args.duration:.0f}s.")
    print("Generate sustained traffic on a test device now. Ctrl-C to stop early.\n")

    previous: dict[str, tuple[int, int]] = {}
    change_times: dict[str, list[float]] = {}
    sink = open(args.save, "a", encoding="utf-8") if args.save else None
    started = time.monotonic()
    samples = 0

    try:
        while time.monotonic() - started < args.duration:
            now = time.monotonic()
            try:
                counters = counters_for(gw.bandwidth_rod())
            except (urllib.error.URLError, RuntimeError) as exc:
                print(f"  [{now - started:6.1f}s] poll failed: {exc}")
                time.sleep(args.interval)
                continue

            samples += 1
            if args.mac:
                counters = {k: v for k, v in counters.items() if k == args.mac.lower()}
            if sink:
                sink.write(json.dumps({"t": round(now - started, 3), "counters": counters}) + "\n")
                sink.flush()

            moved = []
            for mac, value in counters.items():
                if mac in previous and previous[mac] != value:
                    change_times.setdefault(mac, []).append(now)
                    d_rx = value[0] - previous[mac][0]
                    d_tx = value[1] - previous[mac][1]
                    moved.append(f"{mac} rx+{d_rx} tx+{d_tx}")
                previous[mac] = value

            flag = "CHANGED" if moved else "   -   "
            print(f"  [{now - started:6.1f}s] {len(counters):3d} entries  {flag}  {'; '.join(moved[:3])}")
            time.sleep(args.interval)
    except KeyboardInterrupt:
        print("\nStopped early.")
    finally:
        if sink:
            sink.close()

    report(change_times, samples, time.monotonic() - started, args.interval)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
