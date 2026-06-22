using CharacterOG.Data.PureCSharpBackend;
using CharacterOG.Models;
using CharacterOG.Runtime;
using CharacterOG.Runtime.Systems;
using UnityEngine;

namespace POTCO
{
    public static class PotcoEnemyHumanFactory
    {
        private const string MaleModelPath = "phase_2/models/char/mp_2000";
        private const string FemaleModelPath = "phase_2/models/char/fp_2000";
        private static readonly int[] ReferenceNpcSkinColors = { 0, 2, 3, 5, 8 };
        private static readonly int[] ReferenceNpcBodyChoices = { 5, 6, 7, 8, 9 };
        private static readonly float[] NavyBodyHeights = { -0.3f, 0f, 0.3f, 0.6f };
        private static readonly float[] EitcBodyHeights = { 0f, 0.3f, 0.6f };
        private static readonly int[] ReferenceHeadTextures = { 0, 1, 4 };
        private static readonly int[] ReferenceHairStyles = { 1, 9 };
        private static readonly int[] ReferenceBeards = { 0, 7, 9 };
        private static readonly int[] ReferenceMustaches = { 0, 1, 2 };
        private static readonly int[] ReferenceHairColors = { 0, 1, 2, 3, 4, 5, 6, 7 };
        private static readonly int[] ReferenceEyeColors = { 0, 1, 2, 3, 4, 5 };
        private static readonly string[] ReferenceGhostGenderChoices = { "m", "m", "f" };
        private static readonly int[] ReferenceGhostMaleHairStyles = { 1, 2, 3, 4, 5, 6 };
        private static readonly int[] ReferenceGhostMustaches = { 0, 1, 2, 4 };
        private static readonly int[] ReferenceGhostFemaleShirts = { 3, 4, 5 };
        private static readonly int[] ReferenceGhostFemalePants = { 0, 2 };
        private static readonly int[] ReferenceGhostFemaleShoes = { 1, 2, 3, 4 };
        private static readonly string[] ReferenceHeadMorphs =
        {
            "jawWidth",
            "jawLength",
            "jawChinSize",
            "jawAngle",
            "cheekFat",
            "browProtruding",
            "eyeBulge",
            "noseBridgeWidth",
            "noseNostrilWidth",
            "noseLength",
            "noseBump",
            "noseNostrilHeight",
            "noseNostrilAngle",
            "earScale",
            "earFlapAngle",
            "earPosition"
        };

        private static PureCSharpDataSource s_dataSource;

        public static GameObject CreateHumanEnemy(PotcoEnemyVariantData variant, Transform parent)
        {
            PirateDNA dna = CreatePresetDna(variant);
            string modelPath = dna.gender == "f" ? FemaleModelPath : MaleModelPath;
            GameObject prefab = Resources.Load<GameObject>(modelPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[PotcoEnemyHumanFactory] Missing human model Resources/{modelPath}; using capsule fallback for {variant?.TypeName}");
                return CreateFallbackHuman(variant, parent);
            }

            GameObject character = UnityEngine.Object.Instantiate(prefab, parent, false);
            character.name = $"Enemy_{variant?.TypeName ?? "Human"}";

            ApplyDna(character, dna);
            AddGenderPersistence(character, dna);
            return character;
        }

        private static void ApplyDna(GameObject character, PirateDNA dna)
        {
            try
            {
                s_dataSource ??= new PureCSharpDataSource();
                var bodyShapes = s_dataSource.LoadBodyShapes(dna.gender);
                var palettes = s_dataSource.LoadPalettesAndDyeRules();
                var clothing = s_dataSource.LoadClothingCatalog(dna.gender);
                var jewelry = s_dataSource.LoadJewelryAndTattoos(dna.gender);
                var morphs = s_dataSource.LoadFacialMorphs(dna.gender);
                FindDnaRoots(character, out Transform headRoot, out Transform bodyRoot);

                var applier = new DnaApplier(character, bodyShapes, clothing, palettes, jewelry, morphs, dna.gender, headRoot, bodyRoot);
                applier.ApplyDNA(dna);

                CharacterColorPersistence colorPersistence = character.GetComponent<CharacterColorPersistence>();
                if (colorPersistence == null)
                    colorPersistence = character.AddComponent<CharacterColorPersistence>();

                var colors = applier.GetAppliedColors();
                colorPersistence.StoreColors(colors.skin, colors.hair, colors.top, colors.bot, colors.shoe);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PotcoEnemyHumanFactory] DNA application failed for {dna.name}: {ex.Message}");
            }
        }

