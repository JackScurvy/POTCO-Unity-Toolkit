using System;
using System.Collections.Generic;
using POTCO.Inventory;
using UnityEngine;

namespace POTCO.Combat
{
    public enum PotcoWeaponCategory
    {
        Unknown = 0,
        Combat = 1,
        Firearm = 2,
        Grenade = 3,
        Voodoo = 4,
        Staff = 5,
        Melee = 6,
        Throwing = 7,
        Consumable = 7,
        Sailing = 8,
        Cannon = 9,
        Fishing = 10,
        DefenseCannon = 11
    }

    public enum PotcoWeaponClass
    {
        Unknown = 0,
        Melee,
        Sword,
        Pistol,
        Gun,
        Bayonet,
        Dagger,
        Grenade,
        Wand,
        Doll,
        DualCutlass,
        Foil,
        MonsterMelee,
        Torch,
        PowderKeg,
        FishingRod
    }

    public enum PotcoCombatResult
    {
        Miss = 0,
        Hit = 1,
        Delay = 2,
        OutOfRange = 3,
        NotAvailable = 4,
        NotRecharged = 5,
        AgainstPirateCode = 6,
        Parry = 7,
        Dodge = 8,
        Resist = 9,
        MistimedMiss = 10,
        MistimedHit = 11,
        Blocked = 12,
        Reflected = 13,
        Protect = 14
    }

    public enum PotcoClickComboInputState
    {
        Start = 0,
        Active = 1,
        Expired = 2
    }

    public sealed class PotcoWeaponAnimationSet
    {
        public PotcoWeaponAnimationSet(string walk, string run, string walkBack, string neutral, string strafeLeft = "", string strafeRight = "")
        {
            Walk = walk ?? string.Empty;
            Run = run ?? string.Empty;
            WalkBack = walkBack ?? string.Empty;
            Neutral = neutral ?? string.Empty;
            StrafeLeft = strafeLeft ?? string.Empty;
            StrafeRight = strafeRight ?? string.Empty;
        }

        public string Walk { get; }
        public string Run { get; }
        public string WalkBack { get; }
        public string Neutral { get; }
        public string StrafeLeft { get; }
        public string StrafeRight { get; }
    }

    public sealed class PotcoWeaponSkill
    {
        public PotcoWeaponSkill(
            int skillId,
            string name,
            string iconName,
            int skillType,
            int skillTrack,
            float rechargeSeconds,
            float range,
            bool needTarget,
            int attackClass,
            float volley,
            float selfHealth,
            float targetHealth,
            float accuracy,
            float areaRadius,
            float projectilePower,
            int numHits,
            int effectFlag,
            float durationSeconds,
            bool selfUse,
            string animationName)
        {
            SkillId = skillId;
            Name = name ?? string.Empty;
            IconName = string.IsNullOrEmpty(iconName) ? "base" : iconName;
            SkillType = skillType;
            SkillTrack = skillTrack;
            RechargeSeconds = Math.Max(0f, rechargeSeconds);
            Range = range;
            NeedTarget = needTarget;
            AttackClass = attackClass;
            Volley = volley;
            SelfHealth = selfHealth;
            TargetHealth = targetHealth;
            Accuracy = Math.Max(0f, Math.Min(100f, accuracy));
            AreaRadius = Math.Max(0f, areaRadius);
            ProjectilePower = projectilePower;
            NumHits = Math.Max(1, numHits);
            EffectFlag = effectFlag;
            EffectName = PotcoWeaponEffectConstants.GetEffectName(effectFlag);
            DurationSeconds = Math.Max(0f, durationSeconds);
            SelfUse = selfUse;
            AnimationName = animationName ?? string.Empty;
        }

