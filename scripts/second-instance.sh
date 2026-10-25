#!/usr/bin/env bash
# 🤖 AUTODOCUMENTAÇÃO: ver scripts/CONTEXTO.md e ROTEIRO.md (tarefa 1.6).
# Abre uma instância EXTRA do jogo no mesmo PC, com ponte de teste em outra porta.
# Usada para testar o multiplayer (host + convidado) sem um segundo computador.
# Uso: scripts/second-instance.sh [porta=47801] [slot=2]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME_PATH="${GAME_PATH:-/l/Games/STORY OF SEASONS Grand Bazaar}"
PORT="${1:-47801}"
SLOT="${2:-2}"

(cd "$GAME_PATH" && SOSGB_BRIDGE_PORT="$PORT" SOSGB_AUTOLOAD_SLOT="$SLOT" ./SOSGrandBazaar.exe >/dev/null 2>&1 &)
python "$ROOT/scripts/bridge.py" --port "$PORT" --wait 120 ping >/dev/null
echo "[second-instance] ponte respondendo na porta $PORT"
sleep 3
powershell -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$ROOT/scripts/kick-window.ps1")"
for _ in $(seq 1 60); do
  if python "$ROOT/scripts/bridge.py" --port "$PORT" state 2>/dev/null | grep -q '"scene": "Game"'; then
    echo "[second-instance] save carregado na porta $PORT"
    exit 0
  fi
  sleep 2
done
echo "[second-instance] AVISO: não chegou na cena Game"
exit 1
