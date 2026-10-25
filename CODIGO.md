# Mapa do código — STORY OF SEASONS: Grand Bazaar

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](AGENTS.md).

> Gerado a partir do dump do Cpp2IL (`dump/cs/DiffableCs/Assembly-CSharp`), que só traz assinaturas, sem o corpo dos métodos.
> Quase tudo fica no namespace `BokuMono`. Data do mapeamento: 2026-10-24.
> ⚠️ A pasta `dump/` tem código do jogo: **não distribuir**.

---

## 1. Save: objeto central ✅

- **`UserInfo`**: estado vivo do jogo, guardado em `ExternalData.UserInfo` (`ManagedSingleton<ExternalData>`).
- Contém: `PlayerName`, `FarmName`, `BazaarName`, `PlayerMoney`, `PlayerHappyPoint`, `PlayerHP/MaxHP`, `PlayerGender`, `DateTimeTicks`, `FieldId`, `FieldSaveData[] FieldSaveDatas`.
- Também guarda o `SaveData` de cada sistema: `LikeabilityData`, `FamilyData`, `TalkData`, `LetterData`, `EventFlagData`, `BagItemSaveData`, `BagToolSaveData`, `HouseStorageSaveData`, `ToolData`, `AnimalData`, `WindmillCraftingData`, `BazaarSaveData`, `ExpansionData`, `ScheduleData`…
- Cada manager tem `ToSaveData()` / `FromSaveData(SaveData)`.
- `UserInfoMediator.ApplyToUserInfo(...)` / `ApplyFromUserInfo()` juntam os dados dos managers e espalham de volta.
- Serialização (`SaveDataManager.Encode<T>` / `Decode<T>`): provavelmente JSON → deflate → XOR (`Obfuscation`). **Confirmar em runtime.**
- O estado do mundo fica em dados simples dentro dos managers (`ManagedSingleton<T>`); as cenas só exibem.

