# ETW prototype — Final validation preparation (real Dota 2 match)

Статус: инструмент подготовлен и собран. **Реальный матчевый прогон не запускался ассистентом** — его выполняет пользователь.

---

## 1. Точная команда запуска

Из PowerShell **от имени администратора**, в корне репозитория:

```powershell
cd 'C:\Users\moisey29\Downloads\Dota Checker'
Get-Process dota2 | Select-Object Id, ProcessName, StartTime
dotnet run --project tools/EtwProbe -c Release -- `
  --duration 300 `
  --pid <PID_DOTA2> `
  --raw-pid <PID_DOTA2> `
  --out artifacts/etw-match.json
```

- `--pid` → фильтр Phase B (`UdpActivityProbe`).
- `--raw-pid` → фильтр Phase A (сырой ETW-поток).
- Оба флага должны получить **один и тот же PID**, взятый непосредственно перед тестом.
- `--out` → путь JSON-отчёта; каталог создаётся автоматически.
- Без elevate инструмент выводит инструкцию и завершается с кодом 3 (проверено).

## 2. Что проверено перед подготовкой

- `dotnet build --configuration Debug` — **0 ошибок, 0 предупреждений**.
- `dotnet build tools/EtwProbe -c Release` — **0 ошибок, 0 предупреждений**.
- `dotnet test --filter Category!=EtwIntegration` — **44/44**.
- Проверен не-elevated запуск с `--pid 24568 --raw-pid 24568`: в логе оба фильтра отображаются согласованно:
  ```
  Duration=5s PhaseB_PidFilter=24568 PhaseA_PidFilter=24568
  ```
- `EtwProbe.csproj` добавлен в решение.
- Phase A/BFilters применяются **до** подсчёта filtered-счётчиков; глобальные счётчики вынесены в `All*` и не смешиваются.
- Добавлен `DroppedEvents` из `TraceEventSource.EventsLost`.

## 3. Обработка полей `size` и `dsize`

Подтверждённые пользователем поля payload: `context, saddr, sport, size, daddr, dport, dsize`.

- Парсер теперь извлекает **оба** значения:
  - `SizeBytes` ← `size`;
  - `DataSizeBytes` ← `dsize`.
- Агрегатор хранит `BytesObserved` (сумма `size`) и `DataBytesObserved` (сумма `dsize`) отдельно.
- В консоли и JSON выводится строка:
  ```
  BytesObserved(size field)=... DataBytesObserved(dsize field)=...
  [semantics of `size`/`dsize` not confirmed by this tool]
  ```
  и поле `sizeSemanticsConfirmed = false`.
- **Не делать вывода**, что `size` — это размер UDP payload, до отдельного подтверждения семантики.

## 4. Endpoint extraction (по коду)

- Send: `local = saddr:sport`, `remote = daddr:dport`.
- Receive: `local = daddr:dport`, `remote = saddr:sport`.
- Ключ агрегата: `PID | AddressFamily | RemoteAddress | RemotePort | LocalAddress:LocalPort`.
- PID входит в ключ; Send/Receive одной пары дают одинаковый ключ (при корректной роли адресов в payload).
- IPv4 (uint/IPAddress/string/bytes) и IPv6 (bytes) поддерживаются парсером; реальная проверка — только в матче.

## 5. Что ожидается в отчёте пользователя

- `Phase A` с `--raw-pid`: event types, Send/Receive, total уже только для PID Dota 2.
- `Phase B` diagnostics:
  ```
  PIDFilter=<pid> Send=... Receive=... UniqueRemoteEndpoints=... EventsPerSec=...
  PayloadValidated=... Malformed=... Dropped=...
  Global(no PID filter): Send=... Receive=... TotalByType=...
  BytesObserved(size field)=... DataBytesObserved(dsize field)=...
  ```
- `artifacts/etw-match.json` со всеми счётчиками и обезличенными сэмплами.
- Список уникальных endpoints (IP можно маскировать) — нейтрально называть `Observed UDP Endpoint`, **не** SDR relay.

## 6. Подтверждено реальными более ранними прогонами

- Elevated ETW запускается; provider `Microsoft-Windows-Kernel-Network`.
- `UdpIp/Send`, `UdpIp/Recv`, version 2, event ID 65535.
- События с `headerPID=24568 (dota2.exe)`.
- Устойчивая UDP-пара `local:65479 → remote:27024`, направления согласованы.

## 7. Подтверждено синтетически

- 44 unit-теста: IPv4/IPv6, Send/Recv roles, PID (payload + header fallback), ports, event ID 65535, unknown event type, дедупликация, first/last seen, event count, `size`/`dsize`, malformed, capacity.
- PID-фильтрация Phase A/B по коду и не-elevated запуску.

## 8. Не подтверждено реальным матчем

- Отфильтрованные Phase B счётчики после исправления.
- Semantics `size`/`dsize`.
- Реальные `BytesObserved`/`DataBytesObserved`.
- Отсутствие дублей одного endpoint на реальных Send/Receive.
- IPv6 на реальном трафике Dota 2.
- Events/sec, CPU, Working Set, DroppedEvents за 300 с.
- Поведение в главном меню / поиске матча / матче.
- Принадлежность endpoint именно игровому relay (классификатор не реализован).

## 9. Ограничения и запреты

