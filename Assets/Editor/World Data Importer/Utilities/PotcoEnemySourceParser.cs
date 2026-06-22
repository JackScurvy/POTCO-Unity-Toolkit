using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using POTCO;
using UnityEngine;
using WorldDataImporter.Data;

namespace WorldDataImporter.Utilities
{
    public static class PotcoEnemySourceParser
    {
        private static PotcoEnemySourceIndex s_cachedIndex;
        private static readonly object s_cacheLock = new object();

        public static PotcoEnemySourceIndex LoadFromProjectSource()
        {
            return LoadFromSourceRoot(Path.Combine(Application.dataPath, "Editor", "POTCO_Source"));
        }

        public static PotcoEnemySourceIndex LoadFromSourceRoot(string sourceRoot)
        {
            lock (s_cacheLock)
            {
                if (s_cachedIndex != null)
                    return s_cachedIndex;

                s_cachedIndex = ParseSourceRoot(sourceRoot);
                return s_cachedIndex;
            }
        }

        public static void ClearCache()
        {
            lock (s_cacheLock)
                s_cachedIndex = null;
        }

        private static PotcoEnemySourceIndex ParseSourceRoot(string sourceRoot)
        {
            string avatarTypesPath = Path.Combine(sourceRoot, "pirate", "AvatarTypes.py");
            string enemyGlobalsPath = Path.Combine(sourceRoot, "battle", "EnemyGlobals.py");
            string enemySkillsPath = Path.Combine(sourceRoot, "battle", "EnemySkills.py");
            string skeletonPath = Path.Combine(sourceRoot, "npc", "Skeleton.py");
            string bossNpcListPath = Path.Combine(sourceRoot, "npc", "BossNPCList.py");
            string localizerPath = Path.Combine(sourceRoot, "PLocalizerEnglish.py");
            string uberDogPath = Path.Combine(sourceRoot, "uberdog", "UberDogGlobals.py");
            string itemDataPath = Path.Combine(sourceRoot, "inventory", "ItemData.py");

            if (!File.Exists(avatarTypesPath))
                throw new FileNotFoundException("POTCO AvatarTypes.py is required for enemy spawnables.", avatarTypesPath);
            if (!File.Exists(enemyGlobalsPath))
                throw new FileNotFoundException("POTCO EnemyGlobals.py is required for enemy stats.", enemyGlobalsPath);

            string avatarTypes = File.ReadAllText(avatarTypesPath);
            string enemyGlobals = File.ReadAllText(enemyGlobalsPath);
            string enemySkills = File.Exists(enemySkillsPath) ? File.ReadAllText(enemySkillsPath) : string.Empty;
            string skeleton = File.Exists(skeletonPath) ? File.ReadAllText(skeletonPath) : string.Empty;
            string localizer = File.Exists(localizerPath) ? File.ReadAllText(localizerPath) : string.Empty;
            string bossNpcList = File.Exists(bossNpcListPath) ? File.ReadAllText(bossNpcListPath) : string.Empty;

            BossNameCursor localizedBossNames = ParseLocalizedBossNames(localizer);
            Dictionary<string, string> uniqueBossNames = ParseBossNpcNames(localizer);
            Dictionary<string, PotcoBossData> bossDataById = ParseBossData(bossNpcList, uniqueBossNames, out PotcoBossData defaultBossData);
            Dictionary<string, AvatarMeta> avatarMeta = ParseAvatarTypes(avatarTypes, localizedBossNames);
            Dictionary<string, EnemyStats> stats = ParseEnemyStats(enemyGlobals);
            Dictionary<int, LevelMultiplier> levelMultipliers = ParseLevelMultipliers(enemyGlobals);
            Dictionary<string, List<string>> enemyWeaponTable = ParseEnemyWeaponTable(enemyGlobals);
            Dictionary<string, int> enemySkillConstants = ParseSimpleConstants(enemySkills, "EnemySkills.");
            Dictionary<string, int> inventoryTypeConstants = File.Exists(uberDogPath)
                ? ParseSimpleConstants(File.ReadAllText(uberDogPath), "InventoryType.")
                : new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> itemConstants = File.Exists(itemDataPath)
                ? ParseItemDataConstants(File.ReadAllText(itemDataPath))
                : new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, SkillLoadout> skillLoadouts = ParseSkillLoadouts(enemyGlobals, enemySkillConstants, inventoryTypeConstants);
            SkeletonStyleIndex skeletonStyles = ParseSkeletonStyles(skeleton);

            var index = new PotcoEnemySourceIndex();
            index.SetDefaultBossData(defaultBossData);
            foreach (PotcoBossData bossData in bossDataById.Values)
                index.AddBossData(bossData);

            Dictionary<string, List<string>> spawnableMap = ParseSpawnables(avatarTypes, avatarMeta);
            AddEditorSpawnables(spawnableMap, avatarMeta);
            AddAvatarTypeSpawnables(spawnableMap, avatarMeta);

            foreach (KeyValuePair<string, List<string>> entry in spawnableMap)
            {
                var definition = new PotcoEnemySpawnDefinition { SpawnableName = entry.Key };
                foreach (string typeName in entry.Value.Distinct())
                {
                    definition.Variants.Add(BuildVariant(
                        typeName,
                        avatarMeta,
                        stats,
                        levelMultipliers,
                        enemyWeaponTable,
                        itemConstants,
                        inventoryTypeConstants,
                        skillLoadouts,
                        skeletonStyles));
                }

                index.AddSpawnable(definition);
            }

            return index;
        }

