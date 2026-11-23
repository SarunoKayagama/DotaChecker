# UDP / TCP probing — статус исследования

**Не интегрировано в приложение.** ICMP-движок не изменялся.

## UDP (изолированный прототип `tools/UdpProbe`)

- Отправка UDP с TTL: `Socket.SetSocketOption(IP, IpTimeToLive, ttl)` — работает без администратора.
- Получение ICMP Time Exceeded / Destination Unreachable: требуется raw ICMP-сокет
  (`Socket(InterNetwork, Raw, Icmp)`). В текущей не-elevated сессии **создание сокета удалось**,
  но ни одного ICMP-пакета получено не было (localhost и `8.8.8.8`, TTL=1).
  Причины: права/брандмауэр Windows; требуется отдельный elevated-прогон.
- ICMP-ошибка цитирует IP-заголовок + первые 8 байт исходного датаграмма. Для UDP это
  **сам UDP-заголовок**, а не полезная нагрузка. Поэтому сопоставление пробы возможно только
  по паре портов; для параллельных проб нужен уникальный source port на каждую пробу.
- Парсер `Network.Udp.UdpIcmpErrorParser` покрыт unit-тестами на синтетических packet (Type 11/0,
  Type 3/3, повреждённые данные). Это **не** подтверждение реальной работы ETW/raw-приёма.

Команда прототипа:

```powershell
dotnet run --project tools/UdpProbe -c Debug -- <target_ip> <port> <ttl> <source_port> <timeout_ms>
```

## TCP

- Обычный `TcpClient.Connect()` — это проверка доступности порта, **не TCP traceroute**.
- Modern Windows запрещает произвольную отправку TCP SYN с TTL через raw socket; надёжного
  administrative-free способа нет. Прототип не создавался, в приложение ничего не добавлено.

## Итог

UDP-прототип demonstrates только отправку с TTL и разбор синтетических ICMP-ошибок.
Полноценный UDP traceroute требует подтверждённого elevated-приёма ICMP и уникальных портов;
до этого не внедрять. TCP traceroute на текущей ОС нереализуем без рискованных обходных механизмов.
