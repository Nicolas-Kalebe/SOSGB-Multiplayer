#!/usr/bin/env bash
# 🤖 AUTODOCUMENTAÇÃO: ver scripts/CONTEXTO.md.
# Abre host (ponte 47800, hospedando) + convidado (ponte 47801, entrando em 127.0.0.1) e espera o
# convidado terminar de carregar o mundo do host. Recompila o plugin antes (via dev-cycle.sh).
# Variáveis: GUEST_NAME (padrão Convidada), GUEST_GUID (padrão convidadoteste) e as de simulação
# de rede (SOSGB_NET_LAG_MIN/MAX, SOSGB_NET_LOSS) que valem para as duas instâncias.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
NET=host timeout 280 scripts/dev-cycle.sh > /tmp/sosgb-cycle.log 2>&1 || { echo "[two-instances] host falhou:"; grep -E "ERRO| error " /tmp/sosgb-cycle.log; exit 1; }
SOSGB_PLAYER_NAME="${GUEST_NAME:-Convidada}" SOSGB_PLAYER_GUID="${GUEST_GUID:-convidadoteste}" \
  timeout 250 scripts/second-instance.sh 47801 2 join > /tmp/sosgb-second.log 2>&1 || { echo "[two-instances] convidado falhou"; exit 1; }
for _ in $(seq 1 40); do
  python scripts/bridge.py --port 47801 net status 2>/dev/null | grep -q '"firstWorldDone": true' && break
  sleep 2
done
sleep 3
echo "[two-instances] host (47800) e convidado (47801) prontos"
