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
| `ClockDiagnosticsPatch.cs` | `DateManager.Pause/Resume`, `GameController.Pause/Resume/PauseTalk/ResumeTalk` | **Só diagnóstico** (postfix): registra chamadas com stack trace | Tarefa 1.1 | Ativo só com `[DevBridge] Enabled` |
| `../WorldSnapshot.cs` (classes aninhadas) | `SaveDataAccessorWindows.SlotSave/SlotSystemSave/SlotLoad` | Só quando armado: desvia o save para a memória / injeta o snapshot no load | Tarefa 1.5 (base da Fase 3) | — |
