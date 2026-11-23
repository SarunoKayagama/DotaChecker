# Route Optimizer

Отдельный компонент, **не часть RouteAnalyzer**.

## Модели

`RouteCandidate`, `RouteMeasurement`, `RouteValidationResult`, `RouteQualityScore`, `RouteComparison`, `RouteComparisonService`, `RouteOptimizer`.

## Методика

1. Сравнение только одного IP/port и одного протокола.
2. Оба измерения должны иметь подтверждённый интерфейс (`IsInterfaceVerified`) и **разные** интерфейсы.
3. Число проб ≥20, окна измерений сопоставимы (≤10 мин, длительность в пределах ×2).
4. Приоритет метрик: отсутствие ответов → jitter → median RTT.
5. Решение по разнице долей отсутствия ICMP-ответов использует Wilson CI и зазор 3%; это **не** доказательство потери игрового UDP.
6. `Overall` — ранжирующая мера, а не вероятность.

## Статусы

`NotTested`, `Testing`, `Better`, `Worse`, `NoSignificantDifference`, `InsufficientData`, `RouteComparisonInvalid`, `Error`.

`RouteComparisonInvalid` выставляется при разных endpoint/протоколе, неподтверждённом или одинаковом интерфейсе, малом числе проб.

## Проверка фактического пути

`RouteInterfaceResolver` использует `GetBestInterface` — это данные таблицы маршрутизации, **не packet-level proof**. Handshake AmneziaWG и ping до `1.1.1.1` не подтверждают путь игрового трафика. WARP-измерение вводится пользователем из реального теста; DotaChecker туннели не переключает.

## Ограничения

ICMP-измерение не описывает игровой UDP. Разные игровые сессии не считаются строго эквивалентными. При изменении endpoint предыдущее сравнение признаётся недействительным.
