# Mod Multiplayer — STORY OF SEASONS: Grand Bazaar

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](AGENTS.md).

> Objetivo: multiplayer co-op **igual ao do Stardew Valley**.
> Última atualização: 2026-10-24

---

## 1. Decisões tomadas

> Resumo de uma linha. **Contexto completo de cada decisão em [`DECISOES.md`](DECISOES.md).**

| # | Decisão | Status |
|---|---|---|
| D1 | Modelo de referência: **multiplayer do Stardew Valley** | ✅ Decidido |
| D2 | **Mundo compartilhado**: fazenda, armazém, moinhos, animais, construções | ✅ Decidido |
| D3 | **Relacionamentos individuais**: cada jogador tem seus próprios corações, eventos e presentes, e casa com quem quiser | ✅ Decidido |
| D4 | **Dinheiro compartilhado** (carteira única do grupo) | ✅ Decidido |
| D5 | Casamento com NPC **exclusivo**: quando um jogador namora um NPC, ele fica bloqueado para os outros | ✅ Decidido |
| D6 | Até **4 jogadores** (host + 3) | ✅ Decidido |
| D7 | Conexão via **LAN** (IP direto). Para jogar pela internet, usar uma LAN virtual (Radmin VPN, ZeroTier, Tailscale) | ✅ Decidido |
| D8 | **Sem tela dividida**: cada jogador joga no próprio PC | ✅ Decidido |
| D9 | **Pausa configurável**: o relógio pausa quando **N** jogadores estão em menu ou evento. Começa com N = 1 (pausa para todos, mais seguro); o padrão passa a ser N = todos (estilo Stardew) depois que os casos especiais estiverem tratados | ✅ Decidido |
| D10 | **Correio compartilhado**: uma caixa de correio só para o grupo | ✅ Decidido |
| D10a | Cartas pessoais (disparadas pelo relacionamento de um jogador) entram na caixa compartilhada como qualquer outra carta: **todos leem**, e o item anexado vai para **quem abrir**. A etiqueta "Para: Jogador X" fica como **melhoria opcional**: só entra se mexer no texto da UI for barato | ✅ Decidido (por Claude, delegado pelo usuário) |
| D11 | **Casa única compartilhada**: uma casa só para o grupo, com tudo dentro compartilhado (sem cabanas) | ✅ Decidido |
| D12 | **Dormir individual**: ao deitar, só o seu personagem fica desabilitado (dormindo/esperando) até todos dormirem; aí o dia vira | ✅ Decidido |
| D12a | **Todos os cônjuges moram na casa única**, usando a rotina de casado original de cada NPC (patch em `Married`/`PartnerCharaId` para "casado com qualquer jogador"). Para quem **não é host**, ficam **desligados no início**: eventos de casado, filhos, cerimônia de casamento e ajuda na fazenda (`FamilyHelpInfo`); são reativados depois. **Reserva (Plano B):** se o patch do "casado" ficar complicado, o cônjuge do convidado mora na casa de origem | ✅ Decidido |
| D13 | **Itens e upgrades individuais**: mochila, ferramentas e upgrades de ferramenta são de cada jogador (armazém, casa e dinheiro continuam compartilhados) | ✅ Decidido |
| D14 | **NPC fala com um jogador por vez**: enquanto um jogador conversa, dá presente ou está em evento com um NPC, o NPC fica "ocupado" para os outros | ✅ Decidido |
| D15 | **Autodocumentação obrigatória**: decisões com contexto em `DECISOES.md`, um `CONTEXTO.md` por pasta, aviso em todo `.md` (regras em `AGENTS.md`) | ✅ Decidido |
| D16 | Mapear o código com **Cpp2IL** (fora do jogo) antes de instalar o BepInEx | ✅ Decidido (IA) |
| D17 | **Backup**: saves em 2 discos + jogo original inteiro em `L:\Backups\SOSGB`, verificados por SHA-256 (restauração em `backups/CONTEXTO.md`) | ✅ Decidido |
| D18 | **Repo público** no GitHub; `.gitignore` bloqueia dump, arquivos do jogo, saves e binários de ferramentas | ✅ Decidido |
| D19 | **Stack**: BepInEx 6 IL2CPP be.791 + plugin `net6.0` em `src/`; primeira etapa só lê o estado do jogo (`DebugOverlay`) | ✅ Decidido |
| D20 | **Ponte de teste** (só dev): log de estado + comandos em `127.0.0.1` + screenshots + auto-load de save, para a IA testar sozinha | ✅ Decidido (a implementar) |
| D21 | **Jogo não pausa sem foco**: patch em `AppManager.OnFocusChanged` + `runInBackground`; aberto por script precisa do `kick-window.ps1` | ✅ Decidido |
| D22 | **Roteiro** em `ROTEIRO.md` (fases 0–8); commit a cada tarefa, **push só quando o usuário pedir** | ✅ Decidido |
| D78 | **Rosto, pele e cores aplicados de verdade** (funções internas do jogo por peça) nos avatares dos outros e no visual próprio do convidado; peças só do outro copiadas para o avatar | ✅ Decidido |
| D77 | **Peças dos avatares protegidas** contra a descarga do jogo (lista de proteção do gerenciador de peças); avatar estragado fica escondido até remontar | ✅ Decidido |
| D76 | **Rosto/pele não grudam mais** no personagem de quem olha (espera o jogo terminar de aplicar e confere as texturas) | ✅ Decidido |
| D75 | **Pose deitada limpa na virada**; sem montagem de avatar com tela de aparência aberta; outros e painéis somem nessa tela | ✅ Decidido |
| D74 | **Pausa para todos**: quem não abriu o menu fica parado e vê "Jogo pausado — Fulano está no menu" | ✅ Decidido |
| D73 | **Avatar remoto sem corpo** (guarda-roupa, troca de mapa): o mod percebe as malhas descarregadas e monta de novo | ✅ Decidido |
| D72 | **Entrar sem save** ("Entrar agora" na tela inicial) e **criar o personagem** com a tela completa do jogo novo, sem jogo novo nem cenas; F9 → "Mudar visual" | ✅ Decidido |
| D71 | **"Gerar log"**: .zip na Área de Trabalho com os logs das últimas sessões (inclusive as que travaram) para mandar ao desenvolvedor | ✅ Decidido |
| D70 | **Itens largados no chão** aparecem para todos e só um pega (o host desempata; quem pegou depois devolve); coleta automática fica de fora; pulo de tempo só acima de 5 min de jogo | ✅ Decidido |
| D69 | **Espectar quem está acordado** (F7 deitado esperando; experimental; P8) | ✅ Decidido |
| D68 | **Morador em conversa** para nos outros jogos e só aceita um de cada vez; o resto dos moradores não congela no jogo de quem conversa (P9) | ✅ Decidido |
| D67 | **Cenas já vistas por jogador** (P15; parte `events` do perfil; convidado novo começa com as do host) | ✅ Decidido |
| D66 | **Ferramenta no moinho só sai para quem deixou** (P12; quem já tem uma do mesmo tipo não retira) | ✅ Decidido |
| D65 | **Conexão caiu → convidado volta ao menu inicial** (P16; testado com duas instâncias) | ✅ Decidido |
| D64 | **Respostas do usuário**: tentar espectar (P8), analisar NPC parado para os dois (P9), ferramenta do moinho só de quem deixou (P12), festival com um inscrito e resultado comum, junto com o usuário (P13), cenas vistas por jogador (P15), queda de conexão volta ao menu (P16) | ✅ Decidido |
| D63 | **Revisão independente do núcleo**: voltar ao título encerra a sessão, perfil comprimido (limite de 64 KB), mensagens guardadas enquanto carrega (sem perder nem duplicar), convidado sabe da virada, desconectar deitado, captura do mundo sem travar, GUID repetido, perfis gravados de forma segura | ✅ Decidido |
| D62 | **Estante de troféus: só o host arruma** (troféu do convidado posto nela sumiria na virada) | ✅ Decidido |
| D61 | **Happy Bingo compartilhado por mescla** (maior progresso por quadrado, prêmios resgatados em união; fecha o resgate repetido do convidado) | ✅ Decidido |
| D60 | **Revisão independente do Bazaar e da D57**: 10 correções (duplicação com "Ver estoque", pet comprado por convidado, ajudante assume a barraca quando o dono sai, pedidos por valor, "pôr item" sem perder itens...) | ✅ Decidido |
| D59 | **Convidado que perde a conexão não grava o mundo do host** no próprio save (até voltar ao título) | ✅ Decidido |
| D58 | **Virada do dia depois da meia-noite**: o host reconhece o fim da virada pelo personagem (o número do dia não muda); antes ficava preso e os convidados dormiam para sempre | ✅ Decidido |
| D57 | **Conflitos achados na wiki e no save**: Happy Energy compartilhada, pedidos (progresso somado), tutoriais e BRAND em união, pets/cavalos novos só do host | ✅ Decidido |
| D56 | **Bazaar: ajudante dentro da barraca com os botões do jogo** (pedidos ao dono; vendas só no jogo dele) | 🟡 Em implementação |
| D55 | **Bazaar: todos veem os mesmos clientes e a barraca aberta de quem vende** (cópias dos clientes do dono; vendas só no jogo dele) | ✅ Etapa 1 |
| D54 | **Partes que não chegavam**: Bazaar montado à mão; moinho, cogumelos e festivais pelo `JsonUtility` | ✅ Decidido |
| D53 | **Pular logos de abertura** (`[Game] SkipIntroLogos`); ~24 s a menos por abertura | ✅ Decidido |
| D52 | **Pets/cavalo aplicados animal por animal** (o `FromSaveData` do controlador duplicava os pets) | ✅ Decidido |
| D51 | **Bazaar: uma barraca por vez** (trava); "juntos" = repor a barraca do dono (a fazer) | 🟡 Parte feita |
| D50 | **Relógio**: só o menu de pausa pausa (para todos); o resto segue; o tempo segue o mais adiantado (pulos propagam) | ✅ Decidido |
| D49 | **Vigor máximo compartilhado** (SharedSync); **bônus de comida individuais** (perfil); upgrade de ferramenta individual | ✅ Decidido |
| D48 | **Menu de pausa no multiplayer**: "Salvar"/"Carregar" mostram aviso em vez de erro | ✅ Decidido |
| D47 | **Sessão de venda do Bazaar** (ideia do usuário): o relógio corre normal durante a venda (medido), então não precisa pular tempo; "um vende" ou "juntos" sem menu; aviso de barraca aberta | ✅ Decidido |
| D46 | **Bazaar (parte 1)**: deck e peças da barraca como armazéns compartilhados; progresso, tendências e decoração no SharedSync | ✅ Decidido (parte) |
| D45 | **Casamento de convidados**: a rotina de casado não é interceptável (lida direto no código nativo); vale o Plano B da D12a por ora | ⏸ Parcial |
| D44 | **Romance exclusivo**: registro NPC → jogador no host; `CheckMarryable`/`SetFiance` recusam NPC comprometido com outro | ✅ Decidido |
| D43 | **Progresso do mundo, flags de evento (união) e natureza** (minérios, flores, bichinhos) compartilhados; amizade dos pets do mundo | ✅ Decidido |
| D42 | **Estado compartilhado genérico**: gerenciadores com ToSaveData/FromSaveData (moinho, apicultura, cogumelos, vasos, pets, cavalo, ampliações da casa) | ✅ Decidido |
| D41 | **Animais e celeiros**: estado de cuidados de cada animal e ovos/ração de cada celeiro sincronizados (mesclando, sem mexer na posição) | ✅ Decidido |
| D40 | **Fazenda ao vivo**: diferenças dos objetos dos mapas residentes viram operações por objeto; corrige ferramentas "vazias" do perfil do zero | ✅ Decidido |
| D39 | **Correio compartilhado**: estado do `LetterManager` sincronizado (montado à mão: o `JsonToObject` devolve nulo) | ✅ Decidido |
| D38 | **Ferramentas/mochila/vigor por jogador**: upgrade é imediato (já individual); perfil ganha `tooldata` e `stamina` | ✅ Decidido |
| D37 | **Armazém da casa**: uma pessoa por vez (trava no host) + sincronização pelo conteúdo (gzip) | ✅ Decidido |
| D36 | **Carteira compartilhada observando `PlayerMoney`**: convidado manda a diferença, host manda o total | ✅ Decidido |
| D35 | **Espectar enquanto dorme: adiado** (só daria no mesmo mapa; outro mapa é arriscado). Pergunta P8 | ⏸ Adiado |
| D34 | **Levantar da cama** (Backspace ou F9) enquanto alguém ainda está acordado | ✅ Decidido |
| D33 | **Dormir de verdade**: quem dorme não pausa o grupo, o relógio segue para os acordados; corrige a queda do host ao deitar (`UniTask` nulo) | ✅ Decidido |
| D32 | **Aparência própria corrigida e ligada** (`[Visual] OwnAppearance`): a trava era um id de peça inexistente e a cópia com o modelo escondido; gênero diferente funciona | ✅ Decidido |
| D31 | **Prioridade: continuar a aparência própria** (resposta à P4) antes da Fase 4 | ✅ Decidido |
| D30 | **Convidado novo começa do zero** no mundo do host (corações/conversas zerados, mochila vazia, ferramentas básicas, regador vazio; só o visual vem do save dele) | ✅ Decidido |
| D29 | **Aparência própria experimental, desligada por padrão** (`[Visual] SyncAppearance`): trava com peças não carregadas e não troca gênero | ⛔ Substituída (D32) |
| D28 | **Aparência própria** de cada jogador: anunciada pela rede e montada pelo jogo por troca temporária; o convidado reaplica a dele (perfil) | ✅ Decidido |
| D27 | **Interface do multiplayer** (título: modo/nome/IP; jogo: lista com localização; F9 menu) em IMGUI com controles próprios | ✅ Decidido |
| D26 | **Avisos no sistema do jogo** (entrou/saiu, dormiu, desmaiou, evento) reaproveitando o aviso de correio com texto trocado | ✅ Decidido |
| D25 | **Inicialização sem clique**: o mod traz a própria janela para frente se ela ficar presa no Launcher (`StartupGuard`) | ✅ Decidido |
| D24 | **Perfil do convidado**: amizade, conversas, mochila, ferramentas; flags no mundo (provisório); 1ª entrada: ver D30; gravado pelo host na virada | ✅ Decidido (1ª entrada substituída pela D30) |
| D23 | **Save no multiplayer só na virada do dia** (pelo host); sozinho continua livre; snapshot em memória contra queda | ✅ Decidido |