        public int SkillId { get; }
        public string Name { get; }
        public string IconName { get; }
        public int SkillType { get; }
        public int SkillTrack { get; }
        public float RechargeSeconds { get; }
        public float Range { get; }
        public bool NeedTarget { get; }
        public int AttackClass { get; }
        public float Volley { get; }
        public float SelfHealth { get; }
        public float TargetHealth { get; }
        public float Accuracy { get; }
        public float AreaRadius { get; }
        public float ProjectilePower { get; }
        public int NumHits { get; }
        public int EffectFlag { get; }
        public string EffectName { get; }
        public float DurationSeconds { get; }
        public bool SelfUse { get; }
        public string AnimationName { get; }
    }

    public static class PotcoWeaponEffectConstants
    {
        public const int C_FLAMING = 2;
        public const int C_ON_FIRE = 3;
        public const int C_WOUND = 4;
        public const int C_POISON = 5;
        public const int C_STUN = 6;
        public const int C_SLOW = 7;
        public const int C_HOLD = 8;
        public const int C_BLIND = 9;
        public const int C_TAUNT = 10;
        public const int C_MINE = 11;
        public const int C_CURSE = 12;
        public const int C_HASTEN = 13;
        public const int C_WEAKEN = 14;
        public const int C_BUFF_BREAK = 15;
        public const int C_UNDEAD_KILLER = 16;
        public const int C_MONSTER_KILLER = 17;
        public const int C_ATTUNE = 18;
        public const int C_DIRT = 19;
        public const int C_REGEN = 20;
        public const int C_ACID = 21;
        public const int C_SOULTAP = 22;
        public const int C_LIFEDRAIN = 23;
        public const int C_MANADRAIN = 24;
        public const int C_SPAWN = 25;
        public const int C_PAIN = 26;
        public const int C_UNSTUN = 27;
        public const int C_SHIPHEAL = 28;
        public const int C_VOODOO_STUN = 29;
        public const int C_INTERRUPTED = 30;
        public const int C_VOODOO_STUN_LOCK = 31;
        public const int C_VOODOO_HEX_STUN = 32;
        public const int C_COMBO = 33;
        public const int C_FURY = 34;
        public const int C_MISSILE_SHIELD = 35;
        public const int C_MAGIC_SHIELD = 36;
        public const int C_MELEE_SHIELD = 37;
        public const int C_SUMMON_UNDEAD = 38;
        public const int C_CORRUPTION = 39;
        public const int C_UNKNOWN_EFFECT = 40;
        public const int C_CANNON_DAMAGE_LVL1 = 41;
        public const int C_CANNON_DAMAGE_LVL2 = 42;
        public const int C_CANNON_DAMAGE_LVL3 = 43;
        public const int C_PISTOL_DAMAGE_LVL1 = 44;
        public const int C_PISTOL_DAMAGE_LVL2 = 45;
        public const int C_PISTOL_DAMAGE_LVL3 = 46;
        public const int C_CUTLASS_DAMAGE_LVL1 = 47;
        public const int C_CUTLASS_DAMAGE_LVL2 = 48;
        public const int C_CUTLASS_DAMAGE_LVL3 = 49;
        public const int C_DOLL_DAMAGE_LVL1 = 50;
        public const int C_DOLL_DAMAGE_LVL2 = 51;
        public const int C_DOLL_DAMAGE_LVL3 = 52;
        public const int C_HASTEN_LVL1 = 53;
        public const int C_HASTEN_LVL2 = 54;
        public const int C_HASTEN_LVL3 = 55;
        public const int C_REP_BONUS_LVL1 = 56;
        public const int C_REP_BONUS_LVL2 = 57;
        public const int C_GOLD_BONUS_LVL1 = 58;
        public const int C_GOLD_BONUS_LVL2 = 59;
        public const int C_INVISIBILITY_LVL1 = 60;
        public const int C_INVISIBILITY_LVL2 = 61;
        public const int C_REGEN_LVL1 = 62;
        public const int C_REGEN_LVL2 = 63;
        public const int C_REGEN_LVL3 = 64;
        public const int C_REGEN_LVL4 = 65;
        public const int C_BURP = 66;
        public const int C_FART = 67;
        public const int C_VOMIT = 68;
        public const int C_HEAD_GROW = 69;
        public const int C_CRAZY_SKIN_COLOR = 70;
        public const int C_SIZE_REDUCE = 71;
        public const int C_SIZE_INCREASE = 72;
        public const int C_HEAD_FIRE = 73;
        public const int C_SCORPION_TRANSFORM = 74;
        public const int C_ALLIGATOR_TRANSFORM = 75;
        public const int C_CRAB_TRANSFORM = 76;
        public const int C_ACCURACY_BONUS_LVL1 = 77;
        public const int C_ACCURACY_BONUS_LVL2 = 78;
        public const int C_ACCURACY_BONUS_LVL3 = 79;
        public const int C_REMOVE_GROGGY = 80;
        public const int C_CANNON_DEFENSE_FIRE = 81;
        public const int C_CANNON_DEFENSE_SMOKE = 82;
        public const int C_CANNON_DEFENSE_ICE = 83;
        public const int C_REP_BONUS_LVL3 = 84;
        public const int C_FART_LVL2 = 85;
        public const int C_STAFF_ENCHANT_LVL1 = 86;
        public const int C_STAFF_ENCHANT_LVL2 = 87;
        public const int C_SUMMON_CHICKEN = 88;
        public const int C_REP_BONUS_LVLCOMP = 89;
        public const int C_SUMMON_MONKEY = 90;
        public const int C_SUMMON_WASP = 91;
        public const int C_SUMMON_DOG = 92;
        public const int C_FULLSAIL = 100;
        public const int C_COMEABOUT = 101;
        public const int C_OPENFIRE = 102;
        public const int C_RAM = 103;
        public const int C_TAKECOVER = 104;
        public const int C_RECHARGE = 105;
        public const int C_CHAINSHOT = 106;
        public const int C_GRAPESHOT = 107;
        public const int C_WRECKHULL = 108;
        public const int C_WRECKMASTS = 109;
        public const int C_SINKHER = 110;
        public const int C_INCOMING = 111;
        public const int C_FIX_IT_NOW = 112;
        public const int C_SPIRIT = 150;
        public const int C_BANE = 151;
        public const int C_MOJO = 152;
        public const int C_WARDING = 153;
        public const int C_NATURE = 154;
        public const int C_DARK = 155;
        public const int C_KNOCKDOWN = 156;
        public const int C_QUICKLOAD = 157;
        public const int C_DARK_CURSE = 158;
        public const int C_MASTERS_RIPOSTE = 159;
        public const int C_NOT_IN_FACE = 160;
        public const int C_MONKEY_PANIC = 161;
        public const int C_TOXIN = 162;
        public const int C_ON_CURSED_FIRE = 163;
        public const int C_FREEZE = 164;
        public const int C_VOODOO_REFLECT = 165;
        public const int C_FULLSPLIT = 166;
        public const int C_RED_FURY = 167;
        public const int C_GHOST_FORM = 168;
        public const int C_SUMMON_GHOST = 201;

