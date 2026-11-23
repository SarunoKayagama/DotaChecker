# AmneziaWG integration

## Что реализовано

- `Core.Warp.AmneziaWgProfileInfo` — только публичная информация о `.conf`.
- `Core.Warp.AmneziaWgConfigParser` — разбор `[Interface]`/`[Peer]`; значения `PrivateKey`/`PresharedKey` **не сохраняются**.
- `Network.Warp.AmneziaWgController`:
  - поиск `.conf` в указанном каталоге;
  - чтение публичных полей (Endpoint, адреса, AllowedIPs, MTU, наличие Amnezia-параметров);
  - `awg show <name>` — только чтение состояния (требует администратора);
  - состояние службы `AmneziaWGTunnel$<name>` через `ServiceController`;
  - `BuildActivationPlan` — описание будущей операции **без выполнения**.

## Что НЕ реализовано

- Установка/удаление туннелей (`/installtunnelservice`, `/uninstalltunnelservice`) не выполняются.
- Изменение маршрутов, DNS, default gateway, MTU, firewall, адаптеров — не выполняется.
- Автоматическое переключение туннеля отсутствует.
- Стандартный WireGuard CLI не считается совместимым с AmneziaWG.

## Безопасность

Без elevation `awg show` возвращает `PermissionDenied`; приложение продолжает работать.
Секреты не попадают в UI, логи, отчёты, исключения и тестовые fixture. ПланOperations содержит только текстовые шаги и флаги `RequiresUserConfirmation` / `RequiresElevation`.

## Требования к пользователю

Установленный AmneziaWG Windows Client, путь к каталогу `.conf` в настройках, администратор — только для чтения `awg show`. Активация туннеля выполняется пользователем вручную.