---

## 2. Dados técnicos do jogo

Instalação: `L:\Games\STORY OF SEASONS Grand Bazaar`

| Item | Valor | Implicação |
|---|---|---|
| Engine | **Unity 6** (`6000.0.28f1`) | Ferramentas de modding maduras |
| Backend | **IL2CPP** (`GameAssembly.dll`) | Usar BepInEx 6 IL2CPP + Cpp2IL/Il2CppDumper |
| Metadata | `global-metadata.dat` v31, **sem criptografia** | Dump de classes e métodos funciona direto |
| Código do jogo | `Assembly-CSharp.dll`, `BokuMono.*` | Lógica principal (BokuMono = nome interno da série) |
| Libs embutidas | Newtonsoft.Json, UniTask, UniRx, DOTween, Cinemachine, CriWare | JSON pronto para serializar estado |
| Rede | `com.rlabrecque.steamworks.net.dll` | Steamworks.NET disponível (só com a cópia original da Steam) |
| Rede escolhida | LiteNetLib (IP direto / LAN) | Não depende da Steam (D7) |

---

## 3. Arquitetura

**O host é o dono do mundo** (como no Stardew):

- **Só o host salva.** O personagem de cada amigo fica guardado no save do host e o amigo pode reconectar depois.
- Os clientes **mandam ações** (arar, regar, colher, guardar item, dar presente). O host executa a ação com as **funções originais do jogo** e espalha o resultado (deltas).
- Ao conectar, o cliente recebe um **snapshot** do mundo.
- **Relacionamentos individuais por troca de contexto.** Quando o jogador X age com um NPC, o host:
  1. carrega a tabela de amizade do X na estrutura original do jogo;
  2. roda a lógica original (pontos, reações, eventos);
  3. salva o resultado de volta na tabela do X.