        private static readonly Dictionary<int, string> EffectNames = new Dictionary<int, string>
        {
            { C_FLAMING, nameof(C_FLAMING) },
            { C_ON_FIRE, nameof(C_ON_FIRE) },
            { C_WOUND, nameof(C_WOUND) },
            { C_POISON, nameof(C_POISON) },
            { C_STUN, nameof(C_STUN) },
            { C_SLOW, nameof(C_SLOW) },
            { C_HOLD, nameof(C_HOLD) },
            { C_BLIND, nameof(C_BLIND) },
            { C_TAUNT, nameof(C_TAUNT) },
            { C_MINE, nameof(C_MINE) },
            { C_CURSE, nameof(C_CURSE) },
            { C_HASTEN, nameof(C_HASTEN) },
            { C_WEAKEN, nameof(C_WEAKEN) },
            { C_BUFF_BREAK, nameof(C_BUFF_BREAK) },
            { C_UNDEAD_KILLER, nameof(C_UNDEAD_KILLER) },
            { C_MONSTER_KILLER, nameof(C_MONSTER_KILLER) },
            { C_ATTUNE, nameof(C_ATTUNE) },
            { C_DIRT, nameof(C_DIRT) },
            { C_REGEN, nameof(C_REGEN) },
            { C_ACID, nameof(C_ACID) },
            { C_SOULTAP, nameof(C_SOULTAP) },
            { C_LIFEDRAIN, nameof(C_LIFEDRAIN) },
            { C_MANADRAIN, nameof(C_MANADRAIN) },
            { C_SPAWN, nameof(C_SPAWN) },
            { C_PAIN, nameof(C_PAIN) },
            { C_UNSTUN, nameof(C_UNSTUN) },
            { C_SHIPHEAL, nameof(C_SHIPHEAL) },
            { C_VOODOO_STUN, nameof(C_VOODOO_STUN) },
            { C_INTERRUPTED, nameof(C_INTERRUPTED) },
            { C_VOODOO_STUN_LOCK, nameof(C_VOODOO_STUN_LOCK) },
            { C_VOODOO_HEX_STUN, nameof(C_VOODOO_HEX_STUN) },
            { C_COMBO, nameof(C_COMBO) },
            { C_FURY, nameof(C_FURY) },
            { C_MISSILE_SHIELD, nameof(C_MISSILE_SHIELD) },
            { C_MAGIC_SHIELD, nameof(C_MAGIC_SHIELD) },
            { C_MELEE_SHIELD, nameof(C_MELEE_SHIELD) },
            { C_SUMMON_UNDEAD, nameof(C_SUMMON_UNDEAD) },
            { C_CORRUPTION, nameof(C_CORRUPTION) },
            { C_UNKNOWN_EFFECT, nameof(C_UNKNOWN_EFFECT) },
            { C_CANNON_DAMAGE_LVL1, nameof(C_CANNON_DAMAGE_LVL1) },
            { C_CANNON_DAMAGE_LVL2, nameof(C_CANNON_DAMAGE_LVL2) },
            { C_CANNON_DAMAGE_LVL3, nameof(C_CANNON_DAMAGE_LVL3) },
            { C_PISTOL_DAMAGE_LVL1, nameof(C_PISTOL_DAMAGE_LVL1) },
            { C_PISTOL_DAMAGE_LVL2, nameof(C_PISTOL_DAMAGE_LVL2) },
            { C_PISTOL_DAMAGE_LVL3, nameof(C_PISTOL_DAMAGE_LVL3) },
            { C_CUTLASS_DAMAGE_LVL1, nameof(C_CUTLASS_DAMAGE_LVL1) },
            { C_CUTLASS_DAMAGE_LVL2, nameof(C_CUTLASS_DAMAGE_LVL2) },
            { C_CUTLASS_DAMAGE_LVL3, nameof(C_CUTLASS_DAMAGE_LVL3) },
            { C_DOLL_DAMAGE_LVL1, nameof(C_DOLL_DAMAGE_LVL1) },
            { C_DOLL_DAMAGE_LVL2, nameof(C_DOLL_DAMAGE_LVL2) },
            { C_DOLL_DAMAGE_LVL3, nameof(C_DOLL_DAMAGE_LVL3) },
            { C_HASTEN_LVL1, nameof(C_HASTEN_LVL1) },
            { C_HASTEN_LVL2, nameof(C_HASTEN_LVL2) },
            { C_HASTEN_LVL3, nameof(C_HASTEN_LVL3) },
            { C_REP_BONUS_LVL1, nameof(C_REP_BONUS_LVL1) },
            { C_REP_BONUS_LVL2, nameof(C_REP_BONUS_LVL2) },
            { C_GOLD_BONUS_LVL1, nameof(C_GOLD_BONUS_LVL1) },
            { C_GOLD_BONUS_LVL2, nameof(C_GOLD_BONUS_LVL2) },
            { C_INVISIBILITY_LVL1, nameof(C_INVISIBILITY_LVL1) },
            { C_INVISIBILITY_LVL2, nameof(C_INVISIBILITY_LVL2) },
            { C_REGEN_LVL1, nameof(C_REGEN_LVL1) },
            { C_REGEN_LVL2, nameof(C_REGEN_LVL2) },
            { C_REGEN_LVL3, nameof(C_REGEN_LVL3) },
            { C_REGEN_LVL4, nameof(C_REGEN_LVL4) },
            { C_BURP, nameof(C_BURP) },
            { C_FART, nameof(C_FART) },
            { C_VOMIT, nameof(C_VOMIT) },
            { C_HEAD_GROW, nameof(C_HEAD_GROW) },
            { C_CRAZY_SKIN_COLOR, nameof(C_CRAZY_SKIN_COLOR) },
            { C_SIZE_REDUCE, nameof(C_SIZE_REDUCE) },
            { C_SIZE_INCREASE, nameof(C_SIZE_INCREASE) },
            { C_HEAD_FIRE, nameof(C_HEAD_FIRE) },
            { C_SCORPION_TRANSFORM, nameof(C_SCORPION_TRANSFORM) },
            { C_ALLIGATOR_TRANSFORM, nameof(C_ALLIGATOR_TRANSFORM) },
            { C_CRAB_TRANSFORM, nameof(C_CRAB_TRANSFORM) },
            { C_ACCURACY_BONUS_LVL1, nameof(C_ACCURACY_BONUS_LVL1) },
            { C_ACCURACY_BONUS_LVL2, nameof(C_ACCURACY_BONUS_LVL2) },
            { C_ACCURACY_BONUS_LVL3, nameof(C_ACCURACY_BONUS_LVL3) },
            { C_REMOVE_GROGGY, nameof(C_REMOVE_GROGGY) },
            { C_CANNON_DEFENSE_FIRE, nameof(C_CANNON_DEFENSE_FIRE) },
            { C_CANNON_DEFENSE_SMOKE, nameof(C_CANNON_DEFENSE_SMOKE) },
            { C_CANNON_DEFENSE_ICE, nameof(C_CANNON_DEFENSE_ICE) },
            { C_REP_BONUS_LVL3, nameof(C_REP_BONUS_LVL3) },
            { C_FART_LVL2, nameof(C_FART_LVL2) },
            { C_STAFF_ENCHANT_LVL1, nameof(C_STAFF_ENCHANT_LVL1) },
            { C_STAFF_ENCHANT_LVL2, nameof(C_STAFF_ENCHANT_LVL2) },
            { C_SUMMON_CHICKEN, nameof(C_SUMMON_CHICKEN) },
            { C_REP_BONUS_LVLCOMP, nameof(C_REP_BONUS_LVLCOMP) },
            { C_SUMMON_MONKEY, nameof(C_SUMMON_MONKEY) },
            { C_SUMMON_WASP, nameof(C_SUMMON_WASP) },
            { C_SUMMON_DOG, nameof(C_SUMMON_DOG) },
            { C_FULLSAIL, nameof(C_FULLSAIL) },
            { C_COMEABOUT, nameof(C_COMEABOUT) },
            { C_OPENFIRE, nameof(C_OPENFIRE) },
            { C_RAM, nameof(C_RAM) },
            { C_TAKECOVER, nameof(C_TAKECOVER) },
            { C_RECHARGE, nameof(C_RECHARGE) },
            { C_CHAINSHOT, nameof(C_CHAINSHOT) },
            { C_GRAPESHOT, nameof(C_GRAPESHOT) },
            { C_WRECKHULL, nameof(C_WRECKHULL) },
            { C_WRECKMASTS, nameof(C_WRECKMASTS) },
            { C_SINKHER, nameof(C_SINKHER) },
            { C_INCOMING, nameof(C_INCOMING) },
            { C_FIX_IT_NOW, nameof(C_FIX_IT_NOW) },
            { C_SPIRIT, nameof(C_SPIRIT) },
            { C_BANE, nameof(C_BANE) },
            { C_MOJO, nameof(C_MOJO) },
            { C_WARDING, nameof(C_WARDING) },
            { C_NATURE, nameof(C_NATURE) },
            { C_DARK, nameof(C_DARK) },
            { C_KNOCKDOWN, nameof(C_KNOCKDOWN) },
            { C_QUICKLOAD, nameof(C_QUICKLOAD) },
            { C_DARK_CURSE, nameof(C_DARK_CURSE) },
            { C_MASTERS_RIPOSTE, nameof(C_MASTERS_RIPOSTE) },
            { C_NOT_IN_FACE, nameof(C_NOT_IN_FACE) },
            { C_MONKEY_PANIC, nameof(C_MONKEY_PANIC) },
            { C_TOXIN, nameof(C_TOXIN) },
            { C_ON_CURSED_FIRE, nameof(C_ON_CURSED_FIRE) },
            { C_FREEZE, nameof(C_FREEZE) },
            { C_VOODOO_REFLECT, nameof(C_VOODOO_REFLECT) },
            { C_FULLSPLIT, nameof(C_FULLSPLIT) },
            { C_RED_FURY, nameof(C_RED_FURY) },
            { C_GHOST_FORM, nameof(C_GHOST_FORM) },
            { C_SUMMON_GHOST, nameof(C_SUMMON_GHOST) }
        };

