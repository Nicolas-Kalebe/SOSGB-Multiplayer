# CONTEXTO — raiz do projeto `SOSGB-Multiplayer`

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](AGENTS.md).

## O que é

Mod de multiplayer co-op para **STORY OF SEASONS: Grand Bazaar** (Marvelous, 2025), no modelo do **Stardew Valley**:
- até 4 jogadores via LAN, com o host dono do mundo e do save;
- compartilhados: fazenda, armazém, casa, dinheiro e correio;
- individuais: mochila, ferramentas e upgrades, corações com NPCs e casamento.

## Estado atual (2026-10-24)

- **Fase:** validação em runtime. Plugin v0.0.3 em `src/`, com ponte de teste (D20) para a IA testar sozinha e o jogo sem pausa ao perder o foco (D21).
- **Próximo passo:** ver "Estado atual" no `ROTEIRO.md`.

## Jogo

- Instalação do usuário: `L:\Games\STORY OF SEASONS Grand Bazaar`
- Unity `6000.0.28f1`, IL2CPP, `global-metadata.dat` v31 sem criptografia.
- **Saves:** `%APPDATA%\STORY OF SEASONS Grand Bazaar\<SteamID>\` (não ficam na pasta do jogo)
- Código principal no namespace `BokuMono` (`Assembly-CSharp.dll`).

## Repositório

- GitHub (público): https://github.com/Nicolas-Kalebe/SOSGB-Multiplayer · branch `main`
- Local: `Documents/repositorios/SOSGB-Multiplayer`
- **Não sobem:** `dump/`, `backups/`, binários de `tools/` e arquivos do jogo (ver `.gitignore` e [D18](DECISOES.md#d18--repositório-git-público-e-o-que-não-sobe)).

## Estrutura

| Caminho | O que é |
|---|---|
| [`README.md`](README.md) | Página do GitHub |
| [`.gitignore`](.gitignore) | Bloqueia arquivos do jogo, saves e dump |
| [`AGENTS.md`](AGENTS.md) | Regras para IAs (autodocumentação) |
| [`CLAUDE.md`](CLAUDE.md) | Ponte para o Claude Code |
| [`ROTEIRO.md`](ROTEIRO.md) | **Planejamento oficial**: o que fazer e em que ordem |
| [`PLANO.md`](PLANO.md) | Plano: decisões (resumo), arquitetura, checklist, riscos, roadmap |
| [`DECISOES.md`](DECISOES.md) | Decisões completas com contexto |
| [`CODIGO.md`](CODIGO.md) | Mapa das classes do jogo |
| [`docs/`](docs/CONTEXTO.md) | Documentação técnica (protocolo de rede) |
| [`src/`](src/CONTEXTO.md) | **Código do mod** (plugin BepInEx) |
| [`scripts/`](scripts/CONTEXTO.md) | Scripts para a IA testar o jogo sozinha (ponte de teste, ciclo de build) |
| [`tools/`](tools/CONTEXTO.md) | Ferramentas externas (Cpp2IL) |
| [`dump/`](dump/CONTEXTO.md) | Classes do jogo extraídas, **não distribuir** |
| [`backups/`](backups/CONTEXTO.md) | Backups dos saves + **como restaurar** (o jogo original fica em `L:\Backups\SOSGB`) |

