# Revisão externa (A01–A12) — situação e o que falta

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../AGENTS.md).

Uma revisão crítica do código feita por outra LLM (só leitura, sem rodar o jogo) sobre o commit `c7cd5e1` (v0.6.3, protocolo 27), de 2026-10-27. O documento original ficou com o usuário (fora do repositório); aqui fica o resumo de cada achado e o que foi feito. Atualizado em 2026-10-28 (commit `aee3310`, v0.6.5, protocolo 29).

Legenda: ✅ corrigido e testado com duas instâncias · ⏳ falta fazer · 🟨 limitação aceita / depende de regra do usuário.

## Situação de cada achado

| ID | Achado da revisão | Situação | Onde |
|---|---|---|---|
| A01 | Ferramenta no moinho sem dono: dois jogadores com o mesmo tipo de upgrade podiam pegar a do outro | ✅ dono por encomenda (PlayerGuid, guardado pelo host) | [D85](../DECISOES.md#d85--dono-por-encomenda-de-ferramenta-no-moinho-revisão-a01) |
| A02 | Consumo do armazém sem a tela aberta (cozinhar) era descartado | ✅ manda só a diferença; a tela de cozinhar também trava o armazém | [D88](../DECISOES.md#d88--armazém-mexido-sem-a-tela-aberta-manda-a-diferença-revisão-a02) |
| A03 | Sem fechamento antes de virar o dia: mudança recente do convidado podia sumir | ✅ o convidado manda tudo o que falta antes do "dormi" | [D87](../DECISOES.md#d87--fechamento-do-dia-antes-de-dormir-revisão-a03) |
| A08 | Parte da SharedSync desligada em silêncio depois de um erro | ✅ tenta de novo, avisa na tela e volta ao reconectar | [D86](../DECISOES.md#d86--parte-da-sharedsync-com-erro-tenta-de-novo-avisa-e-volta-na-sessão-seguinte-revisão-a08) |
| A04 | Capturas completas (moinho, mel, cogumelos, animais/celeiros) podem apagar ações feitas ao mesmo tempo por dois jogadores | ✅ versões + mescla em três vias no SharedSync e nos animais (celeiros como antes). Falta: reserva de item único (dois retirando o mesmo produto pronto ficam os dois com ele) | [D91](../DECISOES.md#d91--mudanças-ao-mesmo-tempo-não-se-apagam-versões-e-mescla-em-três-vias-revisão-a04) |
| A12 | Coleta disputada: a devolução só funciona se o item ainda estiver na mochila (procura por tipo e qualidade); vendido ou consumido antes → fica duplicado | ✅ devolução em cadeia (mochila → armazém → dinheiro); ovos também protegidos. Falta o teste real de dois pegando o mesmo ovo | [D92](../DECISOES.md#d92--devolução-da-coleta-disputada-em-cadeia-ovos-também-revisão-a12) |
| A06 | Happy Bingo: o progresso fica com o maior (não soma); cartela que só o convidado tem não chega ao host; a seleção de cartela não é transmitida; prêmios resgatados por um valem para todos, mas sem conceder o item a cada um | ⏳ (a regra "compartilhado" já foi escolhida: P17/D61) | `SharedSync.CaptureBingo/MergeBingo` |
| A07 | Títulos (AwardDatas) substituídos inteiros: se cada um concluir um título diferente ao mesmo tempo, um pode sumir; títulos dão recompensa pelo correio | ⏳ | `SharedSync.ApplyAwards` |
| A11 | Conversa/noivado ao mesmo tempo: o aviso de "morador ocupado" sai depois que a conversa começou (postfix de `TalkStart`), não é uma permissão do host | ⏳ | `NpcSync` (D68), `RomanceSync` |
| A05 | Família do convidado: casamento é individual, mas filho, cônjuge em casa e encontros (`FamilyData`, `DateEventSaveData`) ficam no mundo do host | 🟨 casamento em espera no roteiro (D45) | `PlayerContext` |
| A09 | Duas compras simultâneas acima do saldo podem sair parcialmente de graça (dinheiro e Happy Energy) | 🟨 limitação conhecida, aceitável entre amigos | `MoneySync` |
| A10 | Convidado que volta depois de vários dias: só uma virada de frescor é aplicada | 🟨 **depende de uma regra do usuário** (P19) | `PlayerProfiles.ApplyPending` |

**Coberturas ainda não testadas** (a revisão não achou falta de código, só não há teste): fundo de apoio do Bazaar, porão (Cellar) e virada de estação, Nature Sprites (contribuições de jogadores diferentes), concursos com item (suco, culinária), saída do vendedor do Bazaar com ajudante (P14), queda do host e consistência entre o mundo salvo e os perfis.

## Ordem sugerida (2026-10-28)

1. ~~Montar o pacote 0.6.5~~ ✅ (2026-10-28). Com a D91 o protocolo mudou de novo (30): montar o 0.6.6 antes do próximo teste com a amiga.
2. ~~A04 + A12~~ ✅ (D91, D92; 2026-10-28).
3. A07 e A06 (títulos e bingo).
4. A10, depois da resposta do usuário (P19).
5. O resto conforme aparecer no teste com a amiga.

## Como validar (da própria revisão)

O autoteste `shared test` só prova serialização: ele reaplica o mesmo estado. Para cada correção: duas instâncias com ações independentes, conferir host e convidado e os inventários (não só a tela), dormir, reconectar e recarregar, e testar com atraso de rede (`SOSGB_NET_LAG_MIN/MAX`, `SOSGB_NET_LOSS`). Marcar sempre: reproduzido, corrigido, testado ou ainda hipótese.
