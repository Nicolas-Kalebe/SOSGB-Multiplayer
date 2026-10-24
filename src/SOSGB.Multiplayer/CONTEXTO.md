# CONTEXTO — `src/SOSGB.Multiplayer/`

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../../AGENTS.md).

Plugin principal do mod (BepInEx 6 IL2CPP, `net6.0`). GUID: `com.nicolaskalebe.sosgb.multiplayer`.

## Arquivos

| Arquivo | O que faz |
|---|---|
| `SOSGB.Multiplayer.csproj` | Referências ao BepInEx e ao interop do jogo (via `GamePath`). Copia a DLL para `BepInEx/plugins` após o build |
| `Plugin.cs` | Ponto de entrada (`BasePlugin.Load`). Registra os componentes |
| `DebugOverlay.cs` | Janela de diagnóstico (F8): dinheiro, relógio (com estado ▶/⏸), mapa atual e posição do player |

## Estado atual (v0.0.1)

Fase de **validação**: só lê o estado do jogo, sem alterar nada e sem rede. Objetivo: confirmar que os singletons mapeados em [`CODIGO.md`](../../CODIGO.md) funcionam em runtime:
- `ExternalData.Instance.UserInfo.PlayerMoney`
- `DateManager.Instance.Now` / `IsPlay()`
- `FieldManager.Instance.CurrentFieldId`
- `GameController.Instance.GetPlayerTransform()`

Acesso aos singletons: todos herdam de `BokuMono.Utility.Singleton<T>` ou `SingletonMonoBehaviour<T>`, com `Instance` e `IsInstance` estáticos. **Sempre cheque `IsInstance` antes**, porque eles só existem com um save carregado.

## Convenções

- Componentes Unity próprios herdam de `MonoBehaviour`, têm o construtor `(IntPtr ptr) : base(ptr)` e são registrados com `AddComponent<T>()` no `Plugin.Load` (o BepInEx injeta o tipo no IL2CPP).
- Patches Harmony (próximas versões) ficam em `Patches/`, um arquivo por classe do jogo, com comentário citando o método alterado e a decisão (`Dx`) que motivou o patch.
- O jogo usa `Unity.InputSystem`. O `Input` legado pode não funcionar e é tratado com fallback.