Не реализовывались и не изменялись: GameEndpointClassifier, GameEndpointHistory, Game Servers UI, WARPSCOUT, AmneziaWG, Route Optimizer, Route Controller, UDP/TCP traceroute, RouteAnalyzer, ContinuousProbeEngine, StatisticsEngine.

## 10. Дальнейший шаг

Дождаться результатов пользовательского прогона во время матча, сопоставить с разделом 5, при необходимости исправить **только ETW-прототип** и добавить регрессионные тесты. После этого — отдельное подтверждение перед этапами 1B–1E.

---

## 11. Разбор артефакта `artifacts/etw-short-test.json`

PID 24568, длительность 30 с, elevated=true, `payloadValidated=true`, 2339 filtered-событий,
1078 Send / 1261 Receive, 7 endpoints, malformed=0, dropped=0.

### 11.1 `raw.total=690` против `probe.events=2339`

**Root cause — разные окна сбора, разные сессии, не разная семантика счётчиков.**

- Phase A (`InspectRawProviderAsync`) создаёт отдельную ETW-сессию
  `DotaChecker-EtwProbe-Raw` и собирает `Math.Min(10, duration)` секунд — здесь **10 с**.
- Phase B (`UdpActivityProbe`) создаёт другую сессию `DotaChecker-UdpActivityProbe`
  и собирает **30 с** (весь `--duration`).
- Обе сессии имеют `--raw-pid=24568`, поэтому filtered-счётчики в обеих фазах относятся к одному PID.
- Проверка: `690/10 = 69/s`, `2339/30 ≈ 78/s` — сопоставимые скорости, разница объясняется разными
  промежутками времени и нагрузкой в разные моменты матча.

Счётчики **не были подогнаны**. В JSON добавлены `raw.windowSeconds` и `raw.eventsPerSecond`,
в консоль — явная строка `Window=...s ... Rate=.../s`, чтобы устранить двусмысленность.

### 11.2 `id=65535` против `id=Illegal`

**Root cause — представление одного и того же значения.**

- `TraceEvent.ID` имеет тип `TraceEventID`. Для manifest-событий оно равно `65535`
  (`TraceEventID.Illegal` — sentinel).
- Phase A печатает `(int)data.ID` → `65535`.
- Phase B в сэмплах печатал `data.ID` через интерполяцию → `Illegal`.
- В `eventTypeCounts` и в `TryParse` обе фазы уже использовали `(int)data.ID`, то есть **65535**;
  направление определяется по имени (`UdpIp/Send`, `UdpIp/Recv`), поэтому разбора это не затрагивало.

Исправление: сэмплы Phase B теперь тоже печатают числовой ID. Добавлены тесты на `65535`.

### 11.3 `dsize = 0`

Подтверждено реальными событиями: `dsize` равен 0 во всех записанных сэмплах. Semantics не предполагается:
`sizeSemanticsConfirmed` остаётся `false`. `size` и `dsize` хранятся и выводятся раздельно
(`BytesObserved` / `DataBytesObserved`). Значение `size` (85…1261) **не объявляется** размером UDP payload.

### 11.4 Агрегация 7 endpoints

Ключ агрегата: `PID | AddressFamily | RemoteAddress | RemotePort | LocalAddress:LocalPort`.
Для Send: local=`saddr:sport`, remote=`daddr:dport`; для Receive роли меняются. В реальных сэмплах
это подтверждается: Send `65479 → 27024`, Recv `27024 → 65479` (после нормализации — тот же ключ).
PID входит в ключ. В агрегатор добавлены per-endpoint `SendCount`/`ReceiveCount`; в JSON добавлен
массив endpoints с замаскированными адресами, счётчиками и first/last seen — для прямой проверки
в следующем прогоне. Unit-тест проверяет слияние Send+Receive одной пары в одну запись.

### 11.5 `eventsPerSecond`

Использует **filtered** `TotalEvents` (`2339`) и реальное время `probe.Elapsed` от `Start` до `Stop`
(немного больше 30 с из-за накладных задержек), отсюда `77.55`, а не `2339/30=77.97`.
Глобальные счётчики (`allSend=1399`, `allReceive=1592`) в расчёт не входят. В JSON добавлен
`elapsedSeconds` для явной проверки.

### 11.6 Нагрузка

`cpuPercent=0.065%`, `workingSetBytes=46.5 MB`, `workingSetDeltaBytes=0`, dropped=0 — аномалий нет.

### 11.7 Исправления и тесты

- Числовой ID в сэмплах Phase B.
- `raw.windowSeconds` / `raw.eventsPerSecond`, `probe.elapsedSeconds`.
- Per-endpoint `SendCount`/`ReceiveCount` и замаскированный список endpoints в JSON.
- Регрессионные тесты: 65535, unknown event type, dsize, слияние Send/Receive, direction counts.
- `dotnet build --configuration Debug` — 0 ошибок, 0 предупреждений.
- `dotnet build tools/EtwProbe -c Release` — 0 ошибок, 0 предупреждений.
- `dotnet test --filter Category!=EtwIntegration` — **45/45**.

### 11.8 Остающиеся неопределённости

- Нельзя утверждать, что наблюдаемый endpoint является именно игровым SDR relay. Использовать нейтральное
  `Observed UDP Endpoint`.
- Semantics `size`/`dsize` не подтверждена независимо.
- Массив из 7 endpoints в старом артефакте отсутствовал — проверка "по списку" станет возможна только
  в следующем прогоне с обновлённой сборкой.
- IPv6 на реальном трафике Dota 2 не проверен.
