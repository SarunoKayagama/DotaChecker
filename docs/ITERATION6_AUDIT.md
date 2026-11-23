# Iteration 6 — Audit (этап 0)

Статус: аудит выполнен. Этап 1 остановлен на прототипе ETW: запуск kernel-провайдера требует администратора,
реальные события получить не удалось. См. `docs/ETW_PROTOTYPE_REPORT.md`.

## 1. Состояние репозитория

- Решение: `DotaChecker.slnx`; проекты `Core`, `Network`, `App`, `Tests`, утилиты `NetCheck`, `AnalyzerDemo`.
- `Core`: `AppSettings`/`SettingsManager` (JSON), `ConnectionInfo`, `TransportProtocol`, диагностические модели,
  `RouteAnalyzer`, `HopStatistics`, `WindowAggregator`, `RouteComparer`/`RouteStabilityTracker`.
- `Network`: `ConnectionMonitor` + `ProcessResolver` + `IpHelperTables` (`GetExtendedTcpTable`,
  `GetExtendedUdpTable`, OWNER_MODULE), `IcmpProbeTransport`, `IcmpRouteDiscovery`,
  `ContinuousProbeEngine`, `DnsResolver`.
- `App`: WPF/MVVM, вкладки Connections и Route Monitor, `IDiagnosticsLauncher`, `RttChart`.
- Тесты: 32/32 зелёных; `RouteAnalyzer` и `ContinuousProbeEngine` **не трогаем**.

Ключевые точки расширения без переписывания:
- `ConnectionInfo` уже содержит PID/имя/путь и `Key`; можно добавить `GameEndpoint`-модель рядом, не меняя старую.
- `ContinuousProbeEngine.StartWithRouteAsync(RouteSnapshot)` уже позволяет мониторить один адрес (режим Continuous Ping) — готовый вход для игрового endpoint.
- `StatisticsEngine` и `WindowAggregator` переиспользуются для сравнения Direct/WARP; второй статистический движок не нужен.

## 2./windows API — что реально даёт IP Helper

| API | Remote IP/port | PID | Счётчики пакетов | Комментарий |
|---|---|---|---|---|
| `GetExtendedTcpTable(OWNER_MODULE)` | да | да | нет | подходит для Steam Coordinator/TCP |
| `GetExtendedUdpTable(OWNER_MODULE)` | **нет** | да | нет | только локальный endpoint + `liCreateTimestamp` |

**Критичный вывод:** `MIB_UDPROW_OWNER_MODULE` не содержит удалённый адрес. Обнаружить реальный UDP relay Dota 2 только по UDP table **нельзя**. Нужен отдельный модуль наблюдения UDP-активности.

### ETW как источник удалённых UDP-endpoint

`Microsoft-Windows-Kernel-Network` (GUID `{7DD42A49-5329-4832-8DFD-43D979153A88}`, keyword IPv4 `0x10`) в событиях
`UdpIp_TypeGroup1` (Send=10, Recv=11) содержит `PID`, `size`, `saddr`, `sport`, `daddr`, `dport`, `connid`.
Это даёт:
- удалённые IP/порты с привязкой к PID;
- счётчики отправленных/полученных пакетов и байт;
- last activity по времени события.

Риски: высокая интенсивность событий, failure-события без PID, потенциальные требования к elevation для kernel-провайдера; необходимы фильтрация по PID и ограничение потребления. **Требуется прототип** до включения в основное приложение.

Альтернативы (WFP callout, NDIS capture, WinDivert) либо требуют драйвера/админа, либо дают пакеты без PID. Минимально инвазивный вариант — ETW с фильтрацией по PID `dota2.exe`.

## 3. Классификация endpoints

Надёжных признаков «именно игровой relay» по одномуConnection нет. Dota 2 может использовать SDR; публичные списки IP серверов не отражают текущий матч. Поэтому классификатор должен быть **evidence-based** и выдавать:
- `SteamCoordinator` — TCP/UDP к известным Steam-сервисам, корреляция с процессом Steam, как слабый признак;
- `Voice` — UDP с малыми частыми пакетами, отличный connid/порт; слабый признак;
- `LikelyGameRelay` — UDP, PID dota2, долгоживущий endpoint, активность во время матча, несколько package-направлений;
- `Other` / `Unknown` — если признаков недостаточно.

Порты и IP-диапазоны **не могут быть единственным критерием**. В результате показывается `evidence level` и объяснение.

## 4. WARPSCOUT (проверено по исходникам v0.16.1)

- Репозиторий `github.com/vernette/warpscout`, **MIT**, Copyright (c) 2026 Nikita S.
- Один бинарник, без установки и без прав администратора; нужен `register` → `warpscout-account.json` (секрет).
- Команды: `register`, `scan`, `find-junk`, `find-sni`.
- Полезные флаги: `-p awg`, `-P` (TUN PING/LOSS), `-best` (одна строка `ip:port`), `-conf FILE` / `-conf -`,
  `-table-off` (добавляет `Table = off` в конфиг), `-o FILE`, `-no-report`, `-exclude-node`, `-country`, `-node`.
