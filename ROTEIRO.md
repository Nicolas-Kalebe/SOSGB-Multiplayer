# ROTEIRO — o que vai ser feito e em que ordem

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](AGENTS.md).

**Como usar este arquivo:**
- As fases são feitas **em ordem**. Cada fase só começa quando a anterior está **pronta** (critério "✔ Pronto quando").
- Dentro de uma fase, a ordem das tarefas é a sugerida, mas pode mudar se o código mostrar um caminho melhor. Nesse caso, registre o motivo em `DECISOES.md`.
- Ao terminar uma tarefa: marque `[x]`, faça um **commit** e atualize "Estado atual" no fim deste arquivo.
- **Git:** commit a cada tarefa concluída. **Push para o GitHub só quando o usuário pedir** ([D22](DECISOES.md)).
- Os testes usam a ponte de teste (`scripts/`, [D20](DECISOES.md)). "Teste da IA" = a IA executa sozinha. "Teste do usuário" = precisa de alguém jogando.

Legenda de tamanho: **P** = horas · **M** = 1–3 dias · **G** = 1–2 semanas · **GG** = semanas+

---

## Fase 0 — Fundação ✅

- [x] Design fechado (D1–D19) e código do jogo mapeado (`CODIGO.md`)
- [x] Backups (D17), repositório (D18), BepInEx + plugin (D19)
- [x] Ponte de teste para a IA (D20) e jogo sem pausa ao perder o foco (D21)

---

## Fase 1 — Validação técnica (antes de qualquer rede) ✅

> Objetivo: provar, sem rede, que as peças de que o multiplayer depende funcionam. Se alguma falhar, o plano muda **aqui**, com custo baixo.

| # | Tarefa | Tam. | Como testar |
|---|---|---|---|
| 1.1 ✅ | **Relógio em menus e diálogos**: descobrir quem chama `DateManager.Pause()` (patch de log no `Pause`/`Resume` com stack trace). Base do D9 | P | Teste da IA: abrir menu e conversar via comando, ler o log |
| 1.2 ✅ | **Formato do save**: hook em `SaveDataManager.Encode<T>`/`Decode<T>` para confirmar JSON + deflate + XOR | P | Teste da IA: forçar save, inspecionar o dump |
| 1.3 ✅ | **Troca de contexto da amizade**: `LikeabilityManager.ToSaveData()` → mudar pontos → `FromSaveData()` restaura. Base do D3 | M | Teste da IA: comandos `likeability get/set/swap` na ponte |
| 1.4 ✅ | **Troca de contexto do inventário**: idem para mochila, ferramentas, `TalkManager`, `EventFlagManager` (D13) | M | Teste da IA |
| 1.5 ✅ | **Snapshot do mundo**: `UserInfoMediator.ApplyToUserInfo` → serializar → aplicar em outra sessão. É o que o convidado vai receber ao entrar | M | Teste da IA: snapshot do slot A aplicado no B |
| 1.6 ✅ | **Duas instâncias no mesmo PC**: o jogo abre duas vezes? Cada uma com a sua porta de ponte (config por instância) | P | Teste da IA |
| 1.7 ✅ | Mover o overlay de debug para não cobrir a barra de stamina | P | Screenshot |

**✔ Pronto quando:** 1.1–1.6 confirmados em runtime e documentados em `CODIGO.md` §13. Qualquer surpresa vira decisão nova.

---

## Fase 2 — Rede básica · **v0.1** "ver o amigo andando" ✅

| # | Tarefa | Tam. | Como testar |
|---|---|---|---|
| 2.1 ✅ | Integrar **LiteNetLib** (D7): camada `Net/` com host/cliente, handshake, checagem de versão do mod, desconexão | M | Teste da IA: 2 instâncias, `net host` / `net join 127.0.0.1` pela ponte |
| 2.2 ✅ | Protocolo de mensagens (tipos, serialização binária, IDs de jogador 0–3) documentado em `docs/PROTOCOLO.md` | P | Testes unitários de serialização |
| 2.3 ✅ | **Avatar remoto**: representar os outros jogadores (começar com um modelo simples; depois reaproveitar o modelo do player) | M | Screenshot das 2 instâncias |
| 2.4 ✅ | Sincronizar **posição, rotação e animação** (com interpolação) | M | Teste da IA: mover um, ler a posição no outro |
| 2.5 ✅ | Sincronizar **mapa atual**: o avatar só aparece para quem está no mesmo mapa | P | Teste da IA: `field <id>` numa instância |
| 2.6 ✅ | Configuração para entrar e hospedar (IP/porta no `.cfg`; menu no jogo fica para a Fase 8) | P | — |

