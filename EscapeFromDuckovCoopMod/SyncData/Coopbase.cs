using ItemStatsSystem;
using Saves;
using System;
using System.Collections.Generic;
using System.Text;

namespace EscapeFromDuckovCoopMod.SyncData
{
    public static class Coopbase
    {
        public static async UniTask<Item> LoadOrCreateCharacterItemInstance()
        {
            Item item = await ItemSavesUtilities.LoadItem("MainCharacterItemData");
            return item;
        }

    }
}
