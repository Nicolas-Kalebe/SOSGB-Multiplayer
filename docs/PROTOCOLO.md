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
| 1 | `Hello` | convidado → host | `int protocolo, string versãoDoMod, string nome, string guid` | Reliable |
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
| 12 | `SleepStatus` | `byte dormindo, byte total`: contador "dormindo X/Y" (host → todos) | Reliable | 3.4 |
| 11 | `ClockState` | `long ticks, bool rodando`: relógio do host e pausa efetiva do grupo, 2×/s (host → todos) | Sequenced | 3.2/3.3 |
| 10 | `PlayerState` | `byte id, uint fieldId, float x, y, z, float rotY, int animStateHash, float animNormTime` — 10×/s. A animação é replicada pelo `fullPathHash` do estado do Animator (`Animator.Play(hash)` no avatar) | Sequenced | 2.4 |

### Ponto a ponto com o host (≥ 20, **não** são repassadas)

| # | Nome | Direção | Corpo | Entrega | Fase |
|---|---|---|---|---|---|
| 20 | `WorldRequest` | convidado → host | (vazio). Enviada automaticamente após o `Welcome` | Reliable | 3.1 |
| 23 | `SleepReady` | convidado → host | `bool dormindo`: o convidado deitou (a virada dele foi segurada) | Reliable | 3.4 |
| 24 | `ProfileUpload` | convidado → host | `string json`: perfil individual (ao dormir, a cada 60 s, ao sair) | Reliable | 3.6 |
| 25 | `ProfileData` | host → convidado | `string json` (vazio = host não conhece): perfil guardado, logo após o `WorldData` da entrada | Reliable | 3.6 |
| 22 | `PauseReport` | convidado → host | `bool pausado`: o convidado entrou ou saiu de menu/evento (na mudança e a cada 1 s) | Reliable | 3.3 |
| 21 | `WorldData` | host → convidado | `int tamanho, byte[] .sav`: snapshot do mundo (~73 KB, fragmentado pelo LiteNetLib) | Reliable | 3.1 |

Regra de repasse: mensagens de jogo de **10 a 19** são difundidas pelo host; **20 em diante** ficam só entre o convidado e o host (`Protocol.FirstDirectMsg`).

## Histórico

| Versão | Mudança |
|---|---|
| 1 | Sessão (Hello/Welcome/Reject/Joined/Left) + `PlayerState` |
| 5 | GUID no `Hello`; `ProfileUpload`/`ProfileData` (perfil individual do convidado) |
| 4 | `SleepStatus`/`SleepReady` (dormir individual); após a virada o host reenvia `WorldData` a todos |
| 3 | `ClockState` e `PauseReport` (relógio do host e pausa D9)|
| 2 | `WorldRequest`/`WorldData` (convidado recebe o mundo do host) e regra de repasse 10–19 × 20+ |
