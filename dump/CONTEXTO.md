# CONTEXTO — `dump/`

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../AGENTS.md).

> ⚠️ **CONTEÚDO DO JOGO — NÃO DISTRIBUIR, NÃO COMMITAR EM REPOSITÓRIO PÚBLICO.** Coloque `dump/` no `.gitignore` se o projeto virar um repositório git.

## O que é

Classes do jogo extraídas pelo Cpp2IL (ver [`tools/CONTEXTO.md`](../tools/CONTEXTO.md) para o comando exato). É material de **referência** para escrever o mod: nomes de classes, campos, offsets e assinaturas de métodos.

## Estrutura

```
dump/
└── cs/                       ← saída do Cpp2IL (--output-as diffable-cs)
    └── DiffableCs/
        ├── Assembly-CSharp/  ← CÓDIGO DO JOGO (4.219 arquivos), quase tudo em BokuMono/
        ├── BokuMono.*/       ← utilitários da série
        ├── UnityEngine.*/    ← engine
        └── ...               ← libs de terceiros (UniTask, DOTween, Newtonsoft.Json...)
```

**Exceção à regra de `CONTEXTO.md` por pasta:** a árvore `cs/` é **gerada automaticamente** (centenas de subpastas). Ela é documentada **só por este arquivo** e é recriada rodando o Cpp2IL de novo. Não crie nem edite arquivos dentro dela.

## Limitações importantes

- **Só há assinaturas**, sem o corpo dos métodos. Para saber *o que* um método faz, é preciso testar em runtime (BepInEx + log) ou desmontar no Ghidra/IDA.
- Os offsets (`//Field offset: 0x..`) valem só para esta build do jogo (arquivos de 2026-06-02). **Se o jogo atualizar, gere o dump de novo.**

## Por onde começar

O resumo das classes importantes está em [`CODIGO.md`](../CODIGO.md). Pontos de entrada principais (em `Assembly-CSharp/BokuMono/`):
- `UserInfo.cs`, `UserInfoMediator.cs`, `SaveData/` (save)
- `DateManager.cs` (relógio)
- `GameController.cs` (dinheiro, dia, player)
- `LikeabilityManager.cs`, `FamilyManager.cs` (amizade e casamento)
- `ScheduleManager.cs`, `Data/NpcSchedule.cs` (rotinas de NPC)
- `NetworkSetting.cs` (restos do multiplayer removido)
