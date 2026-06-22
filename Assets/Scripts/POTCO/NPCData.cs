using UnityEngine;

namespace POTCO
{
    /// <summary>
    /// Runtime component that stores NPC properties from world data
    /// Applied by PropertyProcessor.SpawnNPC() during import
    /// </summary>
    public class NPCData : MonoBehaviour
    {
        [Header("Identity")]
        public string npcId;                    // Object ID from world data
        public string category = "Commoner";     // Category (Commoner, Cast, etc.)
        public string team = "Villager";         // Team affiliation

        [Header("Behavior")]
        public string startState = "LandRoam";   // Start State (Idle/Walk maps to LandRoam)
        public float patrolRadius = 12f;         // Patrol Radius
        public float aggroRadius = 0f;           // Aggro Radius (0 = non-combat NPC)

        [Header("POTCO Enemy")]
        public bool isEnemy = false;
        public string enemySpawnable = "";
        public string enemyTypeName = "";
        public int enemyLevel = 1;
        public PotcoEnemyKind enemyKind = PotcoEnemyKind.Unknown;
        public PotcoEnemyMonsterClass enemyMonsterClass = PotcoEnemyMonsterClass.Unknown;
        public string enemyFaction = "";
        public string enemyTrack = "";
        public string enemyBipedAnimStyle = "";
        public float enemyScale = 1f;
        public string[] enemyWeaponCategories = System.Array.Empty<string>();
        public string[] enemyWeaponNames = System.Array.Empty<string>();
        public int[] enemyWeaponIds = System.Array.Empty<int>();
        public string[] enemySkillNames = System.Array.Empty<string>();
        public int[] enemySkillIds = System.Array.Empty<int>();

        [Header("POTCO Ghost")]
        public bool isGhost = false;
        public int ghostColorIndex = 0;
        public int ghostMode = 0;
        public Color ghostBodyColor = Color.white;
        public string ghostEffectSource = "";

        [Header("POTCO Boss")]
        public bool isBoss = false;
        public string bossUniqueId = "";
        public string bossName = "";
        public float bossHpScale = 1f;
        public float bossMpScale = 1f;
        public float bossGoldScale = 1f;
        public float bossModelScale = 1f;
        public float bossDamageScale = 1f;
        public float bossArmorScale = 1f;
        public Color bossHighlightColor = Color.white;

        [Header("Animations")]
        public string animSet = "default";       // Animation set
        public string greetingAnimation = "";    // Greeting Animation
        public string noticeAnimation1 = "";     // Notice Animation 1
        public string noticeAnimation2 = "";     // Notice Animation 2

        [Header("Runtime Flags (Set by NPCAnimationPlayer)")]
        [Tooltip("If true, this NPC has contextual animations and should stay locked in place")]
        public bool isStationary = false;        // Set to true if NPC has look variations
    }
}