        private static void AddGenderPersistence(GameObject character, PirateDNA dna)
        {
            CharacterGenderData genderData = character.GetComponent<CharacterGenderData>();
            if (genderData == null)
                genderData = character.AddComponent<CharacterGenderData>();

            genderData.SetGender(dna.gender);
        }

        private static PirateDNA CreatePresetDna(PotcoEnemyVariantData variant)
        {
            return CreatePresetDna(variant, UnityEngine.Random.Range(1, int.MaxValue));
        }

        private static PirateDNA CreatePresetDna(PotcoEnemyVariantData variant, int seed)
        {
            PotcoEnemyHumanPreset preset = variant?.HumanPreset ?? PotcoEnemyHumanPreset.None;
            string typeName = variant?.TypeName ?? "Enemy";
            var random = new System.Random(seed);
            var dna = new PirateDNA(typeName, "m")
            {
                gender = "m",
                bodyShape = ResolveNeutralBodyShapeName(Pick(random, ReferenceNpcBodyChoices)),
                bodyHeight = Pick(random, NavyBodyHeights),
                skinColorIdx = Pick(random, ReferenceNpcSkinColors),
                hair = Pick(random, ReferenceHairStyles),
                beard = Pick(random, ReferenceBeards),
                mustache = Pick(random, ReferenceMustaches),
                hairColorIdx = Pick(random, ReferenceHairColors),
                headTexture = Pick(random, ReferenceHeadTextures),
                eyeColorIdx = Pick(random, ReferenceEyeColors)
            };
            PopulateReferenceHeadMorphs(dna, random);

            switch (preset)
            {
                case PotcoEnemyHumanPreset.TradingCompany:
                    dna.bodyHeight = Pick(random, EitcBodyHeights);
                    ApplyUniform(dna, coat: 4, pants: 5, shoes: 4, hat: 4, topColor: 0, botColor: 0);
                    break;
                case PotcoEnemyHumanPreset.Navy:
                    ApplyUniform(dna, coat: 3, pants: 4, shoes: 3, hat: 3, topColor: 0, botColor: 0);
                    break;
                case PotcoEnemyHumanPreset.VoodooZombie:
                    dna.gender = random.Next(0, 3) == 0 ? "f" : "m";
                    dna.bodyShape = dna.gender == "f" ? "FemaleIdeal" : "MaleIdeal";
                    dna.skinColorIdx = 6 + random.Next(0, 3);
                    dna.shirt = 1 + random.Next(0, 4);
                    dna.vest = random.Next(0, 3);
                    dna.pants = 1 + random.Next(0, 4);
                    dna.hair = random.Next(0, 6);
                    dna.beard = 0;
                    dna.mustache = 0;
                    break;
                case PotcoEnemyHumanPreset.BountyHunter:
                    dna.gender = "m";
                    dna.coat = 1 + random.Next(0, 4);
                    dna.vest = 1 + random.Next(0, 4);
                    dna.pants = 1 + random.Next(0, 5);
                    dna.belt = 1 + random.Next(0, 4);
                    dna.hat = 1 + random.Next(0, 6);
                    dna.topColorIdx = 4 + random.Next(0, 8);
                    dna.botColorIdx = 4 + random.Next(0, 8);
                    break;
                case PotcoEnemyHumanPreset.Ghost:
                    ApplyGhostDna(dna, random);
                    break;
                default:
                    dna.shirt = 1;
                    dna.pants = 1;
                    dna.belt = 1;
                    break;
            }

            return dna;
        }

        private static void ApplyGhostDna(PirateDNA dna, System.Random random)
        {
            int colorCount = Mathf.Max(1, ResolveDyeColorCount());
            dna.gender = Pick(random, ReferenceGhostGenderChoices);
            dna.bodyShape = ResolveBodyShapeName(dna.gender, Pick(random, ReferenceNpcBodyChoices));
            dna.bodyHeight = 0f;
            dna.skinColorIdx = 0;
            dna.belt = 0;
            dna.coat = random.Next(0, 3);
            dna.coatTex = 0;
            dna.topColorIdx = random.Next(0, colorCount);
            dna.botColorIdx = random.Next(0, colorCount);
            dna.hatColorIdx = random.Next(0, colorCount);
            dna.shoesColorIdx = random.Next(0, colorCount);
            dna.hairColorIdx = random.Next(0, 5);
            dna.headTexture = 0;
            dna.eyeColorIdx = Pick(random, ReferenceEyeColors);

            if (dna.gender == "m")
            {
                dna.shirt = 6;
                dna.pants = 1;
                dna.shoes = 1;
                dna.hat = random.Next(0, 5);
                dna.hair = Pick(random, ReferenceGhostMaleHairStyles);
                dna.beard = random.Next(0, 11);
                dna.mustache = Pick(random, ReferenceGhostMustaches);
                return;
            }

            dna.shirt = Pick(random, ReferenceGhostFemaleShirts);
            dna.pants = Pick(random, ReferenceGhostFemalePants);
            dna.shoes = Pick(random, ReferenceGhostFemaleShoes);
            dna.hat = 0;
            dna.hair = random.Next(0, 16);
            dna.beard = 0;
            dna.mustache = 0;
        }

