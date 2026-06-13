#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using CharacterOG.Data;
using CharacterOG.Data.PureCSharpBackend;
using CharacterOG.Models;
using CharacterOG.Runtime.Systems;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace POTCO.Editor.ItemCreator
{
    public sealed class ItemPreviewData
    {
        public Texture2D Icon { get; set; }
        public GameObject ModelPrefab { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool UsesGenericPlayer { get; set; }
    }

    public sealed class ItemPreviewResolver
    {
        private const string MaleModelPath = "phase_2/models/char/mp_2000";
        private const string FemaleModelPath = "phase_2/models/char/fp_2000";

        private readonly Dictionary<string, Texture2D> iconCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> modelCache = new Dictionary<string, GameObject>(StringComparer.Ordinal);

        private IOgDataSource dataSource;

        public ItemPreviewData Resolve(ItemCardData card)
        {
            var preview = new ItemPreviewData();
            if (card == null)
                return preview;

            preview.Icon = ResolveIcon(card.IconName);

            if (card.PreviewMode == ItemPreviewMode.GenericPlayer)
            {
                preview.UsesGenericPlayer = true;
                preview.ModelPrefab = Resources.Load<GameObject>(MaleModelPath) ?? ResolveModelPrefab("Pirate Dummy");
                preview.Status = preview.ModelPrefab == null
                    ? "Generic player model not imported."
                    : "Generic player preview";
                return preview;
            }

            if (card.PreviewMode == ItemPreviewMode.Model)
                preview.ModelPrefab = ResolveModelPrefab(card.ModelName);

            if (preview.ModelPrefab == null && preview.Icon == null)
                preview.Status = "Missing preview asset.";

            return preview;
        }

        public GameObject CreatePreviewObject(ItemCardData card, ItemDataRow row, PotcoSourceIndex index)
        {
            if (card == null)
                return null;

            if (card.PreviewMode == ItemPreviewMode.GenericPlayer)
                return CreateEquippedGenericPlayer(card, row, index);

            GameObject prefab = Resolve(card).ModelPrefab;
            return prefab == null ? null : Object.Instantiate(prefab);
        }

        private GameObject CreateEquippedGenericPlayer(ItemCardData card, ItemDataRow row, PotcoSourceIndex index)
        {
            GameObject prefab = Resources.Load<GameObject>(MaleModelPath);
            if (prefab == null)
                prefab = ResolveModelPrefab("Pirate Dummy");
            if (prefab == null)
                return null;

            GameObject character = Object.Instantiate(prefab);
            character.name = $"ItemPreview_{card.ItemId}_{card.Title}";
            character.hideFlags = HideFlags.HideAndDontSave;

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

        private Texture2D ResolveIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
                return null;

            if (iconCache.TryGetValue(iconName, out Texture2D cached))
                return cached;

            string[] paths =
            {
                $"Assets/Resources/phase_2/maps/{iconName}.png",
                $"Assets/Resources/phase_2/maps/{iconName}.jpg",
                $"Assets/Resources/phase_2/maps/{iconName}.jpeg",
                $"Assets/Resources/phase_2/maps/{iconName}.tga",
                $"Assets/Resources/phase_2/maps/{iconName}.rgb"
            };

            foreach (string path in paths)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                {
                    iconCache[iconName] = texture;
                    return texture;
                }
            }

            string[] guids = AssetDatabase.FindAssets($"{iconName} t:Texture2D", new[] { "Assets/Resources" });
            foreach (string guid in guids)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (texture != null)
                {
                    iconCache[iconName] = texture;
                    return texture;
                }
            }

            iconCache[iconName] = null;
            return null;
        }

        private GameObject ResolveModelPrefab(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
                return null;

            if (modelCache.TryGetValue(modelName, out GameObject cached))
                return cached;

            string normalized = modelName.Replace("\\", "/");
            string[] resourcePaths =
            {
                $"phase_3/models/handheld/{normalized}",
                $"phase_2/models/handheld/{normalized}",
                $"phase_2/models/inventory/{normalized}",
                $"phase_2/models/char/{normalized}"
            };

            foreach (string path in resourcePaths)
            {
                GameObject loaded = Resources.Load<GameObject>(path);
                if (loaded != null)
                {
                    modelCache[modelName] = loaded;
                    return loaded;
                }
            }

            string[] guids = AssetDatabase.FindAssets($"{PathSafeSearchName(modelName)} t:GameObject", new[] { "Assets/Resources" });
            foreach (string guid in guids)
            {
                GameObject loaded = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (loaded != null)
                {
                    modelCache[modelName] = loaded;
                    return loaded;
                }
            }

            modelCache[modelName] = null;
            return null;
        }

        private static string PathSafeSearchName(string modelName)
        {
            int slash = modelName.LastIndexOf('/');
            return slash >= 0 ? modelName.Substring(slash + 1) : modelName;
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
#endif