**Stack prevista:** BepInEx 6 (IL2CPP) + HarmonyX (patches) + LiteNetLib (rede) + Newtonsoft.Json (serialização).

---

## 4. Funcionalidades (Stardew → Grand Bazaar)

### Compartilhado
- [ ] Fazenda: tiles, plantações, rega
- [ ] Armazém / baús
- [ ] Moinhos
- [ ] Animais e celeiro
- [ ] **Dinheiro (carteira única)**
- [ ] Bazaar: barraca e caixa compartilhados
- [ ] **Correio** (caixa única do grupo)
- [ ] **Casa** (única, tudo dentro compartilhado)

### Individual (por jogador)
- [ ] Inventário / mochila
- [ ] Ferramentas e upgrades
- [ ] Pontos de coração com cada NPC
- [ ] Flags de eventos de coração
- [ ] Presente do dia / conversa do dia
- [ ] Namoro, noivado, casamento
- [ ] Trava de NPC (D14): NPC ocupado enquanto conversa com um jogador
- [ ] Filhos (por casal)

### Tempo e eventos
- [ ] Relógio controlado pelo host e enviado aos clientes
- [ ] Regra de pausa configurável (D9): pausa quando N jogadores estão em menu ou evento
- [ ] v0.1: N = 1 (pausa para todos)
- [ ] Tratar casos especiais de tempo correndo em menu (loja fechando com o jogador dentro, dia virando durante diálogo etc.)
- [ ] Mudar o padrão para N = todos (estilo Stardew)
- [ ] Dormir desabilita **só o seu personagem** (estado "dormindo", sem controle) até todos dormirem
- [ ] Contador "dormindo 2/4" visível para todos
- [ ] Dia vira quando **todos dormem**
- [ ] Cutscenes pessoais: só quem disparou vê, o mundo segue para os outros
- [ ] Festivais: todos entram juntos, início por confirmação de todos

