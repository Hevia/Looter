using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using R2API;
using R2API.Utils;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RandomlyGeneratedItems
{
    [BepInDependency(R2API.R2API.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [R2APISubmoduleDependency(nameof(LanguageAPI), nameof(ItemAPI), nameof(PrefabAPI), nameof(RecalculateStatsAPI))]
    public class Main : BaseUnityPlugin
    {
        public const string PluginAuthor = "Nudibranch";
        public const string PluginName = "Looter";
        public const string PluginVersion = "0.0.3";

        public const string PluginGUID = PluginAuthor + "." + PluginName;

        public static ConfigFile RGIConfig;
        public static ManualLogSource RGILogger;

        public static ProcType HealingBonus = (ProcType)89; // hopefully no other mod uses a proc type of 89 because r2api doesnt have proctypeapi
        private static ulong seed;
        public static Xoroshiro128Plus rng;

        private static ItemDef itemDef;

        public static ConfigEntry<ulong> seedConfig { get; set; }

        private static List<GameObject> itemModels = new();
        private static List<Sprite> itemIcons = new();

        private static List<ItemDef> randItemDefs = new();
        private static Dictionary<string, Effect> map = new();

        public static Shader hgStandard;

        public List<ArtifactBase> Artifacts = new List<ArtifactBase>();

        public RandItemContainer randItemContainer = RandItemContainer.Instance;

        public void Awake()
        {
           
            R2API.ContentAddition.AddItemTierDef(randItemContainer.RandItemTier);
          
            var ArtifactTypes = Assembly.GetExecutingAssembly().GetTypes().Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(ArtifactBase)));
            foreach (var artifactType in ArtifactTypes)
            {
                ArtifactBase artifact = (ArtifactBase)Activator.CreateInstance(artifactType);
                if (ValidateArtifact(artifact, Artifacts))
                {
                    artifact.Init(Config);
                }
            }

            Log.Init(Logger);
            RGILogger = Logger;
            RGIConfig = Config; // seedconfig does nothing right now because config.bind.value returns a bepinex.configentry<ulong> instead of a plain ulong???
            seedConfig = Config.Bind<ulong>("Configuration:", "Seed", 264858, "The seed that will be used for random generation. This MUST be the same between all clients in multiplayer!!! A seed of 0 will generate a random seed instead");

            hgStandard = Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion();

            if (seedConfig.Value != 0)
            {
                seed = seedConfig.Value;
            }
            else
            {
                seed = (ulong)UnityEngine.Random.RandomRangeInt(0, 10000) ^ (ulong)UnityEngine.Random.RandomRangeInt(1, 10) << 16;
            }

            rng = new(seed);
            Logger.LogFatal("seed is " + seed);

            Buffs.Awake();

            NameSystem.populate();

            // int maxItems = itemNamePrefix.Count < itemName.Count ? itemNamePrefix.Count : itemName.Count;
            int maxItems = Config.Bind("Configuration:", "Maximum Items", 30, "The maximum amount of items the mod will generate.").Value;
            Logger.LogFatal("Generating " + maxItems + " items.");

            Effect.ModifyPrefabs();

            for (int i = 0; i < maxItems; i++)
            {
                GenerateItem();
                if (i == maxItems - 1)
                {
                    Logger.LogWarning("Max item amount of " + maxItems + " reached");
                }
            }

            On.RoR2.ItemCatalog.Init += ItemCatalog_Init;

            RecalculateStatsAPI.GetStatCoefficients += RecalculateStatsAPI_GetStatCoefficients;

            On.RoR2.GlobalEventManager.ServerDamageDealt += GlobalEventManager_ServerDamageDealt;

            On.RoR2.UI.MainMenu.BaseMainMenuScreen.Awake += (orig, self) =>
            {
                orig(self);
                foreach (ItemDef def in randItemDefs)
                {
                    var index = PickupCatalog.FindPickupIndex(def.itemIndex);
                    Logger.LogInfo("Found " + def.name + " in the pickup catalog");
                    UserProfile.defaultProfile.DiscoverPickup(index);
                }
            };

            On.RoR2.CharacterBody.Update += (orig, self) =>
            {
                orig(self);
                if (UnityEngine.Networking.NetworkServer.active)
                {
                    if (self.characterMotor && self.characterMotor.velocity.magnitude != -self.characterMotor.lastVelocity.magnitude)
                    {
                        if (self.isPlayerControlled)
                        {
                            // self.RecalculateStats(); // trout population restored
                            // :thonk:
                            // you know forcing recalcstats is terrible for the trout population
                        }
                    }
                }
            };

            On.RoR2.CharacterBody.OnSkillActivated += (orig, sender, slot) =>
            {
                orig(sender, slot);
                if (sender && sender.inventory && UnityEngine.Networking.NetworkServer.active)
                {
                    foreach (ItemIndex index in sender.inventory.itemAcquisitionOrder)
                    {
                        ItemDef def = ItemCatalog.GetItemDef(index);
                        Effect effect;

                        bool found = map.TryGetValue(def.nameToken, out effect);
                        if (found && effect.effectType == Effect.EffectType.OnSkillUse && Util.CheckRoll(effect.chance, sender.master))
                        {
                            if (effect.ConditionsMet(sender))
                            {
                                effect.onSkillUseEffect(sender, sender.inventory.GetItemCount(def));
                            }
                        }
                    }
                }
            };

            On.RoR2.GlobalEventManager.OnCharacterDeath += (orig, self, report) =>
            {
                orig(self, report);
                DamageInfo info = report.damageInfo;
                if (UnityEngine.Networking.NetworkServer.active && info.attacker && report.victimIsElite)
                {
                    CharacterBody sender = info.attacker.GetComponent<CharacterBody>();

                    if (sender && sender.inventory && UnityEngine.Networking.NetworkServer.active && info.damageColorIndex != DamageColorIndex.Item)
                    {
                        foreach (ItemIndex index in sender.inventory.itemAcquisitionOrder)
                        {
                            ItemDef def = ItemCatalog.GetItemDef(index);
                            Effect effect;

                            bool found = map.TryGetValue(def.nameToken, out effect);
                            if (found && effect.effectType == Effect.EffectType.OnElite)
                            {
                                if (effect.ConditionsMet(sender))
                                {
                                    effect.onEliteEffect(info, sender.inventory.GetItemCount(def));
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (UnityEngine.Networking.NetworkServer.active && info.attacker)
                    {
                        CharacterBody sender = info.attacker.GetComponent<CharacterBody>();

                        if (sender && sender.inventory && UnityEngine.Networking.NetworkServer.active && info.damageColorIndex != DamageColorIndex.Item)
                        {
                            foreach (ItemIndex index in sender.inventory.itemAcquisitionOrder)
                            {
                                ItemDef def = ItemCatalog.GetItemDef(index);
                                Effect effect;

                                bool found = map.TryGetValue(def.nameToken, out effect);
                                if (found && effect.effectType == Effect.EffectType.OnKill)
                                {
                                    if (effect.ConditionsMet(sender))
                                    {
                                        effect.onKillEffect(info, sender.inventory.GetItemCount(def));
                                    }
                                }
                            }
                        }
                    }
                }
            };

            On.RoR2.HealthComponent.Heal += (orig, self, amount, mask, nonRegen) =>
            {
                CharacterBody sender = self.body;
                if (sender && sender.inventory && UnityEngine.Networking.NetworkServer.active && !mask.HasProc(HealingBonus) && nonRegen)
                {
                    foreach (ItemIndex index in sender.inventory.itemAcquisitionOrder)
                    {
                        ItemDef def = ItemCatalog.GetItemDef(index);
                        Effect effect;

                        bool found = map.TryGetValue(def.nameToken, out effect);
                        if (found && effect.effectType == Effect.EffectType.OnHeal)
                        {
                            if (effect.ConditionsMet(sender))
                            {
                                effect.onHealEffect(self, sender.inventory.GetItemCount(def));
                            }
                        }
                    }
                }
                return orig(self, amount, mask, nonRegen);
            };
        }

        public bool ValidateArtifact(ArtifactBase artifact, List<ArtifactBase> artifactList)
        {
            var enabled = Config.Bind<bool>("Artifact: " + artifact.ArtifactName, "Enable Artifact?", true, "Should this artifact appear for selection?").Value;
            if (enabled)
            {
                artifactList.Add(artifact);
            }
            return enabled;
        }

        private void ItemCatalog_Init(On.RoR2.ItemCatalog.orig_Init orig)
        {
            orig();
        }

        private ItemDisplayRuleDict CreateItemDisplayRules()
        {
            return null;
        }

        private void GenerateItem()
        {
            Log.Info("DEBUGGER Enter GenerateItems()");
            string itemName = "";
            ItemTier tier;

            int attempts = 0;

            string xmlSafeItemName = "E";

            while (attempts <= 5)
            {
                var prefixRng2 = rng.RangeInt(0, NameSystem.itemNamePrefix.Count);
                var nameRng2 = rng.RangeInt(0, NameSystem.itemName.Count);
                itemName = "";

                itemName += NameSystem.itemNamePrefix[prefixRng2] + " ";

                itemName += NameSystem.itemName[nameRng2];

                xmlSafeItemName = itemName.ToUpper();
                xmlSafeItemName = xmlSafeItemName.Replace(" ", "_").Replace("'", "").Replace("&", "AND");

                Effect buffer;
                if (map.TryGetValue("RAND_ITEM_" + xmlSafeItemName + "_NAME", out buffer))
                {
                    attempts++;
                }
                else
                {
                    break;
                }
            }

            if (attempts > 5)
            {
                return;
            }

            // TODO
            string log = "loggy log";

            tier = (ItemTier)rng.RangeInt(0, 4);

            float mult = 1f;
            float stackMult = 1f;
            Effect effect = new();

            int objects = rng.RangeInt(1, 3);
            PrimitiveType[] prims = {
                PrimitiveType.Sphere,
                PrimitiveType.Capsule,
                PrimitiveType.Cylinder,
                PrimitiveType.Cube,
            };

            GameObject first = GameObject.CreatePrimitive(prims[rng.RangeInt(0, prims.Length)]);

            for (int i = 0; i < objects; i++)
            {
                GameObject prim = GameObject.CreatePrimitive(prims[rng.RangeInt(0, prims.Length)]);
                prim.GetComponent<MeshRenderer>().material.color = new Color32((byte)rng.RangeInt(0, 256), (byte)rng.RangeInt(0, 256), (byte)rng.RangeInt(0, 256), (byte)rng.RangeInt(0, 256));
                prim.transform.SetParent(first.transform);
                prim.transform.localPosition = new Vector3(rng.RangeFloat(-1, 1), rng.RangeFloat(-1, 1), rng.RangeFloat(-1, 1));
                prim.transform.localRotation = Quaternion.Euler(new Vector3(rng.RangeFloat(-360, 360), rng.RangeFloat(-360, 360), rng.RangeFloat(-360, 360)));
            }

            GameObject prefab = PrefabAPI.InstantiateClone(first, $"{xmlSafeItemName}-model", false);
            foreach (MeshRenderer mr in prefab.GetComponents<MeshRenderer>())
            {
                mr.sharedMaterial.shader = hgStandard;
                mr.sharedMaterial.color = new Color32((byte)rng.RangeInt(0, 255), (byte)rng.RangeInt(0, 255), (byte)rng.RangeInt(0, 255), 255);
            }

            foreach (MeshRenderer mr in prefab.GetComponentsInChildren<MeshRenderer>())
            {
                mr.sharedMaterial.shader = hgStandard;
                mr.sharedMaterial.color = new Color32((byte)rng.RangeInt(0, 255), (byte)rng.RangeInt(0, 255), (byte)rng.RangeInt(0, 255), 255);
            }

            Log.Info("DEBUGGER Prefab Created....");

            try
            {
                DontDestroyOnLoad(prefab);
            } catch (Exception e)
            {
                Log.Info("DEBUGGER DontDestroyOnLoad(prefab); failed...");
            }
            

            Texture2D tex = new(512, 512);

            Color[] col = new Color[512 * 512];

            float sx = rng.RangeFloat(0, 10000);
            float sy = rng.RangeFloat(0, 10000);

            float scale = rng.RangeFloat(1, 1);

            // Define the color for each item tier
            Dictionary<ItemTier, Color> tierColors = new()
            {
                { ItemTier.Tier1, new Color(0.77f, 0.95f, 0.97f, 0.99f) },
                { ItemTier.Tier2, Color.green },
                { ItemTier.Tier3, Color.red },
            };


            // Get the color for the item's tier.
            Color tierCol = tierColors[tier];

            // Calculate the RGB offset
            int offset = 40; // This can be adjusted to change the amount of offset
            int r = (int)Mathf.Clamp(tierCol.r * 255 + offset, 0, 255);
            int g = (int)Mathf.Clamp(tierCol.g * 255 + offset, 0, 255);
            int b = (int)Mathf.Clamp(tierCol.b * 255 + offset, 0, 255);

            // Apply the offset to the tier color
            var color = new Color32((byte)r, (byte)g, (byte)b, 255);

            switch (tier)
            {
                case ItemTier.Tier1:
                    mult = 1f;
                    stackMult = 1f;
                    break;

                case ItemTier.Tier2:
                    mult = 3.2f;
                    stackMult = 0.5f;
                    break;

                case ItemTier.Tier3:
                    mult = 12f;
                    stackMult = 0.15f;
                    break;


                case ItemTier.Lunar:
                    mult = 1.8f;
                    stackMult = 0.5f;
                    break;

                case ItemTier.VoidTier1:
                    mult = 0.9f;
                    stackMult = 1f;
                    break;

                case ItemTier.VoidTier2:
                    mult = 1.5f;
                    stackMult = 0.75f;
                    break;

                case ItemTier.VoidTier3:
                    mult = 2f;
                    stackMult = 0.45f;
                    break;

                case ItemTier.VoidBoss:
                    mult = 2f;
                    stackMult = 0.6f;
                    break;

                default:
                    mult = 1f;
                    stackMult = 1f;
                    break;
            }

            effect.Generate(rng, mult, stackMult);

            int shape = rng.RangeInt(0, 3);

            for (int y = 0; y < tex.height; y++)
            {
                for (int x = 0; x < tex.width; x++)
                {
                    float xCoord = sx + x / tex.width * scale;
                    float yCoord = sy + y / tex.height * scale;
                    float sample = Mathf.PerlinNoise(xCoord, yCoord);

                    switch (shape)
                    {
                        default: // square
                            // check if the current pixel is inside the square
                            if (x > tex.width / 4 && x < tex.width * 3 / 4 && y > tex.height / 4 && y < tex.height * 3 / 4)
                            {
                                col[y * tex.width + x] = Color.Lerp(color, Color.black, sample);
                            }
                            else
                            {
                                col[y * tex.width + x] = new Color(0, 0, 0, 0);
                            }
                            break;

                        case 1: // circle
                            // check if the current pixel is inside the circle
                            if (Mathf.Sqrt(Mathf.Pow(x - tex.width / 2, 2) + Mathf.Pow(y - tex.height / 2, 2)) < tex.width / 2)
                            {
                                col[y * tex.width + x] = Color.Lerp(color, Color.black, sample);
                            }
                            else
                            {
                                col[y * tex.width + x] = new Color(0, 0, 0, 0);
                            }
                            break;

                        case 2: // rhombus
                            // check if the current pixel is inside the rhombus
                            if (Mathf.Abs(x - tex.width / 2) + Mathf.Abs(y - tex.height / 2) < tex.width / 2)
                            {
                                col[y * tex.width + x] = Color.Lerp(color, Color.black, sample);
                            }
                            else
                            {
                                col[y * tex.width + x] = new Color(0, 0, 0, 0);
                            }
                            break;
                    }
                }
            }
            tex.SetPixels(col);
            tex.Apply();

            Sprite icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

            try
            {
                DontDestroyOnLoad(tex);
            }
            catch (Exception e)
            {
                Log.Info("DEBUGGER DontDestroyOnLoad(tex); failed...");
            }

            try
            {
                DontDestroyOnLoad(icon);
            }
            catch (Exception e)
            {
                Log.Info("DEBUGGER DontDestroyOnLoad(icon); failed...");
            }

            Logger.LogDebug("Attempting to create an item named " + itemName);
            itemDef = ScriptableObject.CreateInstance<ItemDef>();
            itemDef.name = "RAND_ITEM_" + xmlSafeItemName;
            itemDef.nameToken = "RAND_ITEM_" + xmlSafeItemName + "_NAME";
            itemDef.pickupToken = "RAND_ITEM_" + xmlSafeItemName + "_PICKUP";
            itemDef.descriptionToken = "RAND_ITEM_" + xmlSafeItemName + "_DESCRIPTION";
            itemDef.loreToken = "RAND_ITEM_" + xmlSafeItemName + "_LORE";
            itemDef.pickupModelPrefab = prefab;
            itemDef.pickupIconSprite = icon;
            itemDef.hidden = false;
            itemDef._itemTierDef = randItemContainer.RandItemTier;

            if (!map.ContainsKey(itemDef.nameToken))
            {
                map.Add(itemDef.nameToken, effect);
            }
            else
            {
                // Handle the case when the key already exists in the dictionary
                // You might want to log an error or decide how to handle this situation
                Logger.LogError("Key already exists in the map dictionary: " + itemDef.nameToken);
            }

            ItemAPI.Add(new CustomItem(itemDef, CreateItemDisplayRules()));
            randItemDefs.Add(itemDef);

            LanguageAPI.Add(itemDef.nameToken, itemName);
            LanguageAPI.Add(itemDef.pickupToken, effect.description);
            LanguageAPI.Add(itemDef.descriptionToken, effect.description);
            LanguageAPI.Add(itemDef.loreToken, log);

            Logger.LogDebug("Generated an item named " + itemName);
        }

        private void RecalculateStatsAPI_GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (sender && sender.inventory && UnityEngine.Networking.NetworkServer.active)
            {
                foreach (ItemIndex index in sender.inventory.itemAcquisitionOrder)
                {
                    ItemDef def = ItemCatalog.GetItemDef(index);
                    Effect effect;

                    bool found = map.TryGetValue(def.nameToken, out effect);
                    if (found && effect.effectType == Effect.EffectType.Passive)
                    {
                        if (effect.ConditionsMet(sender))
                        {
                            effect.statEffect(args, sender.inventory.GetItemCount(def), sender);
                        }
                    }
                }
            }
        }

        private void GlobalEventManager_ServerDamageDealt(On.RoR2.GlobalEventManager.orig_ServerDamageDealt orig, DamageReport report)
        {
            orig(report);
            DamageInfo info = report.damageInfo;
            if (UnityEngine.Networking.NetworkServer.active && info.attacker)
            {
                CharacterBody sender = info.attacker.GetComponent<CharacterBody>();

                if (sender && sender.inventory && UnityEngine.Networking.NetworkServer.active && info.damageColorIndex != DamageColorIndex.Item)
                {
                    foreach (ItemIndex index in sender.inventory.itemAcquisitionOrder)
                    {
                        ItemDef def = ItemCatalog.GetItemDef(index);
                        Effect effect;

                        /*foreach (KeyValuePair<string, Effect> res in map) {
                            Debug.Log("Key: " + res.Key);
                            Debug.Log("Description: " + res.Value.description);
                            Debug.Log("=====================================");
                        }

                        Debug.Log("Current Item: " + def.nameToken); */

                        bool found = map.TryGetValue(def.nameToken, out effect);
                        if (found && effect.effectType == Effect.EffectType.OnHit)
                        {
                            if (effect.ConditionsMet(sender) && Util.CheckRoll(effect.chance, sender.master))
                            {
                                effect.onHitEffect(info, sender.inventory.GetItemCount(def), report.victim.gameObject);
                            }
                        }
                    }
                }

                GameObject victim = report.victimBody.gameObject;

                sender = victim.GetComponent<CharacterBody>();

                if (sender && sender.inventory && UnityEngine.Networking.NetworkServer.active && info.damageColorIndex != DamageColorIndex.Item)
                {
                    foreach (ItemIndex index in sender.inventory.itemAcquisitionOrder)
                    {
                        ItemDef def = ItemCatalog.GetItemDef(index);
                        Effect effect;

                        /*foreach (KeyValuePair<string, Effect> res in map) {
                            Debug.Log("Key: " + res.Key);
                            Debug.Log("Description: " + res.Value.description);
                            Debug.Log("=====================================");
                        } */

                        bool found = map.TryGetValue(def.nameToken, out effect);
                        if (found && effect.effectType == Effect.EffectType.OnHurt)
                        {
                            /*
                            Debug.Log("Current Item: " + def.nameToken);
                            Debug.Log("Effect: " + effect.description);
                            Debug.Log("Conditions: " + effect.ConditionsMet(sender));
                            */
                            if (effect.ConditionsMet(sender))
                            {
                                effect.onHurtEffect(victim, sender.inventory.GetItemCount(def));
                            }
                        }
                    }
                }
            }
        }
    }
}