# CONTEXTO — `src/SOSGB.Multiplayer/Patches/`

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../../../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../../../AGENTS.md).

Patches **Harmony** (HarmonyX) que alteram métodos do jogo. Todos são aplicados no `Plugin.Load` via `new Harmony(Guid).PatchAll(...)`.

## Convenções

- **Um arquivo por classe do jogo**: `<ClasseDoJogo>Patch.cs`.
- O comentário `<summary>` de cada patch diz:
  - **qual método do jogo** é alterado (`Classe.Metodo(params)`);
  - **por que** (comportamento original × comportamento desejado);
  - **qual decisão** motivou (`Dx`);
  - **qual configuração** liga ou desliga, se houver.
- Prefira `Prefix` retornando `true` (executa o original) na maioria dos casos. Retorne `false` só quando for intencional pular o método.
- Todo patch novo entra na tabela abaixo.

## Patches

| Arquivo | Método do jogo | Efeito | Decisão | Config |
|---|---|---|---|---|
| `AppManagerPatch.cs` | `BokuMono.AppManager.OnFocusChanged(bool)` | Ignora a perda de foco, que zeraria o `Time.timeScale` e pararia a música | [D21](../../../DECISOES.md) | `[Game] KeepRunningWhenUnfocused` (padrão `true`) |
| `MarriageDiagnosticsPatch.cs` | `LikeabilityManager.IsPartner/PartnerCharaId/IsMarried` e afins | **Só diagnóstico** (comando `marrydiag on`): registra quem consulta o casamento (D45) | 6.5 | Só no modo de teste |
| `BazaarDiagnosticsPatch.cs` | `BazaarManager.Sell/ActionAccessShopShelf/SelectExhibit/VisitorBuyEnd/AddBazaarResultData/UpdateCurrentShopShelf`, `UIBazaarMainPage.OpenMiniDeck` | **Só diagnóstico** (prefix): registra o que o jogo chama ao vender/repor (D56) | D56 | Só no modo de teste |
| `SkipLogosPatch.cs` | `CompanyLogoAnimation.EnterPhase`, `CompanyLogo.Update` | Pula as logos de abertura (fase vira FadeOut, transições em 0 s, fundo transparente) | D53 | `[Game] SkipIntroLogos` (padrão `true`) |
| `PauseMenuPatch.cs` | `UIPausePage.OnDecide` | No multiplayer, "Salvar" (1130) e "Carregar" (1140) mostram aviso em vez de abrir | D48 | Sempre (só age conectado) |
| `InputInjector.cs` | `PlayerCharacter.GetHumanActionInput` | **Só testes** (postfix): injeta a intenção "usar ferramenta" (Tool/ToolUp), porque o clique sintético do mouse não chega ao sistema de entrada do jogo. Comando `tool` da ponte | Fase 5 | Só no modo de teste |
| `ClockDiagnosticsPatch.cs` | `DateManager.Pause/Resume`, `GameController.Pause/Resume/PauseTalk/ResumeTalk` | **Só diagnóstico** (postfix): registra chamadas com stack trace | Tarefa 1.1 | Ativo só com `[DevBridge] Enabled` |
| `../WorldSnapshot.cs` (classes aninhadas) | `SaveDataAccessorWindows.SlotSave/SlotSystemSave/SlotLoad` | Só quando armado: desvia o save para a memória / injeta o snapshot no load | Tarefa 1.5 (base da Fase 3) | — |
| `SleepDiagnosticsPatch.cs` | `PlayerCharacter.ActionSleep/StartNextDay`, `GameController.StartNextDay/EndToday/PreChangeDate/PostChangeDate/FadeStartNextDay/IsSleeping` | **Só diagnóstico** (prefix): registra a ordem das chamadas | Tarefas 3.4/3.5 | Ativo só com `[DevBridge] Enabled` |
| `../Net/DaySync.cs` (aninhado) | `GameController.StartNextDay` | No multiplayer, segura a virada de quem deita até todos dormirem (host libera) | D12, D23, tarefas 3.4/3.5 | — (só com rede ativa) |
| `../GameNotify.cs` (aninhado) | `UISideInfomationModerate.SetData` | Se o aviso é o de correio com a contagem-marcador 9731, troca o texto pelo do mod | D26 | — |
| `../Net/ClockSync.cs` (aninhado) | `DateManager.IsUpdateEnabled` (getter) | Guarda o valor natural (este jogador em menu/evento) e força `false` quando o grupo está pausado (alguém no menu de pausa) | D50 | — |
