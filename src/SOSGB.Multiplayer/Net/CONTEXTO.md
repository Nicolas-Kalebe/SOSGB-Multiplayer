# CONTEXTO — `src/SOSGB.Multiplayer/Net/`

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../../../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../../../AGENTS.md).

Camada de rede do mod (Fase 2 do [`ROTEIRO.md`](../../../ROTEIRO.md)). Protocolo documentado em [`docs/PROTOCOLO.md`](../../../docs/PROTOCOLO.md).

| Arquivo | O que faz |
|---|---|
| `Protocol.cs` | Constantes (versão, porta, chave, máximo de jogadores) e o enum `MsgType` |
| `NetSession.cs` | Sessão LiteNetLib: hospedar/entrar, aperto de mão, lista de jogadores, repasse de mensagens de jogo (host-autoritativo) |
| `PlayerSync.cs` | Envia o estado do jogador local (10×/s: posição, 12 camadas e parâmetros do Animator) e mantém os avatares (clone visual do modelo com o override controller real; recriados quando o mundo recarrega; visíveis só no mesmo mapa) |
| `WorldSync.cs` | 3.1: convidado pede o mundo, host captura (`WorldSnapshot`) e envia, convidado recarrega com ele |
| `ClockSync.cs` | 3.2/3.3: relógio do host, pausa do grupo (D9) via patch em `DateManager.IsUpdateEnabled`; quem dorme não pausa o grupo e o relógio segue para os acordados (D33) |
| `DaySync.cs` | 3.4/3.5: dormir individual (segura `StartNextDay`), contador "dormindo X/Y", virada do dia pelo host e reenvio do mundo |
| `PlayerProfiles.cs` | 3.6/D24: perfil individual do convidado (envio, guarda no host, arquivo, reaplicação após cada carga de mundo) |
| `RomanceSync.cs` | 6.4/D44: romance exclusivo (registro NPC → jogador no host; patches em `CheckMarryable`/`SetFiance`) |
| `SharedSync.cs` | 5.4/D42/D43: estado compartilhado genérico (lista de partes com capturar/aplicar, em rodízio): moinho, apicultura, cogumelos, vasos, ampliações, pets, cavalo, prêmios, adega, banho, receitas, missões, peixes, reforços, lojas, Bazaar, festivais, informações gerais, histórico de itens e flags de evento (união) |
| `AnimalSync.cs` | 5.3/D41: animais da fazenda (cuidados) e celeiros (ovos, ração), mesclando sem mexer na posição |
| `FarmSync.cs` | Fase 5/D40: fazenda ao vivo nos mapas residentes (mapa 1); diferenças a cada 0,5 s viram operações por objeto, aplicadas por todos no cache do mapa |
| `FieldState.cs` | Fase 5 (alicerce, D40): estado dos objetos de um mapa (captura pelo serializador oficial, aplicar só as diferenças no mapa atual ou num mapa residente, instalar no save de mapas) |
| `LetterSync.cs` | 4.6/D39: correio compartilhado; estado do `LetterManager` sincronizado (montado à mão a partir do JSON) |
| `StorageSync.cs` | 4.2/D37/D46: armazéns compartilhados (casa, deck do Bazaar, peças da barraca); trava de uma pessoa por vez (host) e sincronização pelo conteúdo |
| `MoneySync.cs` | 4.1/D36: carteira compartilhada; cada jogo observa `PlayerMoney`, convidado manda a diferença, host manda o total |
| `FreshProfile.cs` | D30: perfil "do zero" de quem entra pela 1ª vez (zera corações/conversas, esvazia mochila e regador, ferramentas no nível básico, visual do save próprio) |
| `NotifySync.cs` | D26: detecta os próprios acontecimentos (dormiu, desmaiou, evento) e mostra os avisos de todos |
| `Locations.cs` | Nome traduzido de um mapa (tabela `FieldNameText` + `FieldMaster.ConvertToFieldId` para IDs sazonais) |
| `AppearanceSync.cs` | D28/D32: anuncia a aparência local, monta avatares com a aparência dos outros (troca temporária, com validação de peças, tempo limite real e espera do modelo reaparecer) e aplica a própria no convidado |
| `NetRunner.cs` | `MonoBehaviour` que chama `NetSession.Poll()` a cada frame (tudo na thread principal) |

## Saves e recuperação (D23)

- **Save oficial:** só na virada do dia, pelo host. O jogo grava o save automático no **slot 0**. Fora da virada, `WorldSnapshot.SlotSavePatch` recusa (`Result.TargetLocked`) e mostra um aviso.
- **Convidado:** nunca grava.
- **Perfis dos convidados:** `%APPDATA%\STORY OF SEASONS Grand Bazaar\SOSGB-MP\profiles_<fazenda>_<host>.json`.
- **Recuperação contra queda do host:** `recovery_<fazenda>_<host>.sav` na mesma pasta, a cada `[Net] RecoverySnapshotMinutes` (15). É um `.sav` válido. **Para restaurar (manual):** faça backup do slot desejado e copie o `recovery_*.sav` por cima de `SaveData<N>.sav`. O cabeçalho `SaveSlotSystem<N>.sav` pode mostrar data/preview antigos. Validar na Fase 8 (tarefa 8.5).

## Regras

- **Tudo na thread principal.** Nada de threads próprias mexendo no jogo. O LiteNetLib só entrega eventos dentro do `Poll()`.
- Mensagem nova: adicione ao `MsgType`, documente no `PROTOCOLO.md` e, se quebrar compatibilidade, incremente `Protocol.Version`.
- Sistemas de jogo (sincronização de jogador, relógio, etc.) assinam `NetSession.OnGameMessage` e enviam com `NetSession.SendGame(...)`. Não acessam o LiteNetLib direto.
- Testar com duas instâncias: `scripts/dev-cycle.sh` + `scripts/second-instance.sh`, depois `net host` na porta 47800 e `net join 127.0.0.1` na 47801.
- **Simular rede ruim:** variáveis `SOSGB_NET_LAG_MIN/MAX` e `SOSGB_NET_LOSS` (ver `NetSession.ApplyNetworkSimulation`). ⚠️ O simulador embutido do LiteNetLib (`SimulateLatency`/`SimulatePacketLoss`) **não funciona** no pacote do NuGet (só em build de debug). Por isso a simulação é nossa: fila de recepção atrasada, perda só em pacotes não confiáveis.
- **Firewall:** quem hospeda precisa de uma regra de entrada UDP liberando o `SOSGrandBazaar.exe` no **perfil da rede em uso** (Público/Privado). Incluir no guia de instalação (tarefa 8.5).
