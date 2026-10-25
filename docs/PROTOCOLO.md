# PROTOCOLO DE REDE

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../AGENTS.md).

Fonte da verdade no código: [`src/SOSGB.Multiplayer/Net/Protocol.cs`](../src/SOSGB.Multiplayer/Net/Protocol.cs). **Ao mudar o protocolo, atualize os dois e incremente `Protocol.Version`.**

## Transporte

- **LiteNetLib 2.1.4** sobre UDP ([D7](../DECISOES.md)). Porta padrão do host: **47900** (`[Net] Port`).
- Chave de conexão: `"SOSGB-MP"` (outros programas são recusados).
- Até **4 jogadores** ([D6](../DECISOES.md)). IDs: **0 = host**, 1–3 = convidados, distribuídos pelo host.
- **Host-autoritativo** ([D2](../DECISOES.md)): convidados só falam com o host. O host repassa as mensagens de jogo aos demais.
- Serialização: `NetDataWriter`/`NetDataReader` do LiteNetLib. `string` = UTF-8 com prefixo de tamanho; `float`/`int`/`uint` em little-endian.

## Formato

Todo pacote: `[byte MsgType][corpo]`.

## Mensagens

### Controle de sessão (1–9)

| # | Nome | Direção | Corpo | Entrega |
|---|---|---|---|---|
| 1 | `Hello` | convidado → host | `int protocolo, string versãoDoMod, string nome` | Reliable |
| 2 | `Welcome` | host → convidado | `byte seuId, byte n, n × (byte id, string nome)` | Reliable |
| 3 | `Reject` | host → convidado | `string motivo` (o host desconecta em seguida) | Reliable |
| 4 | `PlayerJoined` | host → outros | `byte id, string nome` | Reliable |
| 5 | `PlayerLeft` | host → todos | `byte id` | Reliable |

**Aperto de mão:**
1. O convidado conecta (com a chave) e manda `Hello`.
2. O host confere a versão do protocolo.
   - Se for diferente, responde `Reject` e desconecta.
   - Se for igual, atribui um ID livre, responde `Welcome` para quem entrou e `PlayerJoined` para os outros.

### Jogo (≥ 10)

Mensagens ≥ 10 enviadas por um convidado chegam ao host, que as entrega localmente e **repassa** para os outros convidados. As do host vão para todos.

| # | Nome | Corpo | Entrega | Fase |
|---|---|---|---|---|
| 10 | `PlayerState` | `byte id, uint fieldId, float x, y, z, float rotY, byte anim` | Unreliable/sequenced | 2.4 |

## Histórico

| Versão | Mudança |
|---|---|
| 1 | Sessão (Hello/Welcome/Reject/Joined/Left) + `PlayerState` |
