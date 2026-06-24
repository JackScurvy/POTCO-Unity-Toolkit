using UnityEngine;
using System.Collections.Generic;
using System;
using POTCO.Combat;
using Player;

namespace POTCO
{
    /// <summary>
    /// Spawn Node component for enemy/creature spawning points.
    /// Stores spawn configuration imported from POTCO world data.
    /// Spawns enemies/creatures at runtime based on spawnable type.
    /// FULLY DYNAMIC - Uses parsed data from AvatarTypes.py and EnemyGlobals.py
    /// </summary>
    [ExecuteAlways] // Run in both edit and play mode
    public class SpawnNode : MonoBehaviour
    {
        [Header("Spawn Configuration")]
        [Tooltip("Type of enemy/creature to spawn (e.g., 'Crab T1', 'Noob Navy', 'Alligator')")]
        public string spawnables;

        [Tooltip("Aggression radius - distance at which spawned entities become aggressive")]
        public float aggroRadius = 12f;

        [Tooltip("Patrol radius - area within which spawned entities patrol")]
        public float patrolRadius = 12f;

        [Tooltip("Initial behavior state (e.g., 'Idle', 'Patrol', 'Ambush')")]
        public string startState = "Idle";

        [Tooltip("Team ID for spawned entities")]
        public int teamId = 0;

        [Header("Spawn Timing")]
        [Tooltip("Spawn time begin (in hours, 0-24)")]
        public float spawnTimeBegin = 0f;

        [Tooltip("Spawn time end (in hours, 0-24)")]
        public float spawnTimeEnd = 0f;

        [Header("Runtime Spawning")]
        [Tooltip("Auto-spawn enemy/creature on Start")]
        public bool autoSpawn = true;

        [Tooltip("Number of enemies to spawn")]
        public int spawnCount = 1;

        [Header("Cached Type Info")]
        [Tooltip("Is this a creature type? (Set automatically during import)")]
        [SerializeField] private bool isCreatureType = false;

        [Tooltip("Creature species for model loading (e.g., 'alligator', 'crab')")]
        [SerializeField] private string creatureSpecies = "";

        [Tooltip("Creature model path from .py file (e.g., 'models/char/alligator_hi')")]
        [SerializeField] private string creatureModelPath = "";

        [Tooltip("Resolved POTCO enemy spawn definition parsed from AvatarTypes.py and EnemyGlobals.py")]
        [SerializeField] private PotcoEnemySpawnDefinition enemyDefinition = new PotcoEnemySpawnDefinition();

        [Header("World Data Animations")]
        [Tooltip("AnimSet imported from the world data file for this spawn node")]
        [SerializeField] private string worldAnimSet = "";

        [Tooltip("Greeting Animation imported from the world data file for this spawn node")]
        [SerializeField] private string worldGreetingAnimation = "";

        [Tooltip("Notice Animation 1 imported from the world data file for this spawn node")]
        [SerializeField] private string worldNoticeAnimation1 = "";

        [Tooltip("Notice Animation 2 imported from the world data file for this spawn node")]
        [SerializeField] private string worldNoticeAnimation2 = "";

        [Header("World Data Boss")]
        [Tooltip("Boss flag imported from the world data file for direct enemy objects")]
        [SerializeField] private bool worldIsBoss = false;

        [Tooltip("Unique world-data boss object id used by BossNPCList.py")]
        [SerializeField] private string worldBossUniqueId = "";

        [Tooltip("Merged boss data from BossNPCList.py and PLocalizerEnglish.py")]
        [SerializeField] private PotcoBossData worldBossData = new PotcoBossData();

        [Tooltip("Explicit non-boss level imported from the world data file")]
        [SerializeField] private int worldLevelOverride = 0;

        [Header("Editor Spawning")]
        [Tooltip("Has this spawn node already spawned its creatures?")]
        [SerializeField] private bool hasSpawned = false;

        // Spawned entity references
        private List<GameObject> spawnedEntities = new List<GameObject>();
        private const float PotcoBipedVisualYawOffset = 180f;

        public PotcoEnemySpawnDefinition EnemyDefinition => enemyDefinition;

        public void SetWorldAnimationData(string animSet, string greetingAnimation, string noticeAnimation1, string noticeAnimation2)
        {
            worldAnimSet = animSet ?? "";
            worldGreetingAnimation = greetingAnimation ?? "";
            worldNoticeAnimation1 = noticeAnimation1 ?? "";
            worldNoticeAnimation2 = noticeAnimation2 ?? "";
        }

        public void SetWorldBossData(bool isBoss, string uniqueId, PotcoBossData bossData)
        {
            worldIsBoss = isBoss;
            worldBossUniqueId = uniqueId ?? "";
            worldBossData = bossData?.Clone() ?? new PotcoBossData();
            if (!string.IsNullOrEmpty(worldBossUniqueId))
                worldBossData.UniqueId = worldBossUniqueId;
        }

        public void SetWorldLevelOverride(int level)
        {
            worldLevelOverride = Mathf.Max(0, level);
        }