### Fora do escopo / bônus
- [ ] Casamento entre jogadores (conteúdo novo, dificuldade extrema)
- [ ] Etiqueta "Para: Jogador X" nas cartas pessoais (D10a)
- ~~Tela dividida~~ (descartado: cada jogador no próprio PC)

---

## 5. Riscos (atualizado com o código — ver `CODIGO.md`)

| Risco | Antes | Depois de ver o código |
|---|---|---|
| Como o jogo guarda o estado | ❓ | ✅ **Fácil**: objeto central `UserInfo`, e todo manager tem `ToSaveData()` / `FromSaveData()` |
| Formato da amizade | ❓ | ✅ **Fácil**: tabela simples `Dictionary<NPC, LikeabilityData>` |
| Dinheiro, armazém, correio, Bazaar | Média | ✅ **Fácil**: um manager cada, com métodos claros de adicionar e remover |
| Fazenda | Alta | ✅ **Média**: dados simples com `Guid` e posição; o jogo já atualiza mapas fora da tela |
| Dados por jogador (mochila, ferramentas, amizade) | Alta | 🟡 **Viável**: são singletons, mas `ToSaveData()`/`FromSaveData()` tornam a troca de contexto direta |
| Relógio e pausa (D9) | Alta | 🟡 **Viável**: um `DateManager` global com `Pause()`/`Resume()`; patch no host |
| Mapas e avatares | Média | 🟡 **Média**: o jogo assume 1 player e 1 mapa ativo por vez, então os outros jogadores precisam de avatares "fantasma" |
| Eventos (cutscenes) | Alta | 🟠 **Alta**: `IsPlayingEvent` é global; precisa rodar só para quem disparou |
| **Vários cônjuges e filhos (D12a)** | Muito alta | 🔴 **O maior trabalho**: o jogo guarda um único `PartnerCharaId`, um `MarriageInfo` e um `ChildInfo`, e eventos, rotinas e a casa dependem disso |
| Formato de serialização | ❓ | 🟡 Provavelmente JSON + deflate + XOR; confirmar em runtime |

