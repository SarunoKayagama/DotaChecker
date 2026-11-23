# Stage 1B–1D — Game endpoint models, classifier, history

Статус: Stage 1A–1E реализованы. Следующий этап (WARPSCOUT/Route Optimizer) не начинался.

## 1. Аудит перед реализацией

- `UdpActivityAggregate` уже содержит PID, remote/local endpoint, first/last seen, Send/Receive,
  `size`/`dsize` (семантика не подтверждена). ETW-прототип не знает о классификации — это сохранено.
- `ConnectionInfo` и `ProcessResolver` из IP Helper дают TCP remote endpoint и PID/имя/путь, но не UDP remote.
- Для подключения источника добавлен интерфейс `IGameEndpointSource`; адаптер
  `UdpActivityGameEndpointSource` только преобразует агрегаты ETW в факты, не классифицирует и не пишет файлы.
- RouteAnalyzer, ContinuousProbeEngine, StatisticsEngine не изменялись.

## 2. Модели (`src/DotaChecker.Core/Game`)

- `GameEndpointCategory`: `Unknown`, `LikelyGameRelay`, `SteamCoordinator`, `Voice`, `Other`.
  Категории `ConfirmedGameServer` намеренно нет — SDR может скрывать реальный сервер.
- `EvidenceLevel`: `Low`, `Medium`, `High` (High ≠ 100% уверенность).
- `GameEndpointObservation`: факт наблюдения (PID/процесс, local/remote, протокол, источник ETW/TCP/Manual,
  first/last, Send/Receive, ObservationCount, ProcessStartTimes, ConnectionId, суммы `size`/`dsize`).
- `GameEndpointRecord`: агрегат фактов + отдельная `GameEndpointClassification`, флаги ручного include/exclude.
- `GameEndpointClassification`: категория, evidence, список причин, время, версия классификатора.
- `GameEndpointHistoryEntry`: персистентная пара «факты + классификация».
- `GameEndpointKeys.Build`: ключ `protocol | PID | AddressFamily | RemoteAddress | RemotePort | LocalAddress:LocalPort`.
  PID входит в ключ; Send/Receive нормализуются к одному ключу.

## 3. Классификатор `GameEndpointClassifier`

Чистый, детерминированный, без WinAPI/ETW/файлов/UI. Версия `1.0`.

Принципы:
1. Не любой UDP endpoint Dota 2 считается relay.
2. Номер порта сам по себе не доказательство.
3. Учитываются PID/имя процесса, повторяемость, двусторонность, длительность, число игровых сессий
   (различные ProcessStartTime) и контекст других endpoint того же PID.
4. `SteamCoordinator`/`Voice` не присваиваются без достаточных признаков — по умолчанию `Unknown`.
5. Недостаточно данных → `Unknown` + `Low`.
6. Причины делятся на `Fact:`, `Heuristic:`, `Hypothesis:`.
7. `size`/`dsize` не используются как подтверждённые размеры пакетов.

Пороги:
- двусторонний + ≥2 наблюдений + активность ≥5 с → `LikelyGameRelay`, не выше `Medium`;
- `High` классификатором **не присваивается**: повторяемость наблюдений и перезапуски не являются доказательством роли relay (версия классификатора 1.1);
- >3 UDP endpoint одного PID и доля endpoint <50% → `Unknown`/`Low`;
- односторонний трафик, короткое наблюдение, TCP-only, неизвестный процесс → `Unknown`/`Low`.

## 4. История `GameEndpointHistory`

JSON (`%AppData%\Dota Checker\game-endpoints.json` в приложении; путь задаётся в конструкторе):

- атомарная запись через `.tmp` + `File.Move(overwrite: true)`;
- повреждённый JSON → исходный файл копируется в `*.corrupt.<ticks>` и остаётся на месте, история начинается пустой;
- лимит записей (200 по умолчанию), удаление устаревших (30 дней по умолчанию);
- повторное наблюдение объединяется: min firstSeen, max lastSeen, max счётчиков, рост ObservationCount,
  уникальные ProcessStartTime → SessionCount;
- ручные `Exclude`/`Include` сохраняются;
- факты и классификация хранятся отдельно: `UpsertObservation` не стирает классификацию,
  `Reclassify(new GameEndpointClassifier())` пересчитывает категории;
- не сохраняются payload, ключи и секреты.

## 5. Тесты

64/64 unit-теста (без реального ETW). Новое:

- классификатор: 10 сценариев (короткий UDP, стабильный двусторонний, много endpoint одного PID,
  односторонний, смена remote port, перезапуск игры, insufficient data, неизвестный процесс,
  TCP-only, manual exclude);
- история: дедупликация, first/last, сохранение/загрузка, повреждённый JSON, атомарность,
  лимит, stale, manual include/exclude, reclassify без потери фактов, отсутствие секретов.

`dotnet build --configuration Debug` — 0 ошибок, 0 предупреждений.

## 6. Ограничения

- Реальный игровой relay по-прежнему не доказан: `LikelyGameRelay` — гипотеза.
- `size`/`dsize` semantics не подтверждена; в историю попадают только суммы полей.
- Источник `IGameEndpointSource` пока не подключён к UI; адаптер ETW создаёт факты по запросу.
- TCP endpoint не считается игровым UDP relay.
- Следующий шаг — Stage 1E (UI) — не начинался.

## 7. Stage 1E — Game Servers UI

- Вкладка **Game Servers**: таблица endpoint (Remote IP/port, Local endpoint, PID, Process, Protocol,
  Category, Evidence, First/Last Seen, Send/Receive, Manual state, Reasons), сортировка по заголовкам,
  фильтр по категории, поиск по IP/порту/процессу, контекстное меню (Copy IP, Copy IP:Port,
  Continuous Ping, Include/Exclude).
- Endpoint Details: полные факты, последние времена обнаружений, история классификаций, причины.
- `GameEndpointDiscoveryService`: один `UdpActivityProbe`, опрос раз в 2 с, поиск PID `dota2`,
  upsert в историю, reclassify, save, событие `Updated`; корректная остановка при выходе из приложения.
- `LikelyGameRelay` помечается как **гипотеза**, `High` — как «не гарантия»; отдельная подпись,
  что ICMP ping не измеряет потери игрового UDP-трафика и что суммы size/dsize не являются сетевым объёмом.
- Continuous Ping использует существующий `IDiagnosticsLauncher.ContinuousPingAsync` →
  `ContinuousProbeEngine.StartWithRouteAsync`; новый движок не создавался.
- UI/ViewModel тесты: 6 сценариев (дедупликация, Start/Stop, фильтры, Include/Exclude, Continuous Ping, Details).
- Проверено: повторный запуск приложения дважды, корректное закрытие, создание `game-endpoints.json`.

`dotnet test --filter Category!=EtwIntegration` — **71/71**.

