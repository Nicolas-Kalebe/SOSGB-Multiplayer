# Riscos do multiplayer: o que no jogo pode conflitar no co-op

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../AGENTS.md).

Levantamento feito em 2026-10-26 (D57), para achar sistemas do jogo que podem quebrar ou duplicar coisas no co-op. Duas fontes:

1. **Inventário do save:** todas as partes que o jogo grava (`UserInfo`, ~80 campos), comparadas com o que o mod já sincroniza. Parte do mundo que não é sincronizada ao vivo se perde no fim do dia para os convidados, porque eles recebem o mundo do host a cada virada (D23).
2. **Wiki e guias** do remake (Fogu, listas de pedidos, notas de atualização): mecânicas que um grupo encontra jogando normalmente.

Legenda: ✅ coberto · 🛠️ feito nesta rodada · ⏳ a fazer · ❓ decisão do usuário (ver [`PERGUNTAS.md`](../PERGUNTAS.md)) · ➖ não precisa.

## 1. Inventário do save (`UserInfo`)

| Parte do save | O que é | Situação |
|---|---|---|
| `PlayerMoney` | Dinheiro | ✅ D36 (carteira compartilhada) |
| `PlayerHappyPoint`, `happyPointData`, `happyPointHistoryData` | **Happy Energy** (moeda dos Nature Sprites / Happy Shoppe) | 🛠️ D57: carteira compartilhada, como o dinheiro (❓ P11) |
| `MissionData` | **Pedidos dos moradores** (requests): estado e progresso de cada pedido | 🛠️ D57: compartilhado, progresso somado entre os jogadores |
| `TutorialSaveData`, `PlayableTutorialReadData` | Tutoriais já lidos | 🛠️ D57: união (quem leu, ninguém vê de novo; sem isso o convidado via o mesmo tutorial todo dia) |
| `EventReadData` | Eventos já vistos (`EventManager.EventInfo`: lidos, histórico, tipos de casamento) | ❓ P15: hoje ficam no mundo do host (o convidado não vê o que o host viu e revê o que só ele viu); proposta de guardar por jogador |
| `BrandItemIds`, `BrandAnimalIds` | Selo BRAND (preço 3x) ganho nos festivais | 🛠️ D57: união |
| `BingoData` | Happy Bingo (cartelas) | ⏳ grande (~300 KB); fica do host por enquanto |
| `AchievementSaveData` | Conquistas | ⏳ |
| `BazaarSales`, `TotalSellShopPrice`, contadores `*OneDayAchievement`, `SuspiciousHoneyCount`, `SuspiciousMushroomCount`, `BarrelJumpCount` | Contadores de títulos/conquistas | ⏳ (afetam títulos, não o jogo do dia a dia) |
| `NpcFoodData`, `ScheduleData` | Rotina e comida dos NPCs | ➖ mundo do host a cada dia |
| `FamilyData`, `DateEventSaveData` | Casamento, filhos, encontros | ⏳ ver D44/D45 (romance individual) |
| `ExcavateData`, `HorseRacingData`, `AccessObjectHistory`, `AnimalHistory`, `HarvestedIrregularCropIdList`, `PictureBookCompleteData` | Escavação, corridas, históricos | ⏳ baixa prioridade |
| `TrophyRackData` | Troféus | ⏳ o `FromSaveData` acumula (D42); precisa de outro caminho |
| `SaveDataSeed` | Semente do sorteio (clima, etc.) | ✅ vem no mundo do host, igual para todos |
| Demais (fazenda, animais, pets, cavalo, armazéns, deck, correio, moinhos, cogumelos, abelhas, vasos, ampliações, prêmios, adega, banho, receitas, Corobockles, peixes, reforços, lojas, festivais, decoração e progresso do Bazaar, flags de evento, histórico de itens) | | ✅ D36–D55 |
| Mochila, ferramentas, amizade, conversa, aparência, bônus | Individuais | ✅ perfil de cada jogador (D24/D30) |

## 2. Mecânicas apontadas pela wiki

### Alta prioridade
- **Visitas da manhã com presentes** (ferramentas na 1ª semana; galinha grátis; pet grátis em 1 de verão; cavalo grátis em 14 de verão; receitas do Felix). Ferramentas são individuais: o convidado ganhar as dele está certo. **Animais** podiam duplicar se o evento rodasse em cada jogo → 🛠️ D57: só o host cria pets e cavalos novos no mundo (o host ignora pets/cavalos novos vindos de convidados). Galinhas novas já não são criadas ao vivo (D41).
- **Pedidos que desbloqueiam o mundo** (moinhos do Isaac, Emp-ore-ium, colmeias e cogumelos, galinhas perdidas...) → 🛠️ D57 (`MissionData` compartilhado). ❓ Contadores (20 presentes, 200 colheitas...) somam a ajuda de todos.
- **Rank do Bazaar e cenas da história** → ✅ progresso do Bazaar compartilhado (D54). Cenas: cada um vê no próprio jogo.
- **Clima e vento** → ✅ vêm da semente do mundo do host (mesmo clima para todos).
- **Upgrade de ferramenta nos moinhos** (o moinho é compartilhado, a ferramenta é individual): quem retirar a ferramenta pronta leva para a mochila dele. ❓ P12.
- **Compras únicas** (ampliação da mochila, pulo duplo...): estoque compartilhado. Ampliações valem para todos (D38/expansion); habilidades viram flag de evento (união). ➖
- **Happy Energy** → 🛠️ D57.
- **Desmaio e cenas que avançam a hora** → ✅ D50 (o tempo segue o mais adiantado) e D33 (virada só quando todos dormem). Desmaio fora de casa: ⏳ conferir que conta como "dormiu".

### Média prioridade
- **Festivais com competição** (Pet Show, Horse Derby, Animal/Crop Show, Cook-off): cada jogo roda o próprio minigame; resultados compartilhados (D43). ❓ Quem inscreve o pet/cavalo compartilhado.
- **Casamento com vários cônjuges na mesma casa** → ⏳ D45.
- **Coleta e mineração fora da fazenda** (regenera às 17h) → ✅ natureza compartilhada (D43, FieldSnapshot).
- **Títulos e conquistas pelo correio** → correio compartilhado (D39); risco de prêmio em dobro, ⏳ observar.

### Baixa prioridade
- Happy Bingo (por enquanto do host), insetos e peixes guardiões (cada jogo sorteia; enciclopédia comum), Wonderstones, Power Berry (vigor máximo compartilhado), recicláveis e medalhas (itens de mochila), contagem de Ano Novo e aniversários.

## Fontes
- Guia do remake na Fogu: https://fogu.com/sos6/index.html (bazar, moinhos, calendário, festivais, casamento, mineração, Happy Shoppe, Felix's Findings, Garon, Wilbur)
- Lista de pedidos: https://xboxplay.games/story-of-seasons-grand-bazaar/list-of-all-requests-in-story-of-seasons-grand-bazaar-66789
- Happy Energy: https://digitalphablet.com/how-to-get-and-use-happy-energy-through-completing-and-solving-in-story-of-seasons-grand-bazaar/
- Happy Bingo (1.1.0): https://www.gematsu.com/2025/10/story-of-seasons-grand-bazaar-version-1-1-0-update-now-available-adds-happy-bingo
