#!/usr/bin/env bash
# 🤖 AUTODOCUMENTAÇÃO: ver scripts/CONTEXTO.md e DECISOES.md#d33.
# Deita pela CAMA DE VERDADE (teclas reais): o jogador precisa estar ao pé da cama da casa, de frente
# para ela (posição inicial do save de teste: 6.5, 0, -5). Use isto nos testes de dormir: o comando
# "sleep" da ponte chama ActionSleep fora do fluxo da cama e pode deixar o personagem preso (ForceCleanMove).
# Uso: scripts/bed.sh [porta=47800]
cd "$(dirname "$0")/.."
P=${1:-47800}; B="timeout 10 python scripts/bridge.py --port $P"
PID=$($B ping | python -c "import sys,json; print(json.load(sys.stdin)['pid'])")
K() { powershell -NoProfile -File scripts/press-keys.ps1 -ProcessId "$PID" -Keys "$1" -Milliseconds "${2:-120}" >/dev/null; }
K W 400; sleep 0.8
for k in ENTER SPACE ENTER SPACE; do K $k; sleep 1.5; $B state | tr -d '\n ' | grep -q '"clockUpdateEnabled":false' && break; done
K ENTER; sleep 1.5; K ENTER; sleep 4
$B net status | python -c "import sys,json; print('porta $P:', json.load(sys.stdin).get('day'))"
