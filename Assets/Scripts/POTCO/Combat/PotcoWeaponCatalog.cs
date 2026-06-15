using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using POTCO.Inventory;
using POTCO.ItemCards;
using UnityEngine;

namespace POTCO.Combat
{
    public sealed class PotcoWeaponCatalog
    {
        private const int ItemTypeSword = 1;
        private const int ItemTypeGun = 2;
        private const int ItemTypeDoll = 3;
        private const int ItemTypeDagger = 4;
        private const int ItemTypeGrenade = 5;
        private const int ItemTypeStaff = 6;
        private const int ItemTypeAxe = 9;
        private const int ItemTypeFencing = 10;
        private const int ItemTypeMonster = 13;
        private const int ItemTypeFishing = 14;
        private const int ItemTypeQuestProp = 15;

        private const int ItemSubtypePistol = 6;
        private const int ItemSubtypeRepeater = 7;
        private const int ItemSubtypeMusket = 8;
        private const int ItemSubtypeBlunderbuss = 9;
        private const int ItemSubtypeBayonet = 10;
        private const int ItemSubtypeRapier = 24;
        private const int ItemSubtypeEpee = 25;
        private const int ItemSubtypeDualCutlass = 32;
        private const int ItemSubtypeMonster = 33;
        private const int ItemSubtypeQuestPropTorch = 35;
        private const int ItemSubtypeQuestPropPowderKeg = 36;

        private const int RadialSkillTrack = 2;

        private const int PistolShootSkill = 12200;
        private const int PistolLeadShotSkill = 12201;
        private const int PistolBaneShotSkill = 12203;
        private const int PistolSharpShooterSkill = 12207;
        private const int PistolEagleEyeSkill = 12209;
        private const int PistolTakeAimSkill = 12210;
        private const int MusketShootSkill = 12300;
        private const int MusketTakeAimSkill = 12301;
        private const int MusketDeadeyeSkill = 12302;
        private const int MusketEagleEyeSkill = 12303;
        private const int MusketCrackShotSkill = 12304;
        private const int MusketMarksmanSkill = 12305;
        private const int MusketLeadShotSkill = 12306;
        private const int MusketScatterShotSkill = 12307;
        private const int MusketCursedShotSkill = 12308;
        private const int MusketCoalfireShotSkill = 12309;
        private const int MusketHeavySlugSkill = 12310;
        private const int MusketExploderShotSkill = 12311;
        private const int BayonetShootSkill = 13100;
        private const int BayonetStabSkill = 13101;
        private const int BayonetRushSkill = 13102;
        private const int BayonetBashSkill = 13103;
        private const int EnemyPistolScatterShotSkill = 2340;
        private const int EnemyPistolDeadeyeSkill = 2341;
        private const int EnemyPistolPointBlankSkill = 2346;
        private const int EnemyPistolHotshotSkill = 2347;
        private const int EnemyPistolBreakshotSkill = 2345;
        private const int EnemyBayonetShootSkill = 2313;
        private const int EnemyBayonetPlayerStabSkill = 2317;
        private const int EnemyBayonetPlayerRushSkill = 2315;
        private const int EnemyBayonetPlayerBashSkill = 2314;

        private readonly Dictionary<int, PotcoWeaponDefinition> weapons = new Dictionary<int, PotcoWeaponDefinition>();
        private readonly Dictionary<int, PotcoWeaponSkill> skills = new Dictionary<int, PotcoWeaponSkill>();

        public IReadOnlyDictionary<int, PotcoWeaponDefinition> Weapons => weapons;
        public IReadOnlyDictionary<int, PotcoWeaponSkill> Skills => skills;

        public static PotcoWeaponCatalog LoadFromAssets()
        {
            return LoadFromAssetsPath(UnityEngine.Application.dataPath, null);
        }

        public static PotcoWeaponCatalog LoadFromAssetsPath(string assetsPath, PotcoRuntimeItemCatalog itemCatalog)
        {
            if (string.IsNullOrEmpty(assetsPath))
                throw new ArgumentException("Assets path is required.", nameof(assetsPath));

            itemCatalog = itemCatalog ?? PotcoRuntimeItemCatalog.LoadFromAssetsPath(assetsPath);
            PotcoSourceIndex sourceIndex = PotcoSourceIndex.LoadFromAssetsPath(assetsPath);

            var catalog = new PotcoWeaponCatalog();
            Dictionary<int, SkillRow> skillRows = LoadSkillRows(Path.Combine(assetsPath, "Editor", "POTCO_Source", "battle", "SkillInfo.py"));
            catalog.LoadSkills(sourceIndex, skillRows);
            catalog.LoadWeapons(itemCatalog);
            return catalog;
        }