**✔ Pronto quando:** duas instâncias (e depois dois PCs na LAN) se veem andando e trocando de mapa. 🎉 **Primeiro teste do usuário com um amigo.**

---

## Fase 3 — Mundo e tempo controlados pelo host ✅

| # | Tarefa | Tam. | Como testar |
|---|---|---|---|
| 3.1 ✅ | **Entrar no mundo do host**: o convidado recebe o snapshot (1.5) e não usa o próprio save; **só o host salva** | G | Teste da IA: convidado entra e vê o mesmo dinheiro e data |
| 3.2 ✅ | **Relógio do host** enviado aos clientes; clientes não avançam o tempo sozinhos | M | Teste da IA: comparar `date` nas 2 instâncias |
| 3.3 ✅ | **Pausa configurável (D9)** com N = 1 | M | Teste da IA: menu numa instância pausa as duas |
| 3.4 ✅ | **Dormir individual (D12)**: deitar desabilita só o seu personagem; contador "dormindo X/4"; dia vira quando todos dormem | M | Teste da IA: `sleep` em cada instância |
| 3.5 ✅ | **Virada do dia** sincronizada (`OnChangeDate` só no host, resultado para todos) | M | Teste da IA |
| 3.7 ✅ | **Save só na virada do dia (D23)**: bloquear "Salvar" do menu no multiplayer (com aviso), host salva após o dia virar; snapshot periódico em memória para recuperar de queda | M | Teste da IA: `save` pelo menu é recusado; dia vira → save gravado |
| 3.6 ✅ | Guardar os dados do convidado (personagem, mochila etc.) **dentro do save do host**, com reconexão | M | Teste da IA: sair e voltar |
| 3.10 ✅ | **Levantar da cama (D34)**: Backspace ou botão no F9 enquanto alguém ainda está acordado | M | Teste da IA: uma instância com `SOSGB_TEST_FAKE_AWAKE=1`, tecla de verdade |
| 3.9 ✅ | **Dormir de verdade (D33)**: cama real sem queda; quem dorme não congela os outros; relógio segue para os acordados | M | Teste da IA: duas instâncias, os dois pela cama |
| 3.8 ✅ | **Convidado novo começa do zero (D30)**: corações/conversas zerados, mochila vazia, ferramentas básicas; só o visual vem do save dele | M | Teste da IA: `ctx fresh` (uma instância). ⏳ falta em rede |

**✔ Pronto quando:** um dia inteiro passa sincronizado, todos dormem, o dia vira e o progresso persiste no save do host.

---

## Fase 4 — Economia · **v0.3** ✅

| # | Tarefa | Tam. | Como testar |
|---|---|---|---|
| 4.1 ✅ | **Dinheiro compartilhado (D4, D36)**: cada jogo observa a carteira; diferenças vão ao host, que manda o total | M | Teste da IA: duas instâncias (ganhar, gastar, os dois juntos, virada do dia) |
| 4.2 ✅ | **Armazém da casa** compartilhado (`HouseStorage`, D37): uma pessoa por vez + sincronização pelo conteúdo | M | Teste da IA: duas instâncias (guardar/pegar dos dois lados, trava, virada do dia) |
| 4.3 ✅ | **Mochila e ferramentas individuais (D13)**: já feitas pelo perfil de cada jogador (D24/D30) | G | Teste da IA: mochila da convidada se mantém dela depois da virada do dia |
| 4.4 ✅ | **Upgrades de ferramenta** por jogador (D38): upgrade é imediato e fica na mochila do perfil; estado das ferramentas e vigor máximo entraram no perfil | M | Teste da IA: `ctx save/load` com as partes novas |
| 4.5 ✅ | **Lojas**: compras e vendas descontam e creditam na carteira do grupo (D36). Conferido em 2026-10-27 pela rotina de compra da loja (`ShopManager.BuyItem`): a carteira muda nos dois jogos e o item vai só para a mochila de quem comprou. Animal comprado: D81 | M | Teste da IA: duas instâncias |
| 4.6 ✅ | **Correio compartilhado (D10/D10a, D39)**: estado do correio sincronizado pelo host | M | Teste da IA: duas instâncias (carta nova, pegar as cartas) |

