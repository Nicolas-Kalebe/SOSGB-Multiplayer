# CONTEXTO — `src/SOSGB.Multiplayer/Net/`

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../../../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../../../AGENTS.md).

Camada de rede do mod (Fase 2 do [`ROTEIRO.md`](../../../ROTEIRO.md)). Protocolo documentado em [`docs/PROTOCOLO.md`](../../../docs/PROTOCOLO.md).

| Arquivo | O que faz |
|---|---|
| `Protocol.cs` | Constantes (versão, porta, chave, máximo de jogadores) e o enum `MsgType` |
| `NetSession.cs` | Sessão LiteNetLib: hospedar/entrar, aperto de mão, lista de jogadores, repasse de mensagens de jogo (host-autoritativo) |
| `PlayerSync.cs` | Envia o estado do jogador local (10×/s: posição, 12 camadas e parâmetros do Animator) e mantém os avatares (clone visual do modelo com o override controller real; recriados quando o mundo recarrega; visíveis só no mesmo mapa) |
| `WorldSync.cs` | 3.1: convidado pede o mundo, host captura (`WorldSnapshot`) e envia, convidado recarrega com ele |
| `ClockSync.cs` | Relógio do grupo (D50): só o menu de pausa pausa; quem está em outra tela é adiantado ao sair; o tempo segue o mais adiantado (`ClockJump`); quem dorme não pausa (D33). Só conta como pulo acima de 5 min de jogo; menos que isso é travada e é corrigido sem aviso (D70). Na pausa de outro jogador, o personagem local fica parado (D74) D83: em dia de festival, diálogo/cena de qualquer um para o tempo do grupo; host sozinho não adianta; ao sair de uma tela vai para a maior hora (sem somar) |
| `DaySync.cs` | 3.4/3.5: dormir individual (segura `StartNextDay`; o desmaio passa pelo mesmo caminho), contador "dormindo X/Y", virada do dia pelo host e reenvio do mundo. Fim da virada reconhecido pelo `PlayerCharacter.StartNextDay` (depois da meia-noite o número do dia não muda, D58). Ao deitar, o convidado manda o que estava pendente e só 1,2 s depois avisa "dormi" (D87) |
| `PlayerProfiles.cs` | 3.6/D24: perfil individual do convidado (envio, guarda no host, arquivo, reaplicação após cada carga de mundo) |
| `BazaarViewSync.cs` | D55: quem vende no Bazaar manda os clientes e os balcões; os outros no mesmo mapa assumem os clientes de mesmo número (cópias sem IA), abrem a barraca e mostram os itens; devolvem tudo quando a loja fecha |
| `BazaarHelpSync.cs` | D56: ajudante na barraca do Bazaar — interceptações instaladas só enquanto ajuda (Enter → pedido "atender", mini deck → pedido "pôr item", estados dos espaços do dono); o dono executa os pedidos; lista do mini deck refeita quando o deck muda. Se o dono sai ou fecha a loja, o ajudante assume a barraca (D60) |
| `HeldMessages.cs` | D63: fila de mensagens que chegam quando o módulo não está pronto (carregando o mundo, trocando de mapa, virada no convidado); aplicadas em ordem depois |
| `WindmillToolGuard.cs` | D66/D85: a ferramenta pronta no moinho só sai para quem a encomendou (registro de dono por encomenda, guardado pelo host em `windmill_owners_*.json` e enviado a todos, mensagem `WindmillOwner`); sem registro, a regra antiga (quem já tem uma do mesmo tipo não retira) |
| `NpcSync.cs` | D68: morador em conversa para nos outros jogos e não aceita outra conversa; os outros moradores não congelam no jogo de quem conversa. D83: anfitrião do festival não aceita conversa enquanto outro está na cerimônia. D89: menu de pausa de qualquer jogador segura os moradores |
| `NpcPosSync.cs` | D90: a referência do mapa (menor número de jogador nele) manda posição/animação dos moradores com a hora do relógio; os outros mostram cópias (agenda local rodando por baixo) e escondem os que a referência não tem |
| `JsonMerge3.cs` | D91: mescla em três vias de estados em JSON (o que cada lado mudou desde a base; listas por Guid, por posição ou multiconjunto) |
| `NpcHold.cs` | D90: segurar UM morador de verdade (pausa Idle + script da agenda desligado + agente sem controle do corpo), com uma chave por motivo (conversa, pausa, festival) |
| `CharacterCreator.cs` | D72/D75 (sabe quando qualquer tela de aparência está aberta: `OnScreen`, `AvatarMakeBusy`): tela de criação completa do jogo novo (modo Prologue) dentro do mundo; abre sozinha na primeira entrada sem save e pelo F9; clareia a tela ao fechar |
| `SleepPoseSync.cs` | D69: quem dorme/desmaia manda o esqueleto deitado e os outros aplicam no avatar dele (antes aparecia em pé dentro da cama) |
| `Spectator.cs` | D69: F7 deitado esperando os outros segue quem está acordado em outro mapa (carrega o mapa, esconde o personagem, câmera no avatar) |
| `MissionSync.cs` | D57: pedidos dos moradores compartilhados (contadores somados, pedidos por valor ficam com o maior, estado mais avançado; host manda a lista oficial). D60 |
| `RomanceSync.cs` | 6.4/D44: romance exclusivo (registro NPC → jogador no host; patches em `CheckMarryable`/`SetFiance`) |
| `SharedSync.cs` | 5.4/D42/D43: estado compartilhado genérico (lista de partes com capturar/aplicar, em rodízio; tudo de uma vez antes de dormir, D87; parte com erro desliga na 3ª falha e avisa, D86): moinho, apicultura, cogumelos, vasos, ampliações, pets, cavalo, prêmios, adega, banho, receitas, missões, peixes, reforços, lojas, Bazaar, festivais, informações gerais, histórico de itens e flags de evento (união); tutoriais e BRAND (união, D57); Happy Bingo por mescla (D61) |
| `AnimalSync.cs` | 5.3/D41: animais da fazenda (cuidados) e celeiros (ovos, ração), mesclando sem mexer na posição; animal comprado/vendido aparece/some ao vivo (D81) |
| `EventWatch.cs` | D84 (EXPERIMENTAL, desligado por padrão): assistir junto à cena do festival (mesma cena em quem está no mapa, falas pelo Enter de quem está nela com espera conjunta, escolhas espelhadas, efeitos e prêmios pulados, saída pelo fechamento normal do jogo, pular junto). Testado com duas instâncias na cerimônia da Exposição de Animais |
| `MapMarkers.cs` | 8.8/D82: no mapa do jogo (M), um marcador colorido para cada outro jogador (lugar calculado pelo próprio jogo com a grade dele, ou ponto do prédio) |
| `FarmSync.cs` | Fase 5/D40: fazenda ao vivo nos mapas residentes (mapa 1); diferenças a cada 0,5 s viram operações por objeto, aplicadas por todos no cache do mapa. Itens largados no chão: o host desempata quem pegou primeiro e o outro devolve (`ItemTakeBack`, D70) |
| `FieldState.cs` | Fase 5 (alicerce, D40): estado dos objetos de um mapa (captura pelo serializador oficial, aplicar só as diferenças no mapa atual ou num mapa residente, instalar no save de mapas). Inclui itens largados no chão, menos os de coleta automática (D70) |
| `LetterSync.cs` | 4.6/D39: correio compartilhado; estado do `LetterManager` sincronizado (montado à mão a partir do JSON) |
| `StorageSync.cs` | 4.2/D37/D46: armazéns compartilhados (casa, deck do Bazaar, peças da barraca); trava de uma pessoa por vez (host) e sincronização pelo conteúdo; mudança sem a tela aberta vai como diferença (`StorageDelta`, D88); barraca do Bazaar (D51); convidado não abre a estante de troféus (D62) |
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