        public bool TryGetWeapon(int itemId, out PotcoWeaponDefinition definition)
        {
            return weapons.TryGetValue(itemId, out definition);
        }

        public PotcoWeaponDefinition GetWeaponOrNull(int itemId)
        {
            weapons.TryGetValue(itemId, out PotcoWeaponDefinition definition);
            return definition;
        }

        public bool TryGetSkill(int skillId, out PotcoWeaponSkill skill)
        {
            return skills.TryGetValue(skillId, out skill);
        }

        private void LoadSkills(PotcoSourceIndex sourceIndex, Dictionary<int, SkillRow> skillRows)
        {
            foreach (KeyValuePair<int, SkillInfoRow> entry in sourceIndex.Skills)
            {
                int skillId = entry.Key;
                SkillInfoRow sourceSkill = entry.Value;
                skillRows.TryGetValue(skillId, out SkillRow row);

                skills[skillId] = CreateSkill(sourceIndex, skillId, sourceSkill, row);
            }

            AddReferenceFallbackSkill(sourceIndex, skillRows, MusketShootSkill, PistolShootSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, MusketTakeAimSkill, PistolTakeAimSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, MusketDeadeyeSkill, EnemyPistolDeadeyeSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, MusketEagleEyeSkill, PistolEagleEyeSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, MusketCrackShotSkill, EnemyPistolDeadeyeSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, MusketMarksmanSkill, PistolSharpShooterSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, MusketLeadShotSkill, PistolLeadShotSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, MusketScatterShotSkill, EnemyPistolScatterShotSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, MusketCursedShotSkill, PistolBaneShotSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, MusketCoalfireShotSkill, EnemyPistolHotshotSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, MusketHeavySlugSkill, EnemyPistolPointBlankSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, MusketExploderShotSkill, EnemyPistolBreakshotSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, BayonetShootSkill, EnemyBayonetShootSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, BayonetStabSkill, EnemyBayonetPlayerStabSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, BayonetRushSkill, EnemyBayonetPlayerRushSkill);
            AddReferenceFallbackSkill(sourceIndex, skillRows, BayonetBashSkill, EnemyBayonetPlayerBashSkill);
        }

        private void AddReferenceFallbackSkill(PotcoSourceIndex sourceIndex, Dictionary<int, SkillRow> skillRows, int playerSkillId, int referenceSkillId)
        {
            if (skills.ContainsKey(playerSkillId))
                return;

            skillRows.TryGetValue(referenceSkillId, out SkillRow referenceRow);
            sourceIndex.Skills.TryGetValue(referenceSkillId, out SkillInfoRow referenceSourceSkill);
            skills[playerSkillId] = CreateSkill(sourceIndex, playerSkillId, referenceSourceSkill, referenceRow);
        }

        private static PotcoWeaponSkill CreateSkill(PotcoSourceIndex sourceIndex, int skillId, SkillInfoRow sourceSkill, SkillRow row)
        {
            string name = sourceIndex.ItemNames.TryGetValue(skillId, out string resolvedName) ? resolvedName : $"Skill {skillId}";
            string iconName = sourceSkill != null ? sourceSkill.IconName : string.Empty;
            if (string.IsNullOrEmpty(iconName) && row != null)
                iconName = row.GetString("SKILL_ICON_INDEX", "base");

            int track = sourceSkill != null && sourceSkill.Track != 0 ? sourceSkill.Track : row?.GetInt("SKILL_TRACK_INDEX", -1) ?? -1;
            if (track == 0)
                track = -1;

            return new PotcoWeaponSkill(
                skillId,
                name,
                iconName,
                row?.GetInt("SKILL_TYPE") ?? 0,
                track,
                row?.GetFloat("RECHARGE_INDEX") ?? 0f,
                ResolveReferenceRange(row?.GetFloat("RANGE_INDEX") ?? 0f),
                (row?.GetInt("NEED_TARGET_INDEX") ?? 0) != 0,
                row?.GetInt("ATTACK_CLASS_INDEX") ?? 0,
                row?.GetFloat("VOLLEY_INDEX") ?? 0f,
                row?.GetFloat("SELF_HP_INDEX") ?? 0f,
                row?.GetFloat("TARGET_HP_INDEX") ?? 0f,
                row?.GetFloat("ACCURACY_INDEX") ?? 0f,
                row?.GetFloat("AREA_EFFECT_INDEX") ?? 0f,
                row?.GetFloat("PROJECTILE_POWER_INDEX") ?? 0f,
                row?.GetInt("NUM_HIT_INDEX") ?? 1,
                row?.GetInt("EFFECT_FLAG_INDEX") ?? 0,
                row?.GetFloat("DURATION_INDEX") ?? 0f,
                (row?.GetInt("SELF_USE_INDEX") ?? 0) != 0,
                ResolveReferenceSkillAnimation(skillId));
        }

