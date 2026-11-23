# Dota Checker

Windows-утилита для непрерывного мониторинга сетевого маршрута, поиска участков с потерями пакетов и диагностики соединения с Dota 2.

Стек: Windows 10/11 x64, C# / .NET 10, WPF, MVVM, CommunityToolkit.Mvvm, WPF-UI.

## Возможности

- **Connections** — TCP/UDP через `GetExtendedTcpTable` / `GetExtendedUdpTable`, PID, имя и путь процесса, время создания, фильтрация, сортировка, контекстное меню IP.
- **Route Monitor** — ICMP discovery (`Ping` + `PingOptions.Ttl`), параллельные полные раунды проб, e2e reference (TTL 255), накопительная статистика, EWMA/jitter, графики, сброс вручную, ECMP-aware смена маршрута.
- **RouteAnalyzer** — Wilson CI, block bootstrap, φ₁, `EvidenceScore`; вердикты `InsufficientData`, `IcmpResponseSuppression`, `Probable`, `ProbableRegion`, `SuspectedSegment`, `Confirmed` (только контролируемый эксперимент).
- **Game Servers** — ETW-обнаружение UDP-endpoint Dota 2 (`Microsoft-Windows-Kernel-Network`), детерминированный классификатор, JSON-история, ICMP-мониторинг выбранного endpoint.
- **WARP Profiles** — WARPSCOUT как внешний процесс (отчёт и `.conf` сохраняются за один запуск), AmneziaWG-профили только для чтения, состояние туннеля через `awg show`.
- **Route Optimization** — сравнение Direct/WARP до одного endpoint с проверкой интерфейсов; приоритет доставка → jitter → RTT; недостоверное сравнение даёт `RouteComparisonInvalid`.
- **Settings** — темы, интервалы, порты, пути к WARPSCOUT и каталогу `.conf`.

UDP/TCP traceroute **не реализован**: требует raw ICMP-сокета и администратора; статус исследования — `docs/UDP_TCP_PROBING.md`. Route Controller — только дизайн (`docs/ROUTE_CONTROLLER_DESIGN.md`).

## Сборка и тесты

Нужен .NET 10 SDK.

```powershell
dotnet restore
dotnet build                                      # Debug
dotnet build -c Release
dotnet test                                       # все тесты
dotnet test --filter "Category!=EtwIntegration"   # без реального ETW
dotnet run --project src/DotaChecker.App
```

Ярлыки в корне создаются скриптом `tools/create-shortcuts.ps1`.

## Структура

```
src/DotaChecker.Core      модели, статистика, классификатор, RouteAnalyzer, RouteOptimizer
src/DotaChecker.Network   IP Helper API, ETW, Ping, WARPSCOUT, AmneziaWG, маршрутизация
src/DotaChecker.App       WPF UI: MVVM, вкладки, темы
tests/DotaChecker.Tests   unit + integration тесты
tools/EtwProbe            elevated-проверка ETW
tools/UdpProbe            прототип UDP TTL (не интегрирован)
tools/NetCheck            проверка ICMP TTL на реальной сети
tools/AnalyzerDemo        демо вердиктов анализатора
docs/                     проектная документация
THIRD-PARTY-NOTICES.md    лицензии и атрибуция
```

## Безопасность

Секреты (`warpscout-account.json`, `.conf`, PrivateKey) не логируются и не попадают в репозиторий — см. `.gitignore`. DotaChecker не меняет системные маршруты, DNS, туннели и не переключает VPN автоматически.

Подробности: [`docs/ITERATION6_FINAL_REPORT.md`](docs/ITERATION6_FINAL_REPORT.md), [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).
