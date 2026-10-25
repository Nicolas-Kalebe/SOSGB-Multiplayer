#!/usr/bin/env python3
# 🤖 AUTODOCUMENTAÇÃO: ver scripts/CONTEXTO.md e DECISOES.md#d20.
"""Cliente da ponte de teste (DevBridge) do plugin SOSGB Multiplayer.

Uso:
    python scripts/bridge.py state
    python scripts/bridge.py load 2
    python scripts/bridge.py --wait 120 ping      # espera o jogo abrir a ponte
    python scripts/bridge.py --port 47801 state   # outra instância do jogo
"""
import argparse
import json
import socket
import sys
import time


def send(port: int, command: str, timeout: float = 70.0) -> dict:
    with socket.create_connection(("127.0.0.1", port), timeout=timeout) as s:
        s.sendall((command + "\n").encode("utf-8"))
        buf = b""
        while not buf.endswith(b"\n"):
            chunk = s.recv(65536)
            if not chunk:
                break
            buf += chunk
    return json.loads(buf.decode("utf-8"))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--port", type=int, default=47800)
    ap.add_argument("--wait", type=float, default=0, help="segundos esperando a ponte aceitar conexão")
    ap.add_argument("command", nargs="+")
    a = ap.parse_args()
    command = " ".join(a.command)

    deadline = time.time() + a.wait
    while True:
        try:
            result = send(a.port, command)
            break
        except (ConnectionRefusedError, socket.timeout, OSError) as e:
            if time.time() >= deadline:
                print(json.dumps({"ok": False, "error": f"ponte indisponível: {e}"}, ensure_ascii=False))
                return 2
            time.sleep(1)

    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result.get("ok") else 1


if __name__ == "__main__":
    sys.exit(main())