        private static string ResolveReferenceSkillAnimation(int skillId)
        {
            switch (skillId)
            {
                case 12000:
                case 12001:
                case 12005:
                    return "boxing_punch";
                case 12002:
                case 12003:
                    return "boxing_kick";
                case 12004:
                case 12006:
                    return "boxing_haymaker";
                case 12100:
                case 12101:
                case 12102:
                case 12103:
                case 12104:
                    return "cutlass_combo";
                case 12107:
                    return "cutlass_sweep";
                case 12108:
                    return "cutlass_headbutt";
                case 12109:
                    return "cutlass_taunt";
                case 12110:
                    return "cutlass_bladestorm";
                case 12200:
                case 12201:
                case 12202:
                case 12203:
                case 12204:
                case 12205:
                case 12206:
                case 12210:
                    return "gun_fire";
                case 12300:
                case 12302:
                case 12304:
                case 12305:
                case 12306:
                case 12307:
                case 12308:
                case 12309:
                case 12310:
                case 12311:
                    return "rifle_fight_shoot_hip";
                case 12301:
                    return "rifle_fight_shoot_high";
                case 12400:
                case 12401:
                case 12402:
                case 12403:
                    return "dagger_combo";
                case 12406:
                case 12407:
                    return "knife_throw";
                case 12408:
                    return "dagger_throw_sand";
                case 12409:
                    return "dagger_asp";
                case 12410:
                    return "dagger_vipers_nest";
                case 12500:
                case 12501:
                case 12502:
                case 12503:
                case 12504:
                case 12505:
                    return "bomb_throw";
                case 12506:
                    return "bomb_charge_throw";
                case 12600:
                    return "voodoo_tune";
                case 12601:
                    return "voodoo_doll_poke";
                case 12602:
                case 12603:
                case 12604:
                case 12605:
                case 12606:
                case 12607:
                case 12608:
                    return "voodoo_swarm";
                case 12700:
                case 12701:
                case 12702:
                case 12703:
                case 12704:
                case 12705:
                case 12706:
                    return "wand_cast_fire";
                case 13100:
                    return "rifle_fight_shoot_hip";
                case 13101:
                    return "bayonet_attackA";
                case 13102:
                    return "bayonet_attackC";
                case 13103:
                    return "bayonet_attackB";
                default:
                    return string.Empty;
            }
        }

        private void LoadWeapons(PotcoRuntimeItemCatalog itemCatalog)
        {
            foreach (PotcoItemDefinition item in itemCatalog.Items.Values.OrderBy(item => item.ItemId))
            {
                if (item.Category != PotcoInventoryCategory.Weapon)
                    continue;

                PotcoWeaponClass weaponClass = ResolveWeaponClass(item);
                if (weaponClass == PotcoWeaponClass.Unknown)
                    continue;

                List<PotcoWeaponSkill> allSkills = ResolveAllSkills(item, weaponClass);
                List<PotcoWeaponSkill> clickSkills = ResolveClickSkills(weaponClass, allSkills);
                List<PotcoWeaponSkill> traySkills = ResolveNumberTraySkills(item, allSkills);

                PotcoWeaponDefinition definition = new PotcoWeaponDefinition(
                    item,
                    ResolveWeaponCategory(item, weaponClass),
                    weaponClass,
                    ResolveReferenceKey(weaponClass),
                    ResolveDrawAnimation(weaponClass),
                    ResolvePutAwayAnimation(weaponClass),
                    ResolveAnimationSet(weaponClass),
                    ResolveAttachmentPose(weaponClass),
                    traySkills,
                    clickSkills,
                    allSkills,
                    ResolveComboAnimations(weaponClass));

                weapons[item.ItemId] = definition;
            }
        }

