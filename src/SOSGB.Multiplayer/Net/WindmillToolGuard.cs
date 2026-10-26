// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d66.
using System;
using System.Collections.Generic;
using BokuMono;
using HarmonyLib;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// D66 (pedido do usuário, P12): a ferramenta que alguém deixou no moinho para melhorar só sai para quem deixou. O resto do
/// moinho continua compartilhado (SharedSync, parte "windmill").
///
/// Como saber de quem é, sem guardar dono: a receita de upgrade pede a PRÓPRIA ferramenta como material (ex.: a enxada
/// nível 4 pede a enxada nível 3), então quem deixou a ferramenta está sem uma daquele tipo na mochila de ferramentas, e
/// os outros jogadores ainda têm a deles. Na retirada (TakeOutAction), as ferramentas prontas de um tipo que este jogador
/// JÁ TEM ficam no moinho: saem da lista antes da retirada e voltam quando ela termina (o jogo avisa o fim pelo endAction,
/// depois da mensagem "Conseguiu ..."). Sem isto, quem retirasse a ferramenta de outro ganhava o upgrade (o jogo troca a
/// ferramenta da mochila dele pela nova; testado) e o dono ficava sem a dele.
/// </summary>
internal static class WindmillToolGuard
{
    private static Dictionary<uint, BokuMono.Data.ToolType> _toolOfItem;

    /// <summary>Item → tipo de ferramenta, pelas receitas de upgrade do moinho (o que elas fazem e a ferramenta que pedem).</summary>
    private static BokuMono.Data.ToolType ToolTypeOf(uint itemId)
    {
        if (_toolOfItem == null)
        {
            var map = new Dictionary<uint, BokuMono.Data.ToolType>();
            foreach (var r in MasterDataManager.Instance.WindmillCraftingMaster.list)
            {
                if (r == null || (int)r.ToolType == 0) continue;
                map[r.CraftingItemId] = r.ToolType;
                if (r.RequiredItemId != null && r.RequiredItemId.Count > 0 && r.RequiredItemId[0] != 0) map[r.RequiredItemId[0]] = r.ToolType;
            }
            _toolOfItem = map;
        }
        return _toolOfItem.TryGetValue(itemId, out var t) ? t : 0;
    }

    [HarmonyPatch(typeof(WindmillCraftingManager), nameof(WindmillCraftingManager.TakeOutAction))]
    private static class TakeOutPatch
    {
        private static void Prefix(WindmillCraftingType type, ref Il2CppSystem.Action endAction)
        {
            if (NetSession.Current == NetSession.Mode.Offline) return;
            try
            {
                var list = WindmillCraftingManager.Instance.saveData.GetCompleteList(type); // a lista viva do jogo (testado)
                var bagTools = InventoryManager.Instance.BagToolStorage;
                if (list == null || bagTools == null) return;
                var kept = new List<BokuMono.Data.ItemData>();
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var it = list[i];
                    if (it == null) continue;
                    var tt = ToolTypeOf(it.ItemId);
                    if ((int)tt == 0 || !bagTools.IsHaved(tt)) continue;
                    kept.Add(it);
                    list.RemoveAt(i);
                }
                if (kept.Count == 0) return;
                GameNotify.Show("A ferramenta no moinho é de outro jogador: só quem deixou pode retirar.");
                Plugin.Logger.LogInfo($"[Moinho] {kept.Count} ferramenta(s) de outro jogador ficaram no moinho {type}");
                var original = endAction;
                endAction = (Il2CppSystem.Action)(() =>
                {
                    Restore(type, kept);
                    original?.Invoke();
                });
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[Moinho] separar ferramentas: {e.Message}"); }
        }

        private static void Restore(WindmillCraftingType type, List<BokuMono.Data.ItemData> kept)
        {
            try
            {
                var sd = WindmillCraftingManager.Instance.saveData;
                for (var i = kept.Count - 1; i >= 0; i--) sd.AddCompleteItem(type, kept[i]);
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[Moinho] devolver ferramentas: {e.Message}"); }
        }
    }
}
