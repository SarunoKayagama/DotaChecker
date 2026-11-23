# Third-Party Notices

This file lists third-party components and reference projects used by **Dota Checker**.
Full license texts are shipped with the corresponding NuGet packages and in the repositories linked below.

## Used as dependencies

| Component | Version | License | Purpose |
|---|---|---|---|
| WPF UI (`WPF-UI`) | 4.3.0 | MIT | Fluent controls, light/dark themes |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | MVVM base classes, commands, source generators |
| Microsoft.Diagnostics.Tracing.TraceEvent | 4.0.2 | MIT | ETW session control and payload parsing for the isolated `UdpActivityProbe` prototype. Justification: hand-written ETW P/Invoke plus MOF/manifest decoding would require substantially more unsafe interop and field-name guesswork; TraceEvent ships the official parsers. |
| System.ServiceProcess.ServiceController | 10.0.15 | MIT | Read-only AmneziaWG tunnel service state in `AmneziaWgController`. |
| Microsoft.Extensions.DependencyInjection | 10.0.0 | MIT | DI container |
| System.Text.Json | in the .NET 10 SDK | MIT | Settings persistence |
| xunit | 2.9.3 | Apache-2.0 | Unit tests |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | Test runner |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | Test host |
| AwesomeAssertions | 9.3.0 | MIT | Fluent assertions |
| NSubstitute | 5.3.0 | BSD-3-Clause | Mocking |
| coverlet.collector | 6.0.4 | MIT | Coverage |

## Used as design references (no code copied)

| Project | License | What was taken |
|---|---|---|
| [PingerPlot](https://github.com/jamesccupps/PingerPlot) | MIT | Full-round parallel probing, per-TTL ring buffers, jitter definition (mean absolute RTT change, lost probes skipped), 3-round reroute confirmation, rule "loss that stops at a router is not transit loss", Windows TCP RST timing caveat |
| [datadog-traceroute](https://github.com/DataDog/datadog-traceroute) | Apache-2.0 | Separate end-to-end reference probe stream, packet-ID allocation per TTL, Paris-traceroute idea for ECMP |
| [microsoft/ethr](https://github.com/microsoft/ethr) | MIT | Loss/latency/jitter measurement methodology, Windows firewall/admin caveats |
| [WinMTR](https://github.com/WinMTR/WinMTR-Official) | **GPL-2.0** | Behavioural reference only. **No source code copied** — copyleft would force the whole application under GPL. |
| [potraceroute](https://github.com/velotraveler/potraceroute) | **GPL-3.0** | Behavioural reference only. No source code copied. |
| [clumsy](https://github.com/jagt/clumsy) | MIT | Test-only tool for controlled loss/latency injection (WinDivert). Not redistributed with the app. |

## Deliberately not used

- **Npcap / SharpPcap** — Npcap's free licence restricts redistribution and caps installs; the built-in Windows raw socket (`SOCK_RAW`, `SIO_RCVALL`) is sufficient and driver-free. Kept as a possible fallback only if raw receive proves unreliable on real hardware.

## Policy

Any algorithm ported from an MIT/Apache-2.0 reference project is documented in
[`docs/REFERENCES.md`](docs/REFERENCES.md) with its origin and an attribution comment in the source file.
GPL-licensed projects are read for behaviour only; no code, no derived code and no verbatim data structures are copied.
This file is updated in the same commit as any new dependency.
