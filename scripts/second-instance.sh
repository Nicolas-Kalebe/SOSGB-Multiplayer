#!/usr/bin/env bash
# 🤖 AUTODOCUMENTAÇÃO: ver scripts/CONTEXTO.md e ROTEIRO.md (tarefa 1.6).
# Abre uma instância EXTRA do jogo no mesmo PC, com ponte de teste em outra porta.
# Usada para testar o multiplayer (host + convidado) sem um segundo computador.
# Uso: scripts/second-instance.sh [porta=47801] [slot=2] [netAutoStart=off|host|join]
# Variáveis repassadas ao jogo: SOSGB_NET_LAG_MIN, SOSGB_NET_LAG_MAX, SOSGB_NET_LOSS (simular rede ruim)
#   e SOSGB_JOIN_ADDRESS (ex.: IP da LAN em vez de 127.0.0.1)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME_PATH="${GAME_PATH:-/l/Games/STORY OF SEASONS Grand Bazaar}"
PORT="${1:-47801}"
SLOT="${2:-2}"
NET="${3:-off}"

# O instance.py abre, espera a cena Game e reabre sozinho se a instância travar (tela inicial, queda).
python "$ROOT/scripts/instance.py" launch --port "$PORT" --slot "$SLOT" --net "$NET" && {
  echo "[second-instance] save carregado na porta $PORT"; exit 0; }
echo "[second-instance] AVISO: não chegou na cena Game"
exit 1