**🎯 Descoberta importante:** sobrou no código a estrutura de um multiplayer que foi removido: `NetworkSetting` com `MaxMembers = 4`, `MaxGuestMembers = 3`, menu `StartHost/StartGuest`, sincronização de horário e lista do que o convidado pode fazer. O jogo foi pensado para **host + 3 convidados**, o mesmo formato do D6.

**D12a → casa única com eventos de casado desligados para convidados (Plano B como reserva).** O código confirma que o jogo só suporta um cônjuge (`PartnerCharaId`).

**Rotinas de NPC (descoberta):** as rotas **não são programadas em código**, são **dados**. Cada NPC tem uma lista de `NpcSchedule`, e cada entrada tem condições: `Married`, estação, dia da semana, clima, festival, `ChildGrowth`, `HomeUpgradeLevel`, prioridade. As rotas ficam em `NpcSchedulePack.NpcScheduleRoutes`, e o `ScheduleManager` escolhe a entrada que bate com as condições do dia.
- **Reserva (Plano B):** não exige mexer em rota nenhuma. Para quem não é o host, a condição `Married` dá falso, então o NPC segue a rotina de solteiro naturalmente.
- **Caminho escolhido:** os desenvolvedores **já criaram uma rotina de casado para cada NPC casável**, com a rota até a casa do jogador. Bastaria um patch na checagem de `Married` ("casado com *qualquer* jogador") para cada cônjuge usar a própria rota original. Existem `ScheduleBedChecker` / `ScheduleChairChecker`, que podem ajudar a evitar dois cônjuges na mesma cama ou cadeira. O que continua difícil (e fica desligado para convidados no início): eventos de casado, filhos, cerimônia e `FamilyHelpInfo`.
- **A testar:** (1) se a checagem de casado é centralizada ou espalhada; (2) dois cônjuges indo para o mesmo ponto da casa (sobreposição visual); (3) diálogos de casado para convidados.