- **Arquivos no disco:** `%APPDATA%\STORY OF SEASONS Grand Bazaar\<SteamID>\` → `SaveData{0,1,2}.sav` (slots), `SaveSlotSystem{0,1,2}.sav` (cabeçalhos), `SaveSystem.sav` (configurações). Classe: `SaveDataAccessorWindows` (`SlotSave`/`SlotLoad`). ✅ Confirmado no disco.

**Para o mod:** snapshot do mundo para quem entra = `ToSaveData()` dos managers. A troca de contexto por jogador = `ToSaveData()` / `FromSaveData()`.

## 2. Relógio

- **`DateManager`** (MonoBehaviour singleton): `updateEnabled` (pausa global), `Now`, `TimeScale`.
- Métodos: `Pause()`, `Resume()`, `Play()`, `IsPlay()`, `AddHours(float)`, `SetDate(BokuMonoDateTime, bool)`, `ChangeDate(h,m,s)`, `SetChangeDateCallback(...)`, `ActivateEventDateTime(...)`.
- `BokuMonoDateTime`: struct com `long Ticks`, `Year/Month/Day/Hour/Season`, `IsBazaarDay()`.
- `GameController.Pauser`: congela animators e NavMesh (não o relógio). `PauseTalk(npc)` / `ResumeTalk()`.
- Virada do dia: `GameController.StartNextDay()`, `EndToday()`, `PreChangeDate()` / `PostChangeDate()`, `IsSleeping`. `PlayerCharacter.ActionSleep()` / `ActionWakeUp()`. Todo manager tem `OnChangeDate()`.

**Para o mod (D9):** patch em `DateManager.Pause()` / `Resume()` no host + envio de `Now.Ticks` aos clientes. **Para o D12:** interceptar `ActionSleep()` e só chamar `StartNextDay()` quando todos dormirem.

## 3. Dinheiro

- `UserInfo.PlayerMoney` (uint).
- `GameController.AddMoney(uint, bool)`, `SpendMoney(uint, bool, Action)`, `IsEnoughMoney(int)`.
- No multiplayer o mod **observa** `PlayerMoney` em vez de interceptar essas funções (D36): pega qualquer caminho de ganho/gasto.

## 4. Inventário, armazém e ferramentas

- `InventoryManager`: `BagItemStorage`, `BagToolStorage`, `HouseStorage`, `DeckStorage`, `ExhibitionShelf`, `BazaarCustomPartsStorage`.
- `StorageManager`: `PutIn(ItemData, out ItemData, bool)`, `PutOut(int slot, int stack)`, `Use(uint itemId, int stack, bool)`, `IsHaved(...)`.
- `ItemData`: `ItemId`, `Stack`, `QualityValue`, `FreshnessValue`, `WrapId`.
- Mochila: 32 slots. Armazém da casa: 24 parcelas (312 espaços).
- **Telas:** `UIManager` NÃO é singleton com `Instance` (use `FindObjectOfType<UIManager>()`). Tela de menu atual: `UIManager.UIMenuKey` + `IsMenu` (armazém = `UILoadKey.HouceStorage`, 102). Fechar: `CloseMenu(null, null, false, null, false)` funciona; `ForceBackUIMain` deu `KeyNotFoundException` com tela aberta fora do histórico.
- **No save, as listas `itemDatas` têm tamanho fixo** (mochila 32, armazém 312, peças do Bazaar 200, deck 24): espaço vazio = entrada com `ItemId` 0 e o resto zerado. Para esvaziar, zere as entradas, **não** a lista (D30).
- Upgrade de ferramenta: `BagToolStorageManager.GradeUpTool(...)` troca o id do item. `ToolManager.SaveData.WateringPotInnerCapacity`.
- **Upgrade de ferramenta é imediato** (`ToolManager.GradeUpTool(tipo, item, cb)`); não há estado de "ferramenta no ferreiro". `UserInfo.ToolSaveDatas`: um bloco cifrado (~38 bytes) por tipo de ferramenta (`ToolManager.SetToolSaveData()` / `FromSaveData(ToolSaveData[])`).
- **Vigor:** `GameController.PlayerHP` / `PlayerMaxHP` (em `UserInfo.PlayerHP/PlayerMaxHP`).
- ⚠️ **Entradas de ferramenta que NÃO são itens:** 200, 300, 400, 500, 600, 700, 800, 900 (nível 1 na tabela, sem item). Na mochila viram espaço vazio. O básico real: 100 (machado), 201, 501, 601, 701.
- **Usar ferramenta por código:** a entrada do jogador passa por `PlayerCharacter.GetHumanActionInput()` → `HumanActionInputFlags` (`Tool` = apertou, `ToolUp` = soltou, `Action` = Enter/interagir). Equipar: `ToolManager.ChangeTool(itemId)`; achar a ferramenta do tipo: `BagToolStorage.GetToolItem(ToolType)`.
- **Níveis de ferramenta:** id = tipo×100 + nível (ex.: 704, 603, 102). A cadeia está em `MasterDataManager.Instance.ToolMaster.list` (`ToolMasterData.UpgradeToolId` aponta para o próximo nível). O nível básico é o que ninguém aponta (ex.: 700, 600, 100, 500); ferramentas sem cadeia (1000, 1100, 1200) não têm upgrade.

## 5. Amizade e casamento

- **`LikeabilityManager`**: `Dictionary<uint, LikeabilityData>`, uma **tabela simples** ✅.
- `LikeabilityData`: `Point`, `PresentCount`, `FestivalPresentCount`, `TotalPresentCount`, `TalkCount`, `CapDayTick`.
- Métodos: `AddPoint(...)`, `GivePresent(...)`, `IsGavePreset(id)`, `IsToDayTalked(id)`, `OnTalk(id)`, `GetLevel(...)`.
- **Um cônjuge só** ❌: `uint FianceCharaId`, `uint PartnerCharaId`, `SetFiance`, `SetPartner`, `FianceToPartner`, `IsDating/IsEngagement/IsMarried`. Presente falha com `Failed_HavedPartnerRefuse` se já tiver parceiro.
- `FamilyManager`: um `MarriageInfo`, um `ChildInfo` (**um filho**), `FamilyHelpInfo` (cônjuge ajuda na fazenda).
- Eventos: `EventMarried`, `EventSpouseBirthDay`, `EventFiance`, `EventWeddingDay`. `FieldStayableChecker.IsPartnersHouse(...)`.
- Conversa: `TalkManager.SaveData` (`TalkOnceDayList`, `AlreadyTalkList`, `TalkedCharaList`, `GreetedList`).
- Flags de evento: `EventFlagManager.SetEventFlag(id, flag, time)` / `GetEventFlag`.

## 6. Fazenda

- `FieldCropEntityData` (`CropId`, `GrowthLevel`, `Waters`, `WateringCount`…) e `FieldRidgeEntityData` (`IsWatered`, `IsFertilized`) herdam de `FieldLayerObjectEntityData` (`FieldId`, `LayerId`, **`Guid`**, `Position`).
- Managers: `FieldCropManager`, `FieldTreeManager`, `FieldSprinklerManager`, `FieldSaveManager`.
- Animais: `AnimalManager` (`AddAnimal`, `Breed`, `FarmAnimalBarn`).
- **Animais da fazenda:** `AnimalManager.Instance.AllAnimalParams` (IEnumerable IL2CPP: o `foreach` do C# não funciona; use `Cast<Il2CppSystem.Collections.IEnumerable>().GetEnumerator()`) → `TryCast<FarmAnimalParameter>()`. Cada um tem `ToSaveData`/`FromSaveData` (Guid, FriendPoint, Daily*Count, AteFeedType, ProductRegetDays, CurrentSign, Position, Rotation, Order...).
- **Celeiros:** `AnimalManager.Instance.FarmAnimalBarns.Barns` → `FarmAnimalBarn` (`ToSaveData`/`FromSaveData`: FeedBoxContainers, ExecutedMorningFeed, **ProductEggContainers** = ovos que o jogo recria ao entrar, ProductEggCount, AnimalContainers). Moinhos: `WindmillCraftingManager`. Também `BeekeepingManager` e `MushroomFarmingManager`.
- `FieldManager.AdvanceDateTime(...)` já atualiza mapas fora da tela.
- **Ampliações:** `ExpansionManager` (`ToSaveData`/`FromSaveData` com `ExpansionData`: flags reservadas/concluídas + datas). Níveis: `BagLevel`, `BedLevel`, `HouseStorageLevel`, `RidgeLevel`, `BeeKeepingLevel`, `MushroomFarmingLevel`. O tamanho da mochila segue o `BagLevel` (do mundo).

### Objetos do mapa (Fase 5, investigação)
- Mapa carregado: `FieldManager.Instance.CurrentFieldCache` (`FieldCache`); objetos em `LayerObjects` (`Dictionary<uint, List<FieldLayerObjectEntity>>`), por GUID: `GetLayerObject(guid)`. Itens no chão são outro tipo (`FieldItemEntity : FieldFreeObjectEntity`, em `FreeObjects`).
- **Mapas residentes:** `FieldManager.fieldCacheDict` (privado) guarda caches que ficam SEMPRE na memória (ex.: a fazenda, mapa 1). Eles não são recarregados do save ao voltar, e o dia é processado neles direto. Mexer nesse cache com o jogador em outro mapa funciona (os objetos aparecem ao voltar).
- **IDs sazonais:** o mapa carregado pode ter ID sazonal (2 = mapa 1 no verão). O save de mapas guarda pelo ID raiz: `MasterDataManager.Instance.FieldMaster.ConvertToFieldId(id)`.
- **Dados de um objeto:** `entidade.ToData<T>()` chamado pela classe base devolve os campos do tipo ZERADOS. Use `entidade.Cast<FieldCropEntity>().ToData<FieldCropEntityData>()` ou o serializador oficial: `FieldSaveManager.CreateData(id, cache)` + `FieldSaveManager.DecodeObjectSaveData(sd)` (listas tipadas: Crop, Ridge, Tree, General, Grass, Flower, InRidge = sprinkler, Item, Egg, Animal). ~1,4 ms + 1 ms para ~140 objetos.
- **Criar/remover ao vivo (validado na tela):** remover = `entidade.Unregister()`. Criar = a função de carga de cada tipo: `AttributeManager.CreateRidge(cache, dados, null)`, `CropManager.CreateCrop(cache, dados, canteiroPai, árvorePai)`, `TreeManager.CreateTree(cache, dados, canteiroPai, null)`, `GeneralManager.CreateLayerObject(cache, dados)`, `GrassManager.CreateGrass`, `FlowerManager.CreateFlower`, `SprinklerManager.CreateObject`, `EggManager.CreateEgg` (gerenciadores em `FieldManager.Instance.ManagerAccessor`).
- O enum `FieldDynamicObjectType` do dump não tem nomes para grama e flor. Flores silvestres são `FieldFlowerEntity`, mas `FieldObjectType` responde `Crop`: decida pela classe (`TryCast`).
- **Bichinhos/insetos e animais selvagens:** objetos "Animal" do mapa (`FieldAnimalEntityData`: FieldAnimalId, CreateTicks, AttachPointId, IsAppearableTime; a posição é o ponto de aparição). O personagem que anda é `WildAnimalCharaecter`. Criar: `FieldAnimalManager.CreateLoadedAnimal(cache, dados)`.
- ⚠️ `TrophyRackManager.FromSaveData` **acumula** (soma os troféus a cada chamada). Não reaplicar.
- `AwardManager`, `BasementManager`, `ShopManager`: `JsonToObject` do SaveData falha (classes internas sem construtor vazio); ver `SharedSync.ApplyAwards/ApplyBasement/ApplyShop`.
- ⚠️ `FieldSaveManager.SaveDataArray` devolve uma **cópia nova** a cada chamada. Para trocar o save de um mapa: pegue o array, troque o elemento e grave com `FieldSaveManager.CopyData(array)`. Ao sair de um mapa, o jogo grava o estado dele ali (`GetData(raiz)` já devolve o novo).
- Ovos (`Egg`) do celeiro são recriados a partir do estado das galinhas ao entrar; plantas silvestres das montanhas aparecem como `Crop` no mapa carregado e como `Flower` no save.

## 7. Correio

- `LetterManager` (singleton): `AddPostSimple(id)` põe carta na caixa; `PutInBag()` pega as cartas; `Read`/`Receive` (ler / receber anexo). `SaveData`: `NowIndex`, `LetterTrendData`, `InPostLetterIndex/Id`, `InBagLetterList` (`InBagLetterData`: Index, Id, IsOpen, IsReceive, Ticks, Info), `InPostLetterInfoList`, `LetterHistoryId`.
- ⚠️ `StringExtensions.JsonToObject<LetterManager.SaveData>` devolve **nulo** (`InBagLetterData` não tem construtor vazio). Monte à mão (ver `LetterSync.Build`).

- `LetterManager`: `AddPostSimple(id)`, `PutInPost(bool)`, `Read(...)`, `Receive(...)`, `OnChangeDate()`.

## 8. Player e mapas

- `PlayerCharacter` (~2600 linhas). `GameController.GetPlayer()` retorna **um** player.
- Cenas Unity: só Launcher, Title e Game. Os mapas ("fields") carregam dentro da cena Game: `GameController.ChangeField(fieldId, spawnName, ...)`. Um mapa ativo por vez; a fazenda fica residente em `fieldCacheDict`.

## 9. Diálogo e eventos

- `TalkManager`, `NonPlayerCharacterManager.TalkNPC(type, npc, callback)`.
- Cutscenes: `EventManager` / `EventConductor` + ~157 `EventCommand*`. Gatilhos `EventTrigger*`. `GameController.TalkEventStart(...)` / `TalkEventEnd`. `IsPlayingEvent` é global.

## 10. Bazaar

- `BazaarManager`: `Sell(idx, bonus)`, `CalcSalePrice(...)`, `IsOpenDay/IsOpenTime`, `IsBazaarTimeStop`. `BazaarSaveData` (`DevelopLevel`, `TotalSalePrice`, `Reputation`…). Clientes NPC: `NPCBazaarVisitorAgent`.

## 11. 🎯 Restos de multiplayer no código

A rede foi removida, mas sobrou a estrutura:
- **`NetworkSetting`**: `MaxMembers = 4`, `MaxGuestMembers = 3`, `HostPlayerPrefabName = "FollowSyncComponent"`, `SyncDateTimeInterval`, `SyncEventStateTimeInterval`, `GuestAccessTypeList` (o que o convidado pode fazer).
- `NetworkMenuCategory { StartHost, StopHost, StartGuest, StopGuest }`.
- `UserInfo.OnlinePlayEnabled` / `OnlinePlayPassword`.
- UI: `UIDailyGameMain.SetNetworkGuestTimeStop(bool)`, `UIGameMainPage.ChangeNetworkDaily(bool)`, erros `ConfirmStopGuest` / `ConfirmNetworkError`.
- `FieldAccessType.NetworkAccess`, condição de evento `EventJoinOtherPlayers`, `FieldSaveManager.SyncFieldSaveData`.
- Steam só é usada para DLC, conquistas e input; nada de rede.

**Para o mod:** o jogo foi pensado para host + 3 convidados, exatamente o D6. Dá para reaproveitar parte da UI e das regras de acesso do convidado.

## 12. Rotinas de NPC (schedule)

- **Rotas são dados, não código.** `NpcSchedulePack { List<NpcSchedule> NpcSchedules; List<Schedule> TimeTables; List<NpcScheduleRoute> NpcScheduleRoutes; }`.
- `NpcSchedule`: `ScheduleId`, `CharaId`, **`bool Married`**, `ChildGrowth`, `CityDevelopLevel`, `HomeUpgradeLevel`, `Type` (Normal/Festival), `SeasonAttr`, `DayOfWeekAttr`, `WeatherAttr`, `FestivalId`, `InviteStateAttr`, `ReadEventIds`, `TalkEventId`, `Priority`.
- `ScheduleManager` (`ManagedSingletonMonoBehaviour`) escolhe a rotina do dia; `ScheduleConditionData` avalia as condições; `ScheduleExecuter` / `NPCScheduleAgent` (NavMesh) executam.
- `TitleScheduleConditionType.PartnerCharaId_OR`: condição baseada no parceiro.
- `ScheduleBedChecker`, `ScheduleChairChecker`: ocupação de cama e cadeira.

**Para o mod:** Plano B = nenhuma mudança. Vários cônjuges na casa (v1.0+) = patch na checagem de `Married` / `PartnerCharaId` para considerar o parceiro de qualquer jogador.

## 13. Confirmado em runtime (2026-10-24)

| API | Resultado |
|---|---|
| `ExternalData.Instance.UserInfo.PlayerMoney` | ✅ lê o dinheiro |
| `DateManager.Instance.Now` / `IsPlay()` / `Pause()` / `Resume()` | ✅ lê e controla o relógio |
| `FieldManager.Instance.CurrentFieldId` | ✅ (fazenda/casa inicial = `100010`) |
| `GameController.Instance.GetPlayerTransform()`, `IsPlayingEvent`, `IsSleeping`, `AddMoney(uint, bool)` | ✅ |
| `SceneManager.Instance.CurrentSceneMode` | ✅ Launcher (10) → Title (13) → Game (14) |
| `SaveLoadManager.Instance.IsLoadedSaveDataAll` + `LoadSlot(slot, cb)` + `SceneManager.LoadScene(SceneMode.Game)` | ✅ **carrega um save sem passar pela UI** |
| `AppManager.OnFocusChanged(bool)` | ✅ é quem zera o `Time.timeScale` sem foco (ver D21) |

### Relógio: como ele para de verdade (tarefa 1.1)
- `DateManager.TimeScale = 60`, ou seja, 1 s real = 1 min de jogo.
- `IsPlay()` só reflete a flag bruta `updateEnabled` (mexida por `Pause()`/`Resume()`).
- A **trava efetiva** é a propriedade `DateManager.IsUpdateEnabled` (getter com lógica própria, 551 bytes). Ela fica `false` com um menu aberto (confirmado com `Menu` e `Calendar`) **sem que `Pause()` seja chamado**: o patch de diagnóstico não registrou nenhuma chamada a `DateManager.Pause/Resume` nem a `GameController.Pause*`.
- **Para o D9:** um patch no getter `IsUpdateEnabled` deixa o host decidir quando o tempo corre (N jogadores em menu).
- **A confirmar:** diálogo com NPC e cutscene provavelmente passam pela mesma trava (testar na Fase 3/6 com um NPC real).
- Ponte: o comando `menu <UILoadKey>` abre menus, mas o `closemenu` (`UIManager.CloseMenu`) falha com `KeyNotFoundException`, porque o menu aberto por fora não é registrado no `UIMenuManager`. Depois de testar menus, reinicie com `dev-cycle.sh`.

### Formato do save (tarefa 1.2) ✅
- `.sav` = `BokuMono.Utility.Obfuscation.Encode(json, Deflate.General)`, aberto com **`Obfuscation.Decode(bytes, Deflate.General)`** (a rotina do próprio jogo; comando `decodesave` da ponte).
- Resultado: **31 caracteres de cabeçalho binário + JSON puro**, com chaves iguais aos nomes dos campos C#.
- `SaveData{N}.sav` → `{"HeaderData":{"Version":10500},"UserData":{...}}`, ~630 KB de JSON (~75 KB no disco), **94 seções**. As maiores: `BingoData` (314 KB), `FieldSaveDatas` (162 KB), `HouseStorageSaveData` (56 KB).
- `LikeabilityData` = `{FianceCharaId, PartnerCharaId, LikeabilityDatas:[{CharaId, Value:{Point, PresentCount, FestivalPresentCount, TotalPresentCount, TalkCount, CapDayTick}}], YesterdayLikeabilityDataList, LikeabilityLevelCapDatas, NonLikeabilityCharaTalkedList}`.
- `SaveSlotSystem{N}.sav` → opções do slot (`OptionGamePlay`...). `SaveSystem.sav` → configurações gerais.
- **Para o mod:** o snapshot que o convidado recebe pode ser esse mesmo JSON (comprimido para a rede). Dados por jogador podem ser guardados como seções extras num arquivo do mod ao lado do save.
- Os arquivos decodificados ficam em `<jogo>/BepInEx/savedump/` (dados pessoais, **fora do repositório**).

### Troca de contexto da amizade (tarefa 1.3) ✅
- `LikeabilityManager.Instance.ToSaveData()` → guardar → `FromSaveData(...)` **funciona**: dois "jogadores" com pontos diferentes no mesmo NPC (745 × 1000 no NPC 104), alternando à vontade.
- ⚠️ **`ToSaveData()` e o construtor de cópia `SaveData(SaveData)` são RASOS**: os `LikeabilityData` continuam sendo os objetos do manager vivo, e um snapshot mudava junto com o jogo. **Solução:** guardar como JSON com `StringExtensions.ToJson(obj)` / `StringExtensions.JsonToObject<T>(json)` (o Newtonsoft do próprio jogo), que dá cópia profunda e já é o formato da rede (~9,5 KB por jogador).
- `AddPoint` respeita o teto do nível (`LikeabilityLevelCapDatas`): 745 + 500 parou em 1000.
- **Regra para todo manager:** suponha que `ToSaveData()` é raso, até prova em contrário.

### Troca de contexto completa por jogador (tarefa 1.4) ✅
`PlayerContext` captura e aplica 6 partes (~57 KB de JSON por jogador):

| Parte | Origem | Verificado |
|---|---|---|
| `likeability` (9,5 KB) | `LikeabilityManager.ToSaveData/FromSaveData` | ✅ conteúdo (pontos 745 × 1000) |
| `talk` (10 KB) | `TalkManager.ToSaveData/FromSaveData` | aplica sem erro |
| `eventflags` (21 KB) | `new EventFlagSaveData(EventFlagManager.ToSaveData())` → aplicar com `ResetEventFlag()` + `SetEventFlag(id, flag, time)` uma a uma | ✅ conteúdo (flag 1: 0 × 1) |
| `bag` (13 KB) | `InventoryManager.Instance.BagItemStorage.ToSaveData/FromSaveData` | ✅ conteúdo (18 × 17 itens no slot 0) |
| `tools` (3 KB) | `InventoryManager.Instance.BagToolStorage.ToSaveData/FromSaveData` | aplica sem erro |
| `toolstate` | `ToolManager.ToSaveData/FromSaveData` (água no regador) | aplica sem erro |

Armadilhas das flags de evento:
- `EventFlagSaveDataRuntime` contém um bitset (`Flags`) que quebra o `ToJson` (NullReference).
- `JsonToObject<EventFlagSaveData>` + `FromSaveData` dá `ArgumentException: same key has already been added` (o desserializador duplica entradas). Por isso a aplicação é feita flag a flag.

### Snapshot do mundo (tarefa 1.5) ✅
- **Capturar:** arma um patch em `SaveDataAccessorWindows.SlotSave/SlotSystemSave` e chama `SaveLoadManager.Instance.Save(slot, (FieldMasterId)FieldManager.CurrentFieldId, cb)`. O patch desvia os bytes para a memória e **não escreve no disco**.
- **Carregar:** arma um patch em `SaveDataAccessorWindows.SlotLoad` que devolve os bytes do snapshot, e chama `LoadSlot(slot, cb)` + `SceneManager.LoadScene(SceneMode.Game)`.
- Verificado: snapshot de **72.773 bytes** (o `.sav` comprimido). Depois de +1000 G e de carregar o snapshot, o dinheiro voltou ao valor capturado. Os `SaveData*.sav` no disco ficaram com o hash intacto.
- O `SaveSystem.sav` (configurações gerais) é regravado pelo próprio jogo ao abrir, e isso não tem relação com o snapshot.
- **Para a Fase 3:** o host captura e manda ~73 KB; o convidado carrega com a injeção no `SlotLoad`. Sem precisar conhecer o formato do save.

### Duas instâncias no mesmo PC (tarefa 1.6) ✅
- O jogo **não** bloqueia uma segunda instância (`boot.config` sem `single-instance`). As duas rodam juntas, cada uma na cena Game, com o relógio independente.
- Elas compartilham a pasta e o `.cfg`. A porta da ponte e o slot do auto-load vêm de variáveis de ambiente: `SOSGB_BRIDGE_PORT`, `SOSGB_AUTOLOAD_SLOT` (ver `scripts/second-instance.sh`). O log de estado e os prints ganham o sufixo `-<porta>`.
- ⚠️ As duas carregam o mesmo slot do disco. Antes de testes de rede, o convidado não pode salvar (Fase 3).

### Player: transform, teleporte e modelo (Fase 2)
- ⚠️ A raiz do `PlayerCharacter` (`GetPlayer().transform`) **fica parada na origem**. O que anda é o **modelo**: `GameController.GetPlayerTransform()` == `PlayerCharacter.Model.transform`. Use sempre ele para posição e rotação.
- Mexer no `position` direto não adianta, porque o jogo devolve o personagem para o lugar. Para teleportar: `PlayerCharacter.SetPlayerClosestGroundPosition(Vector3, Nullable<Vector3> rot, float distance)`.
- O modelo tem 30 `Renderer` (5 ativos: `body`, `skin`, `face`, `M_L`, `hair_00`, todos `SkinnedMeshRenderer`), na layer 11. `Instantiate(player.Model)` sem os `MonoBehaviour` e `Collider` gera um avatar visual que funciona, com `Animator` próprio.
- A animação é replicada com `Animator.GetCurrentAnimatorStateInfo(0).fullPathHash` → `Animator.Play(hash, 0, norm)` no clone.

### Mapas (tarefa 2.5)
- `GameController.ChangeField((FieldMasterId)id, ...)` troca de mapa. Ao pedir `10001`, o jogo foi para **`10002`**: o ID efetivo é "sazonal" (varia com estação/estado). Use o `FieldManager.CurrentFieldId` real, não o pedido.
- Trocar de mapa nesse teste **não** disparou save automático.

### Dormir e virar o dia (tarefas 3.4/3.5)
- ⚠️ Chamar `PlayerCharacter.ActionSleep()` fora da cama deixa o personagem preso em `ForceCleanMove` (andando até um destino que não existe), sem volta nem com `InitializeUpdateState`. O fluxo real é: interagir com a cama → diálogo → "Hora de dormir!" → `ActionSleep` → `GameController.StartNextDay`.
- `GameController.StartNextDay()` (público, `UniTask`) vira o dia **na hora** (data → dia seguinte, 06:00). Ordem observada: `StartNextDay` → `PreChangeDate` → save automático.
- `PlayerCharacter.ActionSleep()` sozinho **não** vira o dia (só a ação/animação). O fluxo real começa na cama: `FieldPlayerBed.Access()` abre um menu de escolhas.
- **O save automático da virada do dia vai para o slot 0**, mesmo com `UserInfo.Slot = 2`: o slot 0 é o **slot de save automático** do jogo (o slot 0 e o slot 1 do usuário têm o mesmo dinheiro). `UserInfo.Slot` é gravado no save (`"Slot": 2`).
- Para segurar a virada no multiplayer, um prefix em `StartNextDay` retorna `false` com `__result = default(UniTask)` (tarefa já concluída).

### Animação do personagem (bugs do 1º teste real)
- O `Animator` do player tem **12 camadas**: `Move Layer` (0), `MoveTool`, `MoveWind`, `MoveToolWind`, `MovePickup`, `Move Carry`, `Move Carry Piggyback`, `Base Layer` (7: ações/emoções), `Mask`, `Body`, `LeftArm`, `RightArm`.
- A `Move Layer` é um **blend tree**: parado/andar/correr dependem de **parâmetros** (`Speed`, `Walk`, `Run`...), e o hash do estado não muda ao andar. O controller tem ~250 parâmetros, a maioria triggers.
- O player usa um **AnimatorOverrideController** carregado depois do modelo, que troca os clips `dummy_fix_mot_*` pelos reais (`fix_mot_Mn_*`; `Mn` = masculino). Um clone com o controller base anima com os dummies e fica numa pose torta.
- Ao recarregar o mundo (convidado recebendo `WorldData`), o player é recriado e os modelos/materiais antigos são liberados. Um clone feito antes **para de ser desenhado** (o objeto continua existindo).

### Aparência do personagem (D28/D32)
- `UserInfo.AvatarInfos` é uma lista compacta (~18 itens) de `AvatarInfo { MasterId, Category }`: `Face`, `SkinColor`, `EyeColor`, `HairStyle`, `HairColor`, `Beard`, `BeardColor`, `Mole`, `Freckles`, `MotionType`, `VoiceType`, `Hat`, `Accessories`, `Clothes`, `HorseRidingHat/Clothes`, `BagDesign`, `Glider`. `UserInfo.PlayerGender` (0 = masculino).
- O modelo é montado a partir de peças (prefabs `avt_M_c_720` = roupa 720 masculina, `avt_P_h_012` = cabelo 012) sobre o esqueleto `Chr_root`, com MagicaCloth para saias e cabelos.
- **Quem monta é o `AvatarMakeManager`** (`SetAvatarParts(info)`, `SetClothesParts`, `CreateAvatar(AvatarInfo[])`, `CreatePlayerModel`...). Ele atua sobre um **único** modelo (`meshRoot`/`makeModel`/`baseModel`) e tem caches de recursos (`AvatarResourceManager`). Montar um segundo personagem por ele pode descarregar as peças do jogador local.
- NPCs "genéricos" (`NpcData.NpcType.Mob/NpcTourist`) recebem `List<AvatarInfo>`: possível caminho alternativo, ainda não investigado.
- ✅ **Funciona (D32), inclusive gênero.** O que parecia "peça não carregada trava" era um **id inexistente** (ex.: cabelo 45020; os válidos são 45000–45019 e DLC 80600+). Peças válidas montam em ~0,1 s sem pré-carregar. Para listar as válidas: `MasterDataManager.Instance.AvatarMakeMasterData` filtrando por `Category` (comando `applab cat <n>`).
- **O jogo esconde o modelo (~1–2 s) enquanto carrega peças novas** e depois mostra sozinho. Copiar o modelo nesse intervalo gera um clone invisível: espere algum `Renderer` ligado.
- **Gênero:** `MotionType` 52000 = masculino (`MotionCategory` 0), 52001 = feminino (1). `ChangePlayerAvatar` troca o corpo; `PlayerCharacter.SwitchGenderMotion(cat, cb)` troca as animações (clips `fix_mot_Mn_*` ↔ `fix_mot_Fn_*`), no mesmo `AnimatorOverrideController` (o objeto é o mesmo: copie com `Instantiate` para o clone e chame `Rebind()`).
- Trocar a roupa pode ajustar outras peças sozinho (ex.: roupa 49040 → bolsa `16:54040`).
- `CreateMakeAvatarModel` + `CreateAvatar(infos)` (modelo da tela de customização) só aplicou o cabelo e escondeu o jogador local: não serve para montar avatares.
- Estrutura da corrotina (vista pelo `CoroutineProbe`): `<ChangePlayerAvatar>d__132` → `<ChangeAvatar>d__133` → `<Coroutine>d__13` (várias vezes).
- **Técnica (D28):** trocar as `AvatarInfos` do jogador local (`AvatarMakeManager.PlayerAvatarInfos` + `GameController.PlayerAvatarInfos` + `UserInfo.AvatarInfos`), avançar `ChangePlayerAvatar(ChangeType.Default)`, chamar `PlayerCharacter.SwitchGenderMotion(motionCategory)`, copiar o `Model` e devolver tudo. A cópia continua válida. Peças criadas com `AvatarInfo.CreateParts(MasterDataManager.AvatarMakeMaster.GetData(id))` / `CreateNoParts(categoria)`.

**Acesso a singletons:** `Classe.IsInstance` / `Classe.Instance` (herdados de `BokuMono.Utility.Singleton<T>` ou `SingletonMonoBehaviour<T>`).

### Peculiaridades do Il2CppInterop neste jogo
- **Structs não-blittable viram CLASSES no interop** (ex.: `Cysharp.Threading.Tasks.UniTask`). Num patch, `__result = default` vira nulo e o jogo cai ao receber o retorno. Use um valor real (`UniTask.CompletedTask`).
- `DateManager.IsUpdateEnabled = false` não para só o relógio: **trava o controle do personagem** também (visto no P2, D33).
- `ScreenCapture.CaptureScreenshot(string)` → `MissingMethodException` (marshalling de string). Use `CaptureScreenshotAsTexture()`.
- `ImageConversion.EncodeToPNG` → `InvalidOperationException` (`BlittableArrayWrapper.Unmarshal`). Use `GetPixels32()` + um codificador próprio (`PngWriter`).
- As constantes de `SceneMode` (`Game`, `Title`...) viram campos estáticos, sem `const`, e não podem ser usadas em `switch`.
- Delegates `System.Action<T>` convertem sozinhos para `Il2CppSystem.Action<T>` com um cast explícito.
