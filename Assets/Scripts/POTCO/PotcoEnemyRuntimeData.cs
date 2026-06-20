using System;
using System.Collections.Generic;
using UnityEngine;

namespace POTCO
{
    public enum PotcoEnemyKind
    {
        Unknown = 0,
        Creature = 1,
        Skeleton = 2,
        Human = 3
    }

    public enum PotcoEnemyHumanPreset
    {
        None = 0,
        Navy = 1,
        TradingCompany = 2,
        BountyHunter = 3,
        VoodooZombie = 4,
        Ghost = 5,
        Pirate = 6,
        Townfolk = 7
    }

    public enum PotcoEnemyMonsterClass
    {
        Unknown = 0,
        Skeleton = 1,
        Monster = 2,
        Human = 3
    }

    [Serializable]
    public sealed class PotcoEnemySpawnDefinition
    {
        [SerializeField] private string spawnableName = string.Empty;
        [SerializeField] private List<PotcoEnemyVariantData> variants = new List<PotcoEnemyVariantData>();

        public string SpawnableName
        {
            get => spawnableName;
            set => spawnableName = value ?? string.Empty;
        }

        public List<PotcoEnemyVariantData> Variants
        {
            get => variants;
            set => variants = value ?? new List<PotcoEnemyVariantData>();
        }

        public bool IsValid => !string.IsNullOrEmpty(spawnableName) && variants != null && variants.Count > 0;

        public static PotcoEnemySpawnDefinition Empty(string spawnable)
        {
            return new PotcoEnemySpawnDefinition { SpawnableName = spawnable };
        }

        public PotcoEnemySpawnDefinition Clone()
        {
            var copy = new PotcoEnemySpawnDefinition { SpawnableName = SpawnableName };
            if (variants != null)
            {
                foreach (PotcoEnemyVariantData variant in variants)
                {
                    if (variant != null)
                        copy.variants.Add(variant.Clone());
                }
            }

            return copy;
        }

        public PotcoEnemyVariantData ChooseVariant()
        {
            if (variants == null || variants.Count == 0)
                return null;

            List<PotcoEnemyVariantData> enabled = variants.FindAll(v => v != null && v.Enabled);
            List<PotcoEnemyVariantData> source = enabled.Count > 0 ? enabled : variants;
            return source[UnityEngine.Random.Range(0, source.Count)];
        }
    }

    [Serializable]
    public sealed class PotcoEnemyVariantData
    {
        [SerializeField] private string typeName = string.Empty;
        [SerializeField] private string faction = string.Empty;
        [SerializeField] private string track = string.Empty;
        [SerializeField] private PotcoEnemyKind kind = PotcoEnemyKind.Unknown;
        [SerializeField] private PotcoEnemyHumanPreset humanPreset = PotcoEnemyHumanPreset.None;
        [SerializeField] private PotcoEnemyMonsterClass monsterClass = PotcoEnemyMonsterClass.Unknown;
        [SerializeField] private int minLevel = 1;
        [SerializeField] private int maxLevel = 1;
        [SerializeField] private float baseScale = 1f;
        [SerializeField] private float height = 1.8f;
        [SerializeField] private float battleTubeRadius = 0.5f;
        [SerializeField] private bool enabled = true;
        [SerializeField] private string creatureSpecies = string.Empty;
        [SerializeField] private string creatureModelPath = string.Empty;
        [SerializeField] private string skeletonStyle = string.Empty;
        [SerializeField] private string skeletonModelPath = string.Empty;
        [SerializeField] private List<string> weaponCategories = new List<string>();
        [SerializeField] private List<string> weaponItemNames = new List<string>();
        [SerializeField] private List<int> weaponItemIds = new List<int>();
        [SerializeField] private List<string> skillNames = new List<string>();
        [SerializeField] private List<int> skillIds = new List<int>();

