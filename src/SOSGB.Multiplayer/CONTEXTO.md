# CONTEXTO — `src/SOSGB.Multiplayer/`

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../../AGENTS.md).

Plugin principal do mod (BepInEx 6 IL2CPP, `net6.0`). GUID: `com.nicolaskalebe.sosgb.multiplayer`.

## Arquivos

| Arquivo | O que faz |
|---|---|
| `SOSGB.Multiplayer.csproj` | Referências ao BepInEx e ao interop do jogo (via `GamePath`). Copia a DLL para `BepInEx/plugins` após o build |
| `Plugin.cs` | Ponto de entrada (`BasePlugin.Load`). Registra os componentes |
| `DebugOverlay.cs` | (Só no modo de teste) |
| `DebugOverlay.cs` (antigo) | Janela de diagnóstico (F8): dinheiro, relógio (com estado ▶/⏸), mapa atual e posição do player |
| `GameState.cs` | Foto do estado do jogo (usada pelo overlay e pela ponte) |
| `DevBridge.cs` | Ponte de teste para IA (D20): comandos em 127.0.0.1, log `sosgb-state.jsonl`, screenshots, auto-load |
| `PngWriter.cs` | Codificador PNG próprio, porque o `EncodeToPNG` da Unity quebra no interop |
| `PlayerContext.cs` | Troca de contexto por jogador (D3/D13): snapshots em JSON dos managers do jogo |
| `WorldSnapshot.cs` | Snapshot do mundo via pipeline de save do jogo (patches em `SaveDataAccessorWindows`) |
| `MultiplayerUI.cs` | D27: painel do título (modo/nome/IP), lista de jogadores no jogo, menu F9. Controles próprios (o IMGUI do jogo foi podado) |
| `AppearanceLab.cs` | (Só testes) laboratório de aparência: sonda da montagem, troca de animação de gênero, "jogador falso" (comandos `applab probe/motion/make/build/set/cat`) |
| `CoroutineProbe.cs` | Roda uma corrotina IL2CPP do jogo passo a passo (descendo nas aninhadas), com tempo limite de verdade e relatório de onde travou (D32) |
| `GameNotify.cs` | D26: avisos no sistema de avisos do jogo (aviso de correio com texto trocado) |
| `Notice.cs` | Aviso curto na tela (via overlay) |
| `StartupGuard.cs` | D25: ativa a janela do jogo se ela ficar presa no Launcher (sem clique/alt-tab) |
| `FocusGuard.cs` | Mantém `runInBackground` (D21) |
| [`Net/`](Net/CONTEXTO.md) | Camada de rede (LiteNetLib) |
| [`Patches/`](Patches/CONTEXTO.md) | Patches Harmony nos métodos do jogo |

## Estado atual (v0.0.3)

Fase de **validação**, ainda sem rede. Lê o estado do jogo, a ponte de teste executa comandos, e há um patch (D21). Objetivo: confirmar que os singletons mapeados em [`CODIGO.md`](../../CODIGO.md) funcionam em runtime:
- `ExternalData.Instance.UserInfo.PlayerMoney`
- `DateManager.Instance.Now` / `IsPlay()`
- `FieldManager.Instance.CurrentFieldId`
- `GameController.Instance.GetPlayerTransform()`

Acesso aos singletons: todos herdam de `BokuMono.Utility.Singleton<T>` ou `SingletonMonoBehaviour<T>`, com `Instance` e `IsInstance` estáticos. **Sempre cheque `IsInstance` antes**, porque eles só existem com um save carregado.

## Convenções

- Componentes Unity próprios herdam de `MonoBehaviour`, têm o construtor `(IntPtr ptr) : base(ptr)` e são registrados com `AddComponent<T>()` no `Plugin.Load` (o BepInEx injeta o tipo no IL2CPP).
- Patches Harmony (próximas versões) ficam em `Patches/`, um arquivo por classe do jogo, com comentário citando o método alterado e a decisão (`Dx`) que motivou o patch.
- O jogo usa `Unity.InputSystem`. O `Input` legado pode não funcionar e é tratado com fallback.
