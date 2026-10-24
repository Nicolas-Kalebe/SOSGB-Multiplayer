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

## 4. Inventário, armazém e ferramentas

- `InventoryManager`: `BagItemStorage`, `BagToolStorage`, `HouseStorage`, `DeckStorage`, `ExhibitionShelf`, `BazaarCustomPartsStorage`.
- `StorageManager`: `PutIn(ItemData, out ItemData, bool)`, `PutOut(int slot, int stack)`, `Use(uint itemId, int stack, bool)`, `IsHaved(...)`.
- `ItemData`: `ItemId`, `Stack`, `QualityValue`, `FreshnessValue`, `WrapId`.
- Mochila: 32 slots. Armazém da casa: 24 parcelas.
- Upgrade de ferramenta: `BagToolStorageManager.GradeUpTool(...)` troca o id do item. `ToolManager.SaveData.WateringPotInnerCapacity`.

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
- Animais: `AnimalManager` (`AddAnimal`, `Breed`, `FarmAnimalBarn`). Moinhos: `WindmillCraftingManager`. Também `BeekeepingManager` e `MushroomFarmingManager`.
- `FieldManager.AdvanceDateTime(...)` já atualiza mapas fora da tela.

## 7. Correio

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