        private List<PotcoWeaponSkill> ResolveAllSkills(PotcoItemDefinition item, PotcoWeaponClass weaponClass)
        {
            var skillIds = new List<int>();
            skillIds.AddRange(DefaultSkillsForClass(weaponClass));
            AddSkillIfPresent(skillIds, item.SpecialAttack);
            AddSkillIfPresent(skillIds, item.SkillBoost1);
            AddSkillIfPresent(skillIds, item.SkillBoost2);
            AddSkillIfPresent(skillIds, item.SkillBoost3);

            return ResolveSkillIds(skillIds);
        }

        private List<PotcoWeaponSkill> ResolveClickSkills(PotcoWeaponClass weaponClass, IReadOnlyList<PotcoWeaponSkill> allSkills)
        {
            HashSet<int> availableSkillIds = new HashSet<int>((allSkills ?? Array.Empty<PotcoWeaponSkill>()).Select(skill => skill.SkillId));
            return ResolveSkillIds(DefaultClickSkillsForClass(weaponClass))
                .Where(skill => availableSkillIds.Count == 0 || availableSkillIds.Contains(skill.SkillId))
                .ToList();
        }

        private List<PotcoWeaponSkill> ResolveNumberTraySkills(PotcoItemDefinition item, IReadOnlyList<PotcoWeaponSkill> allSkills)
        {
            var resolved = new List<PotcoWeaponSkill>();
            foreach (PotcoWeaponSkill skill in allSkills ?? Array.Empty<PotcoWeaponSkill>())
            {
                if (skill.SkillTrack == RadialSkillTrack && !resolved.Any(existing => existing.SkillId == skill.SkillId))
                    resolved.Add(skill);
            }

            if (item != null && item.SpecialAttack > 0 &&
                skills.TryGetValue(item.SpecialAttack, out PotcoWeaponSkill specialSkill) &&
                !resolved.Any(skill => skill.SkillId == specialSkill.SkillId))
            {
                resolved.Add(specialSkill);
            }

            return resolved;
        }

        private List<PotcoWeaponSkill> ResolveSkillIds(IEnumerable<int> skillIds)
        {
            var resolved = new List<PotcoWeaponSkill>();
            foreach (int skillId in skillIds.Distinct())
            {
                if (skills.TryGetValue(skillId, out PotcoWeaponSkill skill))
                    resolved.Add(skill);
            }

            return resolved;
        }

        private static void AddSkillIfPresent(List<int> skillIds, int skillId)
        {
            if (skillId > 0 && !skillIds.Contains(skillId))
                skillIds.Add(skillId);
        }

        private static IEnumerable<int> DefaultSkillsForClass(PotcoWeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case PotcoWeaponClass.Melee:
                    return Range(12000, 12009);
                case PotcoWeaponClass.Sword:
                case PotcoWeaponClass.DualCutlass:
                case PotcoWeaponClass.Foil:
                case PotcoWeaponClass.Torch:
                    return Range(12100, 12110);
                case PotcoWeaponClass.MonsterMelee:
                    return Range(12000, 12009);
                case PotcoWeaponClass.Pistol:
                    return Range(12200, 12211);
                case PotcoWeaponClass.Gun:
                    return Range(12300, 12311);
                case PotcoWeaponClass.Bayonet:
                    return Range(13100, 13103);
                case PotcoWeaponClass.Dagger:
                    return Range(12400, 12410);
                case PotcoWeaponClass.Grenade:
                case PotcoWeaponClass.PowderKeg:
                    return Range(12500, 12510);
                case PotcoWeaponClass.Doll:
                    return Range(12600, 12610);
                case PotcoWeaponClass.Wand:
                    return Range(12700, 12710);
                default:
                    return Array.Empty<int>();
            }
        }

