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

## Fase 4 — Economia · **v0.3** ✅ (falta só o teste do usuário numa loja de verdade, 4.5)

| # | Tarefa | Tam. | Como testar |
|---|---|---|---|
| 4.1 ✅ | **Dinheiro compartilhado (D4, D36)**: cada jogo observa a carteira; diferenças vão ao host, que manda o total | M | Teste da IA: duas instâncias (ganhar, gastar, os dois juntos, virada do dia) |
| 4.2 ✅ | **Armazém da casa** compartilhado (`HouseStorage`, D37): uma pessoa por vez + sincronização pelo conteúdo | M | Teste da IA: duas instâncias (guardar/pegar dos dois lados, trava, virada do dia) |
| 4.3 ✅ | **Mochila e ferramentas individuais (D13)**: já feitas pelo perfil de cada jogador (D24/D30) | G | Teste da IA: mochila da convidada se mantém dela depois da virada do dia |
| 4.4 ✅ | **Upgrades de ferramenta** por jogador (D38): upgrade é imediato e fica na mochila do perfil; estado das ferramentas e vigor máximo entraram no perfil | M | Teste da IA: `ctx save/load` com as partes novas |
| 4.5 🟡 | **Lojas**: compras e vendas descontam e creditam na carteira do grupo (já coberto pela D36; falta testar numa loja de verdade) | M | Teste do usuário |
| 4.6 ✅ | **Correio compartilhado (D10/D10a, D39)**: estado do correio sincronizado pelo host | M | Teste da IA: duas instâncias (carta nova, pegar as cartas) |

**✔ Pronto quando:** dá para comprar, vender, guardar e pegar itens juntos sem duplicar nem perder nada.

---

## Fase 5 — Fazenda · **v0.5** "jogar juntos de verdade"

| # | Tarefa | Tam. | Como testar |
|---|---|---|---|
| 5.1 ✅ | Interações com o mapa (arar, plantar, regar, colher...) sincronizadas por **diferença de estado** (D40) em vez de ação por ação | G | Teste da IA: duas instâncias, arar e colher de verdade |
| 5.2 ✅ | Plantações, solo, árvores, pedras/galhos e sprinklers em todos os mapas: ao vivo entre quem está no mesmo mapa; mapa inteiro ao sair de um não residente (D40) | G | Teste da IA: duas instâncias (fazenda e montanhas) |
| 5.3 ✅ | **Animais e celeiro** (`AnimalManager`, D41): cuidados de cada animal, ovos e ração do celeiro; falta pets e cavalo ao vivo | G | Teste da IA: duas instâncias, coleta real de ovo |
| 5.4 ✅ | **Moinhos**, apicultura e cogumelos (+ vasos de flor, pets e cavalo), D42 | M | Teste da IA: autoteste das 6 partes; pets em rede |
| 5.5 ✅ | Casa única compartilhada (D11): ampliações (`ExpansionManager`) na parte `expansion` da D42; o jogo não tem móveis soltos | M | Teste da IA: autoteste |
| 5.6 | **Primeira sessão longa** com amigos (1 semana do jogo) e lista de bugs | — | Teste do usuário |

**✔ Pronto quando:** um grupo cuida da fazenda junto por uma semana do jogo sem dessincronizar.

---

## Fase 6 — NPCs e relacionamentos · **v0.7**