        private static void ApplyUniform(PirateDNA dna, int coat, int pants, int shoes, int hat, int topColor, int botColor)
        {
            dna.gender = "m";
            dna.coat = coat;
            dna.pants = pants;
            dna.shoes = shoes;
            dna.hat = hat;
            dna.shirt = 0;
            dna.vest = 0;
            dna.belt = 0;
            dna.topColorIdx = topColor;
            dna.botColorIdx = botColor;
            dna.hatColorIdx = topColor;
            dna.shoesColorIdx = botColor;
        }

        private static T Pick<T>(System.Random random, T[] choices)
        {
            return choices[random.Next(0, choices.Length)];
        }

        private static string ResolveNeutralBodyShapeName(int bodyChoiceIndex)
        {
            return ResolveBodyShapeName("m", bodyChoiceIndex);
        }

        private static string ResolveBodyShapeName(string gender, int bodyChoiceIndex)
        {
            try
            {
                s_dataSource ??= new PureCSharpDataSource();
                s_dataSource.LoadBodyShapes(gender);
                return s_dataSource.GetBodyShapeNameFromIndex(gender, bodyChoiceIndex);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PotcoEnemyHumanFactory] Failed to resolve {gender} body shape index {bodyChoiceIndex}: {ex.Message}");
            }

            if (gender == "f")
                return "FemaleIdeal";

            return bodyChoiceIndex switch
            {
                5 => "MaleFat",
                6 => "MaleSkinny",
                7 => "MaleIdeal",
                8 => "MaleOutofShape",
                9 => "MaleHuge",
                _ => "MaleIdeal"
            };
        }

        private static int ResolveDyeColorCount()
        {
            try
            {
                s_dataSource ??= new PureCSharpDataSource();
                var palettes = s_dataSource.LoadPalettesAndDyeRules();
                if (palettes?.dye != null && palettes.dye.Count > 0)
                    return palettes.dye.Count;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PotcoEnemyHumanFactory] Failed to resolve dye palette count for ghost DNA: {ex.Message}");
            }

            return 64;
        }

        private static void PopulateReferenceHeadMorphs(PirateDNA dna, System.Random random)
        {
            for (int i = 0; i < ReferenceHeadMorphs.Length; i++)
            {
                bool allowNegative = ReferenceHeadMorphs[i] != "browProtruding";
                dna.headMorphs[ReferenceHeadMorphs[i]] = TossAValue(random, ReferenceHeadMorphs[i] == "noseBump" || ReferenceHeadMorphs[i].StartsWith("ear") ? 0.5f : 1f, allowNegative);
            }
        }

        private static float TossAValue(System.Random random, float clip, bool allowNegative = true)
        {
            float value = Mathf.Min((float)random.NextDouble(), clip);
            if (allowNegative && random.Next(0, 2) == 1)
                value = -value;

            return value;
        }

        private static void FindDnaRoots(GameObject character, out Transform headRoot, out Transform bodyRoot)
        {
            headRoot = null;
            bodyRoot = null;

            Transform[] allTransforms = character.GetComponentsInChildren<Transform>(true);
            string[] headCandidates = { "def_head01", "def_neck", "zz_neck", "def_head", "zz_head" };
            string[] bodyCandidates = { "def_scale_jt", "def_spine01", "Spine", "spine01", "BodyRoot", "def_spine02" };

            foreach (string candidate in headCandidates)
            {
                headRoot = System.Array.Find(allTransforms, t => t.name == candidate);
                if (headRoot != null)
                    break;
            }

            foreach (string candidate in bodyCandidates)
            {
                bodyRoot = System.Array.Find(allTransforms, t => t.name == candidate);
                if (bodyRoot != null)
                    break;
            }
        }

        private static GameObject CreateFallbackHuman(PotcoEnemyVariantData variant, Transform parent)
        {
            GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fallback.name = $"Enemy_{variant?.TypeName ?? "Human"}_Fallback";
            fallback.transform.SetParent(parent, false);

            Renderer renderer = fallback.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = new Material(Shader.Find("Standard"))
                {
                    color = variant?.HumanPreset == PotcoEnemyHumanPreset.TradingCompany
                        ? new Color(0.25f, 0.2f, 0.15f)
                        : new Color(0.1f, 0.25f, 0.45f)
                };
            }

            return fallback;
        }
    }
}
