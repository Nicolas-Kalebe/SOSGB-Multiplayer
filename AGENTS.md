# AGENTS.md — Regras para IAs neste projeto

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho. Toda IA (Claude, Codex, Copilot, Gemini etc.) que mexer aqui **deve** seguir as regras abaixo, sem precisar que o usuário peça.

Projeto: mod multiplayer co-op (estilo Stardew Valley) para **STORY OF SEASONS: Grand Bazaar** (Unity 6, IL2CPP).
Idioma da documentação: **português do Brasil** (termos técnicos e identificadores de código no original).

---

## Regras de autodocumentação

1. **Toda decisão é registrada** em [`DECISOES.md`](DECISOES.md) no formato da seção "Formato de decisão" abaixo, **com contexto**: o problema, as alternativas consideradas, o motivo da escolha e as consequências. Uma IA que chegar depois tem que entender o *porquê* sem ter lido a conversa original.
2. **A tabela-resumo de decisões** em [`PLANO.md`](PLANO.md) é atualizada junto (ID + resumo de uma linha + status).
3. **Cada pasta tem um `CONTEXTO.md`**, que explica:
   - o que a pasta contém e para que serve;
   - como os arquivos foram gerados (comando exato, se forem gerados);
   - as regras e cuidados específicos daquela pasta;
   - links para as decisões relevantes (`DECISOES.md#dX`).

   Ao criar uma pasta nova, crie o `CONTEXTO.md` **no mesmo momento**.
   - **Exceção:** pastas **geradas automaticamente** por ferramentas (por exemplo, a árvore do dump do Cpp2IL) são documentadas pelo `CONTEXTO.md` da pasta-mãe, e não em cada subpasta.
4. **Todo `.md` do projeto abre com o aviso de autodocumentação** (o bloco 🤖 no topo), para que qualquer IA que abra só aquele arquivo saiba das regras.
5. **Descobertas sobre o código do jogo** (classes, métodos, comportamentos confirmados em runtime) vão para [`CODIGO.md`](CODIGO.md). Marque o que é **inferido** (só assinatura) e o que foi **confirmado em runtime**.
6. **Ao mudar uma decisão, não apague a antiga.** Marque como `Substituída por Dx` e crie a nova entrada, porque o histórico do raciocínio importa.
7. **Código do mod:** comentários explicam o *porquê* (qual decisão, qual comportamento do jogo) e não o *o quê*. Patches Harmony citam a classe e o método do jogo que alteram e a decisão que os motivou.
8. **Git (repositório PÚBLICO):** antes de todo commit, rode `git status` e `git diff --cached --stat` e confirme que **nada** de `dump/`, `backups/`, `*.sav`, binários de `tools/` ou arquivos do jogo está sendo adicionado. Nunca use `git add -f` nesses caminhos. Não coloque dados pessoais nos `.md` versionados. Ver [D18](DECISOES.md).
9. **Ao terminar uma tarefa**, atualize a seção "Próximos passos" do `PLANO.md`.

## Formato de decisão (em `DECISOES.md`)

```markdown
### D<n> — <título curto>
- **Status:** ✅ Decidido | 🟡 Proposto | ❌ Rejeitado | 🔁 Substituída por D<m>
- **Data:** AAAA-MM-DD
- **Decidido por:** usuário | IA (delegado pelo usuário)
- **Contexto:** qual problema ou pergunta originou a decisão.
- **Alternativas:** o que foi considerado.
- **Decisão:** o que foi escolhido.
- **Motivo:** por que essa opção.
- **Consequências / pendências:** o que isso implica no código, riscos, o que testar.
```

---

## Mapa de arquivos

| Arquivo / pasta | Conteúdo |
|---|---|
| `README.md` | Página do repositório no GitHub |
| `.gitignore` | O que nunca sobe (D18) |
| `AGENTS.md` | Este arquivo: regras para IAs |
| `CLAUDE.md` | Ponte para o Claude Code (importa este arquivo) |
| `CONTEXTO.md` | Visão geral do projeto e da estrutura de pastas |
| `PLANO.md` | Objetivo, arquitetura, checklist, riscos, roadmap, próximos passos |
| `DECISOES.md` | Registro completo das decisões, com contexto |
| `CODIGO.md` | Mapa das classes do jogo relevantes para o mod |
| `src/` | Código do mod (ver `src/CONTEXTO.md`) |
| `scripts/` | Testes automáticos via ponte de teste (ver `scripts/CONTEXTO.md`) |
| `tools/` | Ferramentas externas (ver `tools/CONTEXTO.md`) |
| `dump/` | Classes do jogo extraídas, **não distribuir** (ver `dump/CONTEXTO.md`) |
| `backups/` | Backups dos saves e procedimento de restauração (ver `backups/CONTEXTO.md`) |

## Avisos legais

- **Nunca** commitar, publicar ou distribuir o conteúdo de `dump/` nem arquivos do jogo. Um mod publicado contém só o código do mod.
- Antes de qualquer teste que rode o jogo com o mod, fazer um **snapshot novo dos saves** em `backups/saves/` (os saves ficam em `%APPDATA%\STORY OF SEASONS Grand Bazaar`, não na pasta do jogo).