- Формат отчёта — **фиксированно-шириный текст** (`report.go`): `ENDPOINT`, `ENDPOINT PING`, `TUN PING`, `LOSS`,
  `SEEN AS`, `NODE`, `NODE LOCATION`. JSON-вывода таблицы нет; есть только `-conf-type mihomo-json`.
- Вывод: парсить текстовый report-файл или `-best`; версию бинарника фиксировать. Не выдавать сетевые тесты WARPSCOUT за тесты DotaChecker.
- `warpscout-account.json` и сгенерированные `.conf` содержат приватные ключи — **никогда не логировать**.

Локально `warpscout.exe` не найден — интеграция будет через внешний путь, заданный пользователем.

## 5. AmneziaWG (проверено локально)

- `C:\Program Files\AmneziaWG\amneziawg.exe` и `awg.exe` установлены; `AmneziaWGManager` запущен, туннельных сервисов нет.
- `awg show` без повышения → `Permission denied`: чтение состояния требует администратора.
- Официальный CLI: `amneziawg /installtunnelservice <conf>`, `/uninstalltunnelservice <name>`,
  сервисы `AmneziaWGTunnel$<name>`; `awg show <name>`; конфиги в `%ProgramFiles%\AmneziaWG\Data\Configurations`
  превращаются менеджером в `.conf.dpapi`.
- Стандартный WireGuard CLI не считается полностью совместимым; используем только официальный `amneziawg`/`awg`.
- DotaChecker **не реализует** криптографию/WireGuard и не копирует код клиента.

## 6. UDP/TCP probing

- Отправка UDP с TTL через обычный сокет (`setsockopt IP_TTL`) возможна без админа; **получение ICMP Time Exceeded/Unreachable** требует raw ICMP-сокета (`SOCK_RAW`, Admin) — подтверждено в предыдущих итерациях.
- `.NET Ping` не умеет UDP; TCP connect не является TCP traceroute.
- Вывод: UDP/TCP transport — отдельный модуль, **только после прототипа** (этап 2), не трогать ICMP-движок.

## 7. Проверка маршрута Direct vs WARP

- `awg show` подтверждает handshake/transfer, но не путь конкретного UDP-пакета Dota 2.
- ICMP, привязанный к интерфейсу, не доказывает, что UDP игры пойдёт туда же.
- Проверка: состояние туннеля + `GetBestRoute/GetBestInterfaceEx` для endpoint + (если реализуемо) привязка тестового UDP-сокета к адресу туннеля. При `-table-off` маршрутов не добавляется — без явного согласия пользователя туннель нельзя считать «проверенным маршрутом».
- Если путь подтвердить нельзя → verdict `Route comparison invalid` / повышенная неопределённость, а не «WARP лучше».

## 8. Что можно / что нельзя

**Напрямую:** IP Helper TCP/UDP + PID/имя/путь; история endpoints в JSON; evidence-классификатор; переиспользование ICMP-мониторинга; запуск WARPSCOUT как внешнего процесса и парсинг отчёта; чтение состояния AmneziaWG через официальный CLI под админом; логика Route Optimizer на измеренных метриках; UI в стиле MVVM.

**Требует прототипа:** ETW UDP-активность (remote endpoint + счётчики), UDP TTL probing с raw ICMP, фактическая проверка маршрута тестового трафика, парсинг реального WARPSCOUT-отчёта конкретной версии.

**Ненадёжно/невозможно без отдельного прототипа:** однозначное отличие relay от SDR/voice по одному соединению; гарантия совпадения ICMP/UDP-пути; безопасная маршрутизация только трафика Dota 2 без WFP/драйвера; работа raw sockets без админа; автономное переключение VPN без согласия.

## 9. Изменения архитектуры (предлагаемые)

Новые модули, без переписывания существующих:
- `Core.Models.GameEndpointRecord`, `GameEndpointCategory`, `EvidenceLevel`, `GameEndpointHistory`
- `Core.Diagnostics.GameEndpointClassifier` (чистый, тестируемый)
- `Core.Optimization.RouteOptimizer` + `RouteComparisonResult` (отдельно от RouteAnalyzer)
- `Network.Game.UdpActivityProbe` (ETW, отдельный модуль, с флагом доступности)
- `Network.Warp.WarpscoutRunner` + `WarpscoutReportParser`
- `Network.Warp.AmneziaWgController` (только чтение/подготовка; применение — Route Controller позже)
- `App.ViewModels`: `GameServersViewModel`, `RouteOptimizationViewModel`, `WarpProfilesViewModel`
- `App.Services.RouteControllerDesign` — только дизайн, без применения маршрутов

## 10. Риски

1. ETW может требовать elevation и влиять на производительность → prototype + optional module.
2. Фильтрация Dota 2 по PID надёжна, но correlate «match window» без надёжного игрового события невозможна.
3. Текстовый формат WARPSCOUT хрупок → фиксировать версию и версию парсера.
4. Утечка PrivateKey в логи/отчёты → запрет логирования конфигов, маскирование.
5. Изменение системных маршрутов/DNS → только с явного согласия, с rollback.
6. Сравнение в разных условиях (разное время/нагрузка) → фиксировать условия и marks invalid.
7. Отсутствие Dota 2/WARPSCOUT в текущем окружении → реальные тесты только на машине пользователя.