**✔ Pronto quando:** dá para comprar, vender, guardar e pegar itens juntos sem duplicar nem perder nada.

---

## Fase 5 — Fazenda · **v0.5** "jogar juntos de verdade"

| # | Tarefa | Tam. | Como testar |
|---|---|---|---|
| 5.1 ✅ | Interações com o mapa (arar, plantar, regar, colher...) sincronizadas por **diferença de estado** (D40) em vez de ação por ação | G | Teste da IA: duas instâncias, arar e colher de verdade |
| 5.2 ✅ | Plantações, solo, árvores, pedras/galhos e sprinklers em todos os mapas: ao vivo entre quem está no mesmo mapa; mapa inteiro ao sair de um não residente (D40) | G | Teste da IA: duas instâncias (fazenda e montanhas) |
| 5.3 ✅ | **Animais e celeiro** (`AnimalManager`, D41): cuidados de cada animal, ovos e ração do celeiro; pets e cavalo pela D42/D52. Carinho do dia compartilhado: quem faz depois do outro não ganha afeição de novo (conferido em 2026-10-26). Compra e venda de animal ao vivo (D81) | G | Teste da IA: duas instâncias, coleta real de ovo, carinho nos dois sentidos |
| 5.4 ✅ | **Moinhos**, apicultura e cogumelos (+ vasos de flor, pets e cavalo), D42 | M | Teste da IA: autoteste das 6 partes; pets em rede |
| 5.5 ✅ | Casa única compartilhada (D11): ampliações (`ExpansionManager`) na parte `expansion` da D42; o jogo não tem móveis soltos | M | Teste da IA: autoteste |
| 5.6 🟡 | **Primeira sessão longa** com amigos (1 semana do jogo) e lista de bugs. Prévia da IA (2026-10-27, duas instâncias, 4 dias seguidos pela cama): datas, dinheiro, animais, pets e avatares iguais todo dia; mochila da convidada mantida; memória estável (~1,85–1,89 GB); primeira entrada da amiga pelo título com criação de personagem conferida de novo | — | Teste do usuário |

**✔ Pronto quando:** um grupo cuida da fazenda junto por uma semana do jogo sem dessincronizar.

---

## Fase 6 — NPCs e relacionamentos · **v0.7**

| # | Tarefa | Tam. | Como testar |
|---|---|---|---|
| 6.1 ✅ | **Trava de NPC (D14 → D68)**: o morador em conversa para nos outros jogos e só aceita um de cada vez; os outros moradores não congelam no jogo de quem conversa | M | Teste da IA: duas instâncias, Enter de verdade |
| 6.2 ✅ | **Corações individuais (D3)**: cada jogo conversa com os NPCs na própria cópia; corações no perfil de cada um (D24/D30) | G | Teste da IA |
| 6.2b ✅ | **Eventos vistos e progresso do mundo** sincronizados (D43): flags de evento em união, missões, lojas, receitas, peixes... | G | Teste da IA: autoteste + flag em rede |
| 6.3 ✅ | **Cutscenes pessoais**: só quem disparou vê, o mundo segue para os outros. Cena usa a mesma regra de relógio das telas (o outro segue; quem sai é adiantado à maior hora, D83), exceto em dia de festival (o tempo do grupo para, D83); o morador fica ocupado só na conversa (D68). Assistir junto: D84 (experimental, desligado) | GG | Teste da IA: regras do relógio (regressão) e cerimônia do festival com duas instâncias |
| 6.4 ✅ | **Romance exclusivo (D5, D44)** | M | Teste da IA: duas instâncias, bloqueio nos dois sentidos |
| 6.5 ⏸ | **Casamento (D12a, D45)**: cônjuges na casa única (patch em `Married`/`PartnerCharaId`); eventos de casado e filhos só para o host; Plano B como reserva. A escolha de rotina não é interceptável; vale o Plano B por ora | GG | Teste do usuário |
| 6.6 | Verificar a sobreposição de cônjuges na casa (`ScheduleBed/ChairChecker`) | M | Screenshot |

