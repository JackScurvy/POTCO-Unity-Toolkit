using System;
using System.Collections.Generic;
using System.Globalization;

namespace POTCO.ItemCards
{
    public sealed class ItemCardDataBuilder
    {
        private readonly PotcoSourceIndex index;

        public ItemCardDataBuilder(PotcoSourceIndex index)
        {
            this.index = index ?? throw new ArgumentNullException(nameof(index));
        }

        public ItemCardData Build(ItemDataRow row)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            var itemClass = (PotcoItemClass)index.GetInt(row, "ITEM_CLASS");
            int rarity = index.GetInt(row, "RARITY");
            int subtype = index.GetInt(row, "ITEM_SUBTYPE", index.GetInt(row, "SUBTYPE"));
            int itemType = index.GetInt(row, "ITEM_TYPE");

            var card = new ItemCardData
            {
                ItemId = row.ItemId,
                ItemClass = itemClass,
                Rarity = rarity,
                Title = ResolveItemTitle(row),
                Subtitle = BuildSubtitle(itemClass, rarity, subtype, itemType),
                GoldCost = BuildDisplayGoldCost(index.GetInt(row, "GOLD_COST")),
                AttackPower = itemClass == PotcoItemClass.Weapon ? NonZeroString(index.GetInt(row, "POWER")) : string.Empty,
                FlavorText = index.GetString(row, "FLAVOR_TEXT"),
                IconName = index.GetString(row, "ITEM_ICON"),
                ModelName = index.GetString(row, "ITEM_MODEL"),
                PreviewMode = GetPreviewMode(itemClass, index.GetString(row, "ITEM_MODEL"))
            };

            AddSpecialAttack(row, card);
            AddAttributes(row, card);
            AddSkillBoosts(row, card);

            return card;
        }

        private string BuildDisplayGoldCost(int goldCost)
        {
            return Math.Max(0, (int)(goldCost * index.GoldSaleMultiplier)).ToString();
        }

        private string ResolveItemTitle(ItemDataRow row)
        {
            if (index.ItemNames.TryGetValue(row.ItemId, out string localized) && !string.IsNullOrEmpty(localized))
                return localized;

            string rawName = index.GetString(row, "ITEM_NAME");
            return string.IsNullOrEmpty(rawName) ? $"Item {row.ItemId}" : rawName;
        }

        private string BuildSubtitle(PotcoItemClass itemClass, int rarity, int subtype, int itemType)
        {
            string rarityName = index.RarityNames.TryGetValue(rarity, out string rarityValue) ? rarityValue : string.Empty;
            string typeName = GetTypeName(itemClass, subtype, itemType);

            if (string.IsNullOrEmpty(rarityName))
                return typeName;
            if (string.IsNullOrEmpty(typeName))
                return rarityName;

            return $"{rarityName} {typeName}";
        }

        private string GetTypeName(PotcoItemClass itemClass, int subtype, int itemType)
        {
            if (itemClass == PotcoItemClass.Clothing)
                return ClothingTypeNames.TryGetValue(itemType, out string clothingType) ? clothingType : "Clothing";

            if (itemClass == PotcoItemClass.Jewelry)
                return JewelryTypeNames.TryGetValue(itemType, out string jewelryType) ? jewelryType : "Jewelry";

            if (itemClass == PotcoItemClass.Tattoo)
                return TattooTypeNames.TryGetValue(itemType, out string tattooType) ? tattooType : "Tattoo";

            return index.SubtypeNames.TryGetValue(subtype, out string subtypeName) ? subtypeName : itemClass.ToString();
        }

        private static ItemPreviewMode GetPreviewMode(PotcoItemClass itemClass, string modelName)
        {
            if (itemClass == PotcoItemClass.Clothing || itemClass == PotcoItemClass.Jewelry || itemClass == PotcoItemClass.Tattoo)
                return ItemPreviewMode.GenericPlayer;

            return string.IsNullOrEmpty(modelName) ? ItemPreviewMode.Icon : ItemPreviewMode.Model;
        }