        public static string GetEffectName(int effectId)
        {
            if (effectId <= 0)
                return string.Empty;

            return EffectNames.TryGetValue(effectId, out string name) ? name : $"C_EFFECT_{effectId}";
        }
    }

    public sealed class PotcoWeaponStatusEffect
    {
        public PotcoWeaponStatusEffect(
            int effectId,
            float durationSeconds,
            float startTime,
            GameObject attacker,
            PotcoWeaponDefinition weapon,
            PotcoWeaponSkill skill)
        {
            EffectId = effectId;
            EffectName = PotcoWeaponEffectConstants.GetEffectName(effectId);
            DurationSeconds = Math.Max(0f, durationSeconds);
            StartTime = Math.Max(0f, startTime);
            Attacker = attacker;
            Weapon = weapon;
            Skill = skill;
        }

        public int EffectId { get; }
        public string EffectName { get; }
        public float DurationSeconds { get; }
        public float StartTime { get; }
        public float ExpiresAt => StartTime + DurationSeconds;
        public GameObject Attacker { get; }
        public PotcoWeaponDefinition Weapon { get; }
        public PotcoWeaponSkill Skill { get; }

        public bool IsExpired(float now)
        {
            return DurationSeconds <= 0f || now >= ExpiresAt;
        }
    }

