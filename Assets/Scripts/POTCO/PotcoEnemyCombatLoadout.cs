using System.Collections.Generic;
using POTCO.Combat;
using UnityEngine;

namespace POTCO
{
    public sealed class PotcoEnemyCombatLoadout : MonoBehaviour
    {
        [SerializeField] private string enemyType = string.Empty;
        [SerializeField] private int level = 1;
        [SerializeField] private List<string> weaponCategories = new List<string>();
        [SerializeField] private List<string> weaponItemNames = new List<string>();
        [SerializeField] private List<int> weaponItemIds = new List<int>();
        [SerializeField] private List<string> skillNames = new List<string>();
        [SerializeField] private List<int> skillIds = new List<int>();
        [SerializeField] private bool enableSkillAnimations = true;
        [SerializeField] private float skillAnimationInterval = 4f;

        private static PotcoWeaponCatalog s_weaponCatalog;
        private static readonly PotcoWeaponVisualResolver s_visualResolver = new PotcoWeaponVisualResolver();
        private NPCData npcData;
        private Transform playerTransform;
        private float nextSkillAnimationTime;

        public string EnemyType => enemyType;
        public int Level => level;
        public IReadOnlyList<string> WeaponCategories => weaponCategories;
        public IReadOnlyList<string> WeaponItemNames => weaponItemNames;
        public IReadOnlyList<int> WeaponItemIds => weaponItemIds;
        public IReadOnlyList<string> SkillNames => skillNames;
        public IReadOnlyList<int> SkillIds => skillIds;

        private void Awake()
        {
            npcData = GetComponent<NPCData>();
            nextSkillAnimationTime = Time.time + UnityEngine.Random.Range(1.25f, 3f);
        }

        private void Update()
        {
            if (!enableSkillAnimations || skillNames == null || skillNames.Count == 0 || Time.time < nextSkillAnimationTime)
                return;

            npcData ??= GetComponent<NPCData>();
            float aggro = npcData != null && npcData.aggroRadius > 0f ? npcData.aggroRadius : 12f;
            RefreshPlayerReference();
            if (playerTransform == null || Vector3.Distance(transform.position, playerTransform.position) > aggro)
                return;

            PlayResolvedSkillAnimation();
            nextSkillAnimationTime = Time.time + Mathf.Max(1f, skillAnimationInterval);
        }

        public void Initialize(PotcoEnemyVariantData variant, int enemyLevel)
        {
            if (variant == null)
                return;

            enemyType = variant.TypeName;
            level = enemyLevel;
            weaponCategories = new List<string>(variant.WeaponCategories ?? new List<string>());
            weaponItemNames = new List<string>(variant.WeaponItemNames ?? new List<string>());
            weaponItemIds = new List<int>(variant.WeaponItemIds ?? new List<int>());
            skillNames = new List<string>(variant.SkillNames ?? new List<string>());
            skillIds = new List<int>(variant.SkillIds ?? new List<int>());

            AttachPrimaryWeaponVisual();
        }

        private void RefreshPlayerReference()
        {
            if (playerTransform != null && playerTransform.gameObject.activeInHierarchy)
                return;

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Player.PlayerController controller = UnityEngine.Object.FindAnyObjectByType<Player.PlayerController>();
                player = controller != null ? controller.gameObject : null;
            }

            playerTransform = player != null ? player.transform : null;
        }

        private void PlayResolvedSkillAnimation()
        {
            RuntimeAnimatorPlayer animator = GetComponentInChildren<RuntimeAnimatorPlayer>();
            if (animator == null)
                return;

            for (int i = 0; i < skillNames.Count; i++)
            {
                string animationName = ResolveSkillAnimation(skillNames[i]);
                if (string.IsNullOrEmpty(animationName) || !animator.HasClip(animationName))
                    continue;

                animator.CrossFadeUpperBody(animationName, 0.12f, true);
                return;
            }
        }

        private static string ResolveSkillAnimation(string skillName)
        {
            string normalized = (skillName ?? string.Empty).ToUpperInvariant();
            if (normalized.Contains("BAYONET"))
                return "bayonet_attackA";
            if (normalized.Contains("PISTOL"))
                return "gun_fire";
            if (normalized.Contains("MUSKET"))
                return "rifle_fight_shoot_hip";
            if (normalized.Contains("DAGGER_THROW"))
                return "knife_throw";
            if (normalized.Contains("DAGGER"))
                return "dagger_combo";
            if (normalized.Contains("GRENADE"))
                return "bomb_throw";
            if (normalized.Contains("DOLL"))
                return "voodoo_doll_poke";
            if (normalized.Contains("STAFF") || normalized.Contains("WAND"))
                return "wand_cast_fire";
            if (normalized.Contains("CLAW") || normalized.Contains("STUMP") || normalized.Contains("FLYTRAP"))
                return "attack";
            if (normalized.Contains("CUTLASS") || normalized.Contains("BROADSWORD") || normalized.Contains("SABRE"))
                return "cutlass_combo";

            return string.Empty;
        }

        private void AttachPrimaryWeaponVisual()
        {
            if (weaponItemIds == null || weaponItemIds.Count == 0)
                return;

            Transform hand = FindAttachmentTransform(transform);
            if (hand == null)
                return;

            if (hand.Find("POTCO Enemy Weapon") != null)
                return;

            PotcoWeaponDefinition weapon = ResolveWeaponDefinition(weaponItemIds[0]);
            string modelName = ResolveModelName(weapon, weaponItemNames.Count > 0 ? weaponItemNames[0] : string.Empty);
            GameObject weaponInstance = s_visualResolver.ResolveOrCreateWeaponInstance(modelName, hand);
            weaponInstance.name = "POTCO Enemy Weapon";
        }

        private static PotcoWeaponDefinition ResolveWeaponDefinition(int itemId)
        {
            if (itemId <= 0)
                return null;

            try
            {
                s_weaponCatalog ??= PotcoWeaponCatalog.LoadFromAssets();
                return s_weaponCatalog.GetWeaponOrNull(itemId);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PotcoEnemyCombatLoadout] Weapon catalog unavailable: {ex.Message}");
                return null;
            }
        }

        private static string ResolveModelName(PotcoWeaponDefinition weapon, string fallbackName)
        {
            if (weapon != null && weapon.Item != null && !string.IsNullOrEmpty(weapon.Item.ModelName))
                return weapon.Item.ModelName;

            return string.IsNullOrEmpty(fallbackName) ? "enemy_weapon" : fallbackName.ToLowerInvariant();
        }

        private static Transform FindAttachmentTransform(Transform root)
        {
            string[] names =
            {
                "weapon_right",
                "def_weapon_right",
                "right_hand",
                "r_hand",
                "def_r_hand",
                "def_right_hand",
                "joint_right_hold",
                "hand_r"
            };

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (string name in names)
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (string.Equals(transforms[i].name, name, System.StringComparison.OrdinalIgnoreCase))
                        return transforms[i];
                }
            }

            return root;
        }
    }
}
