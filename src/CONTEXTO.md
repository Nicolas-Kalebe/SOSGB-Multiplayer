# CONTEXTO — `src/`

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../AGENTS.md).

Código-fonte do mod. Um projeto por subpasta.

| Pasta | O que é |
|---|---|
| [`SOSGB.Multiplayer/`](SOSGB.Multiplayer/CONTEXTO.md) | Plugin BepInEx 6 (IL2CPP) principal |

## Pré-requisitos para compilar

1. **.NET SDK 6 ou mais novo** (testado com o 9). O alvo é `net6.0`, que é o runtime que o BepInEx 6 IL2CPP traz na pasta `dotnet/` do jogo.
2. **BepInEx 6 IL2CPP instalado no jogo e executado pelo menos uma vez.** É a primeira execução que gera `BepInEx/interop/`, as DLLs C# com as classes do jogo que o projeto referencia. Versão usada: `6.0.0-be.791+b782da4` (https://builds.bepinex.dev/projects/bepinex_be).
3. O caminho do jogo é a propriedade `GamePath` no `.csproj` (padrão: `L:\Games\STORY OF SEASONS Grand Bazaar`). Para outro caminho:
   ```bash
   dotnet build src/SOSGB.Multiplayer -p:GamePath="X:\caminho\do\jogo"
   ```

## Build

```bash
dotnet build src/SOSGB.Multiplayer
```

O build copia `SOSGB.Multiplayer.dll` para `<jogo>/BepInEx/plugins/SOSGB.Multiplayer/`. Feche o jogo antes, senão a DLL fica travada.

## Regras

- `bin/` e `obj/` não são versionados (ver `.gitignore`).
- **Nunca** copie DLLs de `BepInEx/interop/` para dentro do repositório, porque são geradas a partir do código do jogo (D18).
