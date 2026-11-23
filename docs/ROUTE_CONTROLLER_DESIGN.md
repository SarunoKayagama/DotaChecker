# Route Controller — архитектура (проект, не реализация)

Route Controller **не реализован** и не будет автоматически изменять состояние системы.

## Разделение ответственности

- Route Optimizer выбирает лучший вариант по измерениям.
- Route Controller в будущем применяет выбранный вариант только после явного подтверждения пользователя.

## Контракты (`Core.RouteControl`)

`IRouteController`, `RouteControlRequest`, `RouteControlPrecondition`, `RouteControlPlan`, `RouteControlSnapshot`, `RouteControlResult`.

## План безопасной операции

1. `CheckPreconditions` — наличие AmneziaWG, elevated, пользовательское подтверждение, состояние туннеля.
2. `BuildPlan` — шаги, rollback-шаги, флаги `RequiresElevation` / `RequiresUserConfirmation`.
3. Явное подтверждение пользователя.
4. Выполнение только после подтверждения (в будущей версии).
5. Проверка результата через `RouteInterfaceResolver` и состояние туннеля.
6. При ошибке — rollback к сохранённому `RouteControlSnapshot`.

## Запреты

Не изменять маршруты, DNS, default gateway, интерфейсы, MTU и firewall без подтверждения. Не устанавливать/удалять туннели автоматически. Не обходить UAC. Не оставлять систему в изменённом состоянии после ошибки.

## Тесты

Только mock-тесты контрактов (`RouteControllerDesignTests`): план требует подтверждения и elevation, предусловия блокируют без подтверждения. Реальных сетевых операций нет.