**✔ Pronto quando:** cada jogador namora e casa com quem quiser, sem afetar os corações dos outros.

---

## Fase 7 — Festivais e Bazaar · **v0.9**

| # | Tarefa | Tam. | Como testar |
|---|---|---|---|
| 7.1 🟡 | **Bazaar** em grupo: barraca, vendas e caixa compartilhados (`BazaarManager`). Feito: deck e peças compartilhados, progresso no SharedSync (D46). Sessão de venda medida (D47): relógio normal, aviso de barraca aberta. Uma barraca por vez (trava, D51, testada). Todos veem os mesmos clientes e a barraca de quem vende (D55, testado). Ajudante dentro da barraca com os botões do jogo (D56: vender, repor, sino; testado pela IA com duas instâncias e teclas de verdade). Revisão independente e 10 correções (D60): o ajudante assume a barraca se o dono sai. Falta: vender em grupo com os amigos (P14) | GG | Teste do usuário |
| 7.2 ✅ | **Festivais**: resultados e participação compartilhados (`FestivalExecutor` no SharedSync, D43); em dia de festival, diálogo e cena de qualquer um param o tempo do grupo; uma inscrição só (D83). Assistir junto à cerimônia (D84): experimental, desligado | G | Teste da IA: duas instâncias, Exposição de Animais de outono (inscrição e cerimônia reais) |
| 7.3 ✅ | Tempo corre em menus (D50): só o menu de pausa pausa; quem estava numa tela é adiantado ao sair; pulos de tempo propagam | G | Teste da IA: duas instâncias |

**✔ Pronto quando:** uma estação completa (Bazaars e festivais) é jogada em grupo.

---

## Fase 8 — Acabamento · **v1.0**

| # | Tarefa | Tam. |
|---|---|---|
| 8.1 ✅ (IMGUI, D27) | Menu no jogo para **Hospedar / Entrar** (talvez reaproveitando a UI de rede que sobrou: `NetworkMenuCategory`) | G |
| 8.2 🟡 | Nomes dos jogadores, indicadores na tela, lista de quem está online (✅ lista com localização e ping; ✅ nome acima do avatar; ✅ aviso de pausa D74) | M |
| 8.9 ✅ | **Entrar sem save e criar o personagem** (D72): "Entrar agora" na tela inicial, criação completa do jogo novo na primeira vez, F9 → "Mudar visual"; **"Gerar log"** (D71) | M |
| 8.8 ✅ | **Mapa do jogo (M) com a localização dos outros jogadores** (D82, testado com duas instâncias) (pedido do usuário, 2026-10-26: "quero o mapa com localização do outro player"). Caminho: a página do mapa já tem o marcador do jogador local (`UIMapArea.playerPosition`, posto por `SetPlayerPosition`); clonar um marcador por jogador remoto, com nome/cor, usando o mapa e a posição que o `PlayerSync` já recebe. Quem está dentro de um prédio fica no ponto do prédio, como os moradores (`AddCharaToAnchor`) | M |
| 8.7 ✅ | **Aparência própria de cada jogador (D28/D32)**, inclusive gênero diferente. ⏳ falta teste em rede | G |
| 8.3 | Reativar para convidados: eventos de casado, cerimônia, filhos, ajuda na fazenda (D12a) | GG |
| 8.4c ✅ | Esconder/desabilitar a opção "Salvar" no multiplayer: "Salvar" e "Carregar" mostram aviso (D48) | P |
| 8.4b | "Salvar agora" do host com barreira (pausa todos, espera estado limpo) — opcional (D23) | M |
| 8.4 | Etiqueta "Para: Jogador X" nas cartas (D10a, opcional) | P |
| 8.5 🟡 | Guia de instalação para os amigos + pacote de release (`DevBridge.Enabled = false`): guia atualizado; pacote montado por `scripts/make-package.py` (v0.5.1); falta a versão 1.0 | M |
| 8.6 | Checklist de release e versão 1.0 | P |

**Bônus (pós-1.0):** casamento entre jogadores.

---

## Regras transversais (valem em todas as fases)

