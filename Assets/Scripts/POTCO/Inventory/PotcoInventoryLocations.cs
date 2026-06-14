using System.Collections.Generic;

namespace POTCO.Inventory
{
    public static class PotcoInventoryLocations
    {
        public const int InvalidLocation = -1;
        public const int NonLocation = 0;
        public const int FirstLocation = 1;
        public const int TotalNumLocations = 255;
        public const int GoldCap = 200000;

        public static readonly PotcoInventoryRange EquipWeapons = new PotcoInventoryRange(1, 4);
        public static readonly PotcoInventoryRange EquipItems = new PotcoInventoryRange(49, 49);
        public static readonly PotcoInventoryRange WeaponBag = new PotcoInventoryRange(13, 42);
        public static readonly PotcoInventoryRange EquipClothes = new PotcoInventoryRange(50, 56);
        public static readonly PotcoInventoryRange ClothingBag = new PotcoInventoryRange(60, 94);
        public static readonly PotcoInventoryRange EquipJewelry = new PotcoInventoryRange(100, 107);
        public static readonly PotcoInventoryRange EquipTattoo = new PotcoInventoryRange(110, 113);
        public static readonly PotcoInventoryRange JewelryAndTattooBag = new PotcoInventoryRange(116, 143);
        public static readonly PotcoInventoryRange ConsumableBag = new PotcoInventoryRange(150, 191);
        public static readonly PotcoInventoryRange MiscBag = new PotcoInventoryRange(190, 224);
        public static readonly PotcoInventoryRange Gold = new PotcoInventoryRange(225, 225);
        public static readonly PotcoInventoryRange GoldWagered = new PotcoInventoryRange(226, 226);
        public static readonly PotcoInventoryRange Overflow = new PotcoInventoryRange(227, 255);

        public static IReadOnlyList<int> Expand(PotcoInventoryRange range)
        {
            var values = new List<int>(range.Count);
            for (int location = range.First; location <= range.Last; location++)
                values.Add(location);
            return values;
        }

        public static IReadOnlyList<PotcoInventoryRange> GetBagRanges(PotcoInventoryCategory category)
        {
            switch (category)
            {
                case PotcoInventoryCategory.Weapon:
                case PotcoInventoryCategory.Charm:
                    return new[] { WeaponBag };
                case PotcoInventoryCategory.Clothing:
                    return new[] { ClothingBag };
                case PotcoInventoryCategory.Jewelry:
                case PotcoInventoryCategory.Tattoo:
                    return new[] { JewelryAndTattooBag };
                case PotcoInventoryCategory.Consumable:
                    return new[] { ConsumableBag };
                case PotcoInventoryCategory.Music:
                    return new[] { MiscBag };
                case PotcoInventoryCategory.Money:
                    return new[] { Gold };
                case PotcoInventoryCategory.MoneyWagered:
                    return new[] { GoldWagered };
                default:
                    return new[] { Overflow };
            }
        }

        public static IReadOnlyList<int> GetEquipLocations(PotcoInventoryCategory category, int itemType)
        {
            switch (category)
            {
                case PotcoInventoryCategory.Weapon:
                    return Expand(EquipWeapons);
                case PotcoInventoryCategory.Charm:
                    return new[] { 49 };
                case PotcoInventoryCategory.Clothing:
                    return GetClothingEquipLocations(itemType);
                case PotcoInventoryCategory.Jewelry:
                    return GetJewelryEquipLocations(itemType);
                case PotcoInventoryCategory.Tattoo:
                    return GetTattooEquipLocations(itemType);
                default:
                    return new int[0];
            }
        }

        public static bool IsBagLocationFor(PotcoInventoryCategory category, int location)
        {
            foreach (PotcoInventoryRange range in GetBagRanges(category))
            {
                if (range.Contains(location))
                    return true;
            }

            return false;
        }

        public static bool IsEquipLocationFor(PotcoInventoryCategory category, int itemType, int location)
        {
            foreach (int equipLocation in GetEquipLocations(category, itemType))
            {
                if (equipLocation == location)
                    return true;
            }

            return false;
        }

        public static bool IsValidDestination(PotcoItemDefinition definition, int location)
        {
            if (definition == null)
                return false;

            return IsBagLocationFor(definition.Category, location) ||
                   IsEquipLocationFor(definition.Category, definition.ItemType, location) ||
                   Overflow.Contains(location);
        }

        private static IReadOnlyList<int> GetClothingEquipLocations(int itemType)
        {
            switch (itemType)
            {
                case 0:
                    return new[] { 50 };
                case 1:
                    return new[] { 53 };
                case 2:
                    return new[] { 52 };
                case 3:
                    return new[] { 51 };
                case 4:
                    return new[] { 55 };
                case 5:
                case 6:
                    return new[] { 54 };
                case 7:
                    return new[] { 56 };
                default:
                    return Expand(EquipClothes);
            }
        }

        private static IReadOnlyList<int> GetJewelryEquipLocations(int itemType)
        {
            switch (itemType)
            {
                case 0:
                    return new[] { 101, 100 };
                case 1:
                    return new[] { 103, 102 };
                case 2:
                    return new[] { 104 };
                case 3:
                    return new[] { 105 };
                case 4:
                    return new[] { 107, 106 };
                default:
                    return Expand(EquipJewelry);
            }
        }

        private static IReadOnlyList<int> GetTattooEquipLocations(int itemType)
        {
            switch (itemType)
            {
                case 0:
                    return new[] { 112 };
                case 1:
                    return new[] { 110, 111 };
                case 2:
                    return new[] { 113 };
                default:
                    return Expand(EquipTattoo);
            }
        }
    }
}
