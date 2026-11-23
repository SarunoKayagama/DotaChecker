# Контролируемый стенд для сценариев A/B/C

Статус: **план итерации 5, не реализован**.

## 1. Почему clumsy недостаточен

clumsy (WinDivert) на машине с мониторингом может отбрасывать ICMP-ответы **локально**. Это проверяет только устойчивость движка к таймаутам и корректность счётчиков PRL. Но он не моделирует потерю на **конкретном сегменте маршрута**: все TTL-пробы проходят через один и тот же сетевой стек Windows, и невозможно независимо «сломать» участок между роутером 2 и роутером 3.

Для проверки способности анализатора локализовать участок нужна топология с несколькими маршрутизаторами, где можно независимо управлять:

1. подавлением ICMP Time Exceeded конкретного роутера;
2. реальной потерей транзитных пакетов на конкретном сегменте;
3. подавлением ответов конечного узла.

## 2. Схема стенда

```
Windows host (Dota Checker)  192.168.56.1
        │  host-only adapter
        ▼
┌────────────────────────── VM (Ubuntu 24.04) ──────────────────────────┐
│  main netns = R1                                                        │
│    eth1 192.168.56.10        veth12 10.0.12.1                           │
│         │                          │                                   │
│         │                    veth21 10.0.12.2  [ns r2]                  │
│         │                    veth23 10.0.23.1                           │
│         │                          │                                   │
│         │                    veth32 10.0.23.2  [ns r3]                  │
│         │                    veth34 10.0.34.1                           │
│         │                          │                                   │
│         │                    veth43 10.0.34.2  [ns r4]                  │
│         │                    veth45 10.0.45.1                           │
│         │                          │                                   │
│         │                    veth54 10.0.45.2  [ns target]              │
└─────────────────────────────────────────────────────────────────────────┘
```

Hop'ы, которые видит Dota Checker:

| TTL | Ответчик | Адрес |
|---|---|---|
| 1 | R1 | 192.168.56.10 |
| 2 | R2 | 10.0.12.2 |
| 3 | R3 | 10.0.23.2 |
| 4 | R4 | 10.0.34.2 |
| 5 | target | 10.0.45.2 |

Это точно соответствует индексам исходных сценариев A/B/C из ТЗ.

## 3. Подготовка (на VM)

```bash
# Namespace'ы
ip netns add r2; ip netns add r3; ip netns add r4; ip netns add target

# Форвардинг и отключение rp_filter во всех ns и в main
for ns in r2 r3 r4 target; do ip netns exec $ns sysctl -w net.ipv4.ip_forward=1; done
sysctl -w net.ipv4.ip_forward=1
for ns in r2 r3 r4 target; do ip netns exec $ns sysctl -w net.ipv4.conf.all.rp_filter=0; done

# Создание veth-пар (пример для R2)
ip link add veth12 type veth peer name veth21
ip link set veth12 up
ip addr add 10.0.12.1/24 dev veth12
ip link set veth21 netns r2
ip netns exec r2 ip link set veth21 up
ip netns exec r2 ip addr add 10.0.12.2/24 dev veth21

ip link add veth23 type veth peer name veth32
ip link set veth23 netns r2
ip netns exec r2 ip link set veth23 up
ip netns exec r2 ip addr add 10.0.23.1/24 dev veth23
ip link set veth32 netns r3
ip netns exec r3 ip link set veth32 up
ip netns exec r3 ip addr add 10.0.23.2/24 dev veth32

# Аналогично veth34/veth43 и veth45/veth54
```

Маршруты: в каждом роутере маршрут к `10.0.45.0/24` через следующий hop и обратный маршрут к `192.168.56.0/24` через предыдущий. На target — default via `10.0.45.1`.

На Windows:

```powershell
route add 10.0.0.0 mask 255.0.0.0 192.168.56.10
```

Разрешить входящие ICMP Time Exceeded / Unreachable в брандмауэре Windows, иначе ответы роутеров не дойдут до приложения.

## 4. Независимые элементы управления