| # | Tarefa | Tam. | Como testar |
|---|---|---|---|
| 6.1 ⏸ | **Trava de NPC (D14)**: um jogador por vez em conversa, presente ou evento. Sem motivo técnico agora; ver P9 | M | Teste da IA |
| 6.2 ✅ | **Corações individuais (D3)**: cada jogo conversa com os NPCs na própria cópia; corações no perfil de cada um (D24/D30) | G | Teste da IA |
| 6.2b ✅ | **Eventos vistos e progresso do mundo** sincronizados (D43): flags de evento em união, missões, lojas, receitas, peixes... | G | Teste da IA: autoteste + flag em rede |
| 6.3 | **Cutscenes pessoais**: só quem disparou vê, o mundo segue para os outros; `IsPlayingEvent` por jogador | GG | Teste do usuário |
| 6.4 ✅ | **Romance exclusivo (D5, D44)** | M | Teste da IA: duas instâncias, bloqueio nos dois sentidos |
| 6.5 ⏸ | **Casamento (D12a, D45)**: cônjuges na casa única (patch em `Married`/`PartnerCharaId`); eventos de casado e filhos só para o host; Plano B como reserva. A escolha de rotina não é interceptável; vale o Plano B por ora | GG | Teste do usuário |
| 6.6 | Verificar a sobreposição de cônjuges na casa (`ScheduleBed/ChairChecker`) | M | Screenshot |

**✔ Pronto quando:** cada jogador namora e casa com quem quiser, sem afetar os corações dos outros.

---

## Fase 7 — Festivais e Bazaar · **v0.9**

| # | Tarefa | Tam. | Como testar |
|---|---|---|---|
| 7.1 🟡 | **Bazaar** em grupo: barraca, vendas e caixa compartilhados (`BazaarManager`). Feito: deck e peças compartilhados, progresso no SharedSync (D46). Falta: a sessão de venda (D47, ideia do usuário) | GG | Teste do usuário |
| 7.2 | **Festivais**: todos entram juntos, início por confirmação | G | Teste do usuário |
| 7.3 | D9 com **N = todos** (tempo corre em menus) + casos especiais (loja fechando, diálogo na virada do dia) | G | Teste da IA + usuário |

**✔ Pronto quando:** uma estação completa (Bazaars e festivais) é jogada em grupo.

---

## Fase 8 — Acabamento · **v1.0**

| # | Tarefa | Tam. |
|---|---|---|
| 8.1 ✅ (IMGUI, D27) | Menu no jogo para **Hospedar / Entrar** (talvez reaproveitando a UI de rede que sobrou: `NetworkMenuCategory`) | G |
| 8.2 🟡 | Nomes dos jogadores, indicadores na tela, lista de quem está online (✅ lista com localização e ping; ✅ nome acima do avatar) | M |
| 8.7 ✅ | **Aparência própria de cada jogador (D28/D32)**, inclusive gênero diferente. ⏳ falta teste em rede | G |
| 8.3 | Reativar para convidados: eventos de casado, cerimônia, filhos, ajuda na fazenda (D12a) | GG |
| 8.4c | Esconder/desabilitar a opção "Salvar" no multiplayer (hoje o jogo mostra "Não foi possível acessar os dados") | P |
| 8.4b | "Salvar agora" do host com barreira (pausa todos, espera estado limpo) — opcional (D23) | M |
| 8.4 | Etiqueta "Para: Jogador X" nas cartas (D10a, opcional) | P |
| 8.5 | Guia de instalação para os amigos + pacote de release (`DevBridge.Enabled = false`) | M |
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

- **Fase:** 4 — Economia (v0.3) concluída pela IA (falta o teste numa loja de verdade); próxima: 5 — Fazenda. Antes dela, a noite de 2026-10-25 foi de correções e qualidade de vida pedidas pelo usuário (ver itens "Corrigido/Feito" acima).
- **Pacote de teste para amigos:** v0.3.1 (`Downloads\SOSGB-Multiplayer-v0.3.1-amiga.zip`, fora do repositório; protocolo 9). A v0.3 derrubava o host ao deitar (D33) e foi apagada.
- **Pendências com o usuário:** ver [`PERGUNTAS.md`](PERGUNTAS.md) (P3 log da amiga, P5 memória, P7 pausa por menu, P8 espectar, P9 trava de NPC). P2 e P6 feitas pela IA (D33). P1 e P4 respondidas: convidado começa do zero (D30, feito) e aparência (D31 → corrigida na D32).
- **Próxima tarefa sugerida:** Fase 7 (festivais e Bazaar). 6.3/6.5/6.6 dependem de teste com os amigos. Espectar ficou adiado (D35, P8).
- **Última atualização:** 2026-10-25
