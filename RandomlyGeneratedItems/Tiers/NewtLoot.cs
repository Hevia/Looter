using RoR2;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.AddressableAssets;

namespace RandomlyGeneratedItems.Tiers
{
    public class NewtLoot : TierBase<NewtLoot>
    {
        public override string TierName => "NewtLoot";
        public override bool CanScrap => false;
        public override ColorCatalog.ColorIndex ColorIndex => ColorCatalog.ColorIndex.LunarItem;
        public override ColorCatalog.ColorIndex DarkColorIndex => ColorCatalog.ColorIndex.LunarItemDark;
        public override GameObject DropletDisplayPrefab => Addressables.LoadAssetAsync<ItemTierDef>("RoR2/Base/Common/Tier1Def.asset").WaitForCompletion().dropletDisplayPrefab;
        public override bool IsDroppable => true;
        public override ItemTierDef.PickupRules PickupRules => ItemTierDef.PickupRules.Default;
        public override GameObject HighlightPrefab => Addressables.LoadAssetAsync<ItemTierDef>("RoR2/Base/Common/Tier1Def.asset").WaitForCompletion().highlightPrefab;

        public override ItemTier TierEnum => (ItemTier)39;

        public override void PostCreation()
        {
            base.PostCreation();
            On.RoR2.BasicPickupDropTable.GenerateWeightedSelection += (orig, self, run) => {
                orig(self, run);
                List<PickupIndex> indexes = new();
                foreach (ItemDef def in ItemCatalog.allItemDefs.Where(x => x._itemTierDef == this.tier))
                {
                    indexes.Add(PickupCatalog.FindPickupIndex(def.itemIndex));
                }
                self.Add(indexes, self.tier1Weight);
            };
        }
    }
}