        /// <summary>
        /// Set creature type flag, species, and model path (called during import from editor)
        /// </summary>
        public void SetCreatureInfo(bool isCreature, string species, string modelPath)
        {
            isCreatureType = isCreature;
            creatureSpecies = species;
            creatureModelPath = modelPath;
        }

        public void SetEnemyDefinition(PotcoEnemySpawnDefinition definition)
        {
            enemyDefinition = definition?.Clone() ?? new PotcoEnemySpawnDefinition();

            if (enemyDefinition.IsValid)
            {
                PotcoEnemyVariantData firstCreature = enemyDefinition.Variants.Find(v => v != null && v.Kind == PotcoEnemyKind.Creature);
                if (firstCreature != null)
                {
                    SetCreatureInfo(true, firstCreature.CreatureSpecies, firstCreature.CreatureModelPath);
                }
                else
                {
                    SetCreatureInfo(false, "", "");
                }
            }
        }

        /// <summary>
        /// Set creature type flag (called during import from editor)
        /// </summary>
        public void SetIsCreatureType(bool value)
        {
            isCreatureType = value;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Manual spawn trigger for editor (right-click component → Force Spawn)
        /// </summary>
        [UnityEngine.ContextMenu("Force Spawn")]
        public void ForceSpawn()
        {
            Debug.Log($"[SpawnNode] Force spawn requested for '{gameObject.name}'");

            // Clear existing spawned entities
            ClearSpawnedEntities();

            // Reset flag and spawn
            hasSpawned = false;
            Start();
        }

        /// <summary>
        /// Clear spawned entities (right-click component → Clear Spawned)
        /// </summary>
        [UnityEngine.ContextMenu("Clear Spawned")]
        public void ClearSpawnedEntities()
        {
            Debug.Log($"[SpawnNode] Clearing spawned entities for '{gameObject.name}'");

            foreach (GameObject entity in spawnedEntities)
            {
                if (entity != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(entity);
                    }
                    else
                    {
                        DestroyImmediate(entity);
                    }
                }
            }

            spawnedEntities.Clear();
            hasSpawned = false;
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[SpawnNode] ✅ Cleared all spawned entities");
        }
#endif