        private static PotcoEnemyVariantData BuildVariant(
            string typeName,
            IReadOnlyDictionary<string, AvatarMeta> avatarMeta,
            IReadOnlyDictionary<string, EnemyStats> stats,
            IReadOnlyDictionary<int, LevelMultiplier> levelMultipliers,
            IReadOnlyDictionary<string, List<string>> enemyWeaponTable,
            IReadOnlyDictionary<string, int> itemConstants,
            IReadOnlyDictionary<string, int> inventoryTypeConstants,
            IReadOnlyDictionary<string, SkillLoadout> skillLoadouts,
            SkeletonStyleIndex skeletonStyles)
        {
            avatarMeta.TryGetValue(typeName, out AvatarMeta meta);
            string canonicalType = ResolveTypeAlias(typeName, meta, stats, skillLoadouts);
            if (meta == null)
                avatarMeta.TryGetValue(canonicalType, out meta);
            stats.TryGetValue(canonicalType, out EnemyStats stat);
            skillLoadouts.TryGetValue(canonicalType, out SkillLoadout loadout);

            PotcoEnemyKind kind = ResolveKind(meta, stat);
            var variant = new PotcoEnemyVariantData
            {
                TypeName = typeName,
                Faction = meta?.Faction ?? string.Empty,
                Track = meta?.Track ?? string.Empty,
                Kind = kind,
                HumanPreset = ResolveHumanPreset(meta),
                MonsterClass = stat?.MonsterClass ?? PotcoEnemyMonsterClass.Unknown,
                MinLevel = stat?.MinLevel ?? 1,
                MaxLevel = stat?.MaxLevel ?? 1,
                BaseScale = stat?.BaseScale ?? 1f,
                Height = stat?.Height ?? (kind == PotcoEnemyKind.Human ? 1.8f : 1f),
                BattleTubeRadius = stat?.BattleTubeRadius ?? 0.5f,
                Enabled = (stat == null || stat.Enabled) || (meta != null && meta.IsBoss)
            };

            if (meta != null && meta.IsBoss)
            {
                variant.IsBoss = true;
                var bossData = new PotcoBossData
                {
                    DisplayName = string.IsNullOrEmpty(meta.BossName) ? typeName : meta.BossName
                };
                variant.BossData = bossData;
            }

            if (kind == PotcoEnemyKind.Creature)
            {
                variant.CreatureSpecies = ResolveCreatureSpecies(canonicalType, meta);
                CreatureData creatureData = CreatureDataParser.GetCreatureData(variant.CreatureSpecies);
                variant.CreatureModelPath = ResolveCreatureModelPath(variant.CreatureSpecies, creatureData);
                variant.CreatureAnimationPrefix = PotcoCreatureAnimationCatalog.ResolveAnimationPrefix(
                    variant.CreatureSpecies,
                    variant.CreatureModelPath);

                if (creatureData != null)
                {
                    variant.CreatureAnimationNames = creatureData.animations.Keys.ToList();
                    variant.CreatureAnimationFiles = creatureData.animations.Values.ToList();
                }
            }

            if (kind == PotcoEnemyKind.Skeleton)
            {
                SkeletonStyle style = skeletonStyles.Resolve(meta);
                variant.SkeletonStyle = style.Style;
                variant.SkeletonModelPath = style.ModelPath;
            }

            if (loadout != null)
            {
                variant.WeaponCategories = new List<string>(loadout.WeaponCategories);
                variant.SkillNames = new List<string>(loadout.SkillNames);
                variant.SkillIds = new List<int>(loadout.SkillIds);
                PopulateWeaponItems(variant, levelMultipliers, enemyWeaponTable, itemConstants, inventoryTypeConstants);
            }

            return variant;
        }

        private static PotcoEnemyKind ResolveKind(AvatarMeta meta, EnemyStats stats)
        {
            if (stats != null && stats.MonsterClass == PotcoEnemyMonsterClass.Skeleton)
                return PotcoEnemyKind.Skeleton;

            if (stats != null && stats.MonsterClass == PotcoEnemyMonsterClass.Human)
                return PotcoEnemyKind.Human;

            if (meta != null)
            {
                switch (meta.Faction)
                {
                    case "Navy":
                    case "TradingCo":
                    case "Ghost":
                    case "VoodooZombie":
                    case "BountyHunter":
                    case "Pirate":
                    case "Townfolk":
                        return PotcoEnemyKind.Human;
                    case "Undead":
                        return PotcoEnemyKind.Skeleton;
                }
            }

            return PotcoEnemyKind.Creature;
        }

        private static PotcoEnemyHumanPreset ResolveHumanPreset(AvatarMeta meta)
        {
            if (meta == null)
                return PotcoEnemyHumanPreset.None;

            switch (meta.Faction)
            {
                case "Navy":
                    return PotcoEnemyHumanPreset.Navy;
                case "TradingCo":
                    return PotcoEnemyHumanPreset.TradingCompany;
                case "BountyHunter":
                    return PotcoEnemyHumanPreset.BountyHunter;
                case "VoodooZombie":
                    return PotcoEnemyHumanPreset.VoodooZombie;
                case "Ghost":
                    return PotcoEnemyHumanPreset.Ghost;
                case "Pirate":
                    return PotcoEnemyHumanPreset.Pirate;
                case "Townfolk":
                    return PotcoEnemyHumanPreset.Townfolk;
                default:
                    return PotcoEnemyHumanPreset.None;
            }
        }

        private static void PopulateWeaponItems(
            PotcoEnemyVariantData variant,
            IReadOnlyDictionary<int, LevelMultiplier> levelMultipliers,
            IReadOnlyDictionary<string, List<string>> enemyWeaponTable,
            IReadOnlyDictionary<string, int> itemConstants,
            IReadOnlyDictionary<string, int> inventoryTypeConstants)
        {
            if (variant.WeaponCategories == null || variant.WeaponCategories.Count == 0)
                return;

            int referenceLevel = Mathf.Clamp(variant.MaxLevel, 0, 100);
            int weaponLevel = levelMultipliers.TryGetValue(referenceLevel, out LevelMultiplier multiplier)
                ? multiplier.WeaponLevel
                : 1;

            int baseLevel = weaponLevel / 2;
            int remainder = weaponLevel % 2;
            var itemNames = new List<string>();
            var itemIds = new List<int>();

            foreach (string category in variant.WeaponCategories)
            {
                if (!enemyWeaponTable.TryGetValue(category, out List<string> weaponSet) || weaponSet.Count == 0)
                    continue;

                int level = baseLevel + remainder;
                int weaponIndex = weaponSet.Count > level ? Mathf.Max(0, level - 1) : 0;
                string token = weaponSet[weaponIndex];
                itemNames.Add(token);

                if (TryResolveSymbolId(token, itemConstants, inventoryTypeConstants, out int itemId))
                    itemIds.Add(itemId);

                remainder = 0;
            }

            variant.WeaponItemNames = itemNames;
            variant.WeaponItemIds = itemIds;
        }

