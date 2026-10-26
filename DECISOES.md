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
- **Status:** ⛔ Regra N substituída pela [D50](#d50--relógio-do-grupo-só-o-menu-de-pausa-pausa-o-tempo-segue-o-mais-adiantado-substitui-a-regra-n-da-d9) (só o menu de pausa pausa) · **Data:** 2026-10-24 · **Decidido por:** usuário (sugerido pela IA)
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
- **Status:** ⏸ Revisar (pergunta P9): o motivo técnico (troca de contexto no host) deixou de existir, porque cada jogo conversa com a própria cópia do NPC · **Data:** 2026-10-24 · **Decidido por:** usuário
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
- **Decisão (o vigor máximo mudou na [D49](#d49--vigor-máximo-compartilhado-bônus-de-comida-individuais-substitui-parte-da-d38): agora é compartilhado):** duas partes novas no perfil:
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

### D41 — Animais da fazenda e celeiros: estado de cuidados sincronizado
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (ROTEIRO 5.3)
- **Contexto / achados:**
  - cada animal da fazenda é um `FarmAnimalParameter` com `ToSaveData`/`FromSaveData` próprios. O save mistura **cuidados** (afeição, carinho/escova/sino do dia, comida, produto, gravidez, sinais) com **movimento** (posição, rotação, ordens, rotas), que muda o tempo todo;
  - os ovos que aparecem no celeiro vêm do **celeiro** (`FarmAnimalBarn.ProductEggContainers`), não do mapa: remover só o objeto do mapa não basta (o jogo recria ao entrar). O celeiro também guarda os cochos de ração e a alimentação do dia;
  - aplicar `FromSaveData` num animal ou celeiro vivo funciona (o celeiro continua normal na tela).
- **Decisão:** `Net/AnimalSync`. A cada 1 s, cada jogo compara o estado de cada animal (sem os campos de movimento) e de cada celeiro (sem a hora de entrada pelo portão). O que mudou vai ao host (JSON completo) e é repassado. Quem recebe **mescla**: usa o estado recebido, mas mantém os campos de movimento do próprio jogo (o animal não pula de lugar). Animais novos ou vendidos chegam na virada do dia.
- **Verificado (duas instâncias):** afeição e carinho mudados de cada lado aparecem no outro; parados 30 s no celeiro, só há poucos envios na entrada (o "pedido" do animal, `CurrentSign`, é sorteado ao entrar; sincronizar isso faz todos verem o mesmo pedido); **coleta real de ovo** pela convidada → o host entra no celeiro depois e vê 3 ovos em vez de 4.
- **Consequências:** pets e cavalo (`PetAnimalParameter`, `RideableAnimalParameter`) ainda não sincronizam ao vivo (chegam na virada do dia). Protocolo 15 (`AnimalData` 32).

### D42 — Estado compartilhado genérico (moinho, apicultura, cogumelos, vasos, pets, cavalo)
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (ROTEIRO 5.4)
- **Contexto:** vários sistemas do mundo seguem o mesmo padrão: um gerenciador com `ToSaveData`/`FromSaveData` (`WindmillCraftingManager`, `BeekeepingManager`, `MushroomFarmingManager`, `FlowerPotManager`, `PetAnimalController`, `RideableAnimalController`).
- **Decisão:** `Net/SharedSync`, uma lista de "partes" (nome, capturar, aplicar). A cada 1 s cada parte é comparada (JSON sem os campos de movimento: `Position`, `Rotation`, `ScheduleLayerId`, `Sleep`... em qualquer nível); se mudou, o JSON vai ao host, que aplica e repassa. Quem recebe mescla: objetos com `Guid` mantêm os campos de movimento locais. Uma parte que falhar ao capturar ou aplicar é desligada sozinha (fica registrado no log), sem derrubar as outras. Para incluir um sistema novo, basta acrescentar uma linha na lista.
- **Verificado:** autoteste (capturar e reaplicar cada parte, numa instância): as 6 voltam iguais. Em rede: a afeição de um pet mudada na convidada chega ao host (282 → 300 nos dois); parados ~50 s com os pets andando pela casa, o host mandou os pets só 2 vezes.
- **Consequências:** neste save, moinho e cogumelos estão vazios (`{}`): o caminho existe, mas só foi validado com dados reais nos vasos, pets e cavalo. Protocolo 16 (`SharedState` 33).
- **Casa (5.5), acrescentado depois:** o jogo não tem móveis soltos na casa; ela cresce por **ampliações** (`ExpansionManager`: casa, armazém, cama, mochila, canteiros, apicultura, cogumelos), reservadas no carpinteiro e concluídas nos dias seguintes. Entraram como a parte `expansion` (autoteste: ida e volta igual, 856 caracteres). Achado: o **tamanho da mochila** vem do nível de ampliação `BagLevel`, que é do mundo, ou seja, compartilhado (corrige a observação da D38).

### D43 — Progresso do mundo, flags de evento e natureza compartilhados
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** usuário (natureza e pets) + IA (o resto)
- **Pedidos do usuário (2026-10-25):** "os bichinhos de mundo também são compartilhados, então se um pegar no chão some pro outro, o mesmo com tudo coletável da natureza, exemplo os minérios"; "as flores também"; "o sistema de amizade dos animais de estimação deve ser por mundo também, ou seja, compartilhado".
- **Contexto:** o convidado recarrega o mundo do host na virada do dia. Tudo o que ele mudou no mundo e que não for sincronizado durante o dia se perde (ex.: um evento de coração visto voltaria a acontecer).
- **Decisão:**
  - **progresso do mundo** entra no `SharedSync` (D42): prêmios, adega, banho, receitas, missões do Coro, peixes, reforços, lojas (estoque limitado, sorteio, catálogo) e animais à venda. As partes são conferidas em **rodízio** (uma a cada 0,25 s), para espalhar o custo;
  - **flags de evento** (eventos de história e de coração já vistos): **união**, ou seja, só acrescenta ou atualiza, nunca apaga. Quem vê um evento marca para todos;
  - **natureza:** minérios, pedras, galhos e mato (objetos "gerais"), **flores** silvestres e **bichinhos/animais selvagens** (`FieldAnimalEntityData`: insetos e outros, que vivem no mapa como objeto "Animal") entram na sincronização de mapa (D40). Quem pega tira para todos. Os dados do bichinho não mudam com o movimento (é o ponto de aparição), então não há excesso de mensagens;
  - **amizade dos pets, do cavalo e das criações é do mundo:** já era assim (D41/D42), confirmado.
- **Achados:**
  - o `FromSaveData` dos **troféus acumula** (cada aplicação soma tudo de novo). A parte foi retirada para não duplicar troféus;
  - adega, prêmios e loja não se montam pelo `JsonToObject` (classes sem construtor vazio): montados à mão. Na loja, o histórico de compras fica o local;
  - **festivais** (`FestivalExecutor`: participação, estado e resultado de cada festival) e **informações gerais** (`GeneralInfoManager`: histórico de receitas e despesas) entraram depois (autoteste: ida e volta igual). `DateEventObserver` ficou de fora (o dump não mostra de que classe é o `SaveData` dele);
  - o histórico de itens / enciclopédia (`ItemHistoryManager`) entrou depois, também em **união**: cada item recebido é registrado com `AddHistory` (guarda o maior valor). Verificado em rede: um item novo registrado na convidada aparece no host.
- **Verificado:** autoteste das 17 partes, duas vezes (ida e volta igual e idempotente). Em rede: uma flag de evento marcada na convidada chega ao host; bichinho pego pela convidada nas montanhas some no host (3 → 2); flor colhida idem (18 → 17).

### D44 — Romance exclusivo implementado (registro no host + bloqueio no jogo)
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (implementa a D5, decidida pelo usuário)
- **Decisão:** `Net/RomanceSync`. O host monta, a cada 2 s, o registro NPC → jogador comprometido (noivado `FianceCharaId` ou casamento `PartnerCharaId`), a partir do próprio `LikeabilityManager` e da parte `likeability` dos perfis guardados dos convidados, e manda a todos quando muda (`RomanceClaims`). Em todos os jogos, patches em `LikeabilityManager.CheckMarryable` (pode oferecer o item de compromisso?) e `SetFiance` recusam um NPC comprometido com **outro** jogador e mostram "Esse coração já tem dono: está comprometido(a) com X." O convidado manda o perfil na hora em que o próprio noivado ou casamento muda.
- **Verificado (duas instâncias):** a convidada fica noiva do NPC 101 → o host tenta o 101 e é bloqueado (com o aviso); o host fica noivo do 102 → a convidada é bloqueada no 102.
- **Consequências:** o "namoro" antes do noivado (se existir no jogo como estado separado) não reserva o NPC; a reserva começa no noivado. Protocolo 17 (`RomanceClaims` 17).

### D45 — Casamento de convidados: o que dá e o que não dá agora (6.5)
- **Status:** ⏸ Parcial · **Data:** 2026-10-25 · **Decidido por:** IA (investigação da D12a)
- **Achado:** as rotinas dos NPCs (`NpcSchedule.Married`) escolhem "casado" comparando com o casamento do jogador **local**, e essa verificação **não passa** por nenhuma função interceptável (`IsPartner`, `PartnerCharaId`, `IsMarried`, `MarriageInfo.IsMarried`, `CharacterMarriableMasterData.IsMarried/IsLover`, `FieldStayableChecker.IsPartnersHouse`: nenhuma é chamada ao recalcular as rotinas). O jogo lê o campo direto, embutido no código nativo (`Patches/MarriageDiagnosticsPatch`, só no modo de teste, comando `marrydiag`).
- **Consequência prática hoje (Plano B da D12a):**
  - o casamento do convidado fica no perfil dele (`PartnerCharaId`), e o romance exclusivo (D44) impede outro jogador de casar com o mesmo NPC;
  - no jogo do **convidado**, o cônjuge segue a rotina de casado (mora na casa); no jogo do **host** e dos outros, esse NPC segue a rotina de solteiro (mora na casa de origem). As posições dos NPCs são calculadas em cada jogo, então isso não quebra nada, só fica diferente para cada um;
  - dados de família do mundo (`FamilyManager`: cerimônia, filhos) são os do host. Eventos de casamento e filhos do convidado não foram testados e podem se perder na virada do dia.
- **Para fazer de verdade:** precisaria de um patch no código nativo da escolha de rotina ou de reescrever a escolha de rotina do cônjuge. Fica para quando houver um casamento real para testar (ROTEIRO 6.5).

### D46 — Bazaar: deck e progresso compartilhados (primeira parte da 7.1)
- **Status:** ✅ Decidido (parte) · **Data:** 2026-10-25 · **Decidido por:** IA; o formato da sessão de venda é do usuário (ver abaixo)
- **Decisão:**
  - o **deck do Bazaar** (itens separados para vender) e as **peças de decoração da barraca** viraram armazéns compartilhados, com a mesma trava e sincronização do armazém da casa (D37). O `StorageSync` agora cuida de vários armazéns (0 casa, 1 deck, 2 peças), e as mensagens levam o número do armazém;
  - o **progresso do Bazaar** entra no `SharedSync`: `bazaar` (nível, reputação, vendas totais e da semana...), `bazaartrend` (tendências) e `bazaarcustom` (decoração). Achado: `BazaarManager.ToBazaarSaveData` **quebra fora do Bazaar** (o gerenciador só fica completo no dia e no mapa dele). Por isso falha ao capturar passou a ser **temporária** (só pula); só falha ao aplicar desliga uma parte.
- **Verificado (duas instâncias):** a convidada põe 2 itens no deck e aparecem no host; com o host na tela do deck, a convidada é bloqueada com aviso. Tendências e decoração: autoteste ok. O progresso (`bazaar`) só pode ser testado num dia de Bazaar.
- **Ideia do usuário para a sessão de venda (2026-10-25):** "ou os dois juntos, ou um faz e o outro fica livre pra trabalhar e comprar itens, e quando acabar o Bazaar pular o tempo pro que não tava; mas dá a opção: quer trabalhar junto ou não?" → em investigação (D47).
- Protocolo 18 (mensagens de armazém com o número do armazém).

### D47 — Sessão de venda do Bazaar em grupo (ideia do usuário)
- **Status:** ✅ Decidido (medido num sábado do jogo; falta a venda em grupo de verdade com os amigos) · **Data:** 2026-10-25 · **Decidido por:** usuário (ideia) + IA (viabilidade)
- **Ideia do usuário:** "ou os dois juntos, ou um faz e o outro fica livre pra trabalhar e comprar itens, e quando acabar o Bazaar pular o tempo pro que não tava; mas dá a opção: quer trabalhar junto ou não?"
- **O que se sabe do jogo (investigação):** o Bazaar abre aos sábados; a barraca pode ser aberta a partir das 10:00 (`IsEnableOpenMyShopTime`); o mapa do Bazaar fica num estado próprio (`ProcedureMask = HeldMap`) e o relógio pode parar (`IsBazaarTimeStop`); a sessão termina com a tela de resultado (`ShowResultPage`). Os clientes NPC são simulados em cada jogo (`NPCBazaarVisitorAgent`). Dirigir o minijogo por automação não deu (a entrada pelo comando de troca de mapa deixa o jogador preso numa tela), então o comportamento do relógio durante a venda ainda não foi medido.
- **Viabilidade (proposta):**
  - **"Um vende, os outros ficam livres":** dá. Quem está vendendo não pausa o grupo (como quem dorme, D33) e, durante a venda, o relógio dele não é corrigido pelo do host. No fim, os relógios se alinham pelo **mais adiantado**: se a venda terminou "mais tarde", quem estava livre pula para essa hora; se o host vendeu, o pulo chega aos outros sozinho pela correção de relógio que já existe.
  - **"Os dois juntos":** dá, com uma diferença do jogo sozinho: cada um abre a barraca no próprio jogo e vende para os próprios clientes simulados. O deck compartilhado com trava (D46) impede vender o mesmo item duas vezes, e o dinheiro já é compartilhado (D36). O grupo terá mais clientes que no jogo sozinho.
  - **A opção:** não precisa de menu. Quem vai à barraca vende; quem não vai fica livre. Se quiserem vender juntos, os dois vão à barraca. Dá para acrescentar um aviso "Fulano abriu a barraca do Bazaar" no sistema de avisos.
- **Medido num sábado do jogo (2026-10-25), chegando ao Bazaar andando pela cidade:**
  - o aviso "Hoje é o bazar... Turno da manhã 10:00–13:59, turno da tarde 15:00–18:59" aparece ao entrar (é ele que deixava o personagem "preso" no teste anterior: só precisa de Esc);
  - abrindo a loja (menu da barraca: Ver estoque / Redecorar / **Abrir loja**), o jogo leva o jogador para a barraca dele e começa o turno (`ProcedureMask = HeldMapOpen`);
  - **o relógio continua correndo normalmente durante a venda** (1 minuto do jogo por segundo, `IsBazaarTimeStop` falso). Só para por instantes: na faixa "Começar o turno" e na tela de resultados;
  - o turno acaba sozinho na hora certa (14:00), com a tela "Resultados do Turno da Manhã".
- **Conclusão:** **não é preciso pular o tempo**. O relógio do grupo é um só (o do host) e a venda não o congela: enquanto um vende das 10h às 14h, os outros vivem as mesmas 10h às 14h livres (trabalhar, comprar). As duas formas da ideia do usuário funcionam sem menu de escolha:
  - **um vende, os outros ficam livres:** basta só um abrir a loja;
  - **juntos:** cada um abre a loja no próprio jogo (clientes simulados em cada um), com o deck compartilhado (D46, com trava) e o dinheiro em comum (D36).
  - A tela de resultados e a faixa de início contam como "menu" pela regra D9 atual (N = 1), então param os outros por alguns segundos (ver P7).
- **Feito junto:** aviso "Fulano abriu a barraca do Bazaar!" no sistema de avisos do jogo (`NotifySync`, código 5).

### D48 — "Salvar" e "Carregar" do menu de pausa no multiplayer
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (ROTEIRO 8.4c)
- **Contexto:** no multiplayer, "Salvar" mostrava "Não foi possível acessar os dados" (o save fica bloqueado, D23), e "Carregar" trocaria o mundo no meio da sessão.
- **Decisão:** patch em `UIPausePage.OnDecide`. Conectado, "Salvar" (texto 1130) mostra "No multiplayer o jogo salva sozinho quando todos dormem." e "Carregar" (texto 1140) mostra "Para carregar outro save, saia do multiplayer primeiro (F9 → Desconectar)." Nenhuma das duas telas abre. Jogando sozinho, nada muda.
- **Verificado:** hospedando, as duas opções mostram o aviso; o menu continua funcionando, fecha com Esc e o personagem anda depois.

### D49 — Vigor máximo compartilhado; bônus de comida individuais (substitui parte da D38)
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** usuário ("o vigor máximo pode ser compartilhado, já que tem poucos jeitos de aumentar ele, mas os bônus de comida são individuais"; "upgrade de ferramenta não deve ser compartilhado" — já era assim)
- **Decisão:**
  - **vigor máximo** saiu do perfil (D38) e virou a parte `maxstamina` do `SharedSync`: quem aumenta, aumenta para todos. Ao aplicar, o vigor atual fica na mesma proporção;
  - **bônus de comida** e outros efeitos temporários (`PlayerBuffManager`) entraram no perfil de cada jogador (parte `buffs`). No "começar do zero" (D30), o convidado novo chega sem bônus;
  - **upgrade de ferramenta** continua individual (ferramentas no perfil, D24/D38); o dinheiro do ferreiro sai da carteira comum.
  - Perfis antigos que tinham a parte `stamina` simplesmente ignoram essa parte.
- **Verificado (duas instâncias):** a convidada sobe o vigor máximo para 1200 → o host recebe; o host ganha um bônus de comida → a convidada não recebe, e o bônus fica gravado no perfil do host.

### D50 — Relógio do grupo: só o menu de pausa pausa; o tempo segue o mais adiantado (substitui a regra N da D9)
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** usuário ("somente pausar quando for a pausa do jogo mesmo, e aí pausa pra todo mundo, de resto deixar passando o tempo"; e, sobre o Bazaar, "se os produtos vendem todos o jogo pula pro horário do final")
- **Decisão (`Net/ClockSync`):**
  - o tempo do grupo **só para** quando alguém está no **menu de pausa** do jogo (Esc, `UILoadKey.Pause` 119), e para para todos;
  - em qualquer outra tela (mochila, lojas, diálogos, eventos, telas do Bazaar) o tempo do grupo **continua**. O jogo para o relógio de quem está nela; ao sair, ele é **adiantado** até a hora do grupo. No host (que é a fonte da hora), enquanto ele está numa dessas telas, a hora do grupo é calculada (hora do host + o tempo que passou) e ele é adiantado ao sair;
  - o relógio segue o **mais adiantado**: se o tempo pula para frente no jogo de um convidado (barraca do Bazaar esgotou, evento que avança a hora), ele avisa o host (`ClockJump`), e todos pulam junto, com o aviso "O tempo avançou para HH:MM (Fulano)". Pulos maiores que 12 h ou que atravessam a virada do dia são ignorados (a virada é do DaySync);
  - a opção `[Game] PauseWhenPlayersInMenu` saiu.
- **Verificado (duas instâncias):** host na mochila → a convidada anda e o tempo corre (07:28 → 07:38); ao fechar, o host é adiantado (07:43 nos dois). Convidada no menu de pausa → o tempo para para os dois. O tempo da convidada pula 2 h → o host pula junto (10:06 / 10:07) com o aviso.
- Protocolo 19 (`ClockJump` 34; `PauseReport` passa a dizer "está no menu de pausa").

### D51 — Bazaar: uma barraca por vez; "juntos" = repor a mesma barraca (proposta)
- **Status:** 🟡 Parte feita, falta teste · **Data:** 2026-10-25 · **Decidido por:** usuário ("pode ocorrer de duplicar os itens se cada um tem sua barraca"; "ou um só vende, ou os dois vendem juntos repondo as prateleiras num coop")
- **Feito (sem teste completo):** trava da barraca, como a do armazém (D37): enquanto alguém está com a loja aberta (`BazaarManager.IsInShop`), o host registra quem é, e o menu da barraca (`RequestOpenShopDialog`: Ver estoque / Redecorar / Abrir loja) fica bloqueado para os outros, com o aviso "Fulano está vendendo na barraca neste turno." Isso impede duas barracas vendendo os mesmos itens do deck compartilhado. O pulo de tempo quando a barraca esgota é coberto pela D50.
- **"Juntos repondo as prateleiras" (a fazer):** cada jogo simula os próprios clientes; a barraca é um objeto de cada jogo (`BazaarShopShelf` → `BazaarShopItem` → `ShopItem.itemInvData`). Plano: quem abriu a loja é o **dono** (só no jogo dele os clientes compram); o ajudante entra num modo "ajudante" em que vê e repõe as prateleiras do dono (estado das prateleiras sincronizado nos dois sentidos) e o jogo dele não gera clientes. Precisa de investigação com o jogo aberto e de teste jogando.
- **Os dois verem os mesmos clientes (pergunta do usuário, 2026-10-25):** dá, com a mesma técnica dos avatares dos jogadores (PlayerSync): no jogo do dono os clientes são de verdade; ele manda ~10×/s quem são, onde estão, a animação e o que estão fazendo (olhando, comprando, balão de pensamento). No jogo do ajudante o jogo **não gera clientes próprios** e aparecem **cópias** que repetem os do dono. Compras só acontecem no dono; as prateleiras (o que tem em cada espaço) são sincronizadas nos dois sentidos, então o ajudante repõe e vê sumir o que foi vendido. Cuidados: NPCs com nome que também andam pela cidade no jogo do ajudante (não podem aparecer em dobro) e os efeitos da barraca (sino, bônus). É a parte maior do Bazaar; fica para depois de testar a trava.
- **Achado de teste:** trocar de mapa pelo comando de teste (`field`) às vezes deixa o aviso "Hoje é o bazar" sem a caixa, preso (Esc não fecha). Acontece também sem o mod (testado com uma versão antiga e jogando sozinho): é um efeito do atalho de teste, não do mod. Jogando normalmente o aviso abre e fecha certo.

### D52 — Pets e cavalo: estado aplicado animal por animal (corrige pets em dobro)
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (bug visto pelo usuário: "os pets tavam em dobro")
- **Contexto:** a parte `pets` da SharedSync (D42) aplicava o estado recebido com `PetAnimalController.FromSaveData`. Esse método **recria** os pets (parâmetros e personagens na cena) sem descarregar os que já existem. A cada mudança vinda do outro jogador (carinho, amizade), apareciam cópias dos pets. O autoteste da D42 não pegava porque compara só o JSON, que continuava igual.
- **Confirmado (uma instância, comando `pets`):** em casa, 2 pets e 2 personagens. Depois de reaplicar pelo caminho antigo (`pets old`), 2 pets e **4** personagens.
- **Alternativas:** (a) continuar com o `FromSaveData` do controlador e apagar os personagens duplicados depois (frágil: depende de achar e destruir objetos do jogo); (b) aplicar no parâmetro de cada animal, como a AnimalSync faz com os animais da fazenda (D41).
- **Decisão:** (b). `SharedSync.ApplyPets` / `ApplyHorse`: para cada animal do JSON (por `Guid`), `FindParameter(guid).FromSaveData(dados do animal)`, mantendo os campos de movimento locais (mesclagem da D42). Animal que ainda não existe neste jogo (comprado no jogo de outro jogador) é criado com `new PetAnimalParameter()` + `FromSaveData` + `AddParameter`. O mesmo vale para o cavalo (`RideableAnimalController`). A lista `DomesticatedList` do cavalo não é mais aplicada ao vivo (chega na virada do dia).
- **Verificado:** reaplicar pelo caminho novo mantém 2 personagens. Mudar a amizade de um pet (`shared poke pets <guid> FriendPoint 310`) muda o valor sem criar personagens. Um pet novo vindo de fora (`pets addcopy`) aparece **uma vez** (3 pets, 3 personagens).
- **Varredura das outras partes (`shared reapply` + `objcount`, em casa):** nenhuma outra parte cria objetos ao reaplicar. A parte `bazaar` deu erro (`NullReferenceException` em `BazaarManager.FromBazaarSaveData`): corrigido na [D54](#d54--estado-compartilhado-que-não-chegava-bazaar-montado-à-mão-moinho-cogumelos-e-festivais-pelo-jsonutility).
- **Consequências:** remover um pet (vendido/doado) não é aplicado ao vivo; chega na virada do dia (mundo novo do host). Observação: o `RemoveParameter` do próprio jogo não tira o personagem da cena na hora.

### D53 — Pular as logos de abertura
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** usuário ("tem um mod de pular a introdução mostrando as marcas do jogo antes de ir pro menu, consegue dar uma olhada pra poder testar mais rápido?")
- **Contexto:** cada abertura do jogo passa pelas logos (Marvelous, CRIWARE...). Nos testes o jogo é aberto muitas vezes.
- **Alternativas:** (a) instalar o mod "Skip Splash Screen" (Nexus, Lalaz4; código em GitHub gmm39/SOSGrandBazaarMods, sem licença declarada); (b) fazer o mesmo dentro do nosso plugin.
- **Decisão:** (b), com a mesma técnica (é pequena e não copia código protegido de forma relevante): `Patches/SkipLogosPatch.cs` faz toda fase de `CompanyLogoAnimation.EnterPhase` virar a de saída (`FadeOut`), com as transições do `animManager` em 0 s, e deixa transparente o fundo do `CompanyLogo` (`fadeTimer = 0`, alpha 0). Opção `[Game] SkipIntroLogos` (padrão `true`). Precisou da referência `interop/DOTween.dll` (enum `Ease`).
- **Motivo:** um plugin a menos para os amigos instalarem, e não depende de uma DLL de terceiros.
- **Medido:** abrir até o save carregado levou 82 s sem pular e 58 s pulando (~24 s a menos por abertura).
- **Consequências:** se alguém também instalar o Skip Splash, os dois patches fazem a mesma coisa (sem conflito).

### D54 — Estado compartilhado que não chegava: Bazaar montado à mão; moinho, cogumelos e festivais pelo JsonUtility
- **Status:** ✅ Decidido · **Data:** 2026-10-25 · **Decidido por:** IA (achado na varredura da D52)
- **Contexto:** três defeitos silenciosos na SharedSync (D42/D43), que o autoteste não pegava porque ele só confere se reaplicar o próprio estado deixa tudo igual:
  1. **Bazaar (nível, vendas, reputação, recorde):** o `JsonToObject` do jogo devolve **nulo** para o `BazaarSaveData` inteiro, porque `SaleItemCount`, `BazaarMaxSaleData` e `BazaarSaleData` não têm construtor vazio. O `FromBazaarSaveData(null)` dava `NullReferenceException` e a parte se desligava na primeira mudança. Efeito: se o convidado vendesse no Bazaar, o progresso dele se perderia na virada do dia.
  2. **Moinho e cogumelos:** os `SaveData` guardam tudo em campos **privados** `[SerializeField]`. O `StringExtensions.ToJson` do jogo (Newtonsoft) ignora esses campos e devolvia `{}`: nada era enviado (receitas, o que está no moinho, troncos de cogumelo).
  3. **Festivais:** as competições (classificação, recordes, exposições) também ficam em campos privados (`m_...`) e não iam.
- **Como foi achado:** comando `jsoncmp` (tamanho do JSON do jogo × `JsonUtility` da Unity para cada parte) e `shared roundtrip bazaar` (JSON → objeto → JSON).
- **Decisão:**
  - Bazaar: `SharedSync.BuildBazaar` tira as listas do JSON, usa o `JsonToObject` no resto e monta as listas à mão (`new SaleItemCount(categoria, qtd)`; `BazaarSaleData` é criado **sem construtor** com `IL2CPP.il2cpp_object_new` e preenchido, porque os construtores pedem a tabela de preço). Os avisos de interface (`showSideInfoType`, `IsShowNotificationDialog`) ficam os de cada jogo.
  - Moinho, cogumelos e festivais: captura com `JsonUtility.ToJson` e aplica com `JsonUtility.FromJson` (o formato do próprio save). Precisou da referência `interop/UnityEngine.JSONSerializeModule.dll`.
  - As outras partes ficam como estão: o `jsoncmp` mostrou o mesmo conteúdo nos dois serializadores (pets, cavalo, vasos e ampliações só diferem na formatação ou em propriedades calculadas). A loja perde só um contador do sorteio de itens (`LotteryStatusList`), sem importância.
- **Verificado (uma instância):** ida e volta do Bazaar sem nenhuma diferença; reaplicar não cria objetos; mudança simulada vinda de outro jogador chega (`shared set windmill WindmillTotalDataList.1.takeOutItemList.0.Stack 5` → 5; `shared set mushroom MushroomLogDatas.0.GrowthLevel 1` → 1).
- **Consequências:** teste de duas instâncias ainda a fazer (vender no Bazaar como convidado e ver o progresso no host). Para partes novas, conferir sempre com `jsoncmp` e `shared set`, não só com `shared test`.

### D55 — Bazaar: todos veem os mesmos clientes e a barraca de quem vende (etapa 1 do "juntos")
- **Status:** ✅ Etapa 1 feita e testada · **Data:** 2026-10-25 · **Decidido por:** usuário ("mas não dá pros dois verem os mesmos clientes?" → "sim pode executar esse plano")
- **Contexto:** cada jogo simula os próprios clientes do Bazaar (`NPCBazaarVisitorAgent`). Com a trava da D51 só um vende por vez; quem não vende via outra multidão e a barraca do dono fechada (barris e caixotes).
- **Investigação (runtime):**
  - Com o Bazaar aberto (10:00+) os clientes já passeiam pelo mapa em todos os jogos (estado `WanderField`). Ao abrir a loja, o jogo do dono passa a usar ~25 deles como clientes da barraca (`Wander`/`Watch`/`Buy`/`Exit`).
  - O elenco é praticamente o mesmo em qualquer jogo no mesmo dia (números 300–326, menos 2; alguns especiais variam, ex.: 424, 328). Cada número é um modelo (`chr_mob_*`).
  - Um cliente pode ser controlado de fora: `SetEnableUpdate(false)` + `NavMeshAgent` desligado + o script desligado + `Rigidbody` cinemático + `applyRootMotion = false`. Ao devolver, a IA dele volta a funcionar.
  - A barraca aberta é montada pelo jogo (`BazaarMyShop`): `ShopShelfRoot.ChangeTent(true)` troca o visual fechado (`BazaarMyShopClose`) pelo aberto, e `SetBazaarPartsModel` carrega as peças (balcões = `BazaarShopShelf`, decoração). Funciona sem abrir a loja de verdade.
  - Itens no balcão: `ShopItem.UpdateExhibitUniqueData(ItemData)` mostra, `ClearContent()` limpa.
- **Decisão (`Net/BazaarViewSync`):**
  - **Dono** (quem está com `IsInShop`): 10×/s manda cada cliente (número, posição, rotação, estado do Animator, velocidade, balão) e, quando muda, o que está em cada espaço da barraca. Ao fechar a loja, manda "fechada".
  - **Quem está no mesmo mapa e não vende:** assume o cliente de mesmo número no próprio jogo (cria com `PopVisitor` se ainda não existir), move como cópia (interpolação como os avatares) e esconde os clientes que o dono não tem. Abre a barraca (tenda + peças) e mostra os itens do dono. Quando a loja fecha ou as mensagens param (1,5 s), devolve tudo: IA dos clientes de volta, barraca fechada.
  - **As vendas só acontecem no jogo do dono** (os clientes copiados não têm IA), então nada é vendido em dobro. O dinheiro já é compartilhado (D36).
  - Protocolo 20: `BazaarVisitors` (18) e `BazaarShelves` (19), repassadas pelo host.
- **Verificado (duas instâncias, host vende):** a convidada assumiu os 25–28 clientes do host com diferença de posição de 0,1–0,2; a barraca aparece aberta com o host atrás do balcão e os clientes na frente; item posto no balcão do host aparece no da convidada; ao fim do turno (tela de resultados fechada), a convidada devolveu os clientes (voltaram a `WanderField`) e a barraca voltou a fechada.
- **Etapa 2 (a fazer, pedido do usuário "o outro player não ajuda na barraca?"):** quem assiste poder **ajudar**: repor itens nos balcões do dono e tocar o sino. Ver D56.

### D56 — Bazaar: o ajudante entra na barraca e usa os botões do jogo (etapa 2 do "juntos")
- **Status:** 🟡 Decidido, em implementação · **Data:** 2026-10-25 · **Decidido por:** usuário ("o outro player não ajuda na barraca?"; escolheu "dentro da barraca, botões do jogo" em vez de teclas do mod fora da barraca)
- **Contexto:** com a D55 todos veem os clientes e a barraca de quem vende, mas só o dono pode agir.
- **Alternativas:** (a) ajudante fora da barraca, com teclas do mod (atender o cliente mais próximo, repor automático): mais rápido de fazer, menos parecido com o jogo; (b) ajudante dentro da barraca, como vendedor, com os mesmos botões (Enter no item para atender, Tab para repor, Shift para o sino). Escolhida: (b).
- **Como a barraca funciona (descoberto em runtime, com o registro `Patches/BazaarDiagnosticsPatch`, só no modo de teste):**
  - A venda **não é automática**: o cliente reserva um espaço e espera com "!!" (`IsBuyStandby`); o vendedor anda até o item e aperta **Enter** (dica do usuário). O jogo chama `ActionAccessShopShelf(espaço)` → `Sell(espaço)` → `AddBazaarResultData(item, 1)`; o item sai do balcão. O dinheiro do turno só entra no fim (tela de resultados).
  - O vendedor anda livremente atrás do balcão (A/D) e o destaque azul fica no espaço mais próximo. Os índices dos espaços são do **balcão atual** (o jogo atualiza todo quadro com `UpdateCurrentShopShelf(id do ponto de venda)`).
  - Os itens dos balcões ficam no `ExhibitionShelfManager` (armazém de 12 espaços: balcão×4 + espaço; `InventoryManager.ExhibitionShelf`). Pôr por código: `DeckStorage.PutOut` + `ExhibitionShelf.PutIn(item, espaço)` + `BazaarManager.ReExhibit()` (testado: aparece com preço e os clientes compram).
  - Tab com o vendedor num espaço vazio abre o "mini deck" (`UIBazaarMainPage.OpenMiniDeck`) para escolher o item. Ainda falta ver a escolha (`SelectExhibit`).
  - O sino é Shift segurado (atrai clientes; `PlayerActionAtraction`).
- **Plano:**
  - Com alguém vendendo, o menu da barraca deixa de ser bloqueado para os outros (D51 muda): "Abrir loja" vira **ajudar**. O jogo do ajudante entra no modo loja normal (câmera, botões), mas em **modo ajudante**:
    - os clientes são as cópias do dono (D55); os clientes próprios ficam parados e escondidos;
    - os balcões (dados e visual) espelham os do dono;
    - Enter num espaço (`ActionAccessShopShelf`) e a escolha no mini deck (`SelectExhibit`) não agem no jogo do ajudante: viram pedidos ao dono, que executa no jogo dele (balcão certo via `UpdateCurrentShopShelf`), e o resultado volta pelo espelhamento;
    - o sino do ajudante pede o sino ao dono;
    - o ajudante não grava progresso do Bazaar (só o dono), e não vê tela de resultados própria.
  - O dinheiro das vendas entra no fim do turno no jogo do dono e é compartilhado (D36).
- **Andamento (2026-10-25, pausado a pedido do usuário):**
  - Feito (`Net/BazaarHelpSync`): modo ajudante pelo menu normal ("Abrir loja" com outro vendendo); pedidos "atender" (dono: `UpdateCurrentShopShelf` + `Sell`, testado) e "pôr item" (dono: deck → `ExhibitionShelf.PutIn` + `ReExhibit`, testado); balcões do ajudante espelhando os do dono (dados); clientes próprios do ajudante congelados; deck e progresso do Bazaar escritos só pelo dono durante a venda; deck/peças bloqueados para os outros enquanto alguém vende.
  - Teste do usuário como ajudante: os pedidos chegaram (o dono atendeu 4 vezes e pôs 1 item), mas **o aviso de vender não aparecia** e **o menu de escolher item abria vazio**, e alguns clientes não ficavam iguais.
  - Causas achadas: (1) a lista do mini deck (`DeckStorage.ItemDataInDeckList`) só é montada em certos momentos (ex.: "Ver estoque"); deck vindo da rede deixa a lista vazia → recriar com `GetMiniDeckItemsData()` + `BazaarManager.ResetMiniDeck()` (testado: 5 itens). (2) interceptar `IsBuyStandby` faz um erro interno do jogo (`CustomPartsCompositeLevel`) virar "sem cliente esperando", até no jogo do dono.
  - Próximo passo: instalar as interceptações do ajudante só no jogo dele e só enquanto ajuda (Harmony manual em Start/Stop); recriar a lista do mini deck depois de cada deck recebido; restaurar o balcão do dono pela posição dele em vez de patch em `UpdateCurrentShopShelf`; deixar o registro `BazaarDiagnosticsPatch` só sob pedido; testar de novo com o usuário. As interceptações de `IsBuyStandby`/`GetShopShelfAccessState` estão desligadas no código (comentário EXPERIMENTO) e a chave `SOSGB_TEST_UNPATCH` tira patches do mod sem recompilar.

### D57 — Levantamento de conflitos (wiki + inventário do save) e o que passou a ser compartilhado
- **Status:** ✅ Decidido (implementado; Happy Energy compartilhada aguarda confirmação, P11) · **Data:** 2026-10-26 · **Decidido por:** IA (delegado pelo usuário: "tente ter insights e se basear na wiki do jogo pra ver se mais alguma coisa daria conflito no multiplayer")
- **Contexto:** o co-op compartilha o mundo pelo save do host (a cada virada do dia o convidado recebe o mundo do host, D23). Toda parte do mundo que muda durante o dia e NÃO é sincronizada ao vivo se perde para o convidado no fim do dia. Para achar essas partes: (1) listei todos os ~80 campos do save (`UserInfo`) e comparei com o que o mod sincroniza; (2) um levantamento na wiki/guias do remake (Fogu e outros) apontou mecânicas de risco. Resultado completo em [`docs/RISCOS-MULTIPLAYER.md`](docs/RISCOS-MULTIPLAYER.md).
- **Decisões (implementadas):**
  - **Happy Energy** (`UserInfo.PlayerHappyPoint`, a moeda dos Nature Sprites / Happy Shoppe): carteira compartilhada, igual ao dinheiro (D36). `MoneySync` agora cuida de duas carteiras (mensagens `HappyDelta` 36 / `HappyState` 37). Alternativa: individual (no perfil). Escolhida a compartilhada por coerência com o dinheiro; ❓ P11.
  - **Pedidos dos moradores** (`MissionManager`, `MissionData`): novo `Net/MissionSync`. O convidado manda as mudanças (estado novo e a DIFERENÇA de progresso); o host soma o progresso (pedidos como "colha 200" contam a ajuda de todos) e fica com o estado mais avançado; depois manda a lista oficial (mensagem `MissionData` 38). Os desbloqueios (moinhos, lojas) acontecem no mundo do host na virada, como no jogo sozinho.
  - **Tutoriais já lidos** (`TutorialManager`): parte "tutorial" da SharedSync, em UNIÃO (`AddTutorialRead`). Sem isto o convidado veria o mesmo tutorial todo dia.
  - **Selo BRAND** (`UserInfo.BrandItemIds/BrandAnimalIds`, ganho nos festivais): parte "brand", em UNIÃO.
  - **Pets e cavalos novos só no mundo do host:** os eventos da manhã que dão animais (pet grátis em 1 de verão, cavalo em 14 de verão) rodam em cada jogo. O host ignora pets/cavalos NOVOS vindos de convidados e, quando recusa, manda o estado dele a todos (o animal a mais some do jogo do convidado na virada do dia). Mudanças em animais que já existem continuam valendo nos dois sentidos. **Corrigido na [D60](#d60--revisão-independente-do-código-do-bazaar-e-da-d57-correções):** o filtro vale só nos dias dos presentes (pet comprado por convidado em outro dia é aceito).
- **Ficou para depois (anotado em `docs/RISCOS-MULTIPLAYER.md`):** `EventReadData` (eventos vistos), Happy Bingo (~300 KB), conquistas e contadores de títulos, troféus (o `FromSaveData` acumula), festivais com competição (❓ P13), ferramenta deixada no moinho (❓ P12), casamento de vários jogadores (D45).
- Protocolo 22.

### D58 — Virada do dia depois da meia-noite: sinal pelo personagem, não pelo número do dia (corrige impasse)
- **Status:** ✅ Decidido · **Data:** 2026-10-26 · **Decidido por:** IA (achado no teste do desmaio, pedido de polimento do usuário)
- **Contexto:** o host percebia que a virada tinha terminado quando o NÚMERO do dia mudava (D33). Testando o desmaio de sono, apareceu o caso em que ele não muda: depois da meia-noite o calendário já está no dia seguinte, e o jogo acorda no MESMO número (dormindo à 1h → mesmo dia, 10h; desmaio às 5h → mesmo dia, 10h). O host ficava preso em "virando o dia" para sempre: os convidados não recebiam o mundo novo (dormindo para sempre), a contagem de quem dorme travava e as partes que esperam o fim da virada (pedidos, D57) paravam.
- **Confirmado (uma instância hospedando):** desmaio às 5h e cama à 1h passam por `GameController.StartNextDay` (a trava da D33 segura igual à cama, "Dormindo 1/2"). Sem a correção, `hostWaitingNewDay` ficou `true` com o dia já virado.
- **Alternativas:** (a) comparar a hora (um salto de horas para frente); (b) um sinal do próprio jogo de que a virada terminou.
- **Decisão:** (b). Um postfix em `PlayerCharacter.StartNextDay` (o jogo prepara o personagem para o dia novo, depois do save automático) marca a virada como concluída; a mudança do número do dia continua valendo como sinal reserva. Se nenhum sinal vier em 60 s reais, o host manda o mundo assim mesmo (com aviso no log), para nunca prender os convidados. A espera de 3 s para o jogo assentar agora precisa ser contínua (zera se o jogo voltar a "esperando a virada").
- **Verificado:** desmaio às 5h → "dia novo; enviando o mundo" (10:10 do mesmo número); cama de verdade à 1h → idem.

### D59 — Convidado que perde a conexão não grava o mundo do host no próprio save
- **Status:** ✅ Decidido · **Data:** 2026-10-26 · **Decidido por:** IA (achado ao revisar a D56/D57; polimento pedido pelo usuário)
- **Contexto:** o convidado nunca grava no disco porque `SavesBlocked` olhava só o modo da rede ("convidado"). Quando a conexão cai, o modo vira "offline" e o bloqueio sumia, mas o jogo continua com o **mundo do host** na memória. Se a pessoa seguisse jogando e dormisse, o save automático (slot 0, o dela) receberia o mundo do amigo; "Salvar" no menu também gravaria por cima dos saves dela.
- **Alternativas:** (a) voltar ao título sozinho quando a conexão cai (mais radical; perde o que a pessoa estava vendo); (b) manter o bloqueio enquanto o mundo do host estiver na memória e avisar.
- **Decisão:** (b). `WorldSnapshot.HoldingHostWorld` liga quando o mundo do host chega (`WorldSync`) e só desliga ao voltar ao título (`SceneManager.LoadScene` com a cena de título). `SavesBlocked` inclui essa marca. Ao cair, o aviso diz: "Este mundo é do host: nada daqui será salvo. Volte ao título para entrar de novo ou jogar o seu save."
- **Verificado (uma instância, comando `holdworld`):** a marca liga e desliga ao voltar ao título. O primeiro patch em `LoadScene` impediu o plugin inteiro de carregar (o método tem duas versões e o Harmony não soube qual escolher); trocado por `TargetMethod` que escolhe a versão pública. Lição em `CODIGO.md`.

### D60 — Revisão independente do código do Bazaar e da D57: correções
- **Status:** ✅ Decidido · **Data:** 2026-10-26 · **Decidido por:** IA (revisor sem contexto leu `c85f9d3..HEAD`; cada achado foi conferido no código antes de corrigir)
- **Contexto:** a D56 e a D57 foram feitas rápido e testadas só pelos caminhos felizes. Uma revisão de outro agente, que não conhecia as minhas conclusões, achou 12 problemas. 10 foram confirmados no código e corrigidos; 2 ficaram como limitação conhecida.
- **Correções:**
  1. **"Ver estoque" aberto quando outro abre a loja** (duplicação de itens): a tela do deck/peças de quem estava com ela aberta fecha na hora (e manda a versão final), em vez de só bloquear quem abre depois (`StorageSync.WatchLocalUi`).
  2. **Pet comprado por convidado era descartado** (o dinheiro saía e o pet sumia na virada): o filtro da D57 agora vale só nos dias dos presentes da manhã (1 e 2 de verão para pet, 14 e 15 de verão para cavalo). Nos outros dias o pet novo do convidado entra no mundo do host.
  3. **Ajudante preso quando o dono sai** (queda ou loja fechada antes): o ajudante assume a barraca. Se o dono saiu do mundo, fica com os itens dos balcões (só existiam no jogo dele); se o dono fechou a loja, assume vazia. Ao assumir não há folga nem reversão do deck (senão itens postos depois voltariam ao deck e duplicariam). A trava da barraca não é pega por quem ainda está em modo ajudante.
  4. **Reversão do deck sobrevivia ao fim da sessão:** a reversão pendente e o "último deck recebido" são zerados ao hospedar, entrar ou receber um mundo, e quando a sessão acaba. Durante a folga de 8 s depois de ajudar, o deck/peças ficam bloqueados com aviso (antes a mudança era engolida).
  5. **Pedidos por valor somados:** pedidos que o jogo define por valor (afeição de animal e pet, venda do dia no Bazaar: `MissionManager.Overwrite*`) agora vão com o valor e o host fica com o maior; os contadores continuam somando. A detecção é em tempo real: um patch só de leitura vê quais pedidos mudaram dentro de um `Overwrite*`. Protocolo 23 (`MissionData` leva 1 byte por pedido).
  6. **"Pôr item" podia sumir com itens** (deck cheio, índice fora do balcão): valida o espaço, recusa se o item trocado não couber no deck, desfaz se o jogo não aceitar, e o que sobrar vai para a mochila do dono.
  7. **Enter num item sem cliente virava "atender"** (o dono descartava): agora o Enter do ajudante segue o estado do espaço no jogo do dono: Vender → atender; Pôr → mini deck; o resto não faz nada (trocar item sem cliente ainda não foi testado no ajudante).
  8. **Instalação pela metade das interceptações do ajudante** deixava patches soltos: se uma falhar, tira todas; ao sair, sempre tira tudo.
  9. **Um quadro como "dono" no jogo do ajudante:** o dono da barraca também precisa ter a trava (ou ninguém ter).
  10. **Estados dos espaços:** um espaço com erro interno não apaga mais o estado dos outros. **"Loja nova"** dos pedidos: o convidado só acende o aviso quando o host passa de "não" para "sim" (antes o aviso voltava a cada lista do host).
- **Limitações conhecidas (não corrigidas):** (a) se os dois abrirem a loja no mesmo instante e um virar ajudante, os itens que ele já tivesse posto no balcão dele são substituídos pelo espelho do dono (raro: no fim de cada turno o jogo devolve ao deck o que sobrou no balcão, testado; então o balcão de quem entra como ajudante está vazio); (b) trocar um item sem cliente pelo ajudante fica desligado até ser testado.
- **Testado (uma instância):** dono da barraca no sábado continua vendendo (25 G, 50 G), trava com o dono, estados enviados, nenhum aviso do mod no log. O caminho com duas instâncias (ajudante) fica para o teste com o usuário (P14).

### D61 — Happy Bingo compartilhado por mescla
- **Status:** ✅ Decidido · **Data:** 2026-10-26 · **Decidido por:** IA (levantamento de riscos da D57; o usuário pediu para achar o que mais daria conflito)
- **Contexto:** o Happy Bingo (versão 1.1.0 do jogo) tem cartelas com 25 missões; linhas completas dão prêmios. Ele não era sincronizado: (1) o progresso que o convidado fazia sumia na virada do dia (vale o mundo do host); (2) pior, o resgate de um prêmio feito pelo convidado também sumia, então uma linha completa no mundo do host podia ser resgatada pelo convidado **todo dia** (itens repetidos na mochila dele). O save inteiro do bingo é grande (~300 KB), mas o que muda jogando é pouco.
- **Alternativas:** (a) mandar o save do bingo inteiro e sobrescrever (grande e, com os dois jogando, um apagaria o progresso do outro); (b) mandar só estado/progresso de cada quadrado e os prêmios resgatados, e MESCLAR.
- **Decisão:** (b). Parte "bingo" na SharedSync (~7 KB, só quando muda): por cartela, cada quadrado (espaço, missão, estado, progresso) e os "resgatado" de cada linha (horizontal, vertical, diagonal, completa). Quem recebe fica com o MAIOR progresso e o estado mais avançado (Nenhum → pronto para carimbar → feito) de cada quadrado, e com a UNIÃO dos resgates. Quadrado cuja missão foi trocada num dos jogos não mistura. Cartela que o outro jogo não tem chega com o mundo, na virada.
- **Consequências:** o progresso não SOMA (dois jogadores na mesma missão: vale o maior), mas nada duplica. Um prêmio resgatado por qualquer um fica resgatado para todos (o item vai para a mochila de quem resgatou).
- **Verificado (uma instância, `shared set bingo ...`):** progresso 5 depois 2 → fica 5; estado 1 depois 0 → fica 1; resgatado true depois false → fica true; autoteste da parte ok.

### D62 — Estante de troféus: só o host arruma
- **Status:** ✅ Decidido · **Data:** 2026-10-26 · **Decidido por:** IA (levantamento de riscos; evita perda de item)
- **Contexto:** a estante de troféus da casa (`TrophyRackManager`) guarda os troféus colocados nela (no save do host: 4 troféus, todos na estante; nenhum na mochila). Ela não é sincronizada porque o `FromSaveData` dela acumula (D42). Um troféu que o convidado ganha num festival vai para a mochila dele (individual); se ele o pusesse na estante, o troféu sairia da mochila e sumiria na virada do dia, quando chega a estante do host.
- **Alternativas:** (a) sincronizar a estante (precisa descobrir como aplicar sem acumular e testar com troféus de verdade); (b) não deixar o convidado abrir a estante, com um aviso.
- **Decisão:** (b), por ser segura e pequena: `FieldTrophyRack.Access` no jogo do convidado mostra "No multiplayer, só o host arruma a estante de troféus. Guarde os seus troféus na mochila." e não abre. O host arruma normalmente. Fica para depois sincronizar a estante, se o grupo quiser.

### D63 — Revisão independente do núcleo: correções
- **Status:** ✅ Decidido · **Data:** 2026-10-26 · **Decidido por:** IA (segunda revisão por um agente sem contexto, nos módulos centrais: virada do dia, entrada/saída, perfis, relógio, dinheiro, correio, fazenda, animais; cada achado conferido no código antes de corrigir)
- **Correções:**
  1. **Voltar ao título encerra a sessão.** Antes o modo continuava "convidado"/"host" e o save carregado depois mandava o dinheiro, o correio e a mochila DELE para o mundo do grupo (e, no host, os perfis de outra fazenda sobrescreviam os desta). Junto: a **entrada automática** passa a usar a escolha atual do painel do título (antes o modo era lido uma vez ao abrir o jogo e "Sozinho" decidia no primeiro quadro; trocar no título só valia depois de reiniciar) e volta a valer a cada ida ao título.
  2. **Perfil do convidado comprimido.** O texto do perfil já tinha ~40 KB num convidado novo e o limite de um texto no pacote é 64 KB: depois de semanas de jogo o envio estouraria, o "dormindo" do convidado não sairia (impasse toda noite) e "Desconectar" falharia. Agora vai em gzip, com tamanho em int.
  3. **Blocos de bytes com tamanho int em todas as mensagens** (fazenda, mapa inteiro, animais, correio, armazéns, partes compartilhadas): o `PutBytesWithLength` do LiteNetLib usa ushort e cortava, sem avisar, o que passasse de 64 KB.
  4. **Mensagens guardadas em vez de descartadas** (`Net/HeldMessages`): quando um módulo não está pronto (convidado carregando o mundo, host trocando de mapa, convidado durante a virada), as mensagens ficam numa fila e são aplicadas em ordem quando ele fica pronto. Antes eram descartadas: o host colhia de manhã enquanto o convidado carregava o mundo e, para o convidado, a colheita continuava no pé (duplicação); plantar enquanto o host passava por uma porta sumia na virada. No host, durante a virada do dia, nada é guardado (o dia novo substitui o mundo e estados do dia velho desfariam o processamento).
  5. **Perfil enviado logo depois de a mochila ou as ferramentas mudarem** (~1,5 s), não só a cada 60 s: quem caía logo depois de guardar itens no armazém voltava com eles também na mochila.
  6. **O convidado sabe que a virada começou** (`SleepStatus` leva um byte a mais): não levanta da cama nesse meio-tempo, não aplica o relógio do dia novo antes do mundo novo e os módulos guardam as mensagens para depois da recarga.
  7. **Desconectar (ou cair) deitado esperando os outros** não prende mais o personagem: o host, agora sozinho, vira o dia como no jogo sem o mod; o convidado levanta.
  8. **Captura do mundo nunca fica presa:** tempo-limite de 30 s; pedido de entrada que chega durante outra captura (ex.: a de recuperação) é atendido depois; durante a virada, a entrada espera o mundo novo; a virada espera uma captura em andamento; captura que falha tenta de novo.
  9. **GUID repetido é recusado** (dois jogadores dividiriam e apagariam o mesmo perfil; acontece se alguém copiar a pasta `BepInEx` de outro). Mundo cheio também é recusado com mensagem, e a mensagem de versão diferente agora diz as versões do mod.
  10. **Arquivo de perfis:** gravado num temporário e trocado (o anterior vira `.bak`); se não der para ler, usa o `.bak` e nunca sobrescreve um arquivo ilegível. O snapshot de recuperação grava os perfis em `recovery_profiles_*.json`, à parte (antes gravava o arquivo oficial no meio do dia, à frente do save oficial).
- **Ficou para depois (anotado):** animal/celeiro e correio aplicam o estado inteiro recebido; dois jogadores mexendo no mesmo celeiro no mesmo segundo podem desfazer um ao outro (precisa de mescla campo a campo; ver D64 se feita). Perfil aplicado na entrada não roda os dias que passaram enquanto o convidado estava fora (ex.: "já deu presente hoje" voltando dias depois).
- **Testado (uma instância):** plugin carrega; recuperação grava `recovery_profiles_*.json` e o oficial fica intacto; host deitado esperando outro jogador e "Desconectar" → o dia vira sozinho (1/02/22 → 1/02/23 06:05) e o personagem fica livre; voltar ao título hospedando → sessão encerrada; carregar o save → hospeda de novo pela entrada automática. As partes que dependem de duas pessoas (fila de mensagens, aviso da virada no convidado, GUID repetido) ficam para o teste com o usuário.
- Protocolo 23 (ainda não distribuído): `ProfileUpload`/`ProfileData` em gzip com tamanho int, blocos com tamanho int, `SleepStatus` com o byte "virada começou".
