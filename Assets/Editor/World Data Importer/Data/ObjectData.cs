using UnityEngine;
using System.Collections.Generic;

namespace WorldDataImporter.Data
{
    public class ObjectData
    {
        public string id;
        public GameObject gameObject;
        public int indent;
        public Dictionary<string, string> properties = new Dictionary<string, string>();
        
        // Advanced properties
        public Color? visualColor;
        public bool? disableCollision;
        public string holiday;
        public string objectType;
        public bool isInstanced;
        public string visSize;
        public string visZone;
        
        // Light properties
        public string lightType;        // AMBIENT, POINT, SPOT
        public float? intensity;
        public float? attenuation;
        public float? coneAngle;
        public float? dropOff;
        public bool? flickering;
        public float? flickRate;

        // NPC properties
        public string npcDnaId;         // DNA ID from NPCList.py
        public string npcCustomModel;   // Custom model path (e.g., "models/char/js_2000")
        public string npcAnimSet;       // Animation set to apply
        public Vector3? gridPos;        // Absolute world position (GridPos)
        public bool hasPos;             // Track if Pos was set
        public bool npcHprHasPitchOrRoll; // Track world-space HPR corrections for GridPos-only NPCs
        public bool isReadyForNPCSpawn; // Track if all NPC properties are loaded

        // NPC AI properties (for runtime behavior)
        public string npcCategory;      // Category (Commoner, Cast, etc.)
        public float npcPatrolRadius;   // Patrol Radius
        public string npcStartState;    // Start State (Idle, Walk, etc.)
        public string npcTeam;          // Team (Villager, etc.)
        public float npcAggroRadius;    // Aggro Radius
        public string npcGreetingAnim;  // Greeting Animation
        public string npcNoticeAnim1;   // Notice Animation 1
        public string npcNoticeAnim2;   // Notice Animation 2

        // Direct enemy/NPC enemy properties (Creature, Skeleton, NavySailor, Ghost, boss Townsperson)
        public bool isBoss;             // Boss flag from world data
        public string bossName;         // Boss Name field from world data, usually "Anonymous"
        public int? enemyLevel;         // Explicit Level field from world data
        public int? avatarTrack;        // AvTrack for skeleton/navy/ghost enemies
        public int? avatarId;           // AvId for skeleton/navy/ghost enemies
        public string navyFaction;      // NavyFaction: Navy or TradingCo

        // Animal properties (Type = "Animal")
        public string species;          // Species (Chicken, Rooster, Pig, Alligator, etc.)
        public bool? respawns;          // Respawns flag
        public string startState;       // Start State (Idle, Walk, Patrol, etc.)
        public float? patrolRadius;     // Patrol Radius
        public bool isReadyForCreatureSpawn; // Track if all Animal properties are loaded

        // Spawn Node properties (Type = "Spawn Node")
        public string spawnables;       // Spawnables (enemy/creature type to spawn)
        public float? aggroRadius;      // Aggro Radius
        public float? spawnTimeBegin;   // Spawn time begin (in hours)
        public float? spawnTimeEnd;     // Spawn time end (in hours)
        public string team;             // Team (default, Villager, etc.)
        public bool isReadyForEnemySpawn; // Track if all Spawn Node properties are loaded
    }
    
    [System.Serializable]
    public class ImportStatistics
    {
        public int totalObjects;
        public int successfulImports;
        public int missingModels;
        public int colorOverrides;
        public int collisionDisabled;
        public int collisionRemoved;
        public int lightsCreated;
        public int visualColorsApplied;
        public Dictionary<string, int> objectTypeCount = new Dictionary<string, int>();
        public List<string> missingModelPaths = new List<string>();
        public float importTime;
    }
}
