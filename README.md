# SOSGB-Multiplayer

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](AGENTS.md).

Mod de **multiplayer co-op** para **STORY OF SEASONS: Grand Bazaar** (PC), no estilo do Stardew Valley.

- Até 4 jogadores pela rede (LAN, ou LAN virtual como o Radmin VPN), com o host dono do mundo e do save.
- **Compartilhado:** fazenda, animais, armazém, casa, dinheiro, Happy Energy, correio, pedidos dos moradores, festivais e o progresso do mundo.
- **Individual:** mochila, ferramentas e upgrades, visual, corações com os moradores, cenas já vistas e casamento.

**Status:** 🧪 **v0.6.7, versão de teste com amigos** (protocolo 30). As fases 0 a 4 do [`ROTEIRO.md`](ROTEIRO.md) estão concluídas, e boa parte da 5 à 8 também. Tudo o que está abaixo foi testado com duas cópias do jogo abertas ao mesmo tempo; falta a sessão longa com pessoas de verdade.

## O que já funciona

- **Entrar no mundo do amigo** pela tela de título (*Entrar agora*), **sem precisar de save**: na primeira vez abre a criação de personagem do jogo. O convidado começa do zero, como no Stardew.
- **Ver os outros jogadores** andando, animando e com o próprio visual (rosto, pele, cores, roupas). Guarda-roupa e espelho funcionam para todos.
- **Relógio do grupo:**
  - o tempo segue para todos;
  - o menu de pausa (Esc) para todo mundo, inclusive os moradores, com aviso de quem pausou;
  - um relógio que ficasse preso é religado sozinho (o jogo tem um caso em que dois "pausar" seguidos travam o tempo para sempre);
  - em dia de festival, diálogos e cenas de qualquer um param o tempo;
  - para virar o dia, todos dormem; quem já está na cama espera os outros e pode espiar quem ainda está acordado (F7).
- **Fazenda:** plantar, regar, colher, cortar e quebrar, itens largados no chão e ovos (quem pega depois devolve: da mochila, do armazém ou em dinheiro).
- **Dois mexendo na mesma coisa ao mesmo tempo** (moinho, colmeias, cogumelos, cuidados do mesmo animal, títulos, bingo): as duas mudanças ficam; nada se apaga.
- **Animais:** cuidados, carinho do dia (só o primeiro conta, de quem for), ovos e ração do celeiro, pets e cavalo; animal comprado ou vendido aparece ou some na hora para todos.
- **Economia:** carteira e Happy Energy compartilhadas, lojas, armazém com trava (um de cada vez), correio. Compra ao mesmo tempo sem saldo para as duas: a de quem chegou depois é desfeita.
- **Moradores no mesmo lugar para todos:** quem chegou primeiro ao mapa manda a posição e a animação; os outros veem cópias (5–10 cm de diferença). Conversa com um jogador por vez (o morador para para os outros; começando juntos, o primeiro fica com ele), corações por jogador, romance exclusivo; **só o host noiva e casa** (convidados namoram).
- **Quem fica dias fora** volta com a mochila como deixou (nada estraga enquanto está fora).
- **Festivais:** resultados compartilhados; só um se inscreve.
- **Bazaar:** clientes iguais para todos, deck compartilhado, trava da barraca e modo ajudante (um vende e o outro atende, repõe e toca o sino).
- **Mapa (M)** com a posição dos outros jogadores.
- **Gerar log** (tela de título ou F9): um `.zip` na Área de Trabalho para mandar quando algo der errado.
- **Experimental (desligado):** assistir junto à cerimônia do festival. A cena já roda no jogo de quem assiste, com as falas passando junto; falta a saída da cena ([D84](DECISOES.md)).

## Como jogar

Veja [`docs/INSTALACAO.md`](docs/INSTALACAO.md): instalação do BepInEx e do mod, como hospedar e entrar, e o que fazer se der problema.

## Documentação

| Arquivo | Conteúdo |
|---|---|
| [`CONTEXTO.md`](CONTEXTO.md) | Visão geral e estrutura do projeto |
| [`ROTEIRO.md`](ROTEIRO.md) | O que vai ser feito e em que ordem; estado atual |
| [`PLANO.md`](PLANO.md) | Arquitetura, checklist, riscos e roadmap |
| [`DECISOES.md`](DECISOES.md) | Todas as decisões, com contexto e testes |
| [`PERGUNTAS.md`](PERGUNTAS.md) | Dúvidas em aberto para o dono do projeto |
| [`docs/INSTALACAO.md`](docs/INSTALACAO.md) | **Como instalar e jogar** (versão de teste) |
| [`docs/PROTOCOLO.md`](docs/PROTOCOLO.md) | Mensagens de rede e histórico de versões |
| [`docs/RISCOS-MULTIPLAYER.md`](docs/RISCOS-MULTIPLAYER.md) | Levantamento do que pode conflitar no multiplayer |
| [`CODIGO.md`](CODIGO.md) | Mapa das classes do jogo usadas pelo mod |
| [`AGENTS.md`](AGENTS.md) | Regras para IAs que trabalham no projeto |

## Stack

BepInEx 6 (IL2CPP) · HarmonyX · Il2CppInterop · LiteNetLib · .NET 6

## Aviso

Projeto de fãs, sem afiliação com a Marvelous Inc. Este repositório contém **apenas o código do mod**. Nenhum arquivo ou código do jogo é distribuído aqui. Para usar o mod, você precisa ter o jogo.
