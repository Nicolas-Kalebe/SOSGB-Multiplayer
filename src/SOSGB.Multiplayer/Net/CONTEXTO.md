# CONTEXTO — `src/SOSGB.Multiplayer/Net/`

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../../../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../../../AGENTS.md).

Camada de rede do mod (Fase 2 do [`ROTEIRO.md`](../../../ROTEIRO.md)). Protocolo documentado em [`docs/PROTOCOLO.md`](../../../docs/PROTOCOLO.md).

| Arquivo | O que faz |
|---|---|
| `Protocol.cs` | Constantes (versão, porta, chave, máximo de jogadores) e o enum `MsgType` |
| `NetSession.cs` | Sessão LiteNetLib: hospedar/entrar, aperto de mão, lista de jogadores, repasse de mensagens de jogo (host-autoritativo) |
| `PlayerSync.cs` | Envia o estado do jogador local (10×/s: posição, 12 camadas e parâmetros do Animator) e mantém os avatares (clone visual do modelo com o override controller real; recriados quando o mundo recarrega; visíveis só no mesmo mapa) |
| `WorldSync.cs` | 3.1: convidado pede o mundo, host captura (`WorldSnapshot`) e envia, convidado recarrega com ele |
| `ClockSync.cs` | Relógio do grupo (D50): só o menu de pausa pausa; quem está em outra tela é adiantado ao sair; o tempo segue o mais adiantado (`ClockJump`); quem dorme não pausa (D33) |
| `DaySync.cs` | 3.4/3.5: dormir individual (segura `StartNextDay`; o desmaio passa pelo mesmo caminho), contador "dormindo X/Y", virada do dia pelo host e reenvio do mundo. Fim da virada reconhecido pelo `PlayerCharacter.StartNextDay` (depois da meia-noite o número do dia não muda, D58) |
| `PlayerProfiles.cs` | 3.6/D24: perfil individual do convidado (envio, guarda no host, arquivo, reaplicação após cada carga de mundo) |
| `BazaarViewSync.cs` | D55: quem vende no Bazaar manda os clientes e os balcões; os outros no mesmo mapa assumem os clientes de mesmo número (cópias sem IA), abrem a barraca e mostram os itens; devolvem tudo quando a loja fecha |
| `BazaarHelpSync.cs` | D56: ajudante na barraca do Bazaar — interceptações instaladas só enquanto ajuda (Enter → pedido "atender", mini deck → pedido "pôr item", estados dos espaços do dono); o dono executa os pedidos; lista do mini deck refeita quando o deck muda. Se o dono sai ou fecha a loja, o ajudante assume a barraca (D60) |
| `HeldMessages.cs` | D63: fila de mensagens que chegam quando o módulo não está pronto (carregando o mundo, trocando de mapa, virada no convidado); aplicadas em ordem depois |
| `WindmillToolGuard.cs` | D66: a ferramenta pronta no moinho só sai para quem a deixou (quem já tem uma do mesmo tipo não retira) |
| `NpcSync.cs` | D68: morador em conversa para nos outros jogos e não aceita outra conversa; os outros moradores não congelam no jogo de quem conversa |
| `MissionSync.cs` | D57: pedidos dos moradores compartilhados (contadores somados, pedidos por valor ficam com o maior, estado mais avançado; host manda a lista oficial). D60 |
| `RomanceSync.cs` | 6.4/D44: romance exclusivo (registro NPC → jogador no host; patches em `CheckMarryable`/`SetFiance`) |
| `SharedSync.cs` | 5.4/D42/D43: estado compartilhado genérico (lista de partes com capturar/aplicar, em rodízio): moinho, apicultura, cogumelos, vasos, ampliações, pets, cavalo, prêmios, adega, banho, receitas, missões, peixes, reforços, lojas, Bazaar, festivais, informações gerais, histórico de itens e flags de evento (união); tutoriais e BRAND (união, D57); Happy Bingo por mescla (D61) |
| `AnimalSync.cs` | 5.3/D41: animais da fazenda (cuidados) e celeiros (ovos, ração), mesclando sem mexer na posição |
| `FarmSync.cs` | Fase 5/D40: fazenda ao vivo nos mapas residentes (mapa 1); diferenças a cada 0,5 s viram operações por objeto, aplicadas por todos no cache do mapa |
| `FieldState.cs` | Fase 5 (alicerce, D40): estado dos objetos de um mapa (captura pelo serializador oficial, aplicar só as diferenças no mapa atual ou num mapa residente, instalar no save de mapas) |
| `LetterSync.cs` | 4.6/D39: correio compartilhado; estado do `LetterManager` sincronizado (montado à mão a partir do JSON) |
| `StorageSync.cs` | 4.2/D37/D46: armazéns compartilhados (casa, deck do Bazaar, peças da barraca); trava de uma pessoa por vez (host) e sincronização pelo conteúdo; barraca do Bazaar (D51); convidado não abre a estante de troféus (D62) |
| `MoneySync.cs` | 4.1/D36/D57: carteiras compartilhadas (dinheiro e Happy Energy); cada jogo observa o valor, convidado manda a diferença, host manda o total |
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