        private void Start()
        {
            // Only spawn once (either in editor or play mode, but not both)
            if (hasSpawned)
            {
                Debug.Log($"[SpawnNode] Already spawned for '{gameObject.name}', skipping");
                return;
            }

            Debug.Log($"[SpawnNode] Start() called for '{gameObject.name}', autoSpawn={autoSpawn}, spawnables='{spawnables}'");
            Debug.Log($"[SpawnNode] Cached data: isCreatureType={isCreatureType}, creatureSpecies='{creatureSpecies}', creatureModelPath='{creatureModelPath}'");

            if (autoSpawn && !string.IsNullOrEmpty(spawnables))
            {
                SpawnEnemies();
                hasSpawned = true;
#if UNITY_EDITOR
                // Mark dirty to save hasSpawned flag
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
            else
            {
                Debug.LogWarning($"[SpawnNode] Not spawning: autoSpawn={autoSpawn}, spawnables empty={string.IsNullOrEmpty(spawnables)}");
            }
        }

        /// <summary>
        /// Spawn enemies/creatures based on spawnable type
        /// </summary>
        private void SpawnEnemies()
        {
            Debug.Log($"[SpawnNode] ========================================");
            Debug.Log($"[SpawnNode] SpawnEnemies() called");
            Debug.Log($"[SpawnNode] Spawnables: '{spawnables}'");
            Debug.Log($"[SpawnNode] Definition valid: {enemyDefinition != null && enemyDefinition.IsValid}");
            Debug.Log($"[SpawnNode] Spawn Count: {spawnCount}");
            Debug.Log($"[SpawnNode] ========================================");

            for (int i = 0; i < spawnCount; i++)
            {
                GameObject spawned = null;
                PotcoEnemyVariantData variant = enemyDefinition != null && enemyDefinition.IsValid
                    ? enemyDefinition.ChooseVariant()
                    : null;

                if (variant != null)
                {
                    spawned = SpawnEnemyVariant(variant);
                }
                else
                {
                    string baseSpawnable = GetBaseSpawnableName(spawnables);
                    bool isCreature = IsCreatureType(baseSpawnable);
                    spawned = isCreature ? SpawnLegacyCreature(baseSpawnable) : SpawnLegacyHumanEnemy(baseSpawnable);
                }

                if (spawned != null)
                {
                    spawnedEntities.Add(spawned);
                    Debug.Log($"[SpawnNode] ✅ Successfully spawned entity #{i}: {spawned.name}");

                    // Apply spawn point offset for multiple spawns
                    if (i > 0)
                    {
                        Vector3 offset = UnityEngine.Random.insideUnitCircle * 2f;
                        spawned.transform.position += new Vector3(offset.x, 0, offset.y);
                    }
                }
                else
                {
                    Debug.LogError($"[SpawnNode] ❌ Failed to spawn entity #{i}");
                }
            }

            Debug.Log($"[SpawnNode] SpawnEnemies() complete. Total spawned: {spawnedEntities.Count}");
        }

        private GameObject SpawnEnemyVariant(PotcoEnemyVariantData variant)
        {
            if (variant == null)
                return null;

            int level = ResolveEnemyLevel(variant);
            Debug.Log($"[SpawnNode] Spawning POTCO enemy {variant.TypeName} ({variant.Kind}) level {level}");

            switch (variant.Kind)
            {
                case PotcoEnemyKind.Creature:
                    return SpawnCreature(variant, level);
                case PotcoEnemyKind.Skeleton:
                    return SpawnSkeletonEnemy(variant, level);
                case PotcoEnemyKind.Human:
                    return SpawnHumanEnemy(variant, level);
                default:
                    Debug.LogWarning($"[SpawnNode] Unknown enemy kind for {variant.TypeName}; using human fallback");
                    return SpawnHumanEnemy(variant, level);
            }
        }

        private GameObject SpawnCreature(PotcoEnemyVariantData variant, int level)
        {
            if (variant == null || string.IsNullOrEmpty(variant.CreatureModelPath))
                return null;

            GameObject creaturePrefab = LoadCreaturePrefab(variant.CreatureModelPath);
            if (creaturePrefab == null)
                Debug.LogWarning($"[SpawnNode] Missing creature model '{variant.CreatureModelPath}' for {variant.TypeName}; using fallback capsule");

            GameObject enemyRoot = new GameObject($"Enemy_{variant.TypeName}_Lv{level}");
            enemyRoot.transform.SetParent(transform, false);
            enemyRoot.transform.localPosition = Vector3.zero;
            enemyRoot.transform.localRotation = Quaternion.identity;

            GameObject model = creaturePrefab != null
                ? InstantiateModel(creaturePrefab, enemyRoot.transform)
                : CreateFallbackPrimitive($"{variant.TypeName}_Fallback", PrimitiveType.Capsule, enemyRoot.transform);

            RuntimeAnimatorPlayer animComponent = model.GetComponent<RuntimeAnimatorPlayer>();
            if (animComponent == null)
            {
                animComponent = model.AddComponent<RuntimeAnimatorPlayer>();
                animComponent.Initialize();
            }

            string species = string.IsNullOrEmpty(variant.CreatureSpecies) ? variant.TypeName : variant.CreatureSpecies;
            PotcoCreatureAnimationDefinition animationDefinition = PotcoCreatureAnimationCatalog.FromVariant(variant);
            LoadCreatureAnimations(animComponent, animationDefinition);
            AddCreatureAnimationPlayer(model, species, animationDefinition.AnimationPrefix);
            ConfigureEnemyRuntime(enemyRoot, variant, level, species);
            return enemyRoot;
        }

        private GameObject SpawnSkeletonEnemy(PotcoEnemyVariantData variant, int level)
        {
            GameObject skeletonPrefab = LoadSkeletonPrefab(variant.SkeletonModelPath);
            if (skeletonPrefab == null)
                Debug.LogWarning($"[SpawnNode] Missing skeleton model '{variant.SkeletonModelPath}' for {variant.TypeName}; using fallback capsule");

            GameObject enemyRoot = new GameObject($"Enemy_{variant.TypeName}_Lv{level}");
            enemyRoot.transform.SetParent(transform, false);
            enemyRoot.transform.localPosition = Vector3.zero;
            enemyRoot.transform.localRotation = Quaternion.identity;

            GameObject model = skeletonPrefab != null
                ? InstantiateModel(skeletonPrefab, enemyRoot.transform)
                : CreateFallbackPrimitive($"{variant.TypeName}_Fallback", PrimitiveType.Capsule, enemyRoot.transform);
            ApplyBipedVisualFacing(model);

            RuntimeAnimatorPlayer animComponent = model.GetComponent<RuntimeAnimatorPlayer>();
            if (animComponent == null)
            {
                animComponent = model.AddComponent<RuntimeAnimatorPlayer>();
                animComponent.Initialize();
            }

            LoadBipedAnimations(animComponent, variant.SkeletonStyle);
            ConfigureEnemyRuntime(enemyRoot, variant, level, variant.SkeletonStyle);
            return enemyRoot;
        }

        private GameObject SpawnHumanEnemy(PotcoEnemyVariantData variant, int level)
        {
            GameObject enemyRoot = new GameObject($"Enemy_{variant.TypeName}_Lv{level}");
            enemyRoot.transform.SetParent(transform, false);
            enemyRoot.transform.localPosition = Vector3.zero;
            enemyRoot.transform.localRotation = Quaternion.identity;

            GameObject human = PotcoEnemyHumanFactory.CreateHumanEnemy(variant, enemyRoot.transform);
            if (human == null)
            {
                DestroyImmediateSafe(enemyRoot);
                return null;
            }

            human.transform.localPosition = Vector3.zero;
            ApplyBipedVisualFacing(human);
            human.transform.localScale = Vector3.one;
            RuntimeAnimatorPlayer animComponent = human.GetComponent<RuntimeAnimatorPlayer>();
            if (animComponent == null)
            {
                animComponent = human.AddComponent<RuntimeAnimatorPlayer>();
                animComponent.Initialize();
            }

            LoadBipedAnimations(animComponent, "mp");
            ConfigureEnemyRuntime(enemyRoot, variant, level, "default");
            return enemyRoot;
        }

        private GameObject SpawnLegacyCreature(string creatureName)
        {
            var variant = new PotcoEnemyVariantData
            {
                TypeName = creatureName,
                Kind = PotcoEnemyKind.Creature,
                CreatureSpecies = string.IsNullOrEmpty(creatureSpecies) ? creatureName : creatureSpecies,
                CreatureModelPath = creatureModelPath,
                BaseScale = 1f,
                MinLevel = 1,
                MaxLevel = 1,
                Height = 1.5f,
                BattleTubeRadius = 0.5f
            };

            return SpawnCreature(variant, 1);
        }

        private GameObject SpawnLegacyHumanEnemy(string enemyName)
        {
            var variant = new PotcoEnemyVariantData
            {
                TypeName = enemyName,
                Kind = PotcoEnemyKind.Human,
                HumanPreset = PotcoEnemyHumanPreset.None,
                MinLevel = 1,
                MaxLevel = 1,
                Height = 1.8f,
                BattleTubeRadius = 0.3f
            };

            return SpawnHumanEnemy(variant, 1);
        }

        private int ResolveEnemyLevel(PotcoEnemyVariantData variant)
        {
            if (worldIsBoss && worldBossData != null && worldBossData.LevelOverride > 0)
                return worldBossData.LevelOverride;

            if (worldLevelOverride > 0)
                return worldLevelOverride;

            return variant?.PickLevel() ?? 1;
        }

        private GameObject LoadCreaturePrefab(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath))
                return null;

