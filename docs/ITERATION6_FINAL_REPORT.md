# Iteration 6 — Final report

## 1. Что реализовано

- **Stage 1A–1E** (из предыдущих шагов): ETW-прототип `UdpActivityProbe`, модели/классификатор/история endpoint, вкладка Game Servers.
- **Stage 2**: `GameEndpointMonitoringService` + `EndpointMonitoringHistory` — один активный ICMP-мониторинг выбранного endpoint через существующий `ContinuousProbeEngine`; защита от дублей, остановка при исчезновении endpoint, сохранение сводок, отображение статуса в Game Servers.
- **Stage 3**: `WarpscoutRunner`/`WarpscoutService`/`WarpscoutReportParser`, version-aware разбор fixed-width отчёта, асинхронный запуск с отменой/таймаутом и параллельным чтением stdout/stderr, вкладка WARP Profiles.
- **Stage 4**: `AmneziaWgConfigParser` (только публичные поля), `AmneziaWgController` (поиск профилей, `awg show`, состояние службы, план операций без выполнения), вкладка WARP Profiles.
- **Stage 5**: `RouteOptimizer`/`RouteComparisonService` и вкладка Route Optimization. Сравнение Direct/WARP только для одного endpoint, одного протокола, с подтверждёнными и разными интерфейсами; приоритет доставка → jitter → RTT.
- **Stage 6**: контракты и документация Route Controller, mock-тесты; реальных изменений маршрутов нет.
- **Stage 7**: вкладки WARP Profiles, Route Optimization, настройки путей; все долгие операции асинхронные.
- **Stage 8**: 95 unit-тестов, Debug/Release сборки, smoke-проверка приложения.
- **Stage 9**: документы `WARPSCOUT_INTEGRATION.md`, `AMNEZIAWG_INTEGRATION.md`, `ROUTE_OPTIMIZER.md`, `ROUTE_CONTROLLER_DESIGN.md`, этот отчёт.

## 2. Изменения архитектуры

Новые слои: `Core.Warp`, `Core.Optimization`, `Core.RouteControl`, `Network.Warp`, `Network.Routing`, `Network.Monitoring` (расширение).
Существующие `RouteAnalyzer`, `ContinuousProbeEngine`, `StatisticsEngine`, `WindowAggregator`, `GameEndpointClassifier`, `GameEndpointHistory`, `GameEndpointDiscoveryService`, `UdpActivityProbe`, ICMP-трассировка и Connections не переписывались. В `ContinuousProbeEngine` добавлены только методы чтения `Statistics()`/`E2eStatistics()`.

## 3. Новые файлы

`Core/Game/EndpointMonitoringSummary.cs`, `EndpointMonitoringHistory.cs`; `Core/Warp/WarpscoutModels.cs`, `WarpscoutReportParser.cs`, `AmneziaWgProfileInfo.cs`, `AmneziaWgConfigParser.cs`; `Core/Optimization/RouteOptimizationModels.cs`, `RouteOptimizer.cs`, `RouteComparisonService.cs`; `Core/RouteControl/RouteControlContracts.cs`; `Network/Warp/WarpscoutRunner.cs`, `WarpscoutService.cs`, `AmneziaWgController.cs`; `Network/Routing/RouteInterfaceResolver.cs`; `Network/Monitoring/GameEndpointMonitoringService.cs`, `IGameEndpointMonitoringService.cs`; App ViewModels/Views для WARP Profiles и Route Optimization; 5 новых тестовых файлов; 5 новых документов.

## 4. Изменённые файлы

`AppSettings.cs` (новые пути/таймауты), `App.xaml.cs`, `MainWindow.xaml`, `MainViewModel.cs`, `GameServersViewModel.cs`, `GameEndpointRowViewModel.cs`, `GameServersView.xaml`, `ContinuousProbeEngine.cs` (accessors), `DotaChecker.Network.csproj` (ServiceController package), README, ARCHITECTURE (частично), GAME_ENDPOINT_MODEL, THIRD-PARTY-NOTICES.

## 5. Тесты

95/95 unit-тестов (Category != EtwIntegration). Новые тесты: WARPSCOUT parser/config/runner errors, AmneziaWG parser/secret hiding/controller, RouteOptimizer (разные endpoint, интерфейсы, выборка, значимость, приоритет метрик), EndpointMonitoringHistory, RouteController mock contracts, GameServersViewModel.

## 6. Сборка

- `dotnet build --configuration Debug` — 0 ошибок, 0 предупреждений.
- `dotnet build --configuration Release` — 0 ошибок, 0 предупреждений.

## 7. Проверено на реальной системе

- ETW Dota 2 PID 24568 — пользовательские elevated-прогоны, зафиксированы в `docs/ETW_PROTOTYPE_REPORT.md` (2339 событий, 7 endpoints, 0 malformed/dropped, payloadValidated=true).
- AmneziaWG установлен локально; `awg show` без elevation → PermissionDenied (подтверждено тестом).
- Windows routing API доступен через `GetBestInterface` (реальный вызов в Route Optimization UI при загрузке Direct-измерения).
- Приложение запускается и корректно закрывается дважды подряд; JSON-истории создаются.

## 8. Проверено только mock/synthetic

- WARPSCOUT parser и runner (реальный WARPSCOUT не запускался: бинарник не задан/не установлен).
- Route Optimizer (реальный WARP-туннель не активировался).
- Route Controller (только контракты).
- UI ViewModel'ы (fake service/launcher).

## 9. Требует участия пользователя

1. Указать путь к `warpscout.exe` и выполнить `register`, затем Scan из вкладки WARP Profiles.
2. Указать каталог `.conf` AmneziaWG и вручную активировать нужный профиль в клиенте AmneziaWG.
3. Выполнить реальное измерение WARP до того же игрового endpoint и ввести его данные во вкладке Route Optimization.
4. Во время реального матча запустить ETW discovery и endpoint monitoring во вкладке Game Servers.

## 10. Известные ограничения

- WARPSCOUT — предварительный отбор; ping до WARP endpoint/1.1.1.1 не оценивает Dota 2.
- Handshake AmneziaWG не доказывает путь трафика; `GetBestInterface` — таблица маршрутизации, не packet-level proof.
- ICMP-мониторинг endpoint не измеряет игровой UDP.
- `LikelyGameRelay` — гипотеза; `High` классификатором не ставится.
- Реальный сравнение Direct/WARP не выполнялось: нет WARP-подключения.
- UDP/TCP traceroute не реализован (raw ICMP требует администратора; отдельный gate).

## 11. Безопасность

Секреты AmneziaWG и WARPSCOUT не логируются и не сохраняются. Системные маршруты, DNS, gateway, firewall и туннели не изменяются. Route Controller — только дизайн. Нет автоматического UAC, скачивания бинарников и destructive operations.

## 12. Known issues

- Документы ARCHITECTURE/ITERATION6_AUDIT частично устарели (описаны более ранние состояния); фактическое состояние — этот отчёт и исходники.
- WARP-измерение вводится вручную; это честно, но не автоматизировано по безопасности.
- IPv6 в ETW-парсере и Route Interface Resolver поддерживается частично.

## 13. Действия пользователя (точный перечень)

См. раздел 9. DotaChecker не выполнит их автоматически.