- **Commit a cada tarefa concluída**, com mensagem descrevendo o quê e o porquê. **Push só quando o usuário pedir.**
- Antes de testes que alteram o save: snapshot em `backups/saves/` (D17). Testes sempre no **slot 3** (índice 2).
- Toda descoberta sobre o jogo vai para `CODIGO.md`; toda escolha vai para `DECISOES.md`.
- Pasta nova → `CONTEXTO.md` no mesmo commit.

---

## Estado atual

- **Fase:** 4, 5 e boa parte da 6 concluídas pela IA (D36–D45); 7 adiantada: Bazaar em grupo com clientes iguais para todos (D55) e ajudante na barraca (D56); 8.4c feita. Falta teste com os amigos (5.6, Bazaar, festivais, casamento).
- **Noite de 2026-10-26 (IA sozinha, pedido do usuário):** levantamento de conflitos pela wiki e pelo save (D57: Happy Energy, pedidos dos moradores, tutoriais, BRAND, pets de presente); impasse da virada do dia depois da meia-noite (D58, sério: convidados dormiam para sempre); save do convidado protegido depois de uma queda (D59); duas revisões independentes do código com correções (D60 no Bazaar e na D57, D63 no núcleo: título encerra a sessão, perfil comprimido, mensagens guardadas enquanto carrega, captura do mundo sem travar...); Happy Bingo compartilhado (D61); estante de troféus só do host (D62); painel mostra por que não conectou.
- **Pacote de teste para amigos:** v0.6.4, protocolo 28 (`Downloads\SOSGB-Multiplayer-v0.6.4-amiga.zip`, fora do repositório; montado com `python scripts/make-package.py --version 0.6.4 --ip <IP do host>`). Na 0.6.4: assistir junto à cerimônia do festival completo, com saída e pular junto (D84, ainda desligado por padrão: `WatchFestivalScenes`); correções da revisão externa: dono por encomenda no moinho (D85), parte com erro avisa e volta (D86), fechamento do dia antes de dormir (D87), armazém sem a tela aberta manda a diferença (D88). Na 0.6.3: base do D84; regressão geral com duas instâncias sem falhas. Na 0.6.2: festivais com o tempo parado em diálogos e cenas e uma inscrição só (D83). Na 0.6.1: mapa (M) com o outro jogador (D82) e animal comprado/vendido ao vivo (D81). Pacotes anteriores não conectam mais. Host e amigos precisam da mesma versão.
- **Pendências com o usuário:** ver [`PERGUNTAS.md`](PERGUNTAS.md) (P3 log da amiga, P5 memória, P8 espectar, P9 trava de NPC). P7 e P10 respondidas (D50, D47/D51). P2 e P6 feitas pela IA (D33). P1 e P4 respondidas: convidado começa do zero (D30, feito) e aparência (D31 → corrigida na D32).
- **Noite de 2026-10-26 para 27 (IA sozinha, pedido do usuário):** carinho nos animais conferido (já compartilhado); animal comprado/vendido ao vivo (D81); mapa (M) com o outro jogador (D82); Happy Energy, pedidos e queda com o host deitado conferidos (D57/D59); festivais: tempo parado em diálogos e cenas no dia do festival, uma inscrição só, relógio sem salto em dobro (D83); assistir junto à cerimônia (D84, experimental, desligado: a cena não carrega por fora da conversa); lojas conferidas pela rotina de compra (4.5); regressão geral com duas instâncias sem falhas; sessão longa de vários dias (5.6, superficial).
- **Manhã de 2026-10-27:** D84 terminado no teste da IA (a P2 ficava presa no fim porque o mod respondia `ScenarioEnd = false`, e o `CloseEvent` do jogo para ali; pular junto); correções A08, A03, A01 e A02 da revisão externa (D85–D88), testadas com duas instâncias.
- **Próxima tarefa sugerida:** o usuário testar a cerimônia com `WatchFestivalScenes = true` (os dois jogadores) e a semana de teste com a amiga (pacote 0.6.4). Depois: outros festivais assistidos juntos, ajudante do Bazaar com duas pessoas (P14).
- **Última atualização:** 2026-10-27 (manhã: D84 completo no teste da IA, D85–D88, pacote 0.6.4)
