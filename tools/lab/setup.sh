#!/usr/bin/env bash
# Виртуальный стенд A–I: 4 маршрутизатора + target в network namespaces.
# Запускать root внутри Ubuntu VM. eth1 VM должна иметь адрес 192.168.56.10/24
# (host-only адаптер для Windows-хоста с Dota Checker).
set -euo pipefail

for ns in r2 r3 r4 target; do
  ip netns add "$ns" 2>/dev/null || true
  ip netns exec "$ns" sysctl -w net.ipv4.ip_forward=1 >/dev/null
  ip netns exec "$ns" sysctl -w net.ipv4.conf.all.rp_filter=0 >/dev/null
done
sysctl -w net.ipv4.ip_forward=1 >/dev/null
sysctl -w net.ipv4.conf.all.rp_filter=0 >/dev/null

add_veth() {
  local left=$1 right=$2 ns=$3 left_ip=$4 right_ip=$5
  ip link del "$left" 2>/dev/null || true
  ip link add "$left" type veth peer name "$right"
  ip link set "$left" up
  ip addr add "$left_ip/24" dev "$left" 2>/dev/null || true
  ip link set "$right" netns "$ns"
  ip netns exec "$ns" ip link set "$right" up
  ip netns exec "$ns" ip addr add "$right_ip/24" dev "$right"
}

# R1 (main netns) -> R2
add_veth veth12 veth21 r2 10.0.12.1 10.0.12.2
# R2 -> R3
ip link add veth23 type veth peer name veth32
ip link set veth23 netns r2
ip netns exec r2 ip link set veth23 up
ip netns exec r2 ip addr add 10.0.23.1/24 dev veth23
ip link set veth32 netns r3
ip netns exec r3 ip link set veth32 up
ip netns exec r3 ip addr add 10.0.23.2/24 dev veth32
# R3 -> R4
ip link add veth34 type veth peer name veth43
ip link set veth34 netns r3
ip netns exec r3 ip link set veth34 up
ip netns exec r3 ip addr add 10.0.34.1/24 dev veth34
ip link set veth43 netns r4
ip netns exec r4 ip link set veth43 up
ip netns exec r4 ip addr add 10.0.34.2/24 dev veth43
# R4 -> target
ip link add veth45 type veth peer name veth54
ip link set veth45 netns r4
ip netns exec r4 ip link set veth45 up
ip netns exec r4 ip addr add 10.0.45.1/24 dev veth45
ip link set veth54 netns target
ip netns exec target ip link set veth54 up
ip netns exec target ip addr add 10.0.45.2/24 dev veth54

# Маршруты в сторону цели и обратно
ip route replace 10.0.45.0/24 via 10.0.12.2
ip netns exec r2 ip route replace 10.0.45.0/24 via 10.0.23.2
ip netns exec r2 ip route replace 192.168.56.0/24 via 10.0.12.1
ip netns exec r3 ip route replace 10.0.45.0/24 via 10.0.34.2
ip netns exec r3 ip route replace 192.168.56.0/24 via 10.0.23.1
ip netns exec r4 ip route replace 192.168.56.0/24 via 10.0.34.1
ip netns exec target ip route replace default via 10.0.45.1

echo "Стенд готов. На Windows: route add 10.0.0.0 mask 255.0.0.0 192.168.56.10"
echo "Hop'ы: 1=192.168.56.10 2=10.0.12.2 3=10.0.23.2 4=10.0.34.2 5=10.0.45.2"
