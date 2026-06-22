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
    public sealed class PotcoBossData
    {
        [SerializeField] private string uniqueId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private float hpScale = 5f;
        [SerializeField] private float mpScale = 1f;
        [SerializeField] private int levelOverride = 0;
        [SerializeField] private float goldScale = 2f;
        [SerializeField] private float modelScale = 1.1f;
        [SerializeField] private float damageScale = 1f;
        [SerializeField] private float armorScale = 1f;
        [SerializeField] private Color highlightColor = Color.white;

        public string UniqueId { get => uniqueId; set => uniqueId = value ?? string.Empty; }
        public string DisplayName { get => displayName; set => displayName = value ?? string.Empty; }
        public float HpScale { get => hpScale; set => hpScale = value <= 0f ? 1f : value; }
        public float MpScale { get => mpScale; set => mpScale = value <= 0f ? 1f : value; }
        public int LevelOverride { get => levelOverride; set => levelOverride = Mathf.Max(0, value); }
        public float GoldScale { get => goldScale; set => goldScale = value <= 0f ? 1f : value; }
        public float ModelScale { get => modelScale; set => modelScale = value <= 0f ? 1f : value; }
        public float DamageScale { get => damageScale; set => damageScale = value <= 0f ? 1f : value; }
        public float ArmorScale { get => armorScale; set => armorScale = value <= 0f ? 1f : value; }
        public Color HighlightColor { get => highlightColor; set => highlightColor = value; }

        public PotcoBossData Clone()
        {
            return new PotcoBossData
            {
                UniqueId = UniqueId,
                DisplayName = DisplayName,
                HpScale = HpScale,
                MpScale = MpScale,
                LevelOverride = LevelOverride,
                GoldScale = GoldScale,
                ModelScale = ModelScale,
                DamageScale = DamageScale,
                ArmorScale = ArmorScale,
                HighlightColor = HighlightColor
            };
        }

        public void ApplyOverride(PotcoBossData other)
        {
            if (other == null)
                return;

            if (!string.IsNullOrEmpty(other.UniqueId))
                UniqueId = other.UniqueId;
            if (!string.IsNullOrEmpty(other.DisplayName))
                DisplayName = other.DisplayName;

            HpScale = other.HpScale;
            MpScale = other.MpScale;
            if (other.LevelOverride > 0)
                LevelOverride = other.LevelOverride;
            GoldScale = other.GoldScale;
            ModelScale = other.ModelScale;
            DamageScale = other.DamageScale;
            ArmorScale = other.ArmorScale;
            HighlightColor = other.HighlightColor;
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
        [SerializeField] private string creatureAnimationPrefix = string.Empty;
        [SerializeField] private List<string> creatureAnimationNames = new List<string>();
        [SerializeField] private List<string> creatureAnimationFiles = new List<string>();
        [SerializeField] private string skeletonStyle = string.Empty;
        [SerializeField] private string skeletonModelPath = string.Empty;
        [SerializeField] private List<string> weaponCategories = new List<string>();
        [SerializeField] private List<string> weaponItemNames = new List<string>();
        [SerializeField] private List<int> weaponItemIds = new List<int>();
        [SerializeField] private List<string> skillNames = new List<string>();
        [SerializeField] private List<int> skillIds = new List<int>();
        [SerializeField] private bool isBoss = false;
        [SerializeField] private PotcoBossData bossData = new PotcoBossData();

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
        public string CreatureAnimationPrefix { get => creatureAnimationPrefix; set => creatureAnimationPrefix = value ?? string.Empty; }
        public List<string> CreatureAnimationNames { get => creatureAnimationNames; set => creatureAnimationNames = value ?? new List<string>(); }
        public List<string> CreatureAnimationFiles { get => creatureAnimationFiles; set => creatureAnimationFiles = value ?? new List<string>(); }
        public string SkeletonStyle { get => skeletonStyle; set => skeletonStyle = value ?? string.Empty; }
        public string SkeletonModelPath { get => skeletonModelPath; set => skeletonModelPath = value ?? string.Empty; }
        public List<string> WeaponCategories { get => weaponCategories; set => weaponCategories = value ?? new List<string>(); }
        public List<string> WeaponItemNames { get => weaponItemNames; set => weaponItemNames = value ?? new List<string>(); }
        public List<int> WeaponItemIds { get => weaponItemIds; set => weaponItemIds = value ?? new List<int>(); }
        public List<string> SkillNames { get => skillNames; set => skillNames = value ?? new List<string>(); }
        public List<int> SkillIds { get => skillIds; set => skillIds = value ?? new List<int>(); }
        public bool IsBoss { get => isBoss; set => isBoss = value; }
        public PotcoBossData BossData { get => bossData; set => bossData = value?.Clone() ?? new PotcoBossData(); }
        public string BossName
        {
            get => bossData?.DisplayName ?? string.Empty;
            set
            {
                bossData ??= new PotcoBossData();
                bossData.DisplayName = value;
            }
        }
        public float BossModelScale => bossData?.ModelScale ?? 1f;
        public float BossHpScale => bossData?.HpScale ?? 1f;
        public float BossDamageScale => bossData?.DamageScale ?? 1f;

        public int PickLevel()
        {
            if (isBoss && bossData != null && bossData.LevelOverride > 0)
                return bossData.LevelOverride;

            int low = Mathf.Min(minLevel, maxLevel);
            int high = Mathf.Max(minLevel, maxLevel);
            return UnityEngine.Random.Range(low, high + 1);
        }

        public float ResolveScale(int level, float scaleMultiplier = 1f, bool includeVariantBossScale = true)
        {
            if (includeVariantBossScale && isBoss && bossData != null)
                scaleMultiplier *= bossData.ModelScale;

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
                CreatureAnimationPrefix = CreatureAnimationPrefix,
                CreatureAnimationNames = new List<string>(creatureAnimationNames ?? new List<string>()),
                CreatureAnimationFiles = new List<string>(creatureAnimationFiles ?? new List<string>()),
                SkeletonStyle = SkeletonStyle,
                SkeletonModelPath = SkeletonModelPath,
                WeaponCategories = new List<string>(weaponCategories ?? new List<string>()),
                WeaponItemNames = new List<string>(weaponItemNames ?? new List<string>()),
                WeaponItemIds = new List<int>(weaponItemIds ?? new List<int>()),
                SkillNames = new List<string>(skillNames ?? new List<string>()),
                SkillIds = new List<int>(skillIds ?? new List<int>()),
                IsBoss = IsBoss,
                BossData = bossData?.Clone() ?? new PotcoBossData()
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

    public sealed class PotcoCreatureAnimationDefinition
    {
        public string Species { get; }
        public string AnimationPrefix { get; }
        public IReadOnlyList<string> AnimationNames { get; }
        public IReadOnlyList<string> AnimationFiles { get; }

        public PotcoCreatureAnimationDefinition(string species, string animationPrefix, IReadOnlyList<string> animationNames, IReadOnlyList<string> animationFiles)
        {
            Species = species ?? string.Empty;
            AnimationPrefix = animationPrefix ?? string.Empty;
            AnimationNames = animationNames ?? Array.Empty<string>();
            AnimationFiles = animationFiles ?? Array.Empty<string>();
        }

        public IEnumerable<(string Name, string File)> EnumerateAnimations()
        {
            int count = Math.Min(AnimationNames.Count, AnimationFiles.Count);
            for (int i = 0; i < count; i++)
            {
                if (!string.IsNullOrEmpty(AnimationNames[i]) && !string.IsNullOrEmpty(AnimationFiles[i]))
                    yield return (AnimationNames[i], AnimationFiles[i]);
            }
        }
    }

    public static class PotcoCreatureAnimationCatalog
    {
        private static readonly Dictionary<string, PotcoCreatureAnimationDefinition> BuiltIn =
            new Dictionary<string, PotcoCreatureAnimationDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                { "Alligator", Definition("Alligator", "alligator", "idle:idle", "walk:walk", "run:run", "swim:swim", "swim_alt:swim_alt", "pull_back:pull_back", "pain:pull_back", "attack_left:attack_left", "attack_right:attack_right", "attack_straight:attack_straight", "flinch_left:flinch_left", "flinch_right:flinch_right", "death:death") },
                { "Bat", Definition("Bat", "bat", "idle:idle", "idle_hang:idle_hang", "glide:glide", "wounded_flight:wounded_flight", "takeoff:takeoff", "land:land", "attack_forward:attack_forward", "attack_right:attack_right", "sudden_gust:sudden_gust", "sudden_gust_alt:sudden_gust_alt", "pain:pain_left", "pain_right:pain_right", "death:death", "intro:spawn") },
                { "Chicken", Definition("Chicken", "chicken", "idle:idle", "walk:walk", "run:run", "eat:eat", "sleep:sleep", "pain:pain", "death:death") },
                { "Crab", Definition("Crab", "crab", "idle:idle", "walk:walk", "attack_left:attack_left", "attack_right:attack_right", "attack_both:attack_both", "pain:pain", "death:death") },
                { "Dog", Definition("Dog", "dog", "idle:idle", "walk:walk", "run:run", "sit:sit", "sleep:sleep") },
                { "FlyTrap", Definition("FlyTrap", "flytrap", "idle:idle", "attack_a:attack_a", "attack_jab:attack_jab", "attack_left_fake:attack_left_fake", "attack_right_fake:attack_right_fake", "intro:rise_from_ground", "shoot:spit", "pain:hit", "death:death") },
                { "Monkey", Definition("Monkey", "monkey", "idle:idle", "walk:walk", "run:run", "death:death") },
                { "Pig", Definition("Pig", "pig", "idle:idle", "walk:walk", "run:run", "eat:eat", "rooting:rooting", "sleep:sleep", "death:death") },
                { "Raven", Definition("Raven", "raven", "idle:idle", "fly:fly", "glide:glide", "death:death") },
                { "Rooster", Definition("Rooster", "rooster", "idle:idle", "walk:walk", "run:run", "eat:eat", "sleep:sleep", "pain:pain", "death:death") },
                { "Scorpion", Definition("Scorpion", "scorpion", "idle:idle", "walk:walk", "run:run", "attack_left:attack_left", "attack_right:attack_right", "attack_both:attack_both", "attack_tail_sting:attack_tail_sting", "pick_up_human:pick_up_human", "react_left:react_left", "react_right:react_right", "pain:knockback", "rear_up:rear_up", "death:death") },
                { "Seagull", Definition("Seagull", "seagull", "idle:idle", "fly:fly", "glide:glide", "death:death") },
                { "Stump", Definition("Stump", "mossman", "idle:idle", "walk:walk", "run:run", "death:death", "intro:intro", "jump:jump", "kick:kick", "kick_right:kick_right", "slap_left:slap_left", "slap_right:slap_right", "strafe_left:strafe_left", "strafe_right:strafe_right", "swat_left:swat_left", "swat_right:swat_right", "jump_attack:jump_attack", "pain:pain") },
                { "Wasp", Definition("Wasp", "wasp", "idle:idle", "idle_flying:idle_fly", "walk:walk", "drop:react_drop", "advance:attack_advance", "sting:attack_sting", "leap_sting:attack_leap_sting", "pain:react_pull_back", "death:react_death") }
            };

        public static PotcoCreatureAnimationDefinition FromVariant(PotcoEnemyVariantData variant)
        {
            if (variant != null && variant.CreatureAnimationNames != null && variant.CreatureAnimationNames.Count > 0)
            {
                string prefix = !string.IsNullOrEmpty(variant.CreatureAnimationPrefix)
                    ? variant.CreatureAnimationPrefix
                    : ResolveAnimationPrefix(variant.CreatureSpecies, variant.CreatureModelPath);
                return new PotcoCreatureAnimationDefinition(
                    variant.CreatureSpecies,
                    prefix,
                    variant.CreatureAnimationNames,
                    variant.CreatureAnimationFiles);
            }

            return Resolve(variant?.CreatureSpecies ?? variant?.TypeName);
        }

        public static PotcoCreatureAnimationDefinition Resolve(string species)
        {
            if (!string.IsNullOrEmpty(species) && BuiltIn.TryGetValue(species, out PotcoCreatureAnimationDefinition definition))
                return definition;

            string prefix = ResolveAnimationPrefix(species, string.Empty);
            return Definition(species ?? string.Empty, prefix, "idle:idle", "walk:walk", "run:run", "death:death");
        }

        public static string ResolveAnimationPrefix(string species, string modelPath)
        {
            if (!string.IsNullOrEmpty(modelPath))
            {
                string fileName = modelPath.Replace("\\", "/");
                int slash = fileName.LastIndexOf('/');
                if (slash >= 0)
                    fileName = fileName.Substring(slash + 1);

                fileName = StripLodSuffix(fileName);
                if (!string.IsNullOrEmpty(fileName))
                    return fileName.ToLowerInvariant();
            }

            if (!string.IsNullOrEmpty(species) && BuiltIn.TryGetValue(species, out PotcoCreatureAnimationDefinition definition))
                return definition.AnimationPrefix;

            return StripLodSuffix(species ?? string.Empty).ToLowerInvariant();
        }

        private static PotcoCreatureAnimationDefinition Definition(string species, string prefix, params string[] pairs)
        {
            var names = new List<string>();
            var files = new List<string>();
            foreach (string pair in pairs)
            {
                string[] parts = pair.Split(':');
                if (parts.Length != 2)
                    continue;

                names.Add(parts[0]);
                files.Add(parts[1]);
            }

            return new PotcoCreatureAnimationDefinition(species, prefix, names, files);
        }

        private static string StripLodSuffix(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string result = value;
            foreach (string suffix in new[] { "_hi", "_lo", "_mid" })
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return result.Substring(0, result.Length - suffix.Length);
            }

            return result;
        }
    }

    public sealed class PotcoEnemySourceIndex
    {
        private readonly Dictionary<string, PotcoEnemySpawnDefinition> spawnables =
            new Dictionary<string, PotcoEnemySpawnDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PotcoBossData> bosses =
            new Dictionary<string, PotcoBossData>(StringComparer.OrdinalIgnoreCase);
        private PotcoBossData defaultBoss = new PotcoBossData();

        public IReadOnlyDictionary<string, PotcoEnemySpawnDefinition> Spawnables => spawnables;
        public IReadOnlyDictionary<string, PotcoBossData> Bosses => bosses;

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

        public void SetDefaultBossData(PotcoBossData data)
        {
            defaultBoss = data?.Clone() ?? new PotcoBossData();
        }

        public void AddBossData(PotcoBossData data)
        {
            if (data == null || string.IsNullOrEmpty(data.UniqueId))
                return;

            bosses[data.UniqueId] = data.Clone();
        }

        public PotcoBossData ResolveBossData(string uniqueId)
        {
            PotcoBossData result = defaultBoss?.Clone() ?? new PotcoBossData();
            string key = (uniqueId ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(key) && bosses.TryGetValue(key, out PotcoBossData specific))
                result.ApplyOverride(specific);

            return result;
        }
    }
}
