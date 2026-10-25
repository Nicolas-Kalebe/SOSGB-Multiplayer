# CONTEXTO — `scripts/`

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../AGENTS.md).

Scripts de desenvolvimento para **testar o jogo sem intervenção humana**, usando a ponte de teste do plugin ([D20](../DECISOES.md)).

| Script | O que faz |
|---|---|
| `bridge.py` | Cliente da ponte: manda um comando e imprime a resposta JSON. `python scripts/bridge.py state` |
| `dev-cycle.sh` | Ciclo completo: fecha o jogo → compila → abre → ativa a janela → espera o save carregar. `scripts/dev-cycle.sh [--no-build]` |
| `second-instance.sh` | Abre uma **segunda instância** do jogo com a ponte em outra porta (padrão 47801). Para testar host + convidado no mesmo PC |
| `kick-window.ps1` | Dá uma ativação real à janela do jogo e devolve o foco para a janela anterior. Necessário porque, aberto por script, o Launcher trava até um alt-tab ([D21](../DECISOES.md)) |

## Pré-requisitos

No arquivo `<jogo>/BepInEx/config/com.nicolaskalebe.sosgb.multiplayer.cfg`:

```ini
[DevBridge]
Enabled = true
Port = 47800
AutoLoadSlot = 2      # slot de TESTE (o 3º na tela). Nunca use o save principal.
```

## Várias instâncias

Variáveis de ambiente que sobrescrevem o `.cfg` por instância:
- `SOSGB_BRIDGE_PORT`: porta da ponte. Use `python scripts/bridge.py --port 47801 state`.
- `SOSGB_AUTOLOAD_SLOT`: slot carregado automaticamente.

O `kick-window.ps1` aceita `-ProcessId`. Sem ele, ativa a instância mais recente. O `dev-cycle.sh` fecha **todas** as instâncias.

## Comandos da ponte

| Comando | Efeito |
|---|---|
| `ping` | Testa a conexão |
| `help` | Lista os comandos |
| `state` | Cena, dinheiro, data/hora, relógio rodando, foco, `timeScale`, mapa, evento, dormindo, posição |
| `pause` / `resume` | `DateManager.Pause()` / `Resume()` |
| `addmoney <n>` | `GameController.AddMoney(n)` |
| `load <0-2>` | Carrega um slot e entra na cena Game |
| `screenshot [nome]` | Salva `<jogo>/BepInEx/screenshots/<nome>.png` (esperar ~2 s antes de abrir) |
| `menu <UILoadKey>` | Abre um menu (100 = Menu, 101 = BagMenu, 122 = Calendar, 123 = Map) |
| `closemenu` | ⚠️ ainda não funciona (`KeyNotFoundException`); reinicie com `dev-cycle.sh` |
| `decodesave <caminho>` | Decodifica um `.sav` para `<jogo>/BepInEx/savedump/` (JSON) |
| `like get <npc>` / `like add <npc> <pts>` | Lê ou soma pontos de amizade |
| `ctx save/load <nome>` | Guarda ou carrega um contexto completo de jogador (6 partes, JSON) |
| `ctx json <nome> [parte]` | Mostra o JSON de uma parte |
| `bag [take <slot> <qtd>]` | Lista a mochila / tira itens |
| `flag get/set <id> [valor]` | Lê ou define uma flag de evento |
| `world capture/load/info` | Captura o mundo em memória (sem gravar no disco) / carrega o snapshot (slot de teste) |
| `log <texto>` | Escreve no log do BepInEx e no `sosgb-state.jsonl` (marcador de teste) |
| `quit` | Fecha o jogo |

O estado também é gravado a cada 2 s em `<jogo>/BepInEx/sosgb-state.jsonl`.

## Cuidados

- O jogo **fecha e abre sozinho** durante o ciclo. Avise o usuário antes.
- O `kick-window.ps1` rouba o foco por menos de 1 s e devolve. Ele mira a janela da classe `UnityWndClass`, porque o processo também tem a janela de console do BepInEx.
- Antes de testes que alteram o save (dinheiro, itens), faça um snapshot em `backups/saves/` (D17).
