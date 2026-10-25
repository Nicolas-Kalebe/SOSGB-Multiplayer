# DECISÕES — registro completo com contexto

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto **neste arquivo** (formato em [`AGENTS.md`](AGENTS.md)), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. O resumo de uma linha fica na tabela do [`PLANO.md`](PLANO.md).

Regras deste arquivo:
- Decisões **nunca são apagadas**. Se mudar, marque `🔁 Substituída por Dx` e crie uma entrada nova.
- Sempre explique o **porquê**. Uma IA que chegar depois não leu a conversa original.

---

### D1 — Modelo de referência: multiplayer do Stardew Valley
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Contexto:** o jogo é single-player. O remake de 2025 removeu o multiplayer do original de DS (até 3 jogadores visitando fazendas). O usuário quer jogar com amigos.
- **Alternativas:** (a) modo "visita", como no DS: só ver e visitar a fazenda dos outros; (b) co-op completo num mundo único.
- **Decisão:** co-op completo, usando o Stardew Valley como especificação de comportamento.
- **Motivo:** é o modelo de co-op de fazenda que o usuário conhece e quer. Ter uma referência concreta resolve muitas dúvidas de design.
- **Consequências:** projeto grande (estimativa de 8–12 meses). Na dúvida sobre como algo deve se comportar, consultar como o Stardew faz.

### D2 — Mundo compartilhado
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Contexto:** num co-op é preciso definir o que é do grupo e o que é de cada jogador.
- **Decisão:** fazenda, armazém, moinhos, animais e construções são compartilhados.
- **Motivo:** é como no Stardew, e o jogo já guarda esse estado em managers únicos (ver `CODIGO.md`).
- **Consequências:** o host é dono desse estado. Os clientes mandam ações e o host aplica com as funções originais do jogo.

### D3 — Relacionamentos individuais
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Contexto:** inicialmente foi proposto ter corações compartilhados, por ser mais simples. O usuário quer que cada um possa casar com quem quiser.
- **Alternativas:** (a) corações compartilhados, com um único valor por NPC; (b) corações por jogador.
- **Decisão:** (b). Corações, flags de eventos de coração, presente do dia e conversa do dia são por jogador.
- **Motivo:** permite que cada jogador tenha a sua própria história de romance.
- **Consequências:** "troca de contexto" no host: antes de uma ação de um jogador com um NPC, carrega a tabela dele no `LikeabilityManager` (`FromSaveData`), roda a lógica original e salva de volta (`ToSaveData`). O código confirmou que a amizade é uma tabela simples `Dictionary<uint, LikeabilityData>`.

### D4 — Dinheiro compartilhado
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Contexto:** o Stardew permite escolher entre carteira compartilhada e separada.
- **Alternativas:** compartilhado, separado ou configurável.
- **Decisão:** compartilhado, numa carteira única.
- **Motivo:** escolha do usuário. Também é o mais simples, porque o jogo já tem um único `UserInfo.PlayerMoney`.
- **Consequências:** basta sincronizar `GameController.AddMoney` / `SpendMoney` via host.

### D5 — Casamento com NPC exclusivo
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário (sugerido pela IA)
- **Contexto:** com relacionamentos individuais (D3), dois jogadores poderiam namorar o mesmo NPC.
- **Decisão:** quando um jogador começa a namorar um NPC, esse NPC fica indisponível para romance com os outros.
- **Motivo:** é como no Stardew e evita incoerências na rotina e nos eventos do NPC.
- **Consequências:** o host mantém um registro de NPC → jogador parceiro. As ações de romance dos outros jogadores com aquele NPC são bloqueadas.

### D6 — Até 4 jogadores
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário (sugerido pela IA)
- **Decisão:** host + 3 convidados.
- **Motivo:** igual ao Stardew. **O código do jogo confirmou** que existe `NetworkSetting.MaxMembers = 4` / `MaxGuestMembers = 3`, restos de um multiplayer que foi removido.

### D7 — Conexão via LAN
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Contexto:** o mod deve funcionar em qualquer instalação do jogo, sem depender da conta ou do lobby da Steam.
- **Alternativas:** Steamworks P2P, IP direto/LAN, relay próprio.
- **Decisão:** LAN / IP direto (biblioteca prevista: LiteNetLib). Para jogar pela internet, os jogadores usam uma LAN virtual (Radmin VPN, ZeroTier, Tailscale).
- **Motivo:** funciona em qualquer cópia do jogo e não depende da Steam.
- **Consequências:** o mod não precisa de NAT traversal nem de servidor.

### D8 — Sem tela dividida
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Decisão:** cada jogador joga no próprio PC.
- **Motivo:** tela dividida numa Unity com câmera e input únicos seria um projeto à parte.

### D9 — Pausa do relógio configurável
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário (sugerido pela IA)
- **Contexto:** no single-player o tempo para em menus e diálogos. No multiplayer, é preciso escolher entre pausar para todos ou não pausar.
- **Alternativas:** (a) pausa para todos quando alguém abre menu: mais simples, mas trava o dia dos outros; (b) tempo corre em menus, como no Stardew: melhor experiência, mas o jogo pode ter lógica que assume tempo parado.
- **Decisão:** regra "o relógio pausa quando N jogadores estão em menu ou evento". Começa com N = 1 (equivale a a), e o padrão passa a N = todos (equivale a b) depois de tratar os casos especiais.
- **Motivo:** o código das duas opções é quase o mesmo, e N = 1 evita bugs no começo.
- **Consequências:** patch em `DateManager.Pause()` / `Resume()` no host, que envia `Now.Ticks` aos clientes. Casos a tratar com N = todos: loja fechando com o jogador dentro, dia virando durante diálogo etc.

### D10 — Correio compartilhado
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Decisão:** uma caixa de correio para o grupo (`LetterManager` único no host).
- **Motivo:** escolha do usuário. É mais simples que correio individual.

### D10a — Cartas pessoais no correio compartilhado
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** IA (delegado pelo usuário: "se ficar muito difícil pode ser tudo compartilhado, decida você")
- **Contexto:** com relacionamentos individuais (D3), algumas cartas são de um NPC para um jogador específico.
- **Alternativas:** (a) carta só para o destinatário; (b) tudo compartilhado; (c) compartilhado com etiqueta "Para: X".
- **Decisão:** (b) agora: todos leem, e o item anexado vai para quem abrir. A etiqueta (c) fica como melhoria opcional.
- **Motivo:** o mod sabe de qual jogador veio a carta, mas mostrar isso na UI pode exigir mexer em textos fixos. Não vale atrasar o projeto por um detalhe estético.

### D11 — Casa única compartilhada
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Contexto:** o Stardew dá uma cabana para cada farmhand. Criar casas novas exigiria mexer em cenário e mapa, que era o maior risco do projeto.
- **Decisão:** uma casa só, com tudo dentro compartilhado.
- **Motivo:** escolha do usuário. Elimina o maior risco técnico.