        private static bool TryResolveSymbolId(
            string token,
            IReadOnlyDictionary<string, int> itemConstants,
            IReadOnlyDictionary<string, int> inventoryTypeConstants,
            out int id)
        {
            id = 0;
            if (string.IsNullOrEmpty(token))
                return false;

            if (itemConstants.TryGetValue(token, out id))
                return true;
            if (inventoryTypeConstants.TryGetValue(token, out id))
                return true;

            string shortName = token.Contains(".") ? token.Substring(token.LastIndexOf('.') + 1) : token;
            return itemConstants.TryGetValue(shortName, out id) || inventoryTypeConstants.TryGetValue(shortName, out id);
        }

        private static Dictionary<string, AvatarMeta> ParseAvatarTypes(
            string source,
            BossNameCursor localizedBossNames)
        {
            var result = new Dictionary<string, AvatarMeta>(StringComparer.Ordinal);

            var trackAliases = new Regex(
                @"(?m)^(?<group>\w+Tracks)\s*=\s*\[AvatarType\(base=(?<base>\w+),\s*track=x\)\s+for\s+x\s+in\s+xrange\((?<count>\d+)\)\]\s*\r?\n(?<names>[A-Za-z0-9_, ]+?)\s*=\s*\k<group>");
            foreach (Match match in trackAliases.Matches(source))
                AddTrackAliases(result, match.Groups["base"].Value, SplitNames(match.Groups["names"].Value));

            var ranged = new Regex(
                @"(?m)^(?<group>\w+)\s*=\s*\[AvatarType\(base=(?<base>\w+),\s*id=x\)\s+for\s+x\s+in\s+xrange\((?<count>\d+)\)\]\s*\r?\n(?<names>[A-Za-z0-9_, ]+?)\s*=\s*\k<group>");
            foreach (Match match in ranged.Matches(source))
                AddAvatarGroup(result, match.Groups["group"].Value, SplitNames(match.Groups["names"].Value), 0);

            var single = new Regex(
                @"(?m)^(?<group>\w+)\s*=\s*\[AvatarType\(base=(?<base>\w+),\s*id=(?<id>\d+)\)\]\s*\r?\n(?<names>[A-Za-z0-9_, ]+?)\s*=\s*\k<group>");
            foreach (Match match in single.Matches(source))
            {
                int startIndex = int.TryParse(match.Groups["id"].Value, out int parsed) ? parsed : 0;
                AddAvatarGroup(result, match.Groups["group"].Value, SplitNames(match.Groups["names"].Value), startIndex);
            }

            var bossGroupBase = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in Regex.Split(source ?? string.Empty, @"\r?\n"))
            {
                string trimmed = line.Trim();
                Match bossGroup = Regex.Match(trimmed,
                    @"^(?<group>\w+Bosses)\s*=\s*\[AvatarType\(base=(?<base>\w+),\s*boss=(?:x|\d+)\)");
                if (bossGroup.Success)
                {
                    bossGroupBase[bossGroup.Groups["group"].Value] = bossGroup.Groups["base"].Value;
                    continue;
                }

                Match bossAliases = Regex.Match(trimmed,
                    @"^(?<names>[A-Za-z0-9_, ]+?)\s*=\s*(?<group>\w+Bosses)(?:\[(?<index>\d+)\])?$");
                if (bossAliases.Success &&
                    bossGroupBase.TryGetValue(bossAliases.Groups["group"].Value, out string baseName))
                {
                    AddBossAvatarGroup(result, baseName, SplitNames(bossAliases.Groups["names"].Value), localizedBossNames);
                }
            }

            return result;
        }