        private static IEnumerable<int> DefaultClickSkillsForClass(PotcoWeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case PotcoWeaponClass.Melee:
                case PotcoWeaponClass.MonsterMelee:
                    return new[] { 12000 };
                case PotcoWeaponClass.Sword:
                case PotcoWeaponClass.DualCutlass:
                case PotcoWeaponClass.Foil:
                case PotcoWeaponClass.Torch:
                    return new[] { 12100, 12101, 12102, 12103, 12104 };
                case PotcoWeaponClass.Pistol:
                    return new[] { PistolShootSkill };
                case PotcoWeaponClass.Gun:
                    return new[] { MusketShootSkill };
                case PotcoWeaponClass.Bayonet:
                    return new[] { BayonetShootSkill, BayonetStabSkill, BayonetRushSkill, BayonetBashSkill };
                case PotcoWeaponClass.Dagger:
                    return new[] { 12400, 12401, 12402, 12403 };
                case PotcoWeaponClass.Grenade:
                case PotcoWeaponClass.PowderKeg:
                    return new[] { 12500 };
                case PotcoWeaponClass.Doll:
                    return new[] { 12600, 12601 };
                case PotcoWeaponClass.Wand:
                    return new[] { 12700 };
                default:
                    return Array.Empty<int>();
            }
        }

        private static IEnumerable<int> Range(int first, int last)
        {
            for (int value = first; value <= last; value++)
                yield return value;
        }

        public static PotcoWeaponClass ResolveWeaponClass(PotcoItemDefinition item)
        {
            if (item == null)
                return PotcoWeaponClass.Unknown;

            switch (item.Subtype)
            {
                case ItemSubtypeDualCutlass:
                    return PotcoWeaponClass.DualCutlass;
                case ItemSubtypeRapier:
                case ItemSubtypeEpee:
                    return PotcoWeaponClass.Foil;
                case ItemSubtypeMonster:
                    return PotcoWeaponClass.MonsterMelee;
                case ItemSubtypeQuestPropTorch:
                    return PotcoWeaponClass.Torch;
                case ItemSubtypeQuestPropPowderKeg:
                    return PotcoWeaponClass.PowderKeg;
            }

            switch (item.ItemType)
            {
                case ItemTypeSword:
                    return PotcoWeaponClass.Sword;
                case ItemTypeGun:
                    if (item.Subtype == ItemSubtypeBayonet)
                        return PotcoWeaponClass.Bayonet;
                    if (item.Subtype == ItemSubtypeMusket || item.Subtype == ItemSubtypeBlunderbuss)
                        return PotcoWeaponClass.Gun;
                    if (item.Subtype == ItemSubtypePistol || item.Subtype == ItemSubtypeRepeater || item.Subtype == 0)
                        return PotcoWeaponClass.Pistol;
                    return PotcoWeaponClass.Pistol;
                case ItemTypeDoll:
                    return PotcoWeaponClass.Doll;
                case ItemTypeDagger:
                    return PotcoWeaponClass.Dagger;
                case ItemTypeGrenade:
                    return PotcoWeaponClass.Grenade;
                case ItemTypeStaff:
                    return PotcoWeaponClass.Wand;
                case ItemTypeAxe:
                    return PotcoWeaponClass.Sword;
                case ItemTypeFencing:
                    return PotcoWeaponClass.Foil;
                case ItemTypeMonster:
                    return PotcoWeaponClass.MonsterMelee;
                case ItemTypeFishing:
                    return PotcoWeaponClass.FishingRod;
                case ItemTypeQuestProp:
                    return PotcoWeaponClass.Melee;
                default:
                    return PotcoWeaponClass.Unknown;
            }
        }

        public static PotcoWeaponCategory ResolveWeaponCategory(PotcoItemDefinition item, PotcoWeaponClass weaponClass)
        {
            if (item == null)
                return PotcoWeaponCategory.Unknown;

            switch (item.ItemType)
            {
                case ItemTypeSword:
                case ItemTypeAxe:
                case ItemTypeFencing:
                case ItemTypeMonster:
                case ItemTypeQuestProp:
                    return PotcoWeaponCategory.Combat;
                case ItemTypeGun:
                    return PotcoWeaponCategory.Firearm;
                case ItemTypeDoll:
                    return PotcoWeaponCategory.Voodoo;
                case ItemTypeDagger:
                    return PotcoWeaponCategory.Throwing;
                case ItemTypeGrenade:
                    return PotcoWeaponCategory.Grenade;
                case ItemTypeStaff:
                    return PotcoWeaponCategory.Staff;
                case ItemTypeFishing:
                    return PotcoWeaponCategory.Fishing;
                default:
                    return weaponClass == PotcoWeaponClass.Melee ? PotcoWeaponCategory.Melee : PotcoWeaponCategory.Unknown;
            }
        }

        public static int ResolveReferenceKey(PotcoWeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case PotcoWeaponClass.Sword:
                case PotcoWeaponClass.Melee:
                case PotcoWeaponClass.DualCutlass:
                case PotcoWeaponClass.Foil:
                case PotcoWeaponClass.MonsterMelee:
                case PotcoWeaponClass.Torch:
                    return 1;
                case PotcoWeaponClass.Pistol:
                case PotcoWeaponClass.Gun:
                case PotcoWeaponClass.Bayonet:
                    return 2;
                case PotcoWeaponClass.Doll:
                    return 3;
                case PotcoWeaponClass.Dagger:
                    return 4;
                case PotcoWeaponClass.Grenade:
                case PotcoWeaponClass.PowderKeg:
                    return 5;
                case PotcoWeaponClass.Wand:
                    return 6;
                default:
                    return 0;
            }
        }

        public static string ResolveDrawAnimation(PotcoWeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case PotcoWeaponClass.Melee:
                    return "boxing_fromidle";
                case PotcoWeaponClass.DualCutlass:
                    return "dualcutlass_draw";
                case PotcoWeaponClass.Foil:
                case PotcoWeaponClass.Torch:
                    return "sword_draw";
                case PotcoWeaponClass.MonsterMelee:
                    return string.Empty;
                case PotcoWeaponClass.PowderKeg:
                    return "bigbomb_draw";
                case PotcoWeaponClass.Sword:
                case PotcoWeaponClass.Dagger:
                    return "sword_draw";
                case PotcoWeaponClass.Pistol:
                case PotcoWeaponClass.Gun:
                case PotcoWeaponClass.Bayonet:
                    return "gun_draw";
                case PotcoWeaponClass.Grenade:
                    return "bomb_draw";
                case PotcoWeaponClass.Wand:
                case PotcoWeaponClass.Doll:
                    return "voodoo_draw";
                default:
                    return string.Empty;
            }
        }

        public static string ResolvePutAwayAnimation(PotcoWeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case PotcoWeaponClass.Melee:
                    return "boxing_fromidle";
                case PotcoWeaponClass.DualCutlass:
                case PotcoWeaponClass.Foil:
                case PotcoWeaponClass.Torch:
                    return "sword_putaway";
                case PotcoWeaponClass.MonsterMelee:
                    return string.Empty;
                case PotcoWeaponClass.PowderKeg:
                    return "bigbomb_draw";
                case PotcoWeaponClass.Sword:
                case PotcoWeaponClass.Dagger:
                case PotcoWeaponClass.Wand:
                case PotcoWeaponClass.Doll:
                    return "sword_putaway";
                case PotcoWeaponClass.Pistol:
                case PotcoWeaponClass.Gun:
                case PotcoWeaponClass.Bayonet:
                    return "gun_putaway";
                case PotcoWeaponClass.Grenade:
                    return "bomb_draw";
                default:
                    return string.Empty;
            }
        }

        public static PotcoWeaponAnimationSet ResolveAnimationSet(PotcoWeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case PotcoWeaponClass.Melee:
                    return new PotcoWeaponAnimationSet("walk", "run", "walk", "boxing_idle");
                case PotcoWeaponClass.DualCutlass:
                    return new PotcoWeaponAnimationSet("dualcutlass_walk", "dualcutlass_walk", "walk", "dualcutlass_idle", "strafe_left", "strafe_right");
                case PotcoWeaponClass.Foil:
                    return new PotcoWeaponAnimationSet("sword_advance", "sword_advance", "walk", "foil_idle", "strafe_left", "strafe_right");
                case PotcoWeaponClass.MonsterMelee:
                    return new PotcoWeaponAnimationSet("walk", "walk", "walk", "idle");
                case PotcoWeaponClass.Torch:
                    return new PotcoWeaponAnimationSet("walk", "run_with_weapon", "walk", "sword_idle", "strafe_left", "strafe_right");
                case PotcoWeaponClass.PowderKeg:
                    return new PotcoWeaponAnimationSet("bigbomb_walk", "bigbomb_walk", "bigbomb_walk", "bigbomb_idle", "bigbomb_walk_left", "bigbomb_walk_right");
                case PotcoWeaponClass.Sword:
                case PotcoWeaponClass.Dagger:
                    return new PotcoWeaponAnimationSet("walk", "run_with_weapon", "walk", "sword_idle");
                case PotcoWeaponClass.Pistol:
                    return new PotcoWeaponAnimationSet("walk", "run_with_weapon", "walk", "gun_pointedup_idle");
                case PotcoWeaponClass.Gun:
                    return new PotcoWeaponAnimationSet("rifle_fight_walk", "bayonet_run", "walk", "rifle_fight_idle", "rifle_fight_run_strafe_left", "rifle_fight_run_strafe_right");
                case PotcoWeaponClass.Bayonet:
                    return new PotcoWeaponAnimationSet("bayonet_attack_walk", "bayonet_run", "walk", "bayonet_attack_idle");
                case PotcoWeaponClass.Grenade:
                    return new PotcoWeaponAnimationSet("walk", "run_with_weapon", "walk", "bomb_idle");
                case PotcoWeaponClass.Wand:
                    return new PotcoWeaponAnimationSet("walk", "run_with_weapon", "walk", "wand_idle");
                case PotcoWeaponClass.Doll:
                    return new PotcoWeaponAnimationSet("walk", "run", "walk", "idle");
                default:
                    return new PotcoWeaponAnimationSet("walk", "run", "walk", "idle");
            }
        }

        public static PotcoWeaponAttachmentPose ResolveAttachmentPose(PotcoWeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case PotcoWeaponClass.Torch:
                    return new PotcoWeaponAttachmentPose(Vector3.zero, new Vector3(-90f, 0f, 0f), Vector3.one * 0.5f);
                case PotcoWeaponClass.PowderKeg:
                    return new PotcoWeaponAttachmentPose(new Vector3(-0.3f, 0f, -0.8f), new Vector3(0f, 225f, -15f), Vector3.one);
                default:
                    return PotcoWeaponAttachmentPose.Identity;
            }
        }

        public static IReadOnlyList<string> ResolveComboAnimations(PotcoWeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case PotcoWeaponClass.Melee:
                    return new[] { "boxing_punch", "boxing_kick", "boxing_haymaker" };
                case PotcoWeaponClass.DualCutlass:
                    return new[] { "dualcutlass_comboB", "dualcutlass_comboB", "dualcutlass_comboB", "dualcutlass_comboA", "dualcutlass_comboA" };
                case PotcoWeaponClass.Foil:
                    return new[] { "foil_thrust", "foil_hack", "foil_coup", "foil_thrust", "foil_slash", "foil_kick", "foil_coup" };
                case PotcoWeaponClass.MonsterMelee:
                    return new[] { "attack", "attackA", "attackB" };
                case PotcoWeaponClass.Torch:
                    return new[] { "cutlass_combo", "cutlass_combo", "cutlass_combo", "cutlass_combo" };
                case PotcoWeaponClass.PowderKeg:
                    return new[] { "bigbomb_throw", "bigbomb_charge", "bigbomb_charge_throw" };
                case PotcoWeaponClass.Sword:
                    return new[] { "cutlass_combo", "cutlass_combo", "cutlass_combo", "cutlass_combo" };
                case PotcoWeaponClass.Dagger:
                    return new[] { "dagger_combo", "dagger_combo", "dagger_combo", "dagger_combo" };
                case PotcoWeaponClass.Pistol:
                    return new[] { "gun_fire", "gun_fire", "gun_fire" };
                case PotcoWeaponClass.Gun:
                    return new[] { "rifle_fight_shoot_hip", "rifle_fight_shoot_high" };
                case PotcoWeaponClass.Bayonet:
                    return new[] { "bayonet_attackA", "bayonet_attackB", "bayonet_attackC" };
                case PotcoWeaponClass.Grenade:
                    return new[] { "bomb_throw", "bomb_charge", "bomb_charge_throw" };
                case PotcoWeaponClass.Wand:
                    return new[] { "wand_cast_start", "wand_cast_fire", "voodoo_tune" };
                case PotcoWeaponClass.Doll:
                    return new[] { "voodoo_doll_poke", "voodoo_swarm", "voodoo_tune" };
                default:
                    return Array.Empty<string>();
            }
        }

        private static float ResolveReferenceRange(float rawRange)
        {
            return rawRange < 0f ? 0f : rawRange;
        }

        private static Dictionary<int, SkillRow> LoadSkillRows(string path)
        {
            var rows = new Dictionary<int, SkillRow>();
            if (!File.Exists(path))
                return rows;

            string source = File.ReadAllText(path);
            string body = PythonText.ExtractDictionaryBody(source, "skillInfo");
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> entry in PythonText.ParseDictionaryEntries(body))
            {
                string key = PythonText.TrimPythonString(entry.Key);
                if (!string.Equals(key, "columnHeadings", StringComparison.Ordinal))
                    continue;

                foreach (KeyValuePair<string, string> heading in PythonText.ParseDictionaryEntries(PythonText.TrimEnclosure(entry.Value, '{', '}')))
                {
                    string name = PythonText.TrimPythonString(heading.Key);
                    if (int.TryParse(heading.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                        columns[name] = index;
                }
            }

            if (columns.Count == 0)
                LoadColumnHeadings(PythonText.ExtractDictionaryBody(source, "columnHeadings"), columns);

            foreach (KeyValuePair<string, string> entry in PythonText.ParseDictionaryEntries(body))
            {
                string key = PythonText.TrimPythonString(entry.Key);
                if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int skillId))
                    continue;

                rows[skillId] = new SkillRow(columns, PotcoPythonValueParser.ParseList(entry.Value));
            }

            return rows;
        }

        private static void LoadColumnHeadings(string body, Dictionary<string, int> columns)
        {
            foreach (KeyValuePair<string, string> heading in PythonText.ParseDictionaryEntries(body))
            {
                string name = PythonText.TrimPythonString(heading.Key);
                if (int.TryParse(heading.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                    columns[name] = index;
            }
        }

        private sealed class SkillRow
        {
            private readonly IReadOnlyDictionary<string, int> columns;
            private readonly IReadOnlyList<ItemDataValue> values;

            public SkillRow(IReadOnlyDictionary<string, int> columns, IReadOnlyList<ItemDataValue> values)
            {
                this.columns = columns;
                this.values = values;
            }

            public int GetInt(string columnName, int fallback = 0)
            {
                if (!columns.TryGetValue(columnName, out int index) || index < 0 || index >= values.Count)
                    return fallback;
                return values[index].AsInt(fallback);
            }

            public float GetFloat(string columnName, float fallback = 0f)
            {
                if (!columns.TryGetValue(columnName, out int index) || index < 0 || index >= values.Count)
                    return fallback;
                return values[index].AsFloat(fallback);
            }

            public string GetString(string columnName, string fallback = "")
            {
                if (!columns.TryGetValue(columnName, out int index) || index < 0 || index >= values.Count)
                    return fallback;
                return values[index].Raw ?? fallback;
            }
        }

        private static class PythonText
        {
            public static string ExtractDictionaryBody(string source, string dictionaryName)
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

            public static IEnumerable<KeyValuePair<string, string>> ParseDictionaryEntries(string body)
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

            public static string TrimEnclosure(string value, char open, char close)
            {
                value = (value ?? string.Empty).Trim();
                if (value.Length >= 2 && value[0] == open && value[value.Length - 1] == close)
                    return value.Substring(1, value.Length - 2);
                return value;
            }

            public static string TrimPythonString(string value)
            {
                value = (value ?? string.Empty).Trim();
                if (value.StartsWith("u'", StringComparison.Ordinal) || value.StartsWith("u\"", StringComparison.Ordinal))
                    value = value.Substring(1);

                if (value.Length >= 2 && IsQuote(value[0]) && value[value.Length - 1] == value[0])
                {
                    int index = 0;
                    return ParseStringLiteral(value, ref index);
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

                    if (c == '(')
                        round++;
                    else if (c == ')')
                        round--;
                    else if (c == '[')
                        square++;
                    else if (c == ']')
                        square--;
                    else if (c == '{')
                        curly++;
                    else if (c == '}')
                        curly--;

                    if (c == separator && round == 0 && square == 0 && curly == 0)
                    {
                        values.Add(current.ToString());
                        current.Length = 0;
                        continue;
                    }

                    current.Append(c);
                }

                values.Add(current.ToString());
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

                    if (c == '(')
                        round++;
                    else if (c == ')')
                        round--;
                    else if (c == '[')
                        square++;
                    else if (c == ']')
                        square--;
                    else if (c == '{')
                        curly++;
                    else if (c == '}')
                        curly--;

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

            private static string ParseStringLiteral(string text, ref int index)
            {
                char quote = text[index++];
                var builder = new StringBuilder();
                while (index < text.Length)
                {
                    char c = text[index++];
                    if (c == quote)
                        return builder.ToString();

                    if (c == '\\' && index < text.Length)
                        c = text[index++];

                    builder.Append(c);
                }

                return builder.ToString();
            }

            private static bool IsQuote(char c)
            {
                return c == '\'' || c == '"';
            }
        }
    }
}