### D12 — Dormir individual
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Decisão:** ao deitar, só o personagem daquele jogador fica desabilitado (estado "dormindo") até todos dormirem. Aí o dia vira. Aparece um contador "dormindo X/4".
- **Consequências:** interceptar `PlayerCharacter.ActionSleep()` e só chamar `GameController.StartNextDay()` no host quando todos estiverem dormindo.

### D12a — Cônjuges na casa única; eventos de casado só para o host no início
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Contexto:** o código mostrou que o jogo suporta **um** cônjuge (`LikeabilityManager.PartnerCharaId`), um `MarriageInfo` e um `ChildInfo`. Também mostrou que as **rotinas de NPC são dados** (`NpcSchedule` com a condição `Married`) e que **já existe uma rotina de casado para cada NPC casável**.
- **Alternativas:** (a) Plano B: o cônjuge do convidado mora na casa de origem; (b) todos na casa única, reescrevendo o sistema de família inteiro; (c) todos na casa única usando as rotinas originais, com os eventos de casado e filhos desligados para os convidados.
- **Decisão:** (c), com (a) como reserva.
- **Motivo:** aproveita as rotas que já existem, e as partes caras (eventos, filhos) ficam para depois.
- **Consequências / pendências:**
  - patch na checagem de `Married` / `PartnerCharaId` para "casado com qualquer jogador";
  - para convidados, desligar no início os eventos de casado (`EventMarried`, `EventSpouseBirthDay`, `EventFiance`, `EventWeddingDay`), os filhos, a cerimônia e `FamilyHelpInfo`;
  - **a testar:** se a checagem de casado é centralizada; dois cônjuges indo para o mesmo ponto da casa (ver `ScheduleBedChecker` / `ScheduleChairChecker`);
  - o usuário pediu para tratar os detalhes disso **mais para frente no projeto**.

### D13 — Itens e upgrades individuais
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Decisão:** mochila, ferramentas e upgrades de ferramenta são de cada jogador.
- **Consequências:** troca de contexto também para `BagItemStorage`, `BagToolStorage` e `ToolManager.SaveData`. O armazém da casa (`HouseStorage`) continua compartilhado.

### D14 — NPC fala com um jogador por vez
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Decisão:** enquanto um jogador conversa, dá presente ou está em evento com um NPC, esse NPC fica "ocupado" para os outros.
- **Motivo:** escolha do usuário. Também garante que a troca de contexto da amizade (D3) nunca rode para dois jogadores no mesmo NPC ao mesmo tempo.
- **Consequências:** o host mantém uma trava NPC → jogador e libera ao fim do diálogo ou evento.

### D15 — Autodocumentação obrigatória
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Contexto:** outras IAs podem mexer no projeto no futuro e precisam entender o raciocínio sem ter acesso à conversa original.
- **Decisão:**
  - toda decisão é registrada com contexto neste arquivo;
  - cada pasta tem um `CONTEXTO.md` (pastas geradas por ferramenta são documentadas pela pasta-mãe);
  - todo `.md` abre com o aviso de autodocumentação.
  - Regras completas em `AGENTS.md`, importado pelo `CLAUDE.md`.
- **Motivo:** continuidade entre sessões e entre IAs diferentes.

### D16 — Mapear o código com Cpp2IL antes de instalar o BepInEx
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** IA
- **Contexto:** era preciso conhecer as classes do jogo para validar o plano.
- **Alternativas:** (a) instalar o BepInEx na pasta do jogo e rodar o jogo para gerar os interop assemblies; (b) rodar o Cpp2IL fora do jogo.
- **Decisão:** (b).
- **Motivo:** não altera a pasta do jogo e não precisa abrir o jogo. O Cpp2IL suporta metadata v31 (Unity 6), e o Il2CppDumper tem suporte incerto para essa versão.
- **Consequências:** o dump traz só assinaturas. Comportamentos precisam ser confirmados em runtime com o BepInEx (próximo passo).

