# WARPSCOUT integration

WARPSCOUT остаётся **внешним процессом**. Go-код не портировался, бинарник не скачивается автоматически.

## Модели и компоненты

- `Core.Warp.WarpscoutConfiguration` — путь к бинарнику, протокол (`awg`), `-P`, `-best`, фильтры, таймаут, отчёт.
- `Core.Warp.WarpscoutEndpoint` — Endpoint, Endpoint Ping, Tunnel Ping, LOSS, SEEN AS, NODE, NODE LOCATION.
- `Core.Warp.WarpscoutResult`, `WarpscoutExecutionStatus` — статус, версия, ошибки, длительность, журнал.
- `Core.Warp.WarpscoutReportParser` — version-aware разбор fixed-width отчёта по строке заголовка.
- `Network.Warp.WarpscoutRunner` — асинхронный запуск, cancellation, timeout, параллельное чтение stdout/stderr.
- `Network.Warp.WarpscoutService` — хранение последнего результата, событие `ResultPublished`.

## Поддерживаемые аргументы (проверено по v0.16.1)

`scan`, `-p awg`, `-P`, `-best`, `-exclude-node`, `-country`, `-o FILE`, `-no-report`, `version`.

При сканировании DotaChecker **сразу сохраняет оба файла** одним запуском: отчёт в `%AppData%\Dota Checker\warpscout\` и готовый `.conf` в каталоге AmneziaWG из настроек (или `%AppData%\Dota Checker\WarpProfiles\`). Список профилей обновляется автоматически — повторно сканировать для получения конфига не нужно.

В аргументы добавлены `-conf`, `-table-off`, `-no-dns`: конфиг не меняет маршруты и DNS. DotaChecker не применяет конфиг и не активирует туннель — это делает пользователь в клиенте AmneziaWG.

## Безопасность

- Путь задаётся пользователем в Settings/WARP Profiles; жёстко заданного пути нет.
- Перед запуском проверяется существование файла.
- При отсутствии бинарника остальные функции приложения работают.
- `warpscout-account.json` и сгенерированные `.conf` содержат секреты — не логируются и не сохраняются в историю.
- Отмена и таймаут завершают дочерний процесс (`Kill(entireProcessTree: true)`).

## Ограничения

WARPSCOUT — предварительный отбор кандидатов. Минимальный ping или `NODE` не доказывают качество маршрута для Dota 2. Отчёт fixed-width и хрупок: при неизвестной версии парсер добавляет предупреждение, при отсутствии заголовка — `ParseFailed`.

Результат сканирования не является измерением игрового UDP-трафика.
