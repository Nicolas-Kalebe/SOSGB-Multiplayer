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
| A12 | Coleta disputada: a devolução só funciona se o item ainda estiver na mochila (procura por tipo e qualidade); vendido ou consumido antes → fica duplicado | ✅ devolução em cadeia (mochila → armazém → dinheiro), ovos também; dois colhendo o mesmo ovo testado a dois (D102: a devolução espera a mochila por 2 s) | [D92](../DECISOES.md#d92--devolução-da-coleta-disputada-em-cadeia-ovos-também-revisão-a12) |
| A06 | Happy Bingo: o progresso fica com o maior (não soma); cartela que só o convidado tem não chega ao host; a seleção de cartela não é transmitida; prêmios resgatados por um valem para todos, mas sem conceder o item a cada um | ✅ progresso soma, seleção e contadores compartilhados, cartela nova criada. Limites: dois +1 iguais ao mesmo tempo contam 1; cartela só de um lado não reproduzida ao vivo; prêmio segue a P17 | [D93](../DECISOES.md#d93--títulos-e-happy-bingo-progresso-soma-cartelas-por-número-seleção-e-contadores-compartilhados-revisão-a07a06) |
| A07 | Títulos (AwardDatas) substituídos inteiros: se cada um concluir um título diferente ao mesmo tempo, um pode sumir; títulos dão recompensa pelo correio | ✅ títulos diferentes ao mesmo tempo ficam os dois; progresso soma | [D93](../DECISOES.md#d93--títulos-e-happy-bingo-progresso-soma-cartelas-por-número-seleção-e-contadores-compartilhados-revisão-a07a06) |
| A11 | Conversa/noivado ao mesmo tempo: o aviso de "morador ocupado" sai depois que a conversa começou (postfix de `TalkStart`), não é uma permissão do host | ✅ o host decide quem chegou primeiro; o outro segue sem romance; registro de compromissos na hora | [D94](../DECISOES.md#d94--conversa-e-noivado-ao-mesmo-tempo-o-host-decide-quem-chegou-primeiro-revisão-a11) |
| A05 | Família do convidado: casamento é individual, mas filho, cônjuge em casa e encontros (`FamilyData`, `DateEventSaveData`) ficam no mundo do host | ✅ escolha do usuário: só o host noiva e casa; convidados namoram | [D97](../DECISOES.md#d97--só-o-host-noiva-e-casa-convidados-namoram-revisão-a05) |
| A09 | Duas compras simultâneas acima do saldo podem sair parcialmente de graça (dinheiro e Happy Energy) | ✅ o gasto de quem chegou depois é recusado e a compra desfeita (sem item para desfazer: corta em 0) | [D98](../DECISOES.md#d98--gasto-ao-mesmo-tempo-sem-saldo-a-compra-de-quem-chegou-depois-é-desfeita-revisão-a09) |
| A10 | Convidado que volta depois de vários dias: só uma virada de frescor é aplicada | ✅ regra do usuário (P19): a mochila congela enquanto ele está fora | [D95](../DECISOES.md#d95--quem-volta-depois-de-dias-fora-encontra-a-mochila-como-deixou-revisão-a10-p19) |

**Coberturas (2026-11-02):** Nature Sprites: a experiência por sprite agora soma as contribuições (D101, testado); fundo de apoio do Bazaar e porão analisados (D101: dados simples sob a mescla; dois doando na mesma janela pagam os dois). Queda do host testada (2026-11-02): o processo do host foi morto com a convidada na cidade; em 8 s ela detectou a queda ("desconectado do host: Timeout"), em 16 s voltou ao título com o aviso e sem erros (D59/D65). Saída do vendedor com ajudante (P14) testada por automação (2026-11-02): o host saiu do mapa no meio da venda e a ajudante assumiu a barraca, sem erros (o fechamento pelo menu e o fim do turno não foram reproduzidos). Concurso com item (Exposição de Colheitas, outono 25, `fest` 322): tentado por automação em 2026-11-02 — o host foi até o Félix e a conversa abriu ("fale comigo com sua colheita de inscrição em mãos"), mas a inscrição exige o item **em mãos** pela barra (tecla Q: abre a barra e o personagem segura o item selecionado; `bag select` da ponte não segura nada) e o Enter injetado só dispara o "Falar" com ≥160 ms; com a Menta em mãos o Félix respondeu numa página só (não é colheita) e, ao tentar trocar para o tomate, as setas/A-D da barra não registraram pela injeção e a janela de inscrição (12:00–15:59) acabou. Chegar ao Félix por `warp` direto deixa o personagem em `ForceCleanMove` (câmera presa): parar 3 m antes e andar com W resolve. A parte do mod num festival (uma inscrição só, tempo parado na cena, resultado compartilhado) foi testada com a Exposição de Animais e a contagem regressiva (D83/D84); falta só a inscrição com item, pela interface, com o usuário.

**Coberturas ainda não testadas** (a revisão não achou falta de código, só não há teste): fundo de apoio do Bazaar, porão (Cellar) e virada de estação, Nature Sprites (contribuições de jogadores diferentes), concursos com item (suco, culinária), saída do vendedor do Bazaar com ajudante (P14), queda do host e consistência entre o mundo salvo e os perfis.

## Ordem sugerida (2026-10-28)

1. ~~Montar o pacote 0.6.5~~ ✅ (2026-10-28). Com a D91 o protocolo mudou de novo (30): montar o 0.6.6 antes do próximo teste com a amiga.
2. ~~A04 + A12~~ ✅ (D91, D92; 2026-10-28).
3. ~~A07 e A06~~ ✅ (D93; 2026-10-28).
4. ~~A11~~ ✅ (D94; 2026-10-29). Pacote 0.6.6 montado.
5. A10, depois da resposta do usuário (P19).
5. O resto conforme aparecer no teste com a amiga.

## Como validar (da própria revisão)

O autoteste `shared test` só prova serialização: ele reaplica o mesmo estado. Para cada correção: duas instâncias com ações independentes, conferir host e convidado e os inventários (não só a tela), dormir, reconectar e recarregar, e testar com atraso de rede (`SOSGB_NET_LAG_MIN/MAX`, `SOSGB_NET_LOSS`). Marcar sempre: reproduzido, corrigido, testado ou ainda hipótese.