        public string TypeName { get => typeName; set => typeName = value ?? string.Empty; }
        public string Faction { get => faction; set => faction = value ?? string.Empty; }
        public string Track { get => track; set => track = value ?? string.Empty; }
        public PotcoEnemyKind Kind { get => kind; set => kind = value; }
        public PotcoEnemyHumanPreset HumanPreset { get => humanPreset; set => humanPreset = value; }
        public PotcoEnemyMonsterClass MonsterClass { get => monsterClass; set => monsterClass = value; }
        public int MinLevel { get => minLevel; set => minLevel = Mathf.Max(0, value); }
        public int MaxLevel { get => maxLevel; set => maxLevel = Mathf.Max(0, value); }
        public float BaseScale { get => baseScale; set => baseScale = value <= 0f ? 1f : value; }
        public float Height { get => height; set => height = value <= 0f ? 1.8f : value; }
        public float BattleTubeRadius { get => battleTubeRadius; set => battleTubeRadius = value <= 0f ? 0.5f : value; }
        public bool Enabled { get => enabled; set => enabled = value; }
        public string CreatureSpecies { get => creatureSpecies; set => creatureSpecies = value ?? string.Empty; }
        public string CreatureModelPath { get => creatureModelPath; set => creatureModelPath = value ?? string.Empty; }
        public string SkeletonStyle { get => skeletonStyle; set => skeletonStyle = value ?? string.Empty; }
        public string SkeletonModelPath { get => skeletonModelPath; set => skeletonModelPath = value ?? string.Empty; }
        public List<string> WeaponCategories { get => weaponCategories; set => weaponCategories = value ?? new List<string>(); }
        public List<string> WeaponItemNames { get => weaponItemNames; set => weaponItemNames = value ?? new List<string>(); }
        public List<int> WeaponItemIds { get => weaponItemIds; set => weaponItemIds = value ?? new List<int>(); }
        public List<string> SkillNames { get => skillNames; set => skillNames = value ?? new List<string>(); }
        public List<int> SkillIds { get => skillIds; set => skillIds = value ?? new List<int>(); }

        public int PickLevel()
        {
            int low = Mathf.Min(minLevel, maxLevel);
            int high = Mathf.Max(minLevel, maxLevel);
            return UnityEngine.Random.Range(low, high + 1);
        }

        public float ResolveScale(int level, float scaleMultiplier = 1f)
        {
            if (kind == PotcoEnemyKind.Human || humanPreset != PotcoEnemyHumanPreset.None)
                return 1f * scaleMultiplier;

            float averageLevel = (minLevel + maxLevel) * 0.5f;
            float modifier = 1f + 0.03f * (level - averageLevel);
            return baseScale * (modifier + (scaleMultiplier - 1f));
        }

        public PotcoEnemyVariantData Clone()
        {
            return new PotcoEnemyVariantData
            {
                TypeName = TypeName,
                Faction = Faction,
                Track = Track,
                Kind = Kind,
                HumanPreset = HumanPreset,
                MonsterClass = MonsterClass,
                MinLevel = MinLevel,
                MaxLevel = MaxLevel,
                BaseScale = BaseScale,
                Height = Height,
                BattleTubeRadius = BattleTubeRadius,
                Enabled = Enabled,
                CreatureSpecies = CreatureSpecies,
                CreatureModelPath = CreatureModelPath,
                SkeletonStyle = SkeletonStyle,
                SkeletonModelPath = SkeletonModelPath,
                WeaponCategories = new List<string>(weaponCategories ?? new List<string>()),
                WeaponItemNames = new List<string>(weaponItemNames ?? new List<string>()),
                WeaponItemIds = new List<int>(weaponItemIds ?? new List<int>()),
                SkillNames = new List<string>(skillNames ?? new List<string>()),
                SkillIds = new List<int>(skillIds ?? new List<int>())
            };
        }
    }

    public static class PotcoBipedAnimationResolver
    {
        private static readonly string[] AnimationPhases = { "phase_2", "phase_3", "phase_4", "phase_5", "phase_6" };
        private static readonly string[] AnimationFolders = { "models/char", "char" };

