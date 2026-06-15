using System;
using System.Collections.Generic;
using CharacterOG.Data;
using CharacterOG.Data.PureCSharpBackend;
using CharacterOG.Models;
using CharacterOG.Runtime.Systems;
using UnityEngine;
using Object = UnityEngine.Object;

namespace POTCO.ItemCards
{
    public sealed class PotcoGenericPlayerPreviewFactory
    {
        private const string MaleModelPath = "phase_2/models/char/mp_2000";
        private const string FemaleModelPath = "phase_2/models/char/fp_2000";

        private IOgDataSource dataSource;

        public GameObject ResolveGenericPlayerPrefab()
        {
            return Resources.Load<GameObject>(MaleModelPath) ??
                   Resources.Load<GameObject>("Groups/Pirate Dummy") ??
                   Resources.Load<GameObject>("Pirate Dummy");
        }

        public GameObject CreateEquippedGenericPlayer(ItemCardData card, ItemDataRow row, PotcoSourceIndex index)
        {
            GameObject prefab = ResolveGenericPlayerPrefab();
            if (prefab == null)
                return null;

            GameObject character = Object.Instantiate(prefab);
            character.name = card == null ? "ItemPreview_GenericPlayer" : $"ItemPreview_{card.ItemId}_{card.Title}";
            character.hideFlags = HideFlags.HideAndDontSave;

            if (card == null || row == null || index == null)
                return character;

            try
            {
                EnsureCharacterDataSource();

                var bodyShapes = new Dictionary<string, BodyShapeDef>();
                foreach (KeyValuePair<string, BodyShapeDef> shape in dataSource.LoadBodyShapes("m"))
                    bodyShapes[shape.Key] = shape.Value;
                foreach (KeyValuePair<string, BodyShapeDef> shape in dataSource.LoadBodyShapes("f"))
                    if (!bodyShapes.ContainsKey(shape.Key))
                        bodyShapes[shape.Key] = shape.Value;

                ClothingCatalog clothing = dataSource.LoadClothingCatalog("m");
                Palettes palettes = dataSource.LoadPalettesAndDyeRules();
                JewelryTattooDefs jewelry = dataSource.LoadJewelryAndTattoos("m");
                FacialMorphDatabase morphs = dataSource.LoadFacialMorphs("m");

                PirateDNA dna = CreateDefaultPreviewDna(row, index);
                Transform headRoot = FindChild(character.transform, "def_head01", "def_neck", "zz_neck", "def_head", "zz_head");
                Transform bodyRoot = FindChild(character.transform, "def_scale_jt", "def_spine01", "Spine", "spine01", "BodyRoot");

                var applier = new DnaApplier(character, bodyShapes, clothing, palettes, jewelry, morphs, "m", headRoot, bodyRoot);
                applier.ApplyDNA(dna);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Item preview could not equip generic player item {card.ItemId}: {ex.Message}");
            }

            return character;
        }

        private PirateDNA CreateDefaultPreviewDna(ItemDataRow row, PotcoSourceIndex index)
        {
            var dna = new PirateDNA("Item Preview", "m")
            {
                bodyShape = "MaleIdeal",
                bodyHeight = 0.5f,
                skinColorIdx = 0,
                hair = 0,
                shirt = 1,
                pants = 0,
                shoes = 1
            };

            PotcoItemClass itemClass = (PotcoItemClass)index.GetInt(row, "ITEM_CLASS");
            int itemType = index.GetInt(row, "ITEM_TYPE");

            if (itemClass == PotcoItemClass.Clothing)
            {
                int model = index.GetInt(row, "MALE_MODEL_ID");
                int texture = index.GetInt(row, "MALE_TEXTURE_ID");
                int color = index.GetInt(row, "PRIMARY_COLOR", index.GetInt(row, "ITEM_COLOR"));

                switch (itemType)
                {
                    case 0:
                        dna.hat = model;
                        dna.hatTex = texture;
                        dna.hatColorIdx = color;
                        break;
                    case 1:
                        dna.shirt = model;
                        dna.shirtTex = texture;
                        dna.topColorIdx = color;
                        break;
                    case 2:
                        dna.vest = model;
                        dna.vestTex = texture;
                        dna.topColorIdx = color;
                        break;
                    case 3:
                        dna.coat = model;
                        dna.coatTex = texture;
                        dna.topColorIdx = color;
                        break;
                    case 4:
                        dna.pants = model;
                        dna.pantsTex = texture;
                        dna.botColorIdx = color;
                        break;
                    case 5:
                        dna.belt = model;
                        dna.beltTex = texture;
                        break;
                    case 7:
                        dna.shoes = model;
                        dna.shoesTex = texture;
                        dna.shoesColorIdx = color;
                        break;
                }
            }
            else if (itemClass == PotcoItemClass.Jewelry)
            {
                string zone = JewelryZoneNames.TryGetValue(itemType, out string resolvedZone) ? resolvedZone : "RBrow";
                dna.jewelry[zone] = Math.Max(0, index.GetInt(row, "ITEM_SUBTYPE"));
            }
            else if (itemClass == PotcoItemClass.Tattoo)
            {
                dna.tattoos.Add(new TattooSpec
                {
                    zone = Math.Max(0, itemType),
                    idx = Math.Max(0, index.GetInt(row, "ITEM_SUBTYPE")),
                    colorIdx = Math.Max(0, index.GetInt(row, "ITEM_COLOR")),
                    u = 0.5f,
                    v = 0.5f,
                    scale = 1f,
                    rotation = 0f
                });
            }

            return dna;
        }

        private void EnsureCharacterDataSource()
        {
            if (dataSource == null)
                dataSource = new PureCSharpDataSource();
        }

        private static Transform FindChild(Transform root, params string[] names)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (string name in names)
            {
                foreach (Transform transform in transforms)
                    if (transform.name == name)
                        return transform;
            }

            return null;
        }

        private static readonly Dictionary<int, string> JewelryZoneNames = new Dictionary<int, string>
        {
            { 0, "RBrow" },
            { 1, "LBrow" },
            { 2, "LEar" },
            { 3, "REar" },
            { 4, "Nose" },
            { 5, "Mouth" },
            { 6, "LHand" },
            { 7, "RHand" }
        };
    }
}
