# PERGUNTAS PARA O USUÁRIO

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](AGENTS.md).

Dúvidas e testes que dependem do usuário, anotados enquanto a IA trabalha sozinha. **Ao responder, a IA move a resposta para `DECISOES.md` e marca aqui como respondida.**

| # | Pergunta / pedido | Contexto | Status |
|---|---|---|---|
| P1 | **Com o que o amigo começa na 1ª vez que entra?** (a) com os dados do save dele ou (b) do zero, como no Stardew | Resposta: **do zero** ("tipo Minecraft") → [D30](DECISOES.md) | ✅ respondida |
| P2 | **Teste de 1 minuto:** dormir na cama de verdade (pelo menu da cama) com o mod, para confirmar que o caminho real passa por `GameController.StartNextDay` | 3.4/3.5 foram testados com o comando `nextday` | ⏳ aberta |
| P4 | **Aparência de cada jogador:** a função existe, mas é experimental e está **desligada** (`[Visual] SyncAppearance`), porque com algumas roupas/cabelos o personagem fica invisível e trocar gênero não funciona (D29). Quer que eu continue investindo nisso (pode levar tempo), ou prefere priorizar a jogabilidade (dinheiro, armazém, fazenda sincronizados)? | Resposta: **continuar na aparência** → [D31](DECISOES.md) | ✅ respondida |
| P5 | **Memória do PC:** testar com duas cópias do jogo ao mesmo tempo chegou perto do limite de 15 GB e o Claude Code interrompeu um teste. Se possível, fechar outros programas pesados quando a IA for testar | Em 2026-10-25 havia só 4,1 GB livres com o jogo fechado (Discord ~1,8 GB e VS Code ~1,7 GB eram os maiores). Cada instância usa ~2 GB | ⏳ aberta |
| P6 | **Teste em rede (ou com os amigos):** a aparência própria (D32) e o "começar do zero" (D30) foram testados só com uma instância. Quando der, um teste curto: o amigo entra pela 1ª vez (deve chegar com mochila vazia e ferramentas básicas) e cada um deve ver o outro com a própria aparência | Pode ser com os amigos (você avisa) ou a IA testa com duas instâncias se houver ~5 GB livres | ⏳ aberta |
| P3 | **Log da amiga:** o `BepInEx\LogOutput.log` do jogo dela, do teste em que ela não te via | Do lado do host estava tudo certo (ela entrou, o mundo foi enviado). A IA achou e corrigiu uma causa provável (ver `ROTEIRO.md`), mas o log confirmaria | ⏳ aberta |