    public sealed class PotcoWeaponDefinition
    {
        public PotcoWeaponDefinition(
            PotcoItemDefinition item,
            PotcoWeaponCategory category,
            PotcoWeaponClass weaponClass,
            int referenceKey,
            string drawAnimation,
            string putAwayAnimation,
            PotcoWeaponAnimationSet animationSet,
            PotcoWeaponAttachmentPose attachmentPose,
            IReadOnlyList<PotcoWeaponSkill> skills,
            IReadOnlyList<PotcoWeaponSkill> clickSkills,
            IReadOnlyList<PotcoWeaponSkill> allSkills,
            IReadOnlyList<string> comboAnimations)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            ItemId = item.ItemId;
            DisplayName = item.EffectiveDisplayName;
            ModelName = item.ModelName ?? string.Empty;
            IconName = item.IconName ?? string.Empty;
            Category = category;
            Class = weaponClass;
            ReferenceKey = referenceKey;
            DrawAnimation = drawAnimation ?? string.Empty;
            PutAwayAnimation = putAwayAnimation ?? string.Empty;
            AnimationSet = animationSet ?? new PotcoWeaponAnimationSet("walk", "run", "walk", "idle");
            AttachmentPose = attachmentPose ?? PotcoWeaponAttachmentPose.Identity;
            Skills = skills ?? Array.Empty<PotcoWeaponSkill>();
            ClickSkills = clickSkills ?? Array.Empty<PotcoWeaponSkill>();
            AllSkills = allSkills ?? Array.Empty<PotcoWeaponSkill>();
            ComboAnimations = comboAnimations ?? Array.Empty<string>();
            PrimarySkillId = ClickSkills.Count > 0 ? ClickSkills[0].SkillId : (Skills.Count > 0 ? Skills[0].SkillId : 0);
            SecondarySkillId = item.SpecialAttack != 0
                ? item.SpecialAttack
                : (ClickSkills.Count > 1 ? ClickSkills[1].SkillId : (Skills.Count > 0 ? Skills[0].SkillId : PrimarySkillId));
        }

