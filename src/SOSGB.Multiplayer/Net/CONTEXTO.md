# CONTEXTO — `src/SOSGB.Multiplayer/Net/`

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../../../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../../../AGENTS.md).

Camada de rede do mod (Fase 2 do [`ROTEIRO.md`](../../../ROTEIRO.md)). Protocolo documentado em [`docs/PROTOCOLO.md`](../../../docs/PROTOCOLO.md).

| Arquivo | O que faz |
|---|---|
| `Protocol.cs` | Constantes (versão, porta, chave, máximo de jogadores) e o enum `MsgType` |
| `NetSession.cs` | Sessão LiteNetLib: hospedar/entrar, aperto de mão, lista de jogadores, repasse de mensagens de jogo (host-autoritativo) |
| `PlayerSync.cs` | Envia o estado do jogador local (10×/s) e mantém os avatares dos remotos (clone visual do modelo, visível só no mesmo mapa) |
| `WorldSync.cs` | 3.1: convidado pede o mundo, host captura (`WorldSnapshot`) e envia, convidado recarrega com ele |
| `NetRunner.cs` | `MonoBehaviour` que chama `NetSession.Poll()` a cada frame (tudo na thread principal) |

## Regras

- **Tudo na thread principal.** Nada de threads próprias mexendo no jogo. O LiteNetLib só entrega eventos dentro do `Poll()`.
- Mensagem nova: adicione ao `MsgType`, documente no `PROTOCOLO.md` e, se quebrar compatibilidade, incremente `Protocol.Version`.
- Sistemas de jogo (sincronização de jogador, relógio, etc.) assinam `NetSession.OnGameMessage` e enviam com `NetSession.SendGame(...)`. Não acessam o LiteNetLib direto.
- Testar com duas instâncias: `scripts/dev-cycle.sh` + `scripts/second-instance.sh`, depois `net host` na porta 47800 e `net join 127.0.0.1` na 47801.
- **Simular rede ruim:** variáveis `SOSGB_NET_LAG_MIN/MAX` e `SOSGB_NET_LOSS` (ver `NetSession.ApplyNetworkSimulation`). ⚠️ O simulador embutido do LiteNetLib (`SimulateLatency`/`SimulatePacketLoss`) **não funciona** no pacote do NuGet (só em build de debug). Por isso a simulação é nossa: fila de recepção atrasada, perda só em pacotes não confiáveis.
- **Firewall:** quem hospeda precisa de uma regra de entrada UDP liberando o `SOSGrandBazaar.exe` no **perfil da rede em uso** (Público/Privado). Incluir no guia de instalação (tarefa 8.5).
