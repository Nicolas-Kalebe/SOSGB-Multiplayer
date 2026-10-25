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
  - **primeira entrada (provisório):** o perfil inicial do convidado é o do **próprio save** que ele tinha carregado antes de entrar. Alternativa a confirmar com o usuário: começar "do zero", como os farmhands do Stardew;
  - **identidade:** `[Net] PlayerGuid`, gerado automaticamente;
  - **armazenamento:** o host guarda os perfis em `%APPDATA%\STORY OF SEASONS Grand Bazaar\SOSGB-MP\profiles_<fazenda>_<host>.json`, gravado **na virada do dia**, junto com o save oficial (D23). O convidado envia o perfil ao dormir, a cada 60 s e ao sair;
  - **virada do dia:** o convidado reaplica o próprio perfil e roda o `OnChangeDate` dos sistemas pessoais (`LikeabilityManager`, `TalkManager(ano)`, `BagItemStorage`).
- **Consequências:**
  - Se o host travar no meio do dia, os convidados perdem o progresso individual desde a última virada. É o mesmo comportamento do mundo (D23).
  - O `OnChangeDate` pessoal roda no convidado, não no host. Validar na Fase 4/6 que os contadores diários (presente/conversa) zeram certo.
