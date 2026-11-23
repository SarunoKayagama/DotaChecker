# Architecture and locked decisions

## Status

Iterations 1–5 are implemented: shell, connection monitor, ICMP route discovery, ICMP continuous monitoring
and RouteAnalyzer with Wilson CI, block bootstrap and ordered stable route fingerprint.
**Do not continue to UDP/TCP until ICMP monitoring and the analyzer have been validated on the controlled lab
(A/B/C/F/I scenarios) — see `docs/TEST_LAB.md`.**

## Modules

| Module | State | Notes |
|---|---|---|
| `DotaChecker.Core` | iterations 1–4 | Settings, diagnosis verdicts/models, statistics, route comparison. No UI, no WinAPI. |
| `DotaChecker.Network` | iterations 2–4 | IP Helper API, BCL Ping transport, discovery, DNS, continuous engine. |
| `DotaChecker.App` | iterations 1–4 | WPF shell, DI, themes, both tabs, context menu, custom RTT chart. |
| `DotaChecker.Tests` | iterations 1–4 | 23 tests: settings, statistics, discovery, route comparison, real WinAPI/Ping, engine. |
| `tools/NetCheck` | iteration 3 | Real-network TTL / parallel Ping verification utility. |
| `ConnectionMonitor`, `ProcessResolver` | implemented | `GetExtendedTcpTable`/`GetExtendedUdpTable` OWNER_MODULE. |
| `IcmpTraceroute`, `RouteDiscovery` | implemented | BCL `Ping` + `PingOptions.Ttl`, `IPStatus.TtlExpired`. |
| `ContinuousProbeEngine`, `StatisticsEngine`, `DnsResolver` | implemented | Parallel full-round probing plus e2e reference stream. |
| `RouteAnalyzer` | implemented, iteration 5 | Verdicts, Wilson CI, block bootstrap, φ₁, EvidenceScore. Design: `ROUTE_ANALYZER_DESIGN.md`. |
| `RouteHistory` | planned, iteration 6 | Persistent history of route versions and events. Controlled lab: `TEST_LAB.md`. |
| UDP / TCP transports | gated | See "Verification gate" below. |
| `ExportManager` | planned, iteration 9 | CSV/JSON/TXT. |

## Locked requirements

1. **RouteAnalyzer** produces one of four verdicts: `InsufficientData`, `IcmpRateLimit`, `Probable`, `Confirmed`.
   The e2e reference stream is a supporting signal only. Loss is assessed statistically: sample count,
   temporal correlation, measurement uncertainty and the different ICMP handling of routers vs. the
   destination all influence the verdict. No categorical conclusion below the configured minimum sample count.
2. **Primary goal is trustworthy localisation**, not merely displaying percentages: find the first hop where
   loss begins and then persists through the destination.
3. **Unbounded monitoring.** Statistics accumulate for the whole session; the user resets them explicitly.
   Probe interval and concurrency are configurable.
4. **UDP/TCP verification gate.** Before implementing those transports, experimentally confirm on the target
   OS which Windows APIs work, whether replies and TTL behaviour are observable, and whether elevation is
   actually required. No unconfirmed raw-socket capability may be baked into the architecture.
5. **MOS is informational only.** It must never be used to decide whether loss occurred or where it starts.
6. **A/B/C testing.** Automated scenarios with controlled loss and delay must verify both statistics and the
   identified segment. clumsy or an equivalent tool is used; deterministic unit tests use synthetic per-hop data.
7. **Independence and licensing.** Network modules depend only on `Core`, never on WPF. Third-party notices and
   reference attribution are maintained in `THIRD-PARTY-NOTICES.md` and `docs/REFERENCES.md`.

## Analyzer model (to be implemented in iteration 5)

Given loss per hop over a window:

- Find the first hop `k` with significant, sustained loss.
- If downstream loss does not carry through to the destination, classify as `IcmpRateLimit` at `k`.
- If it carries through, the suspect segment is `(k-1 → k)`; additional increases at later hops add further segments.
- Cross-check with the e2e reference stream and with other protocols when available; disagreement lowers confidence.
- Require temporal stability (multiple consecutive windows) for `Confirmed`; a single unstable window gives `Probable`;
  too few samples gives `InsufficientData`.

## Stopping semantics

Every monitoring session owns a `CancellationTokenSource`. Stopping the app cancels all probes, closes sockets,
flushes pending statistics and saves settings. `App.OnExit` in iteration 1 already establishes this shutdown path.

## Iteration 5 design locks (approved plan, not yet implemented)

- `RouteComparer` will compare the **ordered** hop structure and a per-TTL **stable address set**.
  Partial address intersection will not suppress a version change; ECMP stabilization uses persistence over
  the last 3 discoveries and a 3-discovery confirmation before committing a new version.
- e2e reference TTL becomes 255 (optionally a 64/128/255 ladder). It remains a supporting signal only.
- Terminology is strictly split: **probe response loss** (observable) vs **transit packet loss** (inferred).
  UI column will be renamed to "Нет ответа %" in iteration 5.
- Four verdicts only: `Confirmed`, `Probable`, `IcmpRateLimit`, `InsufficientData`.
  `Probable` may report a region instead of an exact segment.
- A/B/C validation uses a Linux network-namespace lab with `tc netem` and `iptables` statistic matching —
  not clumsy on the monitoring host. See `docs/TEST_LAB.md`.

## Known limitations (iterations 2–4)

- Windows `Ping` reports RTT in whole milliseconds; values below 1 ms are displayed as `<1 мс`.
- An absent ICMP reply is recorded as a timeout, never as proof of transit loss.
- The e2e reference stream uses TTL 64. It can be filtered or rate-limited exactly like intermediate ICMP,
  so it only contributes evidence; it is not authoritative.
- ECMP is represented as multiple responder addresses per hop. If sets intersect, the change is logged as an
  ECMP variation and does not create a new statistics version.
- Settings (interval, timeout, concurrency) are read when a session starts; changing them mid-session
  requires a restart of that session.
- IPv4 only. UDP/TCP transports are intentionally absent pending the API verification gate.