        public static IEnumerable<string> BuildResourceCandidates(string animationName, string style)
        {
            foreach (string clipName in BuildClipNameCandidates(animationName, style))
            {
                foreach (string phase in AnimationPhases)
                {
                    foreach (string folder in AnimationFolders)
                        yield return $"{phase}/{folder}/{clipName}";
                }
            }
        }

        public static IEnumerable<string> BuildClipNameCandidates(string animationName, string style)
        {
            var names = new List<string>();
            AddSkeletonStyleNames(names, animationName, style);
            AddUnique(names, $"mp_{animationName}");
            AddUnique(names, $"fp_{animationName}");

            if (!string.IsNullOrWhiteSpace(style) && !string.Equals(style, "mp", StringComparison.OrdinalIgnoreCase))
                AddUnique(names, $"{animationName}_{style}");

            AddUnique(names, animationName);
            return names;
        }

        private static void AddSkeletonStyleNames(List<string> names, string animationName, string style)
        {
            if (string.IsNullOrWhiteSpace(style) || string.Equals(style, "mp", StringComparison.OrdinalIgnoreCase))
                return;

            string normalizedStyle = style.ToLowerInvariant();
            if (TryGetSkeletonPrimarySuffix(normalizedStyle, animationName, out string primarySuffix))
                AddUnique(names, $"mp_{animationName}{primarySuffix}");

            if (TryGetSkeletonSecondarySuffix(normalizedStyle, animationName, out string secondarySuffix))
                AddUnique(names, $"mp_{animationName}{secondarySuffix}");
        }

        private static bool TryGetSkeletonPrimarySuffix(string style, string animationName, out string suffix)
        {
            suffix = string.Empty;
            if (style == "1" || style == "2" || style == "4" || style == "8")
            {
                if (animationName == "idle" || animationName == "walk" || animationName == "run")
                {
                    suffix = "_gp" + style;
                    return true;
                }

                if (animationName == "intro")
                {
                    suffix = "_gp2";
                    return true;
                }
            }

            if (style.StartsWith("fr", StringComparison.Ordinal))
            {
                if (animationName == "walk" || animationName == "sword_advance" || animationName.StartsWith("foil_", StringComparison.Ordinal))
                {
                    suffix = "_fr_gp1";
                    return true;
                }
            }

            if (style.StartsWith("sp", StringComparison.Ordinal) && animationName.StartsWith("dualcutlass_", StringComparison.Ordinal))
            {
                suffix = "_sp_gp4";
                return true;
            }

            return false;
        }

        private static bool TryGetSkeletonSecondarySuffix(string style, string animationName, out string suffix)
        {
            suffix = string.Empty;
            if (!style.StartsWith("fr", StringComparison.Ordinal) && !style.StartsWith("sp", StringComparison.Ordinal))
                return false;

            if (animationName != "idle" && animationName != "walk" && animationName != "run" && animationName != "intro")
                return false;

            suffix = "_gp" + ResolveSkeletonSecondaryGp(style);
            return true;
        }

        private static string ResolveSkeletonSecondaryGp(string style)
        {
            if (style.Contains("2"))
                return "2";
            if (style.Contains("3"))
                return "8";
            if (style.Contains("4") || style.Contains("b"))
                return "4";
            return "1";
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value))
                values.Add(value);
        }
    }

    public sealed class PotcoEnemySourceIndex
    {
        private readonly Dictionary<string, PotcoEnemySpawnDefinition> spawnables =
            new Dictionary<string, PotcoEnemySpawnDefinition>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, PotcoEnemySpawnDefinition> Spawnables => spawnables;

        public void AddSpawnable(PotcoEnemySpawnDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.SpawnableName))
                return;

            spawnables[definition.SpawnableName] = definition;
        }

        public PotcoEnemySpawnDefinition ResolveSpawnable(string spawnableName)
        {
            string key = (spawnableName ?? string.Empty).Trim();
            if (spawnables.TryGetValue(key, out PotcoEnemySpawnDefinition definition))
                return definition.Clone();

            return PotcoEnemySpawnDefinition.Empty(key);
        }
    }
}
