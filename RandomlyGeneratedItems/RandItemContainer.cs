using RoR2;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.AddressableAssets;
using UnityEngine;

namespace RandomlyGeneratedItems
{
    public sealed class RandItemContainer
    {
        public ItemTierDef RandItemTier { get; private set; }
        public static int EnumVal = 42;
        public static RandItemContainer instance = null;
        private static readonly object padlock = new object();

        RandItemContainer()
        {
            RandItemTier = ScriptableObject.CreateInstance<ItemTierDef>();
            RandItemTier.bgIconTexture = Addressables.LoadAssetAsync<ItemTierDef>("RoR2/Base/Common/Tier3Def.asset").WaitForCompletion().bgIconTexture;
            RandItemTier.colorIndex = ColorCatalog.ColorIndex.LunarItem;
            RandItemTier.darkColorIndex = ColorCatalog.ColorIndex.LunarItemDark;
            RandItemTier.name = "RandItemTier";
            RandItemTier.isDroppable = true;
            RandItemTier.canScrap = true;
            RandItemTier.canRestack = true;
            RandItemTier.pickupRules = ItemTierDef.PickupRules.Default;
            RandItemTier.tier = ItemTier.AssignedAtRuntime;
            RandItemTier.dropletDisplayPrefab = Addressables.LoadAssetAsync<ItemTierDef>("RoR2/Base/Common/Tier2Def.asset").WaitForCompletion().dropletDisplayPrefab;
            RandItemTier.highlightPrefab = Addressables.LoadAssetAsync<ItemTierDef>("RoR2/Base/Common/Tier1Def.asset").WaitForCompletion().highlightPrefab;
        }

        public static RandItemContainer Instance
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new RandItemContainer();
                    }
                    return instance;
                }
            }
        }
    }
}
