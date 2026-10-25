#!/usr/bin/env bash
# 🤖 AUTODOCUMENTAÇÃO: ver scripts/CONTEXTO.md e DECISOES.md#d20.
# Ciclo de desenvolvimento: fecha o jogo → compila o plugin → abre o jogo → espera o save carregar.
# Requer DevBridge.Enabled = true e AutoLoadSlot configurado no .cfg do plugin.
#
# Uso: scripts/dev-cycle.sh [--no-build]
#      GAME_PATH="X:/jogo" scripts/dev-cycle.sh
#      NET=host scripts/dev-cycle.sh   (já abre hospedando; ver tarefa 2.6)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME_PATH="${GAME_PATH:-/l/Games/STORY OF SEASONS Grand Bazaar}"
BRIDGE="python $ROOT/scripts/bridge.py"

# 1. Fecha o jogo (pela ponte; se não responder, mata o processo)
if tasklist | grep -qi SOSGrandBazaar; then
  $BRIDGE quit >/dev/null 2>&1 || true
  for _ in $(seq 1 30); do tasklist | grep -qi SOSGrandBazaar || break; sleep 1; done
  tasklist | grep -qi SOSGrandBazaar && taskkill //IM SOSGrandBazaar.exe //F >/dev/null
fi
echo "[dev-cycle] jogo fechado"

# 2. Compila (o build copia a DLL para BepInEx/plugins)
if [[ "${1:-}" != "--no-build" ]]; then
  if ! out=$(dotnet build "$ROOT/src/SOSGB.Multiplayer" -nologo -v q -p:GamePath="$(cygpath -w "$GAME_PATH")" 2>&1); then
    echo "$out" | grep -E " error " | sort -u
    echo "[dev-cycle] ERRO de compilação; jogo não foi aberto"
    exit 1
  fi
  echo "[dev-cycle] compilado"
fi

# 3. Abre o jogo e espera a ponte
(cd "$GAME_PATH" && SOSGB_DEVBRIDGE=1 SOSGB_AUTOLOAD_SLOT="${SOSGB_AUTOLOAD_SLOT:-2}" SOSGB_NET_AUTOSTART="${NET:-off}" ./SOSGrandBazaar.exe >/dev/null 2>&1 &)
$BRIDGE --wait 120 ping >/dev/null
echo "[dev-cycle] ponte respondendo"

# 3b. O plugin ativa a própria janela se ela nascer inativa (StartupGuard, D25); o kick externo
#     (kick-window.ps1) não é mais necessário e fica só como reserva manual.

# 4. Espera entrar na cena Game com relógio (save carregado)
for _ in $(seq 1 60); do
  # Só conta como carregado na cena Game (no Launcher o DateManager já existe, com data zerada).
  if $BRIDGE state 2>/dev/null | grep -q '"scene": "Game"'; then
    echo "[dev-cycle] save carregado:"
    $BRIDGE state
    exit 0
  fi
  sleep 2
done
echo "[dev-cycle] AVISO: o save não carregou em 120 s (AutoLoadSlot configurado?)"
exit 1