        public PotcoItemDefinition Item { get; }
        public int ItemId { get; }
        public string DisplayName { get; }
        public string ModelName { get; }
        public string IconName { get; }
        public PotcoWeaponCategory Category { get; }
        public PotcoWeaponClass Class { get; }
        public int ReferenceKey { get; }
        public string DrawAnimation { get; }
        public string PutAwayAnimation { get; }
        public PotcoWeaponAnimationSet AnimationSet { get; }
        public PotcoWeaponAttachmentPose AttachmentPose { get; }
        public IReadOnlyList<PotcoWeaponSkill> AllSkills { get; }
        public IReadOnlyList<PotcoWeaponSkill> Skills { get; }
        public IReadOnlyList<PotcoWeaponSkill> ClickSkills { get; }
        public IReadOnlyList<string> ComboAnimations { get; }
        public int PrimarySkillId { get; }
        public int SecondarySkillId { get; }

        public PotcoWeaponSkill GetClickSkill(int clickIndex)
        {
            if (ClickSkills.Count == 0)
                return null;

            int index = Math.Abs(clickIndex) % ClickSkills.Count;
            return ClickSkills[index];
        }

        public string GetComboAnimation(int comboIndex)
        {
            if (ComboAnimations.Count == 0)
                return string.Empty;

            int index = Math.Abs(comboIndex) % ComboAnimations.Count;
            return ComboAnimations[index];
        }
    }

    public sealed class PotcoWeaponAttachmentPose
    {
        public static readonly PotcoWeaponAttachmentPose Identity = new PotcoWeaponAttachmentPose(Vector3.zero, Vector3.zero, Vector3.one);

        public PotcoWeaponAttachmentPose(Vector3 localPosition, Vector3 localEulerAngles, Vector3 localScale)
        {
            LocalPosition = localPosition;
            LocalEulerAngles = localEulerAngles;
            LocalScale = localScale;
        }

        public Vector3 LocalPosition { get; }
        public Vector3 LocalEulerAngles { get; }
        public Vector3 LocalScale { get; }
    }

    public sealed class PotcoWeaponUseResult
    {
        private PotcoWeaponUseResult(
            bool success,
            string message,
            PotcoWeaponSkill skill,
            PotcoCombatResult combatResult,
            PotcoCombatTarget target,
            float damageApplied)
        {
            Success = success;
            Message = message ?? string.Empty;
            Skill = skill;
            CombatResult = combatResult;
            Target = target;
            DamageApplied = Math.Max(0f, damageApplied);
        }

        public bool Success { get; }
        public string Message { get; }
        public PotcoWeaponSkill Skill { get; }
        public PotcoCombatResult CombatResult { get; }
        public PotcoCombatTarget Target { get; }
        public float DamageApplied { get; }

        public static PotcoWeaponUseResult Ok(
            string message = "",
            PotcoWeaponSkill skill = null,
            PotcoCombatResult combatResult = PotcoCombatResult.Hit,
            PotcoCombatTarget target = null,
            float damageApplied = 0f)
        {
            return new PotcoWeaponUseResult(true, message, skill, combatResult, target, damageApplied);
        }

        public static PotcoWeaponUseResult Fail(
            string message,
            PotcoCombatResult combatResult = PotcoCombatResult.NotAvailable,
            PotcoWeaponSkill skill = null,
            PotcoCombatTarget target = null)
        {
            return new PotcoWeaponUseResult(false, message, skill, combatResult, target, 0f);
        }
    }
}
