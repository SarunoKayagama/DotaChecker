#!/usr/bin/env bash
# Независимые воздействия сценариев A–I. Запускать root после setup.sh.
set -euo pipefail

reset_all() {
  for ns in r2 r3 r4 target; do
    ip netns exec "$ns" iptables -F 2>/dev/null || true
  done
  for dev in veth23 veth34 veth45; do
    for ns in r2 r3 r4; do
      ip netns exec "$ns" tc qdisc del dev "$dev" root 2>/dev/null || true
    done
  done
}

case "${1:-}" in
  baseline)
    reset_all
    echo "Baseline: воздействий нет"
    ;;
  A)
    # Реальная потеря транзитных пакетов на сегменте R2 -> R3.
    # TE от R2 уходит через veth21 и НЕ попадает под netem на veth23.
    reset_all
    ip netns exec r2 tc qdisc add dev veth23 root netem loss 20%
    echo "A: netem loss 20% на R2->R3 (ожидается Confirmed, сегмент 2->3 в режиме эксперимента)"
    ;;
  B)
    # R2 подавляет генерацию ICMP Time Exceeded, транзит не трогается.
    reset_all
    ip netns exec r2 iptables -A OUTPUT -p icmp --icmp-type 11 \
      -m statistic --mode random --probability 0.30 -j DROP
    echo "B: подавление TE 30% на R2 (ожидается IcmpResponseSuppression)"
    ;;
  C)
    # Цель выборочно не отвечает на Echo Reply.
    reset_all
    ip netns exec target iptables -A OUTPUT -p icmp --icmp-type 0 \
      -m statistic --mode random --probability 0.15 -j DROP
    echo "C: потеря ответов цели 15% (ожидается Probable, DestinationSide)"
    ;;
  G)
    reset_all
    ip netns exec target iptables -A OUTPUT -p icmp --icmp-type 0 -j DROP
    echo "G: цель полностью фильтрует ответы (ожидается Probable, без категоричного вывода)"
    ;;
  D)
    reset_all
    echo "D: замените адрес veth32 с 10.0.23.2 на 10.0.23.9 и обновьте маршруты:"
    echo "  ip netns exec r3 ip addr del 10.0.23.2/24 dev veth32"
    echo "  ip netns exec r3 ip addr add 10.0.23.9/24 dev veth32"
    echo "  ip netns exec r2 ip route replace 10.0.45.0/24 via 10.0.23.9"
    echo "Ожидается: новая версия после стабилизации, задержка обнаружения измеряется."
    ;;
  E)
    reset_all
    echo "E: добавьте второй nexthop между R2 и R3 равной стоимости —"
    echo "ожидается EcmpVariation без новой версии статистики."
    ;;
  I)
    reset_all
    echo "I: коррелированные потери создаются tc netem с burst-параметрами:"
    echo "  ip netns exec r2 tc qdisc add dev veth23 root netem loss 25% 25%"
    echo "Ожидается: φ₁ > 0.2, block bootstrap, не Confirmed."
    ;;
  reset)
    reset_all
    echo "Все воздействия сняты"
    ;;
  *)
    echo "Использование: $0 {baseline|A|B|C|D|E|G|I|reset}"
    exit 1
    ;;
esac