| Что включаем | Команда | Что ломает |
|---|---|---|
| Подавление ICMP TE на R2 (30%) | `ip netns exec r2 iptables -A OUTPUT -p icmp --icmp-type 11 -m statistic --mode random --probability 0.30 -j DROP` | Только ответы R2, не транзит |
| Реальная потеря на сегменте R2→R3 (20%) | `ip netns exec r2 tc qdisc add dev veth23 root netem loss 20%` | Только транзитные пакеты, идущие дальше R2 |
| Потеря ответов цели (15%) | `ip netns exec target iptables -A OUTPUT -p icmp --icmp-type 0 -m statistic --mode random --probability 0.15 -j DROP` | Echo Reply цели и e2e |
| Полная фильтрация цели | вероятность `1.00` в предыдущей команде | Все ответы цели |

Сброс между сценариями: `ip netns exec ... iptables -F`, `tc qdisc del dev ... root`.

## 5. Сценарии и ожидаемые результаты

| Сценарий | Воздействие | hop1 | hop2 | hop3 | hop4 | hop5 | e2e | Ожидаемый verdict |
|---|---|---|---|---|---|---|---|---|
| **Baseline** | нет | 0% | 0% | 0% | 0% | 0% | 0% | `InsufficientData` на малых n; при n≥50 — нет проблемы |
| **A** (транзит) | netem 20% на R2→R3 | 0 | 0 | ≈20 | ≈20 | ≈20 | ≈20 | `SuspectedSegment`, сегмент **2→3**; `Confirmed` только при флаге controlled experiment |
| **B** (ICMP rate-limit) | drop 30% type 11 на R2 | 0 | ≈30 | 0 | 0 | 0 | 0 | `IcmpResponseSuppression` на **hop2**, не транзит |
| **C** (ответ цели) | drop 15% type 0 на target | 0 | 0 | 0 | 0 | ≈15 | ≈15 | `Probable` + **DestinationSide**; не утверждать сегмент 4→5 |
| **G** (цель полностью фильтрует) | drop 100% type 0 | 0 | 0 | 0 | 0 | 100 | 100 | `Probable` + DestinationSide, без категоричного вывода |
| **I** (коррелированные потери) | netem loss 25% 25% на R2→R3 | 0 | 0 | ≈25 burst | ≈25 burst | ≈25 burst | ≈25 burst | `SuspectedSegment`/`ProbableRegion`, φ₁>0.2, block bootstrap, **не** `Confirmed` |
| **D** (смена маршрута) | заменить адрес R3, обновить маршруты | — | — | IP меняется | — | — | — | новая `RouteVersion` после 3 discovery; старая статистика заморожена, сессионная продолжается |
| **E** (ECMP) | два равнозначных nexthop между R2 и R3 | — | — | 2 IP | — | — | — | `EcmpVariation`, **без** новой версии |
| **F** (мало проб) | 10 проб на узел | — | — | — | — | — | — | `InsufficientData` вне зависимости от процентов |
| **H** (soak) | 30 мин, интервал 1 с | — | — | — | — | — | — | память ограничена: 600 точек/узел, 3 окна, 20 версий, 200 событий |

Пояснение к A: TE от R2 уходит через `veth21` и не попадает под netem на `veth23`, поэтому hop2 остаётся чистым — это критично для корректной локализации.

## 6. Процедура прогона

1. Поднять стенд, убедиться в baseline (все 5 hop отвечают, e2e отвечает).
2. Запустить Dota Checker с целью `10.0.45.2`, интервал 200 мс, окно анализа 10 с, `K=3`.
3. Для каждого сценария: сбросить статистику вручную → включить воздействие → собрать 100+ проб на узел (≥20 с) → зафиксировать verdict, локализацию, confidence и доказательства.
4. Сравнить с ожидаемой колонкой. Отклонение = баг анализатора или калибровки порогов.
5. Для D/E дополнительно проверить журнал изменений маршрута и номер версии.
6. Для H снять `GC.GetTotalMemory` до/после, ожидать стабильности в пределах ±10%.

## 7. Критерии готовности итерации 5

- A даёт `SuspectedSegment` и сегмент `2→3`; `Confirmed` — только с флагом controlled experiment.
- B даёт `IcmpResponseSuppression`, а не «потери на участке».
- C/G дают `Probable` + DestinationSide, а не сегмент 4→5.
- I показывает φ₁>0.2, block bootstrap и не даёт `Confirmed`.
- F даёт `InsufficientData` при любых наблюдаемых процентах.
- D создаёт новую версию только после стабилизации и не объединяет маршруты из-за общих IP.
- E не создаёт новую версию при ECMP-переключении.
- Все 23 текущих теста остаются зелёными; добавлены тесты анализатора на синтетических данных A/B/C/F.