        private void AddSpecialAttack(ItemDataRow row, ItemCardData card)
        {
            int rank = index.GetInt(row, "SPECIAL_ATTACK_RANK");
            int skillId = index.GetInt(row, "SPECIAL_ATTACK");
            if (rank <= 0 || skillId <= 0)
                return;

            card.Lines.Add(new ItemCardLine
            {
                Title = ResolveSkillName(skillId),
                Rank = $"Rank {rank}",
                Kind = ResolveSkillKind(skillId),
                Description = FormatRankedDescription(index.SkillDescriptions.TryGetValue(skillId, out string description) ? description : string.Empty, rank),
                IconName = index.Skills.TryGetValue(skillId, out SkillInfoRow skill) ? skill.IconName : string.Empty
            });
        }

        private void AddAttributes(ItemDataRow row, ItemCardData card)
        {
            foreach (Tuple<int, int> pair in GetRankedPairs(row, "ATTRIBUTE"))
            {
                int attributeId = pair.Item1;
                int rank = pair.Item2;
                card.Lines.Add(new ItemCardLine
                {
                    Title = index.GetAttributeName(row.ItemId, attributeId),
                    Rank = $"Rank {rank}",
                    Kind = "Attribute",
                    Description = index.GetAttributeDescription(row.ItemId, attributeId),
                    IconName = index.AttributeIcons.TryGetValue(attributeId, out string iconName) ? iconName : string.Empty
                });
            }
        }

        private void AddSkillBoosts(ItemDataRow row, ItemCardData card)
        {
            foreach (Tuple<int, int> pair in GetRankedPairs(row, "SKILLBOOST"))
            {
                int skillId = pair.Item1;
                int rank = pair.Item2;
                card.Lines.Add(new ItemCardLine
                {
                    Title = ResolveSkillName(skillId),
                    Rank = $"Rank {rank}",
                    Kind = "Skill Boost",
                    Description = FormatRankedDescription(index.SkillDescriptions.TryGetValue(skillId, out string description) ? description : string.Empty, rank),
                    IconName = index.Skills.TryGetValue(skillId, out SkillInfoRow skill) ? skill.IconName : string.Empty
                });
            }
        }

        private IEnumerable<Tuple<int, int>> GetRankedPairs(ItemDataRow row, string prefix)
        {
            for (int i = 1; i <= 3; i++)
            {
                int rank = index.GetInt(row, $"{prefix}_{i}_RANK");
                int id = index.GetInt(row, $"{prefix}_{i}");
                if (rank > 0 && id > 0)
                    yield return Tuple.Create(id, rank);
            }
        }

        private string ResolveSkillName(int skillId)
        {
            if (index.InventoryTypeNames.TryGetValue(skillId, out string name) && !string.IsNullOrEmpty(name))
                return name;

            return $"Skill {skillId}";
        }

        private string ResolveSkillKind(int skillId)
        {
            if (!index.Skills.TryGetValue(skillId, out SkillInfoRow skill))
                return string.Empty;

            if (skill.Track == 5)
                return "Break Attack";
            if (skill.Track == 6)
                return "Defense";

            return "Weapon Skill";
        }

        private static string NonZeroString(int value)
        {
            return value == 0 ? string.Empty : value.ToString();
        }

        private static string FormatRankedDescription(string description, int rank)
        {
            if (string.IsNullOrEmpty(description))
                return string.Empty;

            string value = rank.ToString(CultureInfo.InvariantCulture);
            return description.Replace("%d%%", value + "%").Replace("%d", value);
        }

        private static readonly Dictionary<int, string> ClothingTypeNames = new Dictionary<int, string>
        {
            { 0, "Hat" },
            { 1, "Shirt" },
            { 2, "Vest" },
            { 3, "Coat" },
            { 4, "Pants" },
            { 5, "Belt" },
            { 6, "Belt" },
            { 7, "Shoes" }
        };

        private static readonly Dictionary<int, string> JewelryTypeNames = new Dictionary<int, string>
        {
            { 0, "Right Brow" },
            { 1, "Left Brow" },
            { 2, "Left Ear" },
            { 3, "Right Ear" },
            { 4, "Nose" },
            { 5, "Mouth" },
            { 6, "Left Hand" },
            { 7, "Right Hand" }
        };

        private static readonly Dictionary<int, string> TattooTypeNames = new Dictionary<int, string>
        {
            { 0, "Chest Tattoo" },
            { 1, "Arm Tattoo" },
            { 2, "Face Tattoo" }
        };
    }
}
