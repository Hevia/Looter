using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using UnityEngine.AddressableAssets;
using System;
using Newtonsoft.Json.Utilities;

namespace RandomlyGeneratedItems
{
    class ExampleArtifact : ArtifactBase
    {
        //public RandItemContainer randItemContainer = RandItemContainer.Instance;

        public static string texArtifactCommandDisabled = "RoR2/Base/Command/texArtifactCommandDisabled.png";
        public static string texArtifactCommandEnabled = "RoR2/Base/Command/texArtifactCommandEnabled.png";
        public override string ArtifactName => "Artifact of Loot";
        public override string ArtifactLangTokenName => "ARTIFACT_OF_LOOT";
        public override string ArtifactDescription => "Enable Randomly Generated Items to spawn, and disable all vanilla item drops.";
        public override Sprite ArtifactEnabledIcon => Addressables.LoadAssetAsync<Sprite>(texArtifactCommandEnabled).WaitForCompletion();
        public override Sprite ArtifactDisabledIcon => Addressables.LoadAssetAsync<Sprite>(texArtifactCommandDisabled).WaitForCompletion();

        public Logger RGILogger { get; private set; }

        public override void Init(ConfigFile config)
        {
            CreateConfig(config);
            CreateLang();
            CreateArtifact();
            Hooks();
        }
        private void CreateConfig(ConfigFile config)
        {
            //TimesToPrintMessageOnStart = config.Bind<int>("Artifact: " + ArtifactName, "Times to Print Message in Chat", 5, "How many times should a message be printed to the chat on run start?");
        }
        public override void Hooks()
        {
            Run.onRunSetRuleBookGlobal += (run, rulebook) =>
            {
                if (NetworkServer.active && ArtifactEnabled)
                {
                    foreach (var index in ItemCatalog.allItems)
                    {
                        ItemDef itemDef = ItemCatalog.GetItemDef(index);

                        PickupIndex pickupIndex = PickupCatalog.FindPickupIndex(index);
                        
                        // TODO: Clean this up
                        if (!itemDef.name.StartsWith("RAND_ITEM_"))
                        {
                            run.availableItems.Remove(index);
                            run.availableTier1DropList.Remove(pickupIndex);
                            run.availableTier2DropList.Remove(pickupIndex);
                        }
                        else if (itemDef.name.StartsWith("RAND_ITEM_"))
                        {
                            itemDef.hidden = false;
                            run.availableItems.Add(index);
                            run.avail
                            run.availableTier1DropList.Add(pickupIndex);
                            run.availableTier2DropList.Add(pickupIndex);
                        }
                    }

                    PickupDropTable.RegenerateAll(run);

                    Log.Info("Printing the contents of the availableTier1DropList:");

                    foreach(PickupIndex name in run.availableTier1DropList)
                    {
                        Log.Info("Name Token: " + name.GetPickupNameToken());
                    }

                    Log.Info("Printing the contents of the availableTier2DropList:");

                    foreach (PickupIndex pickUpIndex in run.availableTier2DropList)
                    {
                        Log.Info("Name Token: " + pickUpIndex.GetPickupNameToken());
                    }
                }
            };
         }
    }
}
