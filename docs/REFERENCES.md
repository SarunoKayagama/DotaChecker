# Design references

This document records which external ideas are used, so attribution stays auditable.
It is not a license file; see [`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md).

## PingerPlot (MIT)

- **Full round = full traceroute.** Every round probes all known hops in parallel, so a round costs about one timeout rather than the sum of timeouts. Planned for `ContinuousProbeEngine`.
- **Jitter** is the mean absolute change between consecutive RTT replies, with lost probes skipped. It is deliberately *not* the standard deviation: a permanent latency step would otherwise be reported as jitter.
- **Reroute confirmation** requires a hop's new responder to persist for 3 rounds; a router that alternates (ECMP) is recorded once instead of generating an event storm.
- **Loss interpretation**: loss at an intermediate hop that is absent from downstream hops is that router deprioritising ICMP, not transit loss.
- **Windows TCP caveat**: a connecting socket receives RST only after Windows finishes retransmitting SYN (~2 s). TCP probe timeout must be ≥3000 ms. `TCP_MAXRT` is not used because it replaces `WSAECONNREFUSED` with `WSAETIMEDOUT` and destroys the closed-port signal. These constraints are encoded in `AppSettings.TcpProbeTimeoutMs`.

## datadog-traceroute (Apache-2.0)

- **End-to-end reference probe** is measured separately from the traceroute queries. In our design it is one signal among several, never an absolute source of truth: the target may filter ICMP differently from routers, and its own control plane may be loaded.
- **Packet IDs allocated per TTL** so replies can be correlated deterministically without relying on global counters.
- **Paris traceroute** keeps probe ports constant to avoid ECMP splitting a logical path into several physical ones. Full Paris mode needs raw TCP sends, which Windows does not permit; we will fix the source port per hop and explicitly surface the residual ECMP uncertainty in the UI.

## microsoft/ethr (MIT)

- Methodology for loss, latency and jitter measurement across TCP/UDP/ICMP; Windows requirements for firewall rules and elevation for TCP traceroute.

## Behavioural-only references (GPL)

WinMTR (GPL-2.0) and potraceroute (GPL-3.0) were read to understand expected statistics and Windows limitations. No source code was copied. Copying their code would require releasing this application under GPL.

## Test tooling

clumsy (MIT, WinDivert-based) is used to inject controlled packet loss and latency for the A/B/C verification scenarios. It is a development tool, not a runtime dependency.