### D17 — Estratégia de backup
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário (pediu a cópia do jogo e a garantia dos saves) + IA (locais e verificação)
- **Contexto:** antes de instalar o BepInEx, o usuário pediu para copiar a pasta inteira do jogo e garantir o backup dos saves. Ele achava que os saves ficavam na pasta do jogo, mas **não ficam**: estão em `%APPDATA%\STORY OF SEASONS Grand Bazaar\<SteamID>\`. Isso foi confirmado no código (`SaveDataAccessorWindows`) e no disco.
- **Decisão:**
  - saves copiados para **dois discos**: `backups/saves/` no projeto (C:) e `L:\Backups\SOSGB\saves\`;
  - jogo inteiro copiado para `L:\Backups\SOSGB\jogo_original_2026-10-24\`;
  - tudo verificado por SHA-256.
- **Motivo:** duas cópias dos saves em discos diferentes protegem contra a perda de um disco. A cópia do jogo permite voltar ao estado sem mod com `robocopy /MIR`.
- **Consequências:** procedimento de restauração em `backups/CONTEXTO.md` e em `L:\Backups\SOSGB\LEIA-ME.md`. Fazer um snapshot novo dos saves antes de testes arriscados.

### D18 — Repositório git público e o que não sobe
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário (repo e local) + IA (regras do `.gitignore`)
- **Contexto:** o usuário criou o repositório **público** https://github.com/Nicolas-Kalebe/SOSGB-Multiplayer e pediu para mover o projeto para `Documentos/repositorios/`. Também pediu explicitamente: "só não vai subir os arquivos do jogo por engano".
- **Decisão:**
  - o projeto fica em `C:/Users/<usuário>/Documents/repositorios/SOSGB-Multiplayer`, com branch `main`;
  - o `.gitignore` bloqueia `dump/` (exceto o `CONTEXTO.md`), qualquer arquivo de jogo (`GameAssembly.dll`, `global-metadata.dat`, `*_Data/`, `*.assets`…), `backups/` e `*.sav` (saves pessoais), os binários de `tools/` (baixar pelo link) e a saída do BepInEx;
  - os `.md` versionados não contêm dados pessoais (nome de usuário do Windows, ID Steam, e-mails): o ID Steam aparece como `<SteamID>`.
- **Motivo:** distribuir código ou arquivos do jogo é ilegal e pode derrubar o repositório. Os saves e os caminhos locais são dados pessoais.
- **Consequências:**
  - antes de **todo** commit, conferir `git status` / `git diff --cached --stat` e garantir que nada de `dump/`, `backups/` ou arquivo do jogo foi adicionado;
  - nunca usar `git add -f` nesses caminhos;
  - `dump/` e `backups/` continuam documentados localmente pelos seus `CONTEXTO.md`.

### D19 — Stack do plugin e primeira etapa (validação por leitura)
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** IA (usuário aprovou seguir com o BepInEx)
- **Contexto:** com o design fechado e o código mapeado só por assinaturas, era preciso provar em runtime que conseguimos acessar o estado do jogo.
- **Decisão:**
  - **BepInEx 6 IL2CPP bleeding edge `6.0.0-be.791+b782da4`**, instalado direto na pasta do jogo (o backup do original está em D17);
  - plugin em C# `net6.0` em `src/SOSGB.Multiplayer`, com as referências apontando para `<jogo>/BepInEx/interop` via a propriedade `GamePath`;
  - primeiro componente: `DebugOverlay`, que só lê dinheiro, relógio, mapa e posição do player, sem alterar nada.
- **Alternativas:** MelonLoader (descartado: o BepInEx 6 tem suporte melhor a IL2CPP e Unity 6, e HarmonyX integrado); BepInEx 5 (descartado: não suporta IL2CPP).
- **Motivo:** o bleeding edge é a única linha do BepInEx com suporte a IL2CPP metadata v31 / Unity 6. Começar só com leitura isola problemas de acesso antes de mexer em comportamento.
- **Consequências:** compilar exige o jogo com o BepInEx já executado uma vez. Se o jogo atualizar, é preciso rodar de novo para regenerar o interop.

### D20 — Ponte de teste para a IA testar sozinha
- **Status:** ✅ Decidido (implementação pendente) · **Data:** 2026-10-24 · **Decidido por:** usuário (sugerido pela IA)
- **Contexto:** o usuário perguntou se a IA poderia testar o jogo sozinha, com logs ou com controle do PC. No Claude Code a IA não tem controle de mouse e teclado, e simular teclas é frágil, porque o jogo usa o `Unity.InputSystem`, que costuma ignorar input simulado. Ela consegue abrir e fechar o jogo, ler arquivos e ver imagens.
- **Alternativas:** (a) MCP de terceiros com controle de tela e mouse; (b) screenshot pelo PowerShell + teclas simuladas; (c) "ponte de teste" dentro do próprio plugin.
- **Decisão:** (c). Um componente **só de desenvolvimento** no plugin, desligado por padrão na versão para jogar:
  1. **Log de estado:** grava periodicamente em `BepInEx/sosgb-state.jsonl` (dinheiro, data/hora, pausado, mapa, posição, evento ativo, menu aberto);
  2. **Comandos:** servidor TCP/HTTP **só em `127.0.0.1`** (porta configurável, ex.: 47800), com comandos como `state`, `load <slot>`, `pause`/`resume`, `addmoney <n>`, `field <id>`, `screenshot`, `log <msg>`. A execução acontece na thread principal da Unity (fila consumida no `Update`);
  3. **Screenshot:** `ScreenCapture.CaptureScreenshot` em `BepInEx/screenshots/`;
  4. **Auto-load:** opção de configuração para carregar um slot automaticamente ao abrir, pulando a tela de título.
- **Motivo:** fala direto com o código do jogo, então é confiável e rápido. Fecha o ciclo editar → compilar → abrir → testar → ler resultado sem depender do usuário. Também permite testar o multiplayer com **duas instâncias do jogo no mesmo PC** (host + convidado em `localhost`).
- **Consequências:**
  - liga e desliga pelo `BepInEx/config` (`DevBridge.Enabled`), com padrão desligado em release;
  - só escuta em loopback, nunca em rede;
  - testes de "sensação" de jogo continuam com o usuário;
  - usar sempre um slot de teste, nunca o save principal, e fazer snapshot antes (D17).

### D21 — O jogo não pausa ao perder o foco
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** IA (o usuário reportou o problema durante os testes)
- **Contexto:** testando a ponte (D20), o jogo travava quando a janela saía de foco: a música parava e o carregamento ficava parado. O diagnóstico em runtime mostrou `Time.timeScale = 0` sem foco, causado por `BokuMono.AppManager.OnFocusChanged(false)`. No multiplayer isso congelaria todo mundo quando o host desse alt-tab, e impediria rodar duas instâncias no mesmo PC.
- **Decisão:**
  - patch Harmony `AppManagerPatch`: pula `OnFocusChanged(false)`;
  - componente `FocusGuard`: mantém `Application.runInBackground = true`;
  - configurável em `[Game] KeepRunningWhenUnfocused`, com padrão `true`, porque é necessário para o multiplayer.
- **Verificado em runtime:** sem foco, `timeScale` fica em 1 e o relógio continua andando (06:34 → 06:40 em 5 s).
- **Pendência conhecida:** quando o jogo é **aberto por script** (sem foco desde o início), a tela Launcher não avança até a janela receber uma ativação real do Windows (alt-tab). Mensagens `WM_ACTIVATE` não resolvem, e `AppManager.IsSuspended` continua `false`. A solução é o `scripts/kick-window.ps1`, que ativa a janela `UnityWndClass` e devolve o foco. Para o jogador, que abre o jogo com clique duplo, isso não acontece.

### D22 — Roteiro por fases e política de commit/push
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário
- **Contexto:** o usuário pediu um roteiro com o que vai ser feito e em que ordem, e definiu a regra de git: "sempre comitar. Subir para o GitHub só quando eu pedir".
- **Decisão:**
  - o planejamento oficial passa a ser o [`ROTEIRO.md`](ROTEIRO.md), com 9 fases (0–8) em ordem, critério de pronto e forma de teste para cada uma. O roadmap resumido do `PLANO.md` aponta para ele;
  - **commit local a cada tarefa concluída**;
  - **`git push` somente quando o usuário pedir explicitamente.**
- **Motivo:** o usuário quer controlar o que vai para o repositório público.
- **Consequências:** IAs nunca fazem push por conta própria. Ao terminar uma tarefa, fazem commit e atualizam o "Estado atual" do `ROTEIRO.md`.

### D23 — No multiplayer, o save oficial acontece só na virada do dia
- **Status:** ✅ Decidido · **Data:** 2026-10-24 · **Decidido por:** usuário (proposto por ele, analisado pela IA)
- **Contexto:** o jogo permite salvar a qualquer momento. O usuário gosta disso, mas desconfiou que, no multiplayer, poderia gerar bugs.
- **Análise (por que salvar no meio do dia é perigoso no multiplayer):**
  1. ações "em trânsito" na rede (ex.: colheita pedida ao host) podem sumir ou duplicar se o save sair no meio;
  2. os dados individuais (D3/D13) ficam em contextos separados, e o host precisaria coletar todos de forma consistente no mesmo instante;
  3. jogadores podem estar em conversa, evento, loja ou com NPC travado (D14), o que gera um save que o jogo não espera;
  4. o save original supõe um único jogador parado num menu.

  Na virada do dia todos estão dormindo (D12), sem menus nem eventos e sem nada em trânsito: é o único momento naturalmente consistente. É também o que o Stardew faz.
- **Alternativas:** (a) manter o save livre; (b) save só na virada do dia; (c) save livre com "barreira" (pausar todos e esperar estado limpo).
- **Decisão:**
  - **jogando sozinho (sem rede):** nada muda, o save continua livre;
  - **no multiplayer:** save oficial **só na virada do dia**, feito **pelo host** (os convidados nunca gravam, D2). O "Salvar" do menu fica bloqueado com um aviso;
  - **proteção contra queda:** o host guarda periodicamente um snapshot do mundo **em memória** (`WorldSnapshot`, tarefa 1.5) só para recuperação, sem ser o save oficial;
  - **depois (Fase 8, opcional):** "salvar agora" do host com barreira (opção c).
- **Consequências:** pode-se perder até um dia de jogo se o host travar sem o snapshot de recuperação. Entra na Fase 3 (tarefas 3.5/3.6).

### D24 — Perfil individual do convidado: o que entra, de onde vem e onde fica
- **Status:** ✅ Decidido (provisório nos pontos marcados) · **Data:** 2026-10-25 · **Decidido por:** IA (o usuário pediu para seguir sozinho; os pontos provisórios serão confirmados com ele)
- **Contexto:** o mundo que o convidado recebe é o save do host, com mochila, corações etc. **do host**. Para os dados individuais (D3/D13), o convidado precisa de um "perfil" próprio, guardado pelo host (D2/D23).
- **Decisão:**
  - **partes do perfil:** amizade (`likeability`), conversas (`talk`), mochila (`bag`), ferramentas (`tools`) e estado das ferramentas (`toolstate`);
  - **flags de evento ficam no MUNDO (compartilhadas) por enquanto (provisório):** elas misturam progresso do mundo (ex.: Bazaar) e eventos de coração pessoais, e ainda não sabemos separar por ID. Trocar tudo por jogador poderia reverter o progresso do mundo. Revisar na Fase 6 (eventos de coração);
  - **primeira entrada (provisório, SUBSTITUÍDO pela [D30](#d30--convidado-novo-começa-do-zero-no-mundo-do-host-substitui-a-parte-provisória-da-d24)):** o perfil inicial do convidado era o do **próprio save** que ele tinha carregado antes de entrar. Agora ele começa do zero;
  - **identidade:** `[Net] PlayerGuid`, gerado automaticamente;
  - **armazenamento:** o host guarda os perfis em `%APPDATA%\STORY OF SEASONS Grand Bazaar\SOSGB-MP\profiles_<fazenda>_<host>.json`, gravado **na virada do dia**, junto com o save oficial (D23). O convidado envia o perfil ao dormir, a cada 60 s e ao sair;
  - **virada do dia:** o convidado reaplica o próprio perfil e roda o `OnChangeDate` dos sistemas pessoais (`LikeabilityManager`, `TalkManager(ano)`, `BagItemStorage`).
- **Consequências:**
  - Se o host travar no meio do dia, os convidados perdem o progresso individual desde a última virada. É o mesmo comportamento do mundo (D23).
  - O `OnChangeDate` pessoal roda no convidado, não no host. Validar na Fase 4/6 que os contadores diários (presente/conversa) zeram certo.

### D25 — O próprio mod ativa a janela do jogo na inicialização
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (o usuário pediu: "tenha certeza de não deixar o jogo sem rodar [...] por causa do bug que o jogo só inicializa minimizando ou clicando")
- **Contexto:** com o BepInEx, o console abre **antes** da janela do jogo e fica com o foco. A janela da Unity nasce inativa e a tela inicial (Launcher) não avança até uma ativação real do Windows (clique/alt-tab). Mandar `WM_ACTIVATE` não resolve (D21). Antes, o contorno era o `scripts/kick-window.ps1`, que só existia nos testes da IA. Para os jogadores, o problema continuava.
- **Experimento (em runtime, de dentro do jogo):** "só mostrar" (`ShowWindow`) não destrava; **trazer para frente** (toque de ALT + `ShowWindow(RESTORE)` + `BringWindowToTop` + `SetForegroundWindow`) **destrava**. Funciona porque o processo do jogo é dono do console que está em primeiro plano, e o Windows deixa ele trocar o foco.
- **Decisão:** componente `StartupGuard`. Enquanto o jogo estiver no Launcher, a cada 4 s, se a janela da Unity não estiver em primeiro plano, ela é trazida para frente. Minimizar/restaurar fica só como reserva. Configurável em `[Game] FixStartupFocus` (padrão `true`).
- **Verificado:** abrindo por script, sem nenhuma ajuda externa, o jogo chega ao título e ao save com **1 ativação**, inclusive com duas instâncias. Os scripts de teste não usam mais o `kick-window.ps1`.

### D26 — Avisos do multiplayer no sistema de avisos do próprio jogo
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (pedido do usuário: "aviso de que um player se conectou, ou desmaiou, ou entrou em um evento, aparecendo no próprio sistema de avisos do jogo, igual o de que um item ficou pronto")
- **Contexto:** o jogo tem as "informações laterais" (`UISideInfomation*`), usadas para correio, item pronto e festival. Elas só aceitam textos traduzidos por ID (`GetInfomationTextInfo()` → id + tabela + argumentos).
- **Alternativas:** (a) janela própria (IMGUI), que destoa do jogo; (b) injetar uma classe nova de `ISideInfoData` no IL2CPP, complexo e frágil; (c) reaproveitar um aviso nativo e trocar o texto.
- **Decisão:** (c). `GameNotify.Show(texto)` pede `UIAccessor.RequestShowSideInfoPost(9731)`, uma contagem-marcador, e um postfix em `UISideInfomationModerate.SetData` troca o texto pelo nosso. Fica com a animação, o som e a posição nativos, com o ícone de carta. Fora da cena de jogo cai no `Notice` do overlay.
- **Avisos atuais:** entrou/saiu do mundo, conectado ao mundo de X (depois da recarga), conexão perdida, foi dormir (X/Y), desmaiou de cansaço/sono, entrou em um evento. Cada jogador detecta os próprios acontecimentos lendo o estado do jogo (sem patch) e avisa os outros (`PlayerEvent`).
- **Verificado:** screenshot com o aviso nativo; entrada e dormir testados com 2 instâncias.

### D27 — Interface do multiplayer em IMGUI com layout e controles próprios
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (pedido do usuário: "crie uma interface de host e entrada")
- **Contexto:** até aqui, hospedar e entrar só pelo `.cfg`. O usuário quer uma interface. A UI nativa do jogo (`UIManager`/páginas) exigiria criar páginas novas no sistema de UI do jogo, com prefabs e localização: muito trabalho e risco.
- **Decisão:** interface IMGUI própria (`MultiplayerUI`):
  - **título:** painel no canto inferior direito com nome, modo (Sozinho/Hospedar/Entrar), IP do host e os IPs deste PC (identificando o Radmin VPN e escondendo adaptadores virtuais). Grava direto no `.cfg`;
  - **no jogo:** lista compacta de jogadores com **onde cada um está** (nome traduzido do mapa, `Locations`), ping e "dormindo X/Y";
  - **F9:** menu com hospedar agora, entrar agora e desconectar. **F10** esconde a lista;
  - o overlay de diagnóstico (`DebugOverlay`) só aparece no modo de teste.
- **Achado importante — o IMGUI deste jogo foi "podado" no build:** `GUILayout.*`, `GUI.TextField`, `GUI.DrawTexture` e `GUIStyle.background` não funcionam ("Method unstripping failed" ou sem efeito). `GUI.Button` existe, mas não reage. Funcionam `GUI.Box` (estilo padrão), `GUI.Label`, `GUI.color` e `Event.current`. Por isso o layout é manual, os botões e o campo de texto leem o clique e as teclas de `Event.current`, e o fundo é `GUI.Box` tingido.
- **Verificado:** clique real no botão "Entrar" e digitação real no campo de IP (gravados no `.cfg`); F9 abre o menu no jogo; lista mostra "Juia — Casa (aqui)".
- **Limitações conhecidas:** cliques no painel também chegam ao jogo (no título, avança o "aperte qualquer botão"); digitar no campo não bloqueia o jogo de receber as teclas. Aceitável por enquanto.

### D28 — Aparência própria de cada jogador (montada pelo próprio jogo, por "troca temporária")
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (pedido do usuário: consertar os bugs visuais e "criar o personagem entrando no mundo"; antes, todos apareciam com a cara de quem estava olhando)
- **Contexto:** a aparência é uma lista compacta de `AvatarInfo` (rosto, pele, olhos, cabelo, cor, barba, roupa, `MotionType`=gênero…). Só o `AvatarMakeManager` sabe montar o modelo, e ele trabalha sobre o modelo do **jogador local**.
- **Experimento (AppearanceLab, em runtime):** trocar temporariamente as `AvatarInfos` do jogador local → `ChangePlayerAvatar` → copiar o modelo → devolver as `AvatarInfos` originais → `ChangePlayerAvatar`. **A cópia continua válida e desenhada depois da devolução**, e o jogador local volta ao normal.
- **Decisão:**
  - cada jogador anuncia a própria aparência (`PlayerAppearance`, "categoria:masterId,…") ao entrar e quando alguém entra; reanuncia se mudar (ex.: alfaiate);
  - quem recebe monta o avatar pelo truque acima (`AppearanceSync`, uma montagem por vez, avançando a corrotina do jogo até o fim, mais `SwitchGenderMotion` pelo `MotionType`);
  - até a aparência chegar, o avatar é provisório (cópia do modelo local);
  - **o convidado herda a aparência do host ao carregar o mundo** (ela está no save). Por isso a aparência virou uma parte do perfil individual (D24: `appearance`), reaplicada no personagem local depois de cada carga de mundo. Na 1ª entrada vem do save do próprio convidado;
  - controller de animação: mesmo `MotionType` do jogador local → o controller dele; diferente → cópia própria (`Instantiate`) do controller capturado na montagem.
- **Efeito colateral aceito:** durante a montagem (poucos segundos, quando alguém entra), **o personagem local troca de aparência e volta**. Nunca acontece durante a virada do dia.
- **Corrigido junto:** antes do `Welcome` o convidado achava que o próprio ID era 0 (o do host) e descartava mensagens do host. Agora o ID começa "desconhecido" (255), o convidado não envia nada até o `Welcome`, o host valida o ID do remetente, e o "entrou no mundo" só sai depois do `Welcome` (sumiu o avatar fantasma "Jogador 255").
- **Verificado:** screenshots dos dois lados com aparências diferentes (variante de cabelo/roupa aplicada no convidado). **Ainda não testado:** gênero diferente.

### D29 — Aparência própria (D28) fica EXPERIMENTAL e desligada por padrão
- **Status:** ⛔ Substituída pela [D32](#d32--aparência-própria-corrigida-e-ligada-por-padrão-substitui-a-d29) (a causa foi achada e corrigida) · **Data:** 2026-10-25 · **Decidido por:** IA (trabalhando sozinha; o usuário pediu "algo bem feito e funcional")
- **Contexto:** a D28 funcionou nos testes com peças **padrão** (cabelo 45000, roupa 49000) e com o mesmo gênero. Testes seguintes mostraram dois problemas graves:
  1. **Peças que não estão carregadas na memória** (ex.: rosto 42005, cabelo 45020, cor 46003, roupa 49040): a corrotina `ChangePlayerAvatar` espera para sempre numa etapa interna e o **personagem local fica invisível**. `PreloadAvatarSource` e `DetailPageResourceLoad` terminam em 0,1 s e não resolvem. O teste que isolaria qual peça causa isso foi **interrompido por falta de memória** no PC (duas instâncias do jogo + sistema, em 15 GB).
  2. **Gênero diferente:** trocar `MotionType` em jogo não reconstrói o corpo (modelo invisível). Aplicar a aparência/gênero no save recebido antes da cena trava o carregamento (personagem nulo), **mesmo com o mesmo gênero**.
- **Decisão:** `[Visual] SyncAppearance = false` por padrão. Desligado: avatares com o modelo do jogador local + **nome em cima** (estável, testado). O código da D28 fica, para retomar.
- **Ainda vale mesmo desligado:** se outro gênero for montado, usa o corpo do gênero local (fallback); a aparência própria **nunca** é aplicada no save antes da cena.
- **Próximos passos para retomar:** (a) isolar qual peça trava (rodar uma instância por vez para não faltar memória); (b) descobrir como o jogo carrega as peças da tela de customização/alfaiate (provavelmente `AllResourceLoadAsync`/`InitializeBackyardLoader`); (c) montar com tempo limite **sem** aninhar a corrotina do jogo (hoje o tempo limite não dispara porque o `yield` aninhado nunca volta).

### D30 — Convidado novo começa DO ZERO no mundo do host (substitui a parte provisória da D24)
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** usuário (resposta à P1: "o amigo começa do zero no save do host [...] Não importa o que ele jogou no mundo dele sozinho, só importa o que vocês jogaram juntos. Tipo Minecraft", "igual no Stardew")
- **Contexto:** pela D24, o convidado entrava pela 1ª vez com a mochila, as ferramentas e os corações do **próprio save**. Isso deixava trazer progresso de fora para o mundo do host (ex.: ferramentas melhoradas, casamento de outro save).
- **Decisão:** o progresso do convidado **só existe no mundo do host**. Quando o host não conhece o jogador (`ProfileData` vazio, ou sem resposta em 8 s), o convidado monta um perfil "do zero" (`Net/FreshProfile.cs`) a partir dos dados do mundo recém-carregado:
  - **corações:** todos os contadores de `LikeabilityDatas`/`YesterdayLikeabilityDataList` zerados, sem noivado/casamento e sem a lista de "já conversou". Os **tetos de coração** (`LikeabilityLevelCapDatas`) ficam, porque vêm do progresso do mundo;
  - **conversas:** todas as listas zeradas, inclusive as de `TalkCond` (as falas de "primeiro encontro" voltam);
  - **mochila:** vazia. Ela tem espaços **fixos** (ex.: 32, os vazios com `ItemId` 0); esvaziar a lista quebraria a mochila. Por isso cada espaço vira vazio e o tamanho é mantido;
  - **ferramentas:** as **mesmas do host**, mas no **nível básico**, descendo a cadeia de `ToolMasterData.UpgradeToolId` até o primeiro nível que existe como item (ex.: 704→701, 603→601, 102→100, 503→501; corrigido na [D40](#d40--fazenda-ao-vivo-operações-por-objeto-nos-mapas-residentes): 700/600/500 não são itens). Sem ferramentas o convidado não consegue jogar, e o jogo não tem uma lista "ferramentas iniciais" fácil de ler;
  - **regador:** vazio;
  - **aparência:** a do personagem do save do próprio convidado. É o único dado aproveitado: é "o boneco" dele, não progresso.
- **Verificado:** comando de teste `ctx fresh` (uma instância só): corações zerados, mochila vazia com 32 espaços, ferramentas no nível básico, regador vazio. O jogo seguiu normal (relógio, personagem, evento da manhã).
- **Consequências:**
  - O tamanho da mochila é o do host (se o host já aumentou a mochila, o convidado começa com ela maior). Aceito por ser simples e não atrapalhar.
  - Quem já jogou com a versão anterior e tem perfil guardado no host **mantém** o perfil (o host manda o perfil guardado e ele vence).
  - Falta testar com rede de verdade (duas instâncias ou com os amigos).

### D31 — Prioridade: continuar a aparência própria de cada jogador
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** usuário (resposta à P4: "P4 continua na aparência")
- **Contexto:** a aparência própria (D28) ficou experimental e desligada (D29) porque peças não carregadas travam a montagem e a troca de gênero não funciona.
- **Decisão:** seguir investigando a aparência antes da Fase 4 (economia). Testes com **uma instância por vez**, por causa da memória (P5).

### D32 — Aparência própria corrigida e LIGADA por padrão (substitui a D29)
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (seguindo a D31: "P4 continua na aparência")
- **Contexto:** a D29 desligou a aparência própria porque algumas peças "travavam" a montagem (personagem invisível) e a troca de gênero "não funcionava". Para achar a causa, foi criada uma sonda (`CoroutineProbe`): ela avança a corrotina `ChangePlayerAvatar` do jogo passo a passo, descendo nas corrotinas aninhadas, com tempo limite de verdade e registro de onde parou. Testes com **uma instância só** (por causa da memória, P5).
- **O que a sonda mostrou:**
  1. **Peças válidas nunca travam**, mesmo sem estar carregadas: cabelo 45005, rosto 42005, cor 46003 e roupa 49040 montam em ~0,1 s, sem pré-carregamento.
  2. **A causa do travamento era um id inexistente:** o cabelo **45020 não existe** (os cabelos vão de 45000 a 45019, mais os de DLC 80600+). Com ele, o carregamento nunca termina.
  3. **O jogo esconde o modelo por ~1–2 s** depois da troca, enquanto carrega as peças novas (0 renderers ligados), e depois mostra de novo sozinho. A montagem antiga **copiava o modelo nesse intervalo**, por isso o avatar remoto saía invisível.
  4. **Gênero funciona:** `ChangePlayerAvatar` com `MotionType` 52001 troca o corpo, e `SwitchGenderMotion(1)` troca as animações (`fix_mot_Mn_*` → `fix_mot_Fn_*`). O modelo fica visível e anda normalmente.
  5. O modelo separado da tela de customização (`CreateMakeAvatarModel` + `CreateAvatar`) **não serve**: só aplicou o cabelo (sem cor nem roupa) e escondeu o jogador local. A troca temporária continua sendo o caminho.
- **Decisão:**
  - `AppearanceSync.Sanitize`: peça com id inexistente (ou de outra categoria) vira a peça do jogador local nessa categoria;
  - a montagem usa o `CoroutineProbe` (tempo limite de 15 s que funciona) e **espera o modelo reaparecer** (algum renderer ligado por 0,3 s) antes de copiar;
  - se falhar, tenta de novo com as **roupas** (chapéu, acessório, roupa, bolsa, planador, montaria) do jogador local, pensando em peças de DLC/loja que o outro PC não tenha. Se falhar de novo: o avatar remoto fica com o modelo provisório (cópia do local + nome em cima) e não tenta de novo;
  - **gênero diferente liberado**: o avatar remoto recebe uma cópia do controlador de animação do gênero dele (+ `Animator.Rebind()`);
  - nada de montar durante eventos/cenas (o jogador local aparece neles) nem na virada do dia; uma montagem interrompida por recarga do mundo é descartada;
  - opção nova **`[Visual] OwnAppearance = true`** (ligada). A chave antiga `SyncAppearance` ficou `false` nos `.cfg` já criados; por isso a chave mudou de nome.
- **Verificado (uma instância):** "jogador falso" (`applab build`) com outro gênero, cabelo, cor e roupa: cópia visível com animação feminina, jogador local devolvido intacto. Convidado aplicando a própria aparência (`applab set`) com o cabelo inválido 45020: a peça foi trocada pela local e o resto aplicado, inclusive o gênero.
- **Consequências:**
  - Ao montar a aparência de quem entrou (ou de quem trocou de roupa), **o seu personagem pisca** com outra aparência por cerca de 1 a 2 segundos. É o custo da troca temporária. Acontece pouco: na entrada de alguém e quando alguém troca de visual.
  - **Falta o teste em rede de verdade** (duas instâncias ou com os amigos). Não foi feito por falta de memória (4 GB livres com o jogo fechado; cada instância usa ~2 GB).

### D33 — Dormir de verdade: quem dorme não pausa o grupo e o relógio continua para os acordados
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (achados do teste P2, pedido pelo usuário)
- **Contexto:** o teste P2 (dormir pela cama de verdade, com duas instâncias) achou três problemas que o comando de teste `sleep` não mostrava:
  1. **O host caía ao deitar.** O patch de `GameController.StartNextDay` devolvia `default` para um `UniTask`. No interop IL2CPP o `UniTask` é uma **classe** (struct não-blittable), então `default` = nulo e o jogo morria com `NullReferenceException` no retorno.
  2. **Quem ficava acordado congelava.** Ao deitar, o jogo para o relógio local (estado de sono). O mod contava isso como "jogador em menu" e, pela regra da D9 (N = 1), pausava o grupo. A pausa do grupo força `DateManager.IsUpdateEnabled = false`, o que **também trava o personagem** dos outros. Resultado: ninguém conseguia chegar à cama (impasse).
  3. O nome dos outros jogadores aparecia **por cima** das janelas e diálogos do jogo.
- **Decisão:**
  - o patch devolve `UniTask.CompletedTask` (nunca `default`);
  - quem está dormindo **não conta** como pausado para a regra do grupo (`ClockSync.PausedForGroup`);
  - enquanto alguém dorme esperando os outros, o relógio do mundo **continua** para quem está acordado (como no Stardew). No host isso é essencial, porque ele é a fonte da hora de todos;
  - nomes acima dos avatares somem enquanto o jogador local está em menu, diálogo ou evento.
- **Verificado (duas instâncias):** host deita pela cama (`ActionSleep` → `GameController.StartNextDay`, sem cair); a convidada continua andando e o relógio corre (11:37 → 11:45); ela deita pela cama; o dia vira para os dois (dia 24, 06:18). Mochila, aparência e animações de cada um se mantêm depois da virada. A convidada vê o host deitado na cama (animação sincronizada).
- **Consequências:**
  - A pausa por menu (D9, N = 1) continua congelando os outros jogadores, não só o relógio. Isso vale para menus e eventos. Ver pergunta P7.
  - Junto: os perfis e snapshots do **modo de teste** agora vão para `SOSGB-MP\dev`, longe dos perfis reais.

### D34 — Levantar da cama enquanto alguém ainda está acordado
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** usuário pediu ("a opção de levantar da cama se alguém ainda estiver de pé, essa é importante"); a forma foi escolhida pela IA
- **Contexto:** pela D12, quem deita fica esperando os outros. Sem um jeito de levantar, quem deitou cedo fica preso até todos dormirem.
- **Decisão:** deitado esperando os outros, o jogador aperta **Backspace** (ou usa o botão "Levantar da cama" no menu F9). O mod desfaz o estado de sono e avisa o host (`SleepReady = false`), e o contador "dormindo X/Y" diminui. Não dá para levantar depois que o host começou a virar o dia (todos já dormiam).
- **Como desfazer o sono (achado testando):**
  - só `PlayerCharacter.ActionWakeUp()` faz o personagem levantar, mas **não devolve o controle**: o estado de movimento fica em `ForceCleanMove`;
  - também é preciso `IsSleep = false` e `IsMoveInputEnabled = true`, além de `GameController.SetWaitChangeDate(false)` (o jogo marca "esperando a virada" ao deitar) e `GameController.Resume()`;
  - o personagem levanta em cima da cama e sai andando normalmente. Teleportar para o lado da cama não teve efeito e foi tirado.
- **Verificado:** deitar → levantar → deitar → levantar pela cama de verdade; levantar pela tecla Backspace de verdade; o personagem anda depois. Testado no host com o "jogador acordado fantasma" (`SOSGB_TEST_FAKE_AWAKE=1`, só testes), que segura a virada com uma instância só.
- **Consequências:** o convidado usa o mesmo caminho (as mesmas chamadas no personagem local + `SleepReady = false`), ainda não testado em rede.

### D35 — Espectar quem está acordado: adiado
- **Status:** ⏸ Adiado · **Data:** 2026-10-25 · **Decidido por:** IA (o usuário pediu "se não for difícil; se for muito difícil pode deixar quieto")
- **Contexto:** a câmera do jogo aceita outro alvo (`CameraManager.Instance.FollowingCamera.SetTarget(GameObject)`), mas cada jogo só carrega o **mapa onde o próprio jogador está**. Quem dorme está na casa; quem está acordado normalmente está na fazenda, na cidade etc. (outros mapas), e ali não existe avatar para seguir.
- **Alternativas:** (a) espectar só no mesmo mapa: fácil, mas inútil (quem está na casa você já vê); (b) carregar o mapa do outro jogador no PC de quem dorme: troca de mapa tira o personagem da cama e mexe no estado de sono e na virada do dia (no host, a virada roda no PC dele). Arriscado e trabalhoso.
- **Decisão:** não fazer agora. O painel já mostra **onde cada um está** (nome do mapa). Pergunta P8 para o usuário.

### D36 — Carteira compartilhada observando o valor (não interceptando funções)
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (implementa a D4, decidida pelo usuário)
- **Contexto:** a D4 previa interceptar `GameController.AddMoney`/`SpendMoney`. Mas o dinheiro também muda por outros caminhos (vendas do Bazaar, prêmios, eventos), e cada um precisaria de um patch.
- **Decisão:** cada jogo **observa** o próprio `UserInfo.PlayerMoney` a cada quadro:
  - convidado: se o valor mudou por conta própria, manda a **diferença** ao host (`MoneyDelta`). A mudança local vale na hora (a loja não espera a rede);
  - host: aplica a diferença na carteira oficial (nunca abaixo de 0) e manda o **total** a todos (`MoneyState`), também quando o próprio host ganha ou gasta, quando alguém entra e a cada 5 s;
  - convidado ao receber o total: adota o total **mais** qualquer mudança local que ainda não tinha saído (evita perder uma compra feita no mesmo quadro);
  - depois de carregar um mundo (entrada, virada do dia), a base é zerada: o valor do snapshot não conta como diferença.
- **Verificado (duas instâncias):** convidada +500, host +1000, convidada −300, os dois gastando juntos (−200 e −100): os dois ficaram sempre com o mesmo valor. Depois da virada do dia o valor continua igual nos dois.
- **Consequências:**
  - Lojas, vendas e prêmios entram sem código extra (4.5 sai junto; falta o teste numa loja de verdade).
  - Limite conhecido: se dois jogadores gastam ao mesmo tempo mais do que existe, o host corta em 0 e uma das compras sai "de graça". Aceito entre amigos.
  - Protocolo 10 (`MoneyState` 15, `MoneyDelta` 26).

### D37 — Armazém da casa: uma pessoa por vez + sincronização pelo conteúdo
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (implementa a D2/ROTEIRO 4.2: armazém compartilhado)
- **Contexto:** com itens, dois jogadores mexendo ao mesmo tempo podem **duplicar ou perder** coisas. No Stardew, só uma pessoa abre um baú por vez.
- **Decisão:**
  - **trava:** o host guarda quem está com a tela do armazém aberta (`StorageHolder`). A abertura é vista pela tela atual (`UIManager.UIMenuKey == HouceStorage`, 102). Se outro jogador abrir, o mod fecha a tela dele (`UIManager.CloseMenu`) e mostra "X está usando o armazém. Espere fechar." (`ForceBackUIMain` deu `KeyNotFoundException` e ficou só de reserva);
  - **conteúdo:** cada jogo confere o armazém a cada 0,5 s (assinatura FNV dos espaços: item, quantidade, qualidade, frescor, embrulho). Mudou por conta própria (guardou, pegou, cozinhou com ingredientes de lá)? Manda o armazém inteiro (`ToSaveData` em JSON, comprimido com gzip: ~2 KB) ao host, que aplica (`FromSaveData`) e repassa aos outros. Ao fechar a tela, a versão final sai na hora;
  - **conflito:** se o armazém muda num jogo enquanto outro está com a tela aberta, vale o de quem está com ela. O host recusa armazém de quem não tem a trava e devolve o oficial;
  - depois de carregar um mundo (entrada, virada do dia) a base é zerada, porque o snapshot já traz o armazém.
- **Verificado (duas instâncias):** convidada guarda → aparece no host; host tira → some na convidada; host com o armazém aberto → a tela da convidada fecha com o aviso; trava solta ao fechar; convidada com a trava guarda e fecha → host recebe; depois da virada do dia os dois ficam idênticos (mesma assinatura).
- **Consequências:**
  - Com a regra D9 atual (N = 1), quem está com o armazém aberto também congela os outros. Ver P7.
  - Outros armazéns compartilhados (moinhos, peças do Bazaar, deck) ainda não sincronizam. Ficam para quando forem usados (Fases 5 e 7).
  - Protocolo 11 (`StorageHolder` 16, `StorageData` 27, `StorageUse` 28).

### D38 — Upgrades e estado das ferramentas, mochila e vigor máximo por jogador
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (ROTEIRO 4.4, seguindo a D13: ferramentas individuais)
- **Contexto / achados no código do jogo:**
  - o upgrade de ferramenta é **imediato** (`ToolManager.GradeUpTool` / `BagToolStorageManager.GradeUpTool` trocam o item na hora; não existe "ferramenta no ferreiro por N dias"). A ferramenta melhorada fica na mochila de ferramentas do jogador, que já é do perfil (D24). O dinheiro (D36) e os materiais (mochila do perfil ou armazém, D37) já sincronizam;
  - o tamanho da mochila fica na própria lista de espaços dela (`BagItemSaveData.itemDatas`), que também é do perfil: upgrade de mochila já é individual;
  - porém dois dados individuais ficavam no mundo e o convidado herdava os do host a cada virada: `UserInfo.ToolSaveDatas` (um bloco cifrado de ~38 bytes por tipo de ferramenta) e o **vigor máximo** (`GameController.PlayerMaxHP`);
  - `ReinForceData` (reforços de canteiros) e `ExpansionData` (ampliações) são da fazenda: continuam do mundo.
- **Decisão:** duas partes novas no perfil:
  - `tooldata`: `ToolManager.SetToolSaveData()` / `FromSaveData(ToolSaveData[])`. Cada item vira um JSON numa lista, porque o `ToJson` do jogo não aceita o array IL2CPP direto;
  - `stamina`: o vigor máximo. Ao aplicar, o vigor atual fica **na mesma proporção** do que o mundo trouxe, respeitando a recuperação da noite que o jogo calculou.
- **Verificado:** captura e aplicação das duas partes numa instância (`ctx save`/`ctx load`).
- **Consequências:** no "começar do zero" (D30), essas duas partes vêm do host como estão (o vigor máximo do novo jogador é o do host). Ajustar se o usuário achar que deveria começar menor.

### D39 — Correio compartilhado sincronizando o estado do LetterManager
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (implementa a D10/D10a, decididas antes)
- **Contexto:** o correio é o `LetterManager` (cartas na caixa, cartas já pegas, lidas, anexo recebido, histórico). Cada jogo tinha a sua cópia: o convidado pegava as cartas e o anexo, mas a caixa do host continuava cheia (anexo duplicado), e na virada do dia tudo voltava ao do host.
- **Decisão:** como o armazém (D37), mas sem trava: cada jogo confere o JSON do `ToSaveData` a cada 1 s; mudou por conta própria, manda o estado inteiro comprimido (~2,5 KB) ao host, que aplica e repassa aos outros. Vale a última mudança (duas pessoas no correio no mesmo segundo é raro).
- **Achado:** o `JsonToObject` do jogo devolve **nulo** para `LetterManager.SaveData` (a carta, `InBagLetterData`, não tem construtor vazio). O mod monta o `SaveData` à mão a partir do JSON, com os construtores do jogo. Verificado: ida e volta do JSON idêntica (16.939 caracteres).
- **Verificado (duas instâncias):** carta nova chega na caixa do host → aparece na da convidada; a convidada pega → some da caixa do host e os dois ficam com as mesmas cartas (133). Sem mensagens repetidas.
- **Consequências:** a etiqueta "Para: jogador X" (D10a, opção c) continua como melhoria opcional (ROTEIRO 8.4).

### D40 — Fazenda ao vivo: operações por objeto nos mapas residentes
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (ROTEIRO 5.1/5.2)
- **Contexto / achados (laboratório `farm`, uma instância):**
  - cada mapa carregado é um `FieldCache` com objetos por GUID (terra arada, plantas, árvores, pedras/mato, grama, flores, sprinklers, ovos);
  - o jogo mantém alguns mapas **sempre na memória** ("residentes", `FieldManager.fieldCacheDict`; hoje o mapa 1, a área externa com a fazenda) e processa o dia neles direto. Mexer no cache residente com o jogador em outro mapa funciona: os objetos aparecem quando ele volta;
  - remover (`Unregister`) e criar com as funções de carga do jogo (`CreateCrop(cache, dados, pai)` etc.) funciona ao vivo, na tela. O serializador oficial (`CreateData` + `DecodeObjectSaveData`) dá os dados completos de tudo em ~2,5 ms.
- **Alternativas:** (a) "ação → host → resultado" (patch em cada ação: arar, plantar, regar, colher, quebrar...), com dezenas de caminhos; (b) **comparar o estado** e mandar as diferenças.
- **Decisão:** (b). Quem está num mapa residente compara, a cada 0,5 s, os objetos com a última versão sincronizada e manda operações (`FieldOps`): criar/alterar = dados completos, remover = GUID. Convidado → host; o host aplica e repassa. Todos aplicam no cache daquele mapa, **estejam onde estiverem**. Antes de aplicar no mapa atual, as mudanças locais pendentes saem primeiro. Partes "escravas" de objetos grandes (árvores) vêm junto com o mestre. Na virada do dia, todos recarregam o mundo do host (que já tem tudo).
- **Verificado (duas instâncias):** remoções pela ponte nos dois sentidos; convidada fora do mapa recebe no cache residente e vê ao voltar; **ações reais**: o host ara com a enxada (canteiros 38 → 39 nos dois) e colhe uma soja com Enter (plantas 38 → 37 nos dois; 92 bytes).
- **Achado no teste — bug da D30 corrigido:** na tabela de ferramentas existem entradas internas de nível 1 (200, 500, 600, 700) que **não são itens**. O "começar do zero" descia até elas, e o convidado ficava com vara, regador, foice e enxada como **espaços vazios** na barra. Agora a descida para no primeiro nível que existe como item (701, 601, 201, 501; machado 100). Verificado na tela: as 8 ferramentas aparecem.
- **Mapas não residentes (montanhas, cidade, celeiro...), acrescentado depois:** as operações valem entre quem está no mesmo mapa; quem não está com ele carregado ignora. Ao **sair** de um mapa não residente em que algo mudou, o jogador manda o mapa inteiro (`FieldSnapshot`: o `FieldSaveData` que o jogo acabou de gravar) e os outros instalam no save de mapas (inclusive o host, que processa o dia com ele). Achado: `FieldSaveManager.SaveDataArray` devolve uma **cópia** a cada chamada; instalar exige gravar o array com `CopyData`. Verificado em rede: a convidada tira 2 objetos nas montanhas e sai; o host entra depois e vê 91 em vez de 93.
- **Ovos do celeiro:** o jogo recria os ovos a partir do estado das galinhas toda vez que alguém entra no celeiro. Sincronizar só o objeto não basta; fica com os animais (5.3).
- **Consequências / limites:**
  - Animais e itens soltos no chão ficam de fora por enquanto (5.3).
  - Conflito (dois mexendo no mesmo objeto no mesmo meio segundo): vale a última operação que o host processou.
  - Protocolo 13 (`FieldOps` 30).
  - Para testar ações reais sem mouse: `Patches/InputInjector` (só modo de teste) injeta a intenção "usar ferramenta" (comando `tool`), e `equip <tipo>` põe a ferramenta na mão. O clique sintético não chega ao jogo.