        private static void AddTrackAliases(Dictionary<string, AvatarMeta> result, string baseName, List<string> names)
        {
            ResolveFactionInfo(baseName, out string faction, out int factionId);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (result.ContainsKey(name))
                    continue;

                result[name] = new AvatarMeta
                {
                    Name = name,
                    Group = name,
                    Index = 0,
                    Faction = faction,
                    Track = name,
                    FactionId = factionId,
                    TrackId = i
                };
            }
        }

        private static void AddAvatarGroup(Dictionary<string, AvatarMeta> result, string group, List<string> names, int startIndex)
        {
            ResolveGroupInfo(group, out string faction, out string track, out int factionId, out int trackId);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                result[name] = new AvatarMeta
                {
                    Name = name,
                    Group = group,
                    Index = startIndex + i,
                    Faction = faction,
                    Track = track,
                    FactionId = factionId,
                    TrackId = trackId
                };
            }
        }

        private static void AddBossAvatarGroup(
            Dictionary<string, AvatarMeta> result,
            string baseName,
            List<string> names,
            BossNameCursor localizedBossNames)
        {
            if (!result.TryGetValue(baseName, out AvatarMeta baseMeta))
                baseMeta = CreateFallbackBaseMeta(baseName);

            List<string> bossNames = localizedBossNames?.TakeNames(baseMeta.FactionId, baseMeta.TrackId, names.Count)
                ?? new List<string>();
            for (int i = 0; i < names.Count; i++)
            {
                string alias = names[i];
                string bossName = i < bossNames.Count ? bossNames[i] : string.Empty;

                result[alias] = new AvatarMeta
                {
                    Name = alias,
                    Group = baseMeta.Group,
                    Index = baseMeta.Index,
                    Faction = baseMeta.Faction,
                    Track = baseMeta.Track,
                    FactionId = baseMeta.FactionId,
                    TrackId = baseMeta.TrackId,
                    IsBoss = true,
                    BossBaseName = baseName,
                    BossIndex = i,
                    BossName = string.IsNullOrEmpty(bossName) ? alias : bossName
                };
            }
        }

        private static AvatarMeta CreateFallbackBaseMeta(string baseName)
        {
            switch (baseName)
            {
                case "Cast":
                    return new AvatarMeta { Name = baseName, Group = "Cast", Index = 0, Faction = "Townfolk", Track = "Cast", FactionId = 3, TrackId = 2 };
                default:
                    return new AvatarMeta { Name = baseName, Group = baseName, Index = 0, Faction = string.Empty, Track = baseName, FactionId = -1, TrackId = -1 };
            }
        }

        private static void ResolveFactionInfo(string factionName, out string faction, out int factionId)
        {
            switch (factionName)
            {
                case "Undead": faction = "Undead"; factionId = 0; return;
                case "Navy": faction = "Navy"; factionId = 1; return;
                case "Creature": faction = "Creature"; factionId = 2; return;
                case "Townfolk": faction = "Townfolk"; factionId = 3; return;
                case "Pirate": faction = "Pirate"; factionId = 4; return;
                case "TradingCo": faction = "TradingCo"; factionId = 5; return;
                case "Ghost": faction = "Ghost"; factionId = 6; return;
                case "VoodooZombie": faction = "VoodooZombie"; factionId = 7; return;
                case "BountyHunter": faction = "BountyHunter"; factionId = 8; return;
                default: faction = factionName ?? string.Empty; factionId = -1; return;
            }
        }

        private static void ResolveGroupInfo(string group, out string faction, out string track, out int factionId, out int trackId)
        {
            factionId = -1;
            trackId = -1;
            switch (group)
            {
                case "LandCreatures":
                    faction = "Creature"; track = "LandCreature"; factionId = 2; trackId = 0; return;
                case "Animals":
                    faction = "Creature"; track = "Animal"; factionId = 2; trackId = 4; return;
                case "AirCreatures":
                    faction = "Creature"; track = "AirCreature"; factionId = 2; trackId = 2; return;
                case "SeaCreatures":
                    faction = "Creature"; track = "SeaCreature"; factionId = 2; trackId = 1; return;
                case "EarthUndead":
                    faction = "Undead"; track = "Earth"; factionId = 0; trackId = 0; return;
                case "AirUndead":
                    faction = "Undead"; track = "Air"; factionId = 0; trackId = 1; return;
                case "FireUndead":
                    faction = "Undead"; track = "Fire"; factionId = 0; trackId = 2; return;
                case "WaterUndead":
                    faction = "Undead"; track = "Water"; factionId = 0; trackId = 3; return;
                case "BossUndead":
                    faction = "Undead"; track = "Boss"; factionId = 0; trackId = 5; return;
                case "FrenchUndead":
                    faction = "Undead"; track = "French"; factionId = 0; trackId = 6; return;
                case "SpanishUndead":
                    faction = "Undead"; track = "Spanish"; factionId = 0; trackId = 7; return;
                case "EarthSpecialUndead":
                    faction = "Undead"; track = "EarthSpecial"; factionId = 0; trackId = 8; return;
                case "Marksmen":
                    faction = "Navy"; track = group; factionId = 1; trackId = 1; return;
                case "Soldiers":
                    faction = "Navy"; track = group; factionId = 1; trackId = 0; return;
                case "Leaders":
                    faction = "Navy"; track = group; factionId = 1; trackId = 2; return;
                case "Mercenaries":
                    faction = "TradingCo"; track = group; factionId = 5; trackId = 0; return;
                case "Assassins":
                    faction = "TradingCo"; track = group; factionId = 5; trackId = 1; return;
                case "Officials":
                    faction = "TradingCo"; track = group; factionId = 5; trackId = 2; return;
                case "GhostPirates":
                    faction = "Ghost"; track = group; factionId = 6; trackId = 0; return;
                case "KillerGhosts":
                    faction = "Ghost"; track = group; factionId = 6; trackId = 1; return;
                case "VoodooZombiePirates":
                    faction = "VoodooZombie"; track = group; factionId = 7; trackId = 0; return;
                case "BountyHunters":
                    faction = "BountyHunter"; track = group; factionId = 8; trackId = 0; return;
                case "Brawlers":
                    faction = "Pirate"; track = group; factionId = 4; trackId = 0; return;
                case "Gunners":
                    faction = "Pirate"; track = group; factionId = 4; trackId = 1; return;
                case "Commoners":
                    faction = "Townfolk"; track = group; factionId = 3; trackId = 0; return;
                case "StoreOwners":
                    faction = "Townfolk"; track = group; factionId = 3; trackId = 1; return;
                default:
                    faction = string.Empty; track = group; return;
            }
        }

        private static Dictionary<string, List<string>> ParseSpawnables(
            string avatarTypes,
            IReadOnlyDictionary<string, AvatarMeta> avatarMeta)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string body = ExtractDictionaryBody(avatarTypes, "NPC_SPAWNABLES");
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(body))
            {
                string spawnableName = TrimPythonString(entry.Key);
                List<string> variants = ResolveSpawnableVariants(entry.Value, avatarMeta);
                if (variants.Count > 0)
                    result[spawnableName] = variants;
            }

            return result;
        }

        private static List<string> ResolveSpawnableVariants(string value, IReadOnlyDictionary<string, AvatarMeta> avatarMeta)
        {
            Match lambda = Regex.Match(value, @"lambda\s+(?<params>[^:]+):\s*(?<body>[^\]\r\n]+)", RegexOptions.Singleline);
            if (!lambda.Success)
                return new List<string>();

            Dictionary<string, string> defaults = ParseLambdaDefaults(lambda.Groups["params"].Value);
            string body = lambda.Groups["body"].Value;

            if (body.Contains("typePassthrough"))
            {
                return defaults.TryGetValue("p0", out string token) && !int.TryParse(token, out _)
                    ? new List<string> { token }
                    : new List<string>();
            }

            Match picker = Regex.Match(body, @"(?<name>pick\w+)\s*\(");
            if (!picker.Success)
                return new List<string>();

            string group = ResolvePickerGroup(picker.Groups["name"].Value);
            if (string.IsNullOrEmpty(group))
                return new List<string>();

            int low = ParseIntDefault(defaults, "p0", 0);
            int high = ParseIntDefault(defaults, "p1", low);
            return avatarMeta.Values
                .Where(meta => !meta.IsBoss && meta.Group == group && meta.Index >= low && meta.Index <= high)
                .OrderBy(meta => meta.Index)
                .Select(meta => meta.Name)
                .ToList();
        }

        private static string ResolvePickerGroup(string picker)
        {
            switch (picker)
            {
                case "pickEarthUndead": return "EarthUndead";
                case "pickWaterUndead": return "WaterUndead";
                case "pickSpanishUndead": return "SpanishUndead";
                case "pickFrenchUndead": return "FrenchUndead";
                case "pickNavy": return "Marksmen";
                case "pickTrading": return "Mercenaries";
                case "pickGhost": return "GhostPirates";
                case "pickVoodooZombie": return "VoodooZombiePirates";
                case "pickBountyHunter": return "BountyHunters";
                default: return string.Empty;
            }
        }

        private static void AddEditorSpawnables(Dictionary<string, List<string>> spawnables, IReadOnlyDictionary<string, AvatarMeta> avatarMeta)
        {
            AddEditorSpawnablesForGroup(spawnables, avatarMeta, "Marksmen", "Navy - ");
            AddEditorSpawnablesForGroup(spawnables, avatarMeta, "Mercenaries", "EITC - ");
            AddEditorSpawnablesForGroup(spawnables, avatarMeta, "EarthUndead", "Undead - ");
            AddEditorSpawnablesForGroup(spawnables, avatarMeta, "WaterUndead", "Undead - ");
        }

        private static void AddAvatarTypeSpawnables(Dictionary<string, List<string>> spawnables, IReadOnlyDictionary<string, AvatarMeta> avatarMeta)
        {
            foreach (AvatarMeta meta in avatarMeta.Values.GroupBy(m => m.Name).Select(g => g.First()))
            {
                if (string.IsNullOrEmpty(meta.Name))
                    continue;

                spawnables["Avatar - " + meta.Name] = new List<string> { meta.Name };
            }
        }

        private static void AddEditorSpawnablesForGroup(
            Dictionary<string, List<string>> spawnables,
            IReadOnlyDictionary<string, AvatarMeta> avatarMeta,
            string group,
            string prefix)
        {
            foreach (AvatarMeta meta in avatarMeta.Values.Where(m => m.Group == group).OrderBy(m => m.Index))
                spawnables[prefix + meta.Name] = new List<string> { meta.Name };
        }

        private static BossNameCursor ParseLocalizedBossNames(string source)
        {
            var namesByTrack = new Dictionary<(int Faction, int Track), List<string>>();
            string body = ExtractDictionaryBody(source ?? string.Empty, "BossNames");
            if (string.IsNullOrEmpty(body))
                return new BossNameCursor(namesByTrack);

            var stack = new List<(int Indent, int Key)>();
            foreach (string line in Regex.Split(body, @"\r?\n"))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int indent = line.TakeWhile(char.IsWhiteSpace).Count();
                string trimmed = line.Trim();
                while (stack.Count > 0 && stack[stack.Count - 1].Indent >= indent)
                    stack.RemoveAt(stack.Count - 1);

                Match branch = Regex.Match(trimmed, @"^(?<key>\d+)\s*:\s*\{");
                if (branch.Success)
                {
                    stack.Add((indent, int.Parse(branch.Groups["key"].Value, CultureInfo.InvariantCulture)));
                    continue;
                }

                Match leaf = Regex.Match(trimmed, @"^(?<key>\d+)\s*:\s*(?<value>u?'(?:\\'|[^'])*')");
                if (!leaf.Success || stack.Count < 3)
                    continue;

                int faction = stack[stack.Count - 3].Key;
                int track = stack[stack.Count - 2].Key;
                var key = (faction, track);
                if (!namesByTrack.TryGetValue(key, out List<string> names))
                {
                    names = new List<string>();
                    namesByTrack[key] = names;
                }

                names.Add(TrimPythonString(leaf.Groups["value"].Value));
            }

            return new BossNameCursor(namesByTrack);
        }

        private static Dictionary<string, string> ParseBossNpcNames(string source)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string body = ExtractDictionaryBody(source ?? string.Empty, "BossNPCNames");
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(body))
            {
                string key = TrimPythonString(entry.Key);
                if (string.IsNullOrEmpty(key) || key.StartsWith("NPCIds.", StringComparison.Ordinal))
                    continue;

                result[key] = TrimPythonString(entry.Value);
            }

            return result;
        }

        private static Dictionary<string, PotcoBossData> ParseBossData(
            string source,
            IReadOnlyDictionary<string, string> uniqueBossNames,
            out PotcoBossData defaultBossData)
        {
            var result = new Dictionary<string, PotcoBossData>(StringComparer.OrdinalIgnoreCase);
            defaultBossData = new PotcoBossData();

            string body = ExtractDictionaryBody(source ?? string.Empty, "BOSS_NPC_LIST");
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(body))
            {
                string key = TrimPythonString(entry.Key);
                if (key == string.Empty)
                {
                    ApplyBossFields(defaultBossData, entry.Value);
                    continue;
                }

                PotcoBossData data = defaultBossData.Clone();
                data.UniqueId = key;
                ApplyBossFields(data, entry.Value);
                if (uniqueBossNames != null && uniqueBossNames.TryGetValue(key, out string displayName))
                    data.DisplayName = displayName;

                result[key] = data;
            }

            return result;
        }

        private static void ApplyBossFields(PotcoBossData data, string body)
        {
            if (data == null || string.IsNullOrEmpty(body))
                return;

            foreach (Match match in Regex.Matches(body, @"'(?<field>HpScale|MpScale|GoldScale|ModelScale|DamageScale|ArmorScale)'\s*:\s*(?<value>[-+]?\d+(?:\.\d+)?)"))
            {
                float value = ParseFloat(match.Groups["value"].Value, 1f);
                switch (match.Groups["field"].Value)
                {
                    case "HpScale": data.HpScale = value; break;
                    case "MpScale": data.MpScale = value; break;
                    case "GoldScale": data.GoldScale = value; break;
                    case "ModelScale": data.ModelScale = value; break;
                    case "DamageScale": data.DamageScale = value; break;
                    case "ArmorScale": data.ArmorScale = value; break;
                }
            }

            Match level = Regex.Match(body, @"'Level'\s*:\s*(?<value>\d+)");
            if (level.Success)
                data.LevelOverride = ParseInt(level.Groups["value"].Value, 0);

            Match highlight = Regex.Match(body, @"'HighlightColor'\s*:\s*VBase3\((?<args>[^\)]*)\)");
            if (highlight.Success)
            {
                List<string> parts = SplitTopLevel(highlight.Groups["args"].Value, ',');
                if (parts.Count == 1)
                {
                    float value = ParseFloat(parts[0], 1f);
                    data.HighlightColor = new Color(value, value, value, 1f);
                }
                else if (parts.Count >= 3)
                {
                    data.HighlightColor = new Color(
                        ParseFloat(parts[0], 1f),
                        ParseFloat(parts[1], 1f),
                        ParseFloat(parts[2], 1f),
                        1f);
                }
            }
        }

        private static Dictionary<string, EnemyStats> ParseEnemyStats(string source)
        {
            var result = new Dictionary<string, EnemyStats>(StringComparer.Ordinal);
            string body = ExtractDictionaryBody(source, "__baseAvatarStats");
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(body))
            {
                string typeName = ParseAvatarTypeKey(entry.Key);
                if (string.IsNullOrEmpty(typeName))
                    continue;

                List<string> values = SplitTopLevel(TrimEnclosure(entry.Value, '[', ']'), ',');
                if (values.Count < 7)
                    continue;

                result[typeName] = new EnemyStats
                {
                    MinLevel = ParseInt(values[0], 1),
                    MaxLevel = ParseInt(values[1], 1),
                    BaseScale = ParseFloat(values[2], 1f),
                    Height = ParseFloat(values[3], 1.8f),
                    BattleTubeRadius = ParseFloat(values[4], 0.5f),
                    MonsterClass = ParseMonsterClass(values[5]),
                    Enabled = ParseInt(values[6], 0) != 0
                };
            }

            return result;
        }

        private static Dictionary<int, LevelMultiplier> ParseLevelMultipliers(string source)
        {
            var result = new Dictionary<int, LevelMultiplier>();
            string body = ExtractDictionaryBody(source, "__baseLevelStatMultiplier");
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(body))
            {
                if (!int.TryParse(entry.Key.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level))
                    continue;

                List<string> values = SplitTopLevel(TrimEnclosure(entry.Value, '(', ')'), ',');
                if (values.Count < 4)
                    continue;

                result[level] = new LevelMultiplier
                {
                    HpModifier = ParseFloat(values[0], 1f),
                    WeaponLevel = ParseInt(values[1], 1),
                    SkillLevel = ParseInt(values[2], 1),
                    ScaleModifier = ParseFloat(values[3], 1f)
                };
            }

            return result;
        }

        private static Dictionary<string, List<string>> ParseEnemyWeaponTable(string source)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            string body = ExtractDictionaryBody(source, "__enemyWeaponTable");
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(body))
            {
                string category = entry.Key.Trim();
                List<string> values = SplitTopLevel(TrimEnclosure(entry.Value, '[', ']'), ',')
                    .Select(NormalizeSymbol)
                    .Where(token => !string.IsNullOrEmpty(token))
                    .ToList();
                result[category] = values;
            }

            return result;
        }

        private static Dictionary<string, SkillLoadout> ParseSkillLoadouts(
            string source,
            IReadOnlyDictionary<string, int> enemySkillConstants,
            IReadOnlyDictionary<string, int> inventoryTypeConstants)
        {
            var result = new Dictionary<string, SkillLoadout>(StringComparer.Ordinal);
            string body = ExtractDictionaryBody(source, "__baseAvatarSkills");
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(body))
            {
                string typeName = ParseAvatarTypeKey(entry.Key);
                if (string.IsNullOrEmpty(typeName))
                    continue;

                List<string> listBodies = ExtractTopLevelSquareLists(entry.Value);
                if (listBodies.Count < 2)
                    continue;

                var loadout = new SkillLoadout();
                loadout.WeaponCategories.AddRange(SplitTopLevel(listBodies[0], ',')
                    .Select(NormalizeSymbol)
                    .Where(token => !string.IsNullOrEmpty(token)));

                foreach (string token in SplitTopLevel(listBodies[1], ',').Select(NormalizeSymbol))
                {
                    if (string.IsNullOrEmpty(token))
                        continue;

                    string name = NormalizeSkillToken(token, enemySkillConstants, inventoryTypeConstants);
                    loadout.SkillNames.Add(name);
                    if (TryResolveSkillId(name, enemySkillConstants, inventoryTypeConstants, out int id))
                        loadout.SkillIds.Add(id);
                }

                result[typeName] = loadout;
            }

            return result;
        }

        private static SkeletonStyleIndex ParseSkeletonStyles(string source)
        {
            var index = new SkeletonStyleIndex();
            if (string.IsNullOrEmpty(source))
                return index;

            string modelBody = ExtractDictionaryBody(source, "ModelDict");
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(modelBody))
            {
                string style = TrimPythonString(entry.Key);
                string model = TrimPythonString(entry.Value);
                if (!string.IsNullOrEmpty(style) && !string.IsNullOrEmpty(model))
                    index.ModelByStyle[style] = model;
            }

            string styleBody = ExtractDictionaryBody(source, "AvType2style");
            var styleRegex = new Regex(@"AvatarTypes\.(?<group>\w+)\[(?<index>\d+)\]\s*:\s*'(?<style>[^']+)'");
            foreach (Match match in styleRegex.Matches(styleBody))
            {
                string group = match.Groups["group"].Value;
                int typeIndex = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
                string style = match.Groups["style"].Value;
                index.StyleByGroupIndex[(group, typeIndex)] = style;
            }

            return index;
        }

        private static string ResolveCreatureSpecies(string typeName, AvatarMeta meta)
        {
            switch (typeName)
            {
                case "StoneCrab":
                case "RockCrab":
                case "GiantCrab":
                case "CrusherCrab":
                    return "Crab";
                case "BayouGator":
                case "BigGator":
                case "HugeGator":
                    return "Alligator";
                case "DireScorpion":
                case "DreadScorpion":
                    return "Scorpion";
                case "RabidBat":
                case "VampireBat":
                case "FireBat":
                    return "Bat";
                case "KillerWasp":
                case "AngryWasp":
                case "SoldierWasp":
                    return "Wasp";
                case "RancidFlyTrap":
                case "AncientFlyTrap":
                    return "FlyTrap";
                case "TwistedStump":
                    return "Stump";
                default:
                    return typeName;
            }
        }

        private static string ResolveCreatureModelPath(string species, CreatureData creatureData = null)
        {
            if (string.IsNullOrEmpty(species))
                return string.Empty;

            if (creatureData != null && !string.IsNullOrEmpty(creatureData.GetBestModelPath()))
                return creatureData.GetBestModelPath();

            return $"models/char/{species.ToLowerInvariant()}_hi";
        }

        private static Dictionary<string, int> ParseSimpleConstants(string source, string prefix)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            var regex = new Regex(@"(?m)^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>\d+)\b");
            foreach (Match match in regex.Matches(source ?? string.Empty))
            {
                string name = match.Groups["name"].Value;
                int value = int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
                result[name] = value;
                result[prefix + name] = value;
            }

            return result;
        }

        private static Dictionary<string, int> ParseItemDataConstants(string source)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            var regex = new Regex(@"(?m)^\s*(?<id>\d+)\s*:\s*\[[^\r\n]*?u'[^']*'\s*,\s*u'(?<constant>[A-Z0-9_]+)'");
            foreach (Match match in regex.Matches(source ?? string.Empty))
            {
                int id = int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture);
                string constant = match.Groups["constant"].Value;
                result[constant] = id;
                result["ItemGlobals." + constant] = id;
            }

            return result;
        }

        private static string NormalizeSkillToken(
            string token,
            IReadOnlyDictionary<string, int> enemySkillConstants,
            IReadOnlyDictionary<string, int> inventoryTypeConstants)
        {
            if (token.Contains("."))
                return token;

            if (enemySkillConstants.ContainsKey(token) || enemySkillConstants.ContainsKey("EnemySkills." + token))
                return "EnemySkills." + token;

            if (inventoryTypeConstants.ContainsKey(token) || inventoryTypeConstants.ContainsKey("InventoryType." + token))
                return "InventoryType." + token;

            return token;
        }

        private static bool TryResolveSkillId(
            string token,
            IReadOnlyDictionary<string, int> enemySkillConstants,
            IReadOnlyDictionary<string, int> inventoryTypeConstants,
            out int id)
        {
            id = 0;
            return enemySkillConstants.TryGetValue(token, out id) ||
                   inventoryTypeConstants.TryGetValue(token, out id);
        }

        private static string ResolveTypeAlias(
            string typeName,
            AvatarMeta meta,
            IReadOnlyDictionary<string, EnemyStats> stats,
            IReadOnlyDictionary<string, SkillLoadout> skillLoadouts)
        {
            if ((stats != null && stats.ContainsKey(typeName)) ||
                (skillLoadouts != null && skillLoadouts.ContainsKey(typeName)))
            {
                return typeName;
            }

            if (meta != null && meta.IsBoss && !string.IsNullOrEmpty(meta.BossBaseName))
                return meta.BossBaseName;

            return typeName;
        }

        private static string ParseAvatarTypeKey(string key)
        {
            Match match = Regex.Match(key ?? string.Empty, @"AvatarTypes\.(?<name>[A-Za-z_][A-Za-z0-9_]*)");
            return match.Success ? match.Groups["name"].Value : string.Empty;
        }

        private static PotcoEnemyMonsterClass ParseMonsterClass(string token)
        {
            switch (NormalizeSymbol(token))
            {
                case "SKELETON": return PotcoEnemyMonsterClass.Skeleton;
                case "HUMAN": return PotcoEnemyMonsterClass.Human;
                case "MONSTER": return PotcoEnemyMonsterClass.Monster;
                default: return PotcoEnemyMonsterClass.Unknown;
            }
        }

        private static Dictionary<string, string> ParseLambdaDefaults(string parameters)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(parameters ?? string.Empty, @"(?<name>p\d+)\s*=\s*(?<value>[A-Za-z_][A-Za-z0-9_]*|-?\d+)"))
                result[match.Groups["name"].Value] = match.Groups["value"].Value;

            return result;
        }

        private static int ParseIntDefault(IReadOnlyDictionary<string, string> defaults, string key, int fallback)
        {
            return defaults.TryGetValue(key, out string value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
        }

        private static List<string> SplitNames(string names)
        {
            return names.Split(',')
                .Select(name => name.Trim())
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();
        }

        private static string NormalizeSymbol(string token)
        {
            token = (token ?? string.Empty).Trim();
            if (token.EndsWith("]", StringComparison.Ordinal))
                token = token.Substring(0, token.Length - 1).Trim();
            if (token.EndsWith(")", StringComparison.Ordinal))
                token = token.Substring(0, token.Length - 1).Trim();
            return token;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
        }

        private static float ParseFloat(string value, float fallback)
        {
            return float.TryParse((value ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : fallback;
        }

        private static string ExtractDictionaryBody(string source, string dictionaryName)
        {
            int nameIndex = source.IndexOf(dictionaryName, StringComparison.Ordinal);
            if (nameIndex < 0)
                return string.Empty;

            int openBrace = source.IndexOf('{', nameIndex);
            if (openBrace < 0)
                return string.Empty;

            int closeBrace = FindMatching(source, openBrace, '{', '}');
            return source.Substring(openBrace + 1, closeBrace - openBrace - 1);
        }

        private static IEnumerable<KeyValuePair<string, string>> ParseDictionaryEntries(string body)
        {
            foreach (string entry in SplitTopLevel(body, ','))
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;

                int colon = FindTopLevel(entry, ':');
                if (colon < 0)
                    continue;

                yield return new KeyValuePair<string, string>(
                    entry.Substring(0, colon).Trim(),
                    entry.Substring(colon + 1).Trim());
            }
        }

        private static List<string> ExtractTopLevelSquareLists(string text)
        {
            var result = new List<string>();
            int index = 0;
            while (index < text.Length)
            {
                int open = text.IndexOf('[', index);
                if (open < 0)
                    break;

                int close = FindMatching(text, open, '[', ']');
                result.Add(text.Substring(open + 1, close - open - 1));
                index = close + 1;
            }

            return result;
        }

        private static string TrimEnclosure(string value, char open, char close)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length >= 2 && value[0] == open && value[value.Length - 1] == close)
                return value.Substring(1, value.Length - 2);
            return value;
        }

        private static string TrimPythonString(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (value.StartsWith("u'", StringComparison.Ordinal) || value.StartsWith("u\"", StringComparison.Ordinal))
                value = value.Substring(1);

            if (value.Length >= 2 && IsQuote(value[0]) && value[value.Length - 1] == value[0])
            {
                int index = 1;
                var builder = new StringBuilder();
                while (index < value.Length - 1)
                {
                    char current = value[index++];
                    if (current == '\\' && index < value.Length - 1)
                        current = value[index++];
                    builder.Append(current);
                }

                return builder.ToString();
            }

            return value;
        }

        private static List<string> SplitTopLevel(string text, char separator)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            char quote = '\0';
            bool escaped = false;
            int round = 0;
            int square = 0;
            int curly = 0;

            foreach (char c in text ?? string.Empty)
            {
                if (quote != '\0')
                {
                    current.Append(c);
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == quote)
                        quote = '\0';
                    continue;
                }

                if (IsQuote(c))
                {
                    quote = c;
                    current.Append(c);
                    continue;
                }

                if (c == '(') round++;
                else if (c == ')') round--;
                else if (c == '[') square++;
                else if (c == ']') square--;
                else if (c == '{') curly++;
                else if (c == '}') curly--;

                if (c == separator && round == 0 && square == 0 && curly == 0)
                {
                    values.Add(current.ToString().Trim());
                    current.Length = 0;
                    continue;
                }

                current.Append(c);
            }

            values.Add(current.ToString().Trim());
            return values;
        }

        private static int FindTopLevel(string text, char target)
        {
            char quote = '\0';
            bool escaped = false;
            int round = 0;
            int square = 0;
            int curly = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == quote)
                        quote = '\0';
                    continue;
                }

                if (IsQuote(c))
                {
                    quote = c;
                    continue;
                }

                if (c == '(') round++;
                else if (c == ')') round--;
                else if (c == '[') square++;
                else if (c == ']') square--;
                else if (c == '{') curly++;
                else if (c == '}') curly--;

                if (c == target && round == 0 && square == 0 && curly == 0)
                    return i;
            }

            return -1;
        }

        private static int FindMatching(string source, int openIndex, char open, char close)
        {
            char quote = '\0';
            bool escaped = false;
            int depth = 0;

            for (int i = openIndex; i < source.Length; i++)
            {
                char current = source[i];
                if (quote != '\0')
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == quote)
                        quote = '\0';
                    continue;
                }

                if (IsQuote(current))
                {
                    quote = current;
                    continue;
                }

                if (current == open)
                    depth++;
                else if (current == close)
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return source.Length - 1;
        }

        private static bool IsQuote(char c)
        {
            return c == '\'' || c == '"';
        }

        private sealed class BossNameCursor
        {
            private readonly Dictionary<(int Faction, int Track), List<string>> namesByTrack;
            private readonly Dictionary<(int Faction, int Track), int> positions =
                new Dictionary<(int Faction, int Track), int>();

            public BossNameCursor(Dictionary<(int Faction, int Track), List<string>> namesByTrack)
            {
                this.namesByTrack = namesByTrack ?? new Dictionary<(int Faction, int Track), List<string>>();
            }

            public List<string> TakeNames(int faction, int track, int count)
            {
                var result = new List<string>();
                if (count <= 0)
                    return result;

                var key = (faction, track);
                positions.TryGetValue(key, out int position);
                if (namesByTrack.TryGetValue(key, out List<string> names))
                {
                    for (int i = 0; i < count && position + i < names.Count; i++)
                        result.Add(names[position + i]);
                }

                positions[key] = position + count;
                return result;
            }
        }

        private sealed class AvatarMeta
        {
            public string Name;
            public string Group;
            public int Index;
            public string Faction;
            public string Track;
            public int FactionId;
            public int TrackId;
            public bool IsBoss;
            public string BossBaseName;
            public int BossIndex;
            public string BossName;
        }

        private sealed class EnemyStats
        {
            public int MinLevel;
            public int MaxLevel;
            public float BaseScale;
            public float Height;
            public float BattleTubeRadius;
            public PotcoEnemyMonsterClass MonsterClass;
            public bool Enabled;
        }

        private sealed class LevelMultiplier
        {
            public float HpModifier;
            public int WeaponLevel;
            public int SkillLevel;
            public float ScaleModifier;
        }

        private sealed class SkillLoadout
        {
            public readonly List<string> WeaponCategories = new List<string>();
            public readonly List<string> SkillNames = new List<string>();
            public readonly List<int> SkillIds = new List<int>();
        }

        private readonly struct SkeletonStyle
        {
            public SkeletonStyle(string style, string modelPath)
            {
                Style = style ?? string.Empty;
                ModelPath = modelPath ?? string.Empty;
            }

            public string Style { get; }
            public string ModelPath { get; }
        }

        private sealed class SkeletonStyleIndex
        {
            public readonly Dictionary<(string Group, int Index), string> StyleByGroupIndex =
                new Dictionary<(string Group, int Index), string>();
            public readonly Dictionary<string, string> ModelByStyle =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public SkeletonStyle Resolve(AvatarMeta meta)
            {
                if (meta == null)
                    return new SkeletonStyle(string.Empty, string.Empty);

                if (!StyleByGroupIndex.TryGetValue((meta.Group, meta.Index), out string style))
                    style = "1";

                ModelByStyle.TryGetValue(style, out string modelPath);
                return new SkeletonStyle(style, modelPath);
            }
        }
    }
}
