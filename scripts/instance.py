#!/usr/bin/env python3
# 🤖 AUTODOCUMENTAÇÃO: ver scripts/CONTEXTO.md.
"""Abre e vigia instâncias do jogo para testes, reabrindo sozinho quando travam.

Problemas que ele cobre (vistos nos testes):
  - a janela fica presa na tela preta inicial (cena "Launcher") mesmo com o StartupGuard (D25);
  - o processo morre (queda) ou a ponte de teste para de responder (jogo congelado).

Uso:
    python scripts/instance.py launch --port 47801 --slot 2 --net join [--env NOME=valor ...]
        abre, espera a ponte e a cena Game; se travar, mata e tenta de novo (até --tries vezes)
    python scripts/instance.py health --port 47801
        imprime: ok <cena> | sem-ponte | morto   (código de saída 0 só quando ok)
    python scripts/instance.py kill --port 47801
"""
import argparse
import json
import os
import socket
import subprocess
import sys
import time

GAME = os.environ.get("GAME_PATH_WIN", r"L:\Games\STORY OF SEASONS Grand Bazaar")
EXE = os.path.join(GAME, "SOSGrandBazaar.exe")
PID_DIR = os.path.join(os.environ.get("TEMP", "."), "sosgb-instances")


def send(port, command, timeout=8.0):
    with socket.create_connection(("127.0.0.1", port), timeout=timeout) as s:
        s.settimeout(timeout)
        s.sendall((command + "\n").encode("utf-8"))
        buf = b""
        while not buf.endswith(b"\n"):
            chunk = s.recv(65536)
            if not chunk:
                break
            buf += chunk
    return json.loads(buf.decode("utf-8"))


def pid_file(port):
    os.makedirs(PID_DIR, exist_ok=True)
    return os.path.join(PID_DIR, f"{port}.pid")


def saved_pid(port):
    try:
        return int(open(pid_file(port)).read().strip())
    except (OSError, ValueError):
        return None


def alive(pid):
    if not pid:
        return False
    out = subprocess.run(["tasklist", "/FI", f"PID eq {pid}", "/NH"], capture_output=True, text=True).stdout
    return "SOSGrandBazaar" in out


def kill(pid):
    if pid:
        subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True)
        time.sleep(3)


def scene(port):
    try:
        return send(port, "state")["state"]["scene"]
    except Exception:
        return None


def health(port):
    pid = saved_pid(port)
    try:
        st = send(port, "state", timeout=8.0)["state"]
        return "ok", st["scene"]
    except Exception:
        pass
    if pid and not alive(pid):
        return "morto", None
    # Uma segunda chance: o jogo pode estar carregando (a ponte só responde entre quadros).
    time.sleep(5)
    try:
        st = send(port, "state", timeout=8.0)["state"]
        return "ok", st["scene"]
    except Exception:
        return ("sem-ponte" if alive(pid) else "morto"), None


def launch(port, slot, net, extra_env, tries, ping_timeout, launcher_timeout, game_timeout):
    for attempt in range(1, tries + 1):
        old = saved_pid(port)
        if old and alive(old):
            kill(old)
        env = dict(os.environ)
        env.update({
            "SOSGB_DEVBRIDGE": "1",
            "SOSGB_AUTOLOAD_SLOT": str(slot),
            "SOSGB_NET_AUTOSTART": net,
        })
        # Na porta padrão (47800) não passamos a porta: a instância fica "padrão" (sem sufixo no nome).
        if port != 47800:
            env["SOSGB_BRIDGE_PORT"] = str(port)
        env.update(extra_env)
        proc = subprocess.Popen([EXE], cwd=GAME, env=env,
                                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                creationflags=getattr(subprocess, "DETACHED_PROCESS", 0))
        open(pid_file(port), "w").write(str(proc.pid))
        print(f"[instance {port}] tentativa {attempt}: pid {proc.pid}", flush=True)

        # 1) ponte
        t0 = time.time()
        while time.time() - t0 < ping_timeout:
            if not alive(proc.pid):
                break
            try:
                send(port, "ping", timeout=3)
                break
            except Exception:
                time.sleep(2)
        else:
            print(f"[instance {port}] ponte não abriu em {ping_timeout:.0f}s; reabrindo", flush=True)
            kill(proc.pid)
            continue
        if not alive(proc.pid):
            print(f"[instance {port}] o processo morreu na abertura; reabrindo", flush=True)
            continue

        # 2) sair do Launcher e chegar na cena Game
        t1 = time.time()
        ok = False
        while time.time() - t1 < game_timeout:
            sc = scene(port)
            if sc == "Game":
                ok = True
                break
            if not alive(proc.pid):
                break
            if sc == "Launcher" and time.time() - t1 > launcher_timeout:
                print(f"[instance {port}] presa na tela inicial (Launcher) por {launcher_timeout:.0f}s; reabrindo", flush=True)
                break
            time.sleep(2)
        if ok:
            print(f"[instance {port}] pronta na cena Game (pid {proc.pid})", flush=True)
            return 0
        kill(proc.pid)
    print(f"[instance {port}] FALHOU depois de {tries} tentativas", flush=True)
    return 1


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("action", choices=["launch", "health", "kill"])
    ap.add_argument("--port", type=int, default=47800)
    ap.add_argument("--slot", type=int, default=2)
    ap.add_argument("--net", default="off")
    ap.add_argument("--env", action="append", default=[], help="NOME=valor repassado ao jogo")
    ap.add_argument("--tries", type=int, default=3)
    ap.add_argument("--ping-timeout", type=float, default=120)
    ap.add_argument("--launcher-timeout", type=float, default=70)
    ap.add_argument("--game-timeout", type=float, default=150)
    a = ap.parse_args()

    if a.action == "health":
        state, sc = health(a.port)
        print(state if sc is None else f"{state} {sc}")
        return 0 if state == "ok" else 1
    if a.action == "kill":
        kill(saved_pid(a.port))
        return 0
    extra = dict(e.split("=", 1) for e in a.env)
    # Variáveis SOSGB_* do ambiente de quem chamou também valem (ex.: SOSGB_PLAYER_NAME).
    return launch(a.port, a.slot, a.net, extra, a.tries, a.ping_timeout, a.launcher_timeout, a.game_timeout)


if __name__ == "__main__":
    sys.exit(main())
