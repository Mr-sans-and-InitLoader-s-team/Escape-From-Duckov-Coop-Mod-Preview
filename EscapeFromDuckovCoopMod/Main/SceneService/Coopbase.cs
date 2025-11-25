// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using Cysharp.Threading.Tasks;
using Duckov.Utilities;
using ItemStatsSystem;
using Saves;

namespace EscapeFromDuckovCoopMod;

public static class Coopbase
{
    private const string CharacterItemSaveKey = "MainCharacterItemData";

    //加载玩家背包用的，AI不需要所以注释了2个，客户端默认背包
    public static async UniTask<Item> LoadOrCreateCharacterItemInstance()
    {
        Item item = null;
        //try
        //{
        //    item = await ItemSavesUtilities.LoadItem(CharacterItemSaveKey);
        //}
        //catch
        //{
        //}

        //if (item == null)
        //{
        //    try
        //    {
        //        var fallbackKey = LevelManager.MainCharacterItemSaveKey;
        //        if (!string.IsNullOrEmpty(fallbackKey))
        //            item = await ItemSavesUtilities.LoadItem(fallbackKey);
        //    }
        //    catch
        //    {
        //    }
        //}

        if (item == null)
        {
            try
            {
                item = await ItemAssetsCollection.InstantiateAsync(GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID);
            }
            catch
            {
            }
        }

        return item;
    }
}