            if (s_creaturePrefabCache.TryGetValue(modelPath, out GameObject cached))
                return cached;

            GameObject prefab = LoadModelWithPhaseFallback(modelPath, includeLodSuffixes: false);
            s_creaturePrefabCache[modelPath] = prefab;
            return prefab;
        }

        private GameObject LoadSkeletonPrefab(string modelPath)
        {
            return LoadModelWithPhaseFallback(modelPath, includeLodSuffixes: true);
        }

        private GameObject LoadModelWithPhaseFallback(string modelPath, bool includeLodSuffixes)
        {
            if (string.IsNullOrEmpty(modelPath))
                return null;

            string normalized = modelPath.Replace("\\", "/").Trim();
            string[] phases = { "", "phase_2/", "phase_3/", "phase_4/", "phase_5/", "phase_6/" };
            string[] suffixes = includeLodSuffixes
                ? new[] { "", "_1000", "_2000", "_500", "_250" }
                : new[] { "" };

            foreach (string phase in phases)
            {
                foreach (string suffix in suffixes)
                {
                    GameObject prefab = Resources.Load<GameObject>(phase + normalized + suffix);
                    if (prefab != null)
                        return prefab;
                }
            }

            return null;
        }

        private GameObject InstantiateModel(GameObject prefab, Transform parent)
        {
            GameObject instance = null;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                instance = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
#endif
            if (instance == null)
                instance = Instantiate(prefab, parent, false);

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        private GameObject CreateFallbackPrimitive(string name, PrimitiveType primitiveType, Transform parent)
        {
            GameObject fallback = GameObject.CreatePrimitive(primitiveType);
            fallback.name = name;
            fallback.transform.SetParent(parent, false);
            fallback.transform.localPosition = Vector3.zero;
            fallback.transform.localRotation = Quaternion.identity;
            fallback.transform.localScale = Vector3.one;
            return fallback;
        }

        private static void ApplyBipedVisualFacing(GameObject model)
        {
            if (model != null)
                model.transform.localRotation = Quaternion.Euler(0f, PotcoBipedVisualYawOffset, 0f);
        }

        private static void DestroyImmediateSafe(GameObject go)
        {
            if (go == null)
                return;

            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }

        private void ConfigureEnemyRuntime(GameObject enemyRoot, PotcoEnemyVariantData variant, int level, string animSet)
        {
            if (enemyRoot == null || variant == null)
                return;

            PotcoBossData effectiveBoss = ResolveEffectiveBossData(variant);
            float bossScaleMultiplier = worldIsBoss ? (effectiveBoss?.ModelScale ?? 1f) : 1f;
            float resolvedScale = variant.ResolveScale(level, bossScaleMultiplier, !worldIsBoss);
            enemyRoot.transform.localScale = Vector3.one * Mathf.Max(0.01f, resolvedScale);

            NPCData npcData = enemyRoot.GetComponent<NPCData>();
            if (npcData == null)
                npcData = enemyRoot.AddComponent<NPCData>();

            npcData.npcId = $"{spawnables}_{variant.TypeName}_{enemyRoot.GetInstanceID()}";
            npcData.category = variant.Kind == PotcoEnemyKind.Creature ? "Animal" : "Enemy";
            npcData.team = string.IsNullOrEmpty(variant.Faction) ? GetTeamName(teamId) : variant.Faction;
            npcData.startState = string.IsNullOrEmpty(startState) ? "LandRoam" : startState;
            npcData.patrolRadius = patrolRadius;
            npcData.aggroRadius = aggroRadius;
            npcData.animSet = !string.IsNullOrEmpty(worldAnimSet)
                ? worldAnimSet
                : (string.IsNullOrEmpty(animSet) ? "default" : animSet);
            npcData.greetingAnimation = worldGreetingAnimation;
            npcData.noticeAnimation1 = worldNoticeAnimation1;
            npcData.noticeAnimation2 = worldNoticeAnimation2;
            npcData.isEnemy = true;
            npcData.enemySpawnable = spawnables;
            npcData.enemyTypeName = variant.TypeName;
            npcData.enemyLevel = level;
            npcData.enemyKind = variant.Kind;
            npcData.enemyMonsterClass = variant.MonsterClass;
            npcData.enemyFaction = variant.Faction;
            npcData.enemyTrack = variant.Track;
            npcData.enemyBipedAnimStyle = variant.Kind == PotcoEnemyKind.Human ? "mp" : (animSet ?? "");
            npcData.enemyScale = resolvedScale;
            npcData.enemyWeaponCategories = variant.WeaponCategories?.ToArray() ?? Array.Empty<string>();
            npcData.enemyWeaponNames = variant.WeaponItemNames?.ToArray() ?? Array.Empty<string>();
            npcData.enemyWeaponIds = variant.WeaponItemIds?.ToArray() ?? Array.Empty<int>();
            npcData.enemySkillNames = variant.SkillNames?.ToArray() ?? Array.Empty<string>();
            npcData.enemySkillIds = variant.SkillIds?.ToArray() ?? Array.Empty<int>();
            npcData.isBoss = effectiveBoss != null;
            npcData.bossUniqueId = effectiveBoss?.UniqueId ?? "";
            npcData.bossName = ResolveBossDisplayName(variant, effectiveBoss);
            npcData.bossHpScale = effectiveBoss?.HpScale ?? 1f;
            npcData.bossMpScale = effectiveBoss?.MpScale ?? 1f;
            npcData.bossGoldScale = effectiveBoss?.GoldScale ?? 1f;
            npcData.bossModelScale = effectiveBoss?.ModelScale ?? 1f;
            npcData.bossDamageScale = effectiveBoss?.DamageScale ?? 1f;
            npcData.bossArmorScale = effectiveBoss?.ArmorScale ?? 1f;
            npcData.bossHighlightColor = effectiveBoss?.HighlightColor ?? Color.white;
            ConfigureGhostRuntime(enemyRoot, variant, npcData, effectiveBoss);

            CharacterController controller = enemyRoot.GetComponent<CharacterController>();
            if (controller == null)
                controller = enemyRoot.AddComponent<CharacterController>();

            float height = Mathf.Max(1f, variant.Kind == PotcoEnemyKind.Human ? 1.8f : variant.Height);
            float radius = Mathf.Clamp(variant.BattleTubeRadius * 0.5f, 0.25f, 2.5f);
            controller.height = height;
            controller.radius = radius;
            controller.center = new Vector3(0f, height * 0.5f, 0f);

            NPCController npcController = enemyRoot.GetComponent<NPCController>();
            if (npcController == null)
                npcController = enemyRoot.AddComponent<NPCController>();
            EnablePatrol(npcController);
            npcController.enabled = true;

            if (variant.Kind != PotcoEnemyKind.Creature && enemyRoot.GetComponent<SimpleAnimationPlayer>() == null)
                enemyRoot.AddComponent<SimpleAnimationPlayer>();

            PotcoEnemyCombatLoadout loadout = enemyRoot.GetComponent<PotcoEnemyCombatLoadout>();
            if (loadout == null)
                loadout = enemyRoot.AddComponent<PotcoEnemyCombatLoadout>();
            loadout.Initialize(variant, level);

            PotcoCombatTarget combatTarget = enemyRoot.GetComponent<PotcoCombatTarget>();
            if (combatTarget == null)
                combatTarget = enemyRoot.AddComponent<PotcoCombatTarget>();
            float hpScale = Mathf.Max(1f, npcData.bossHpScale);
            combatTarget.ResetHealth(Mathf.Max(100f, level * 100f) * hpScale);
        }

        private void ConfigureGhostRuntime(GameObject enemyRoot, PotcoEnemyVariantData variant, NPCData npcData, PotcoBossData effectiveBoss)
        {
            if (enemyRoot == null || variant == null || npcData == null)
                return;

            bool isGhost = variant.Kind == PotcoEnemyKind.Human &&
                (variant.HumanPreset == PotcoEnemyHumanPreset.Ghost ||
                 string.Equals(variant.Faction, "Ghost", StringComparison.OrdinalIgnoreCase));

            npcData.isGhost = isGhost;
            npcData.ghostColorIndex = 0;
            npcData.ghostMode = 0;
            npcData.ghostBodyColor = Color.white;
            npcData.ghostEffectSource = "";

            if (!isGhost)
                return;

            ResolveReferenceGhostState(variant, effectiveBoss, out int colorIndex, out int ghostMode, out string source);
            PotcoGhostEffect effect = enemyRoot.GetComponent<PotcoGhostEffect>();
            if (effect == null)
                effect = enemyRoot.AddComponent<PotcoGhostEffect>();

            effect.Configure(colorIndex, ghostMode);
            npcData.ghostColorIndex = colorIndex;
            npcData.ghostMode = ghostMode;
            npcData.ghostBodyColor = PotcoGhostEffect.ResolveGhostColor(colorIndex);
            npcData.ghostEffectSource = source;
        }

        private static void ResolveReferenceGhostState(PotcoEnemyVariantData variant, PotcoBossData effectiveBoss, out int colorIndex, out int ghostMode, out string source)
        {
            if (variant != null &&
                (string.Equals(variant.Track, "KillerGhosts", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(variant.TypeName, "RageGhost", StringComparison.OrdinalIgnoreCase) ||
                 (variant.TypeName ?? string.Empty).IndexOf("KillerGhost", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                colorIndex = 4;
                ghostMode = 3;
                source = "DistributedKillerGhost.selectedGhostColor/peaceGhostMode";
                return;
            }

            if (effectiveBoss != null || (variant != null && variant.IsBoss))
            {
                colorIndex = 13;
                ghostMode = 2;
                source = "DistributedBossGhost.enemyColor/attackGhostMode";
                return;
            }

            colorIndex = 2;
            ghostMode = 1;
            source = "DistributedGhost.enemyColor/peaceGhostMode";
        }

        private PotcoBossData ResolveEffectiveBossData(PotcoEnemyVariantData variant)
        {
            if (worldIsBoss)
            {
                PotcoBossData data = worldBossData?.Clone() ?? new PotcoBossData();
                if (!string.IsNullOrEmpty(worldBossUniqueId))
                    data.UniqueId = worldBossUniqueId;
                if (string.IsNullOrEmpty(data.DisplayName) && variant != null && variant.IsBoss)
                    data.DisplayName = variant.BossName;
                return data;
            }

            return variant != null && variant.IsBoss ? variant.BossData?.Clone() : null;
        }

        private string ResolveBossDisplayName(PotcoEnemyVariantData variant, PotcoBossData bossData)
        {
            if (bossData != null && !string.IsNullOrEmpty(bossData.DisplayName))
                return bossData.DisplayName;

            if (variant != null && variant.IsBoss && !string.IsNullOrEmpty(variant.BossName))
                return variant.BossName;

            return variant?.TypeName ?? "";
        }

        private void EnablePatrol(NPCController npcController)
        {
            var enablePatrolField = typeof(NPCController).GetField("enablePatrol",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (enablePatrolField != null)
                enablePatrolField.SetValue(npcController, true);
        }

        private void AddCreatureAnimationPlayer(GameObject model, string species)
        {
            AddCreatureAnimationPlayer(model, species, PotcoCreatureAnimationCatalog.Resolve(species).AnimationPrefix);
        }

        private void AddCreatureAnimationPlayer(GameObject model, string species, string animationPrefix)
        {
            if (model == null)
                return;

            AnimalAnimationPlayer animalAnimPlayer = model.GetComponent<AnimalAnimationPlayer>();
            if (animalAnimPlayer == null)
                animalAnimPlayer = model.AddComponent<AnimalAnimationPlayer>();

            animalAnimPlayer.animationPrefix = string.IsNullOrEmpty(animationPrefix)
                ? PotcoCreatureAnimationCatalog.Resolve(species).AnimationPrefix
                : animationPrefix;
            animalAnimPlayer.currentState = string.IsNullOrEmpty(startState) ? "LandRoam" : startState;
        }

        private void LoadBipedAnimations(RuntimeAnimatorPlayer animComponent, string style)
        {
            if (animComponent == null)
                return;

            string[] names =
            {
                "idle",
                "walk",
                "run",
                "intro",
                "cutlass_combo",
                "dagger_combo",
                "gun_fire",
                "rifle_fight_shoot_hip",
                "bayonet_attackA",
                "knife_throw",
                "bomb_throw",
                "voodoo_tune",
                "voodoo_doll_poke",
                "wand_cast_fire"
            };
            foreach (string name in names)
            {
                foreach (string candidate in PotcoBipedAnimationResolver.BuildResourceCandidates(name, style))
                {
                    AnimationClip clip = Resources.Load<AnimationClip>(candidate);
                    if (clip == null)
                        continue;

                    animComponent.AddClip(clip, name);
                    animComponent.SetWrapMode(name, WrapMode.Loop);
                    break;
                }
            }
        }

        // Static caches to prevent redundant Resources.Load calls across multiple SpawnNodes
        private static Dictionary<string, GameObject> s_creaturePrefabCache = new Dictionary<string, GameObject>();
        private static Dictionary<string, AnimationClip> s_creatureAnimCache = new Dictionary<string, AnimationClip>();

        /// <summary>
        /// Spawn a creature (uses Animal AI system)
        /// Replicates the working logic from PropertyProcessor.SpawnCreature
        /// </summary>
        private GameObject SpawnCreature(string creatureName)
        {
            // Debug.Log($"[SpawnNode] SpawnCreature: {creatureName}");

            // Use cached model path from import (set by PropertyProcessor)
            if (string.IsNullOrEmpty(creatureModelPath)) return null;

            string species = string.IsNullOrEmpty(creatureSpecies) ? creatureName.ToLower() : creatureSpecies.ToLower();

            GameObject creaturePrefab = null;

            // CHECK CACHE FIRST
            if (!s_creaturePrefabCache.TryGetValue(creatureModelPath, out creaturePrefab))
            {
                // Try to load the model from Resources using cached path
                creaturePrefab = Resources.Load<GameObject>(creatureModelPath);

                if (creaturePrefab == null)
                {
                    // If not found, try adding phase prefixes (matches PropertyProcessor logic)
                    string[] phasePrefixes = new string[] { "", "phase_2/", "phase_3/", "phase_4/", "phase_5/", "phase_6/" };
                    foreach (string prefix in phasePrefixes)
                    {
                        string testPath = prefix + creatureModelPath;
                        creaturePrefab = Resources.Load<GameObject>(testPath);
                        if (creaturePrefab != null) break;
                    }
                }

                if (creaturePrefab != null)
                {
                    s_creaturePrefabCache[creatureModelPath] = creaturePrefab;
                }
            }

            if (creaturePrefab == null)
            {
                Debug.LogError($"[SpawnNode] ❌ FAILED to load creature model: {creatureModelPath}");
                return null;
            }

            // Instantiate creature (match PropertyProcessor instantiation)
            GameObject instance = null;

#if UNITY_EDITOR
            // In editor, use PrefabUtility to maintain prefab link
            if (!Application.isPlaying)
            {
                instance = UnityEditor.PrefabUtility.InstantiatePrefab(creaturePrefab) as GameObject;
            }
#endif

            // Fallback to regular Instantiate (play mode or if PrefabUtility failed)
            if (instance == null)
            {
                instance = Instantiate(creaturePrefab);
            }

            // Parent to this spawn node (using false to maintain world scale/rotation)
            instance.transform.SetParent(transform, false);

            // Reset local position to (0,0,0) - creature spawns at SpawnNode position
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            // Add RuntimeAnimatorPlayer component to the instance
            RuntimeAnimatorPlayer animComponent = instance.GetComponent<RuntimeAnimatorPlayer>();
            if (animComponent == null)
            {
                animComponent = instance.AddComponent<RuntimeAnimatorPlayer>();
                animComponent.Initialize();
            }

            // Add AI components - pass the PARENT (this gameObject), not the instance
            AddCreatureAIComponents(gameObject, species);

            return instance;
        }

        /// <summary>
        /// Spawn a human enemy (uses NPC AI system)
        /// </summary>
        private GameObject SpawnHumanEnemy(string enemyName)
        {
            // TODO: Implement human enemy spawning using DNA system
            GameObject enemyParent = new GameObject(enemyName);
            enemyParent.transform.SetParent(transform);
            enemyParent.transform.position = transform.position;

            return enemyParent;
        }

        /// <summary>
        /// Add AI components to spawned creature
        /// </summary>
        private void AddCreatureAIComponents(GameObject parentNode, string species)
        {
            // Find the creature model (first child of parent)
            GameObject creatureModel = null;
            if (parentNode.transform.childCount > 0)
            {
                creatureModel = parentNode.transform.GetChild(0).gameObject;
            }
            else
            {
                return;
            }

            // Ensure RuntimeAnimatorPlayer component exists on creature model
            RuntimeAnimatorPlayer animComponent = null;
            if (creatureModel != null)
            {
                animComponent = creatureModel.GetComponent<RuntimeAnimatorPlayer>();
                if (animComponent == null)
                {
                    animComponent = creatureModel.AddComponent<RuntimeAnimatorPlayer>();
                    animComponent.Initialize();
                }

                LoadCreatureAnimations(animComponent, species);
            }

            // Add NPCData component to parent node
            NPCData npcData = parentNode.GetComponent<NPCData>();
            if (npcData == null)
            {
                npcData = parentNode.AddComponent<NPCData>();
            }

            // Configure NPC data
            npcData.npcId = $"{spawnables}_{gameObject.name}";
            npcData.category = "Animal";
            npcData.team = "Animal";
            npcData.startState = string.IsNullOrEmpty(startState) ? "LandRoam" : startState;
            npcData.patrolRadius = patrolRadius;
            npcData.aggroRadius = 0f;
            npcData.animSet = species.ToLower();

            // Add CharacterController
            CharacterController controller = parentNode.GetComponent<CharacterController>();
            if (controller == null)
            {
                controller = parentNode.AddComponent<CharacterController>();
                controller.radius = 0.5f;
                controller.height = 1.5f;
                controller.center = new Vector3(0, 0.75f, 0);
            }

            // Add NPCController
            NPCController npcController = parentNode.GetComponent<NPCController>();
            if (npcController == null)
            {
                npcController = parentNode.AddComponent<NPCController>();
            }

            // Enable patrol
            var enablePatrolField = typeof(NPCController).GetField("enablePatrol",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (enablePatrolField != null)
            {
                enablePatrolField.SetValue(npcController, true);
            }

            npcController.enabled = true;

            // Add AnimalAnimationPlayer
            if (creatureModel != null)
            {
                AnimalAnimationPlayer animalAnimPlayer = creatureModel.GetComponent<AnimalAnimationPlayer>();
                if (animalAnimPlayer == null)
                {
                    animalAnimPlayer = creatureModel.AddComponent<AnimalAnimationPlayer>();
                    string animPrefix = species.ToLower();
                    animPrefix = System.Text.RegularExpressions.Regex.Replace(animPrefix, "_hi$|_lo$|_mid$", "");
                    animalAnimPlayer.animationPrefix = animPrefix;
                    animalAnimPlayer.currentState = string.IsNullOrEmpty(startState) ? "LandRoam" : startState;
                }
            }
        }

        /// <summary>
        /// Load animation clips for creature's Animation component from Resources
        /// Matches PropertyProcessor.LoadCreatureAnimations logic
        /// </summary>
        private void LoadCreatureAnimations(RuntimeAnimatorPlayer animComponent, string species)
        {
            LoadCreatureAnimations(animComponent, PotcoCreatureAnimationCatalog.Resolve(species));
        }

        private void LoadCreatureAnimations(RuntimeAnimatorPlayer animComponent, PotcoCreatureAnimationDefinition definition)
        {
            if (animComponent == null || definition == null)
                return;

            string animationPrefix = string.IsNullOrEmpty(definition.AnimationPrefix)
                ? PotcoCreatureAnimationCatalog.ResolveAnimationPrefix(definition.Species, string.Empty)
                : definition.AnimationPrefix;

            foreach ((string animName, string animFile) in definition.EnumerateAnimations())
            {
                string clipName = $"{animationPrefix}_{animName}";
                string cacheKey = $"{animationPrefix}_{animName}_{animFile}";

                if (!s_creatureAnimCache.TryGetValue(cacheKey, out AnimationClip clip))
                {
                    foreach (string path in BuildCreatureAnimationResourceCandidates(animationPrefix, animFile))
                    {
                        clip = Resources.Load<AnimationClip>(path);
                        if (clip != null)
                            break;
                    }

                    if (clip != null)
                        s_creatureAnimCache[cacheKey] = clip;
                }

                if (clip == null)
                    continue;

                animComponent.AddClip(clip, clipName);
                animComponent.SetWrapMode(clipName, WrapMode.Loop);
            }
        }

        private static IEnumerable<string> BuildCreatureAnimationResourceCandidates(string animationPrefix, string animFile)
        {
            string[] phases = { "phase_4", "phase_3", "phase_5", "phase_2", "phase_6" };
            string[] folders = { "models/char", "char" };
            string clipName = string.IsNullOrEmpty(animationPrefix) ? animFile : $"{animationPrefix}_{animFile}";

            foreach (string phase in phases)
            {
                foreach (string folder in folders)
                    yield return $"{phase}/{folder}/{clipName}";
            }
        }

        /// <summary>
        /// Check if spawnable is a creature type (uses Animal AI)
        /// Uses cached value set during import
        /// </summary>
        private bool IsCreatureType(string spawnableName)
        {
            // Use the cached value that was set during import
            return isCreatureType;
        }

        /// <summary>
        /// Get base spawnable name from spawn string (e.g., "Crab T1" -> "Crab")
        /// </summary>
        private string GetBaseSpawnableName(string spawnableName)
        {
            if (string.IsNullOrEmpty(spawnableName))
                return "";

            // Remove tier indicators (T1, T2, etc.)
            string[] parts = spawnableName.Split(' ');
            return parts[0];
        }

        /// <summary>
        /// Convert team ID to team name
        /// </summary>
        private string GetTeamName(int teamId)
        {
            switch (teamId)
            {
                case 0: return "default";
                case 1: return "Villager";
                case 2: return "Navy";
                case 3: return "EITC";
                case 4: return "Undead";
                default: return "default";
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Draw gizmos in editor to visualize spawn area
        /// </summary>
        private void OnDrawGizmos()
        {
            // Draw patrol radius as green wire sphere
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, patrolRadius);

            // Draw aggro radius as red wire sphere
            if (aggroRadius > 0)
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
                Gizmos.DrawWireSphere(transform.position, aggroRadius);
            }

            // Draw center point
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(transform.position, 0.3f);
        }

        /// <summary>
        /// Draw labels in scene view
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            // Draw detailed info when selected
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
                $"Spawn Node\n{spawnables}\nTeam: {teamId}\nState: {startState}");
        }
#endif
    }
}
