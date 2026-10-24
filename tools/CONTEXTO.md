# CONTEXTO — `tools/`

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../AGENTS.md).

## O que é

Ferramentas externas de terceiros usadas no projeto. **Não é código do mod.**

| Arquivo | Ferramenta | Versão | Origem | Para que serve |
|---|---|---|---|---|
| `Cpp2IL.exe` | Cpp2IL | `2022.1.0-pre-release.21` | https://github.com/SamboyCoding/Cpp2IL/releases | Lê `GameAssembly.dll` + `global-metadata.dat` e gera as classes do jogo (sem o corpo dos métodos) |

## Como foi usado

Rodado a partir da raiz do projeto (gera a pasta `dump/cs`):

```bash
./tools/Cpp2IL.exe --game-path "L:/Games/STORY OF SEASONS Grand Bazaar" --exe-name SOSGrandBazaar --output-as diffable-cs --output-to dump/cs
```

- Leva uns 40 segundos. **Não altera a pasta do jogo**, só lê os arquivos.
- Escolhido no lugar do Il2CppDumper porque suporta metadata v31 (Unity 6).

## Cuidados

- Ao atualizar uma ferramenta, atualize a tabela acima e anote o motivo em `DECISOES.md`.
- Ferramentas futuras previstas: BepInEx 6 IL2CPP (vai na pasta do jogo, não aqui), dnSpy/ILSpy.