---

## 6. Roadmap

> **O planejamento detalhado e oficial está em [`ROTEIRO.md`](ROTEIRO.md)** (fases, ordem, critérios de pronto). A tabela abaixo é só o resumo original por versão.

| Versão | Conteúdo | Estimativa |
|---|---|---|
| v0.1 | Conexão via LAN, ver os amigos andando e trocando de mapa | ~1 mês |
| v0.3 | Dinheiro compartilhado, armazém, inventário individual | +1–2 meses |
| v0.5 | Fazenda e animais sincronizados, dia termina quando todos dormem | +2–3 meses |
| v0.7 | Corações e eventos individuais; casamento com todos na casa única (eventos de casado e filhos só para o host) | +2 meses |
| v0.9 | Festivais e Bazaar em grupo | +2 meses |
| v1.0 | Reativar para convidados: eventos de casado, cerimônia, filhos, ajuda na fazenda; casamento entre jogadores como bônus | +2–3 meses |

**Total estimado:** 8–12 meses. O save central e a tabela simples de amizade facilitam; os vários cônjuges continuam caros. Jogável em grupo a partir da v0.5.

---

## 7. Próximos passos

- [x] Gerar as classes do jogo (Cpp2IL → `dump/`, sem tocar na pasta do jogo)
- [x] Mapear classes: save, armazém, amizade, fazenda, relógio, dinheiro, player (→ `CODIGO.md`)
- [x] Revisar o plano com base no código
- [x] Backup dos saves (2 discos) e do jogo original, verificado por SHA-256 (D17)
- [x] Instalar **BepInEx 6 IL2CPP be.791** na pasta do jogo (reversível: ver `backups/CONTEXTO.md`)
- [x] Criar `src/SOSGB.Multiplayer` (plugin `net6.0`)
- [x] Plugin carrega no jogo (2026-10-24): BepInEx inicializou, `DebugOverlay` registrado e a janela apareceu
- [x] Usuário conferiu de olho: a janela aparece e os valores parecem corretos
- [x] Ponte de teste (D20): log de estado, comandos em 127.0.0.1:47800, screenshot (PNG próprio), auto-load de slot, `scripts/dev-cycle.sh`
- [x] Confirmado em runtime pela IA: auto-load do slot 3, dinheiro (69.365 G), data/hora, `DateManager.Pause()`/`Resume()`, `GameController.AddMoney`
- [x] Primeiro patch Harmony (D21): jogo não pausa sem foco
- [x] **Fase 1 concluída** (ver `ROTEIRO.md`): relógio/menus, formato do save, troca de contexto (6 partes), snapshot do mundo, duas instâncias
- [ ] **Retomar aqui:** Fase 2 do `ROTEIRO.md` (rede básica)
- [ ] Plugin "olá mundo": mostrar na tela o dinheiro (`UserInfo.PlayerMoney`), a hora (`DateManager.Now`) e o mapa atual
- [ ] Hook em `SaveDataManager.Encode<T>` para confirmar o formato do save
- [ ] Testar `LikeabilityManager.ToSaveData()` / `FromSaveData()` (base da troca de contexto)
- [ ] Investigar `NetworkSetting` e a UI de rede que sobrou (dá para reativar o menu de host/convidado?)

---

## 8. Avisos

- Modificar o jogo para uso pessoal é aceito pela comunidade. **Distribuir código decompilado do jogo, não.** Um mod publicado deve conter só o código do mod.
- Fazer um snapshot novo dos saves antes de testes arriscados. **Os saves ficam em `%APPDATA%\STORY OF SEASONS Grand Bazaar`, não na pasta do jogo.** Restauração: `backups/CONTEXTO.md`.
