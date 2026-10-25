# SOSGB-Multiplayer

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](AGENTS.md).

Mod de **multiplayer co-op** para **STORY OF SEASONS: Grand Bazaar** (PC), no estilo do Stardew Valley.

- Até 4 jogadores via LAN, com o host dono do mundo e do save.
- **Compartilhado:** fazenda, armazém, casa, dinheiro e correio.
- **Individual:** mochila, ferramentas e upgrades, corações com NPCs e casamento.

**Status:** 🧪 Fase 1 — validação técnica. O plugin carrega no jogo e lê e controla o estado (dinheiro, relógio, saves); a rede ainda não começou. Veja o [`ROTEIRO.md`](ROTEIRO.md).

## Documentação

| Arquivo | Conteúdo |
|---|---|
| [`CONTEXTO.md`](CONTEXTO.md) | Visão geral e estrutura do projeto |
| [`ROTEIRO.md`](ROTEIRO.md) | O que vai ser feito e em que ordem |
| [`PLANO.md`](PLANO.md) | Arquitetura, checklist, riscos e roadmap |
| [`DECISOES.md`](DECISOES.md) | Todas as decisões, com contexto |
| [`docs/INSTALACAO.md`](docs/INSTALACAO.md) | **Como instalar e jogar** (versão de teste) |
| [`CODIGO.md`](CODIGO.md) | Mapa das classes do jogo usadas pelo mod |
| [`AGENTS.md`](AGENTS.md) | Regras para IAs que trabalham no projeto |

## Stack

BepInEx 6 (IL2CPP) · HarmonyX · LiteNetLib · Newtonsoft.Json

## Aviso

Projeto de fãs, sem afiliação com a Marvelous Inc. Este repositório contém **apenas o código do mod**. Nenhum arquivo ou código do jogo é distribuído aqui. Para usar o mod, você precisa ter o jogo.
