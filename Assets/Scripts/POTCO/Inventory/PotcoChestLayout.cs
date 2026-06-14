using System;
using System.Collections.Generic;
using System.Linq;

namespace POTCO.Inventory
{
    public enum PotcoChestPageKind
    {
        WeaponBelt,
        Garb,
        JewelryAndTattoos,
        PotionsPouch,
        Treasure
    }

    public sealed class PotcoChestPageLayout
    {
        public PotcoChestPageLayout(PotcoChestPageKind kind, string title, string iconGroup, PotcoInventoryRange slotRange, int columns, int rows)
        {
            Kind = kind;
            Title = title ?? string.Empty;
            IconGroup = iconGroup ?? string.Empty;
            SlotRange = slotRange;
            Columns = columns;
            Rows = rows;
        }

        public PotcoChestPageKind Kind { get; }
        public string Title { get; }
        public string IconGroup { get; }
        public PotcoInventoryRange SlotRange { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int FirstSlot => SlotRange.First;
        public int LastSlot => SlotRange.Last;
    }

    public sealed class PotcoChestLayout
    {
        private readonly Dictionary<PotcoChestPageKind, PotcoChestPageLayout> pages;

        private PotcoChestLayout(IEnumerable<PotcoChestPageLayout> pageLayouts)
        {
            pages = pageLayouts.ToDictionary(page => page.Kind);
        }

        public IReadOnlyList<string> HotbarLabels { get; } = new[] { "F1", "F2", "F3", "F4", "Item" };
        public IReadOnlyList<int> HotbarSlots { get; } = new[] { 1, 2, 3, 4, 49 };
        public IReadOnlyCollection<PotcoChestPageLayout> Pages => pages.Values;

        public static PotcoChestLayout CreateDefault()
        {
            return new PotcoChestLayout(new[]
            {
                new PotcoChestPageLayout(PotcoChestPageKind.WeaponBelt, "Weapon Belt", "topgui_icon_weapons", PotcoInventoryLocations.WeaponBag, 6, 5),
                new PotcoChestPageLayout(PotcoChestPageKind.Garb, "Garb", "topgui_icon_clothing", PotcoInventoryLocations.ClothingBag, 5, 7),
                new PotcoChestPageLayout(PotcoChestPageKind.JewelryAndTattoos, "Jewelry & Tattoos", "pir_t_gui_gen_trinket", PotcoInventoryLocations.JewelryAndTattooBag, 4, 7),
                new PotcoChestPageLayout(PotcoChestPageKind.PotionsPouch, "Potions Pouch", "pir_t_ico_pot_elixir", PotcoInventoryLocations.ConsumableBag, 6, 7),
                new PotcoChestPageLayout(PotcoChestPageKind.Treasure, "Treasure", "topgui_icon_treasure", PotcoInventoryLocations.Gold, 1, 1)
            });
        }

        public PotcoChestPageLayout GetPage(PotcoChestPageKind kind)
        {
            if (!pages.TryGetValue(kind, out PotcoChestPageLayout page))
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown chest page.");
            return page;
        }
    }
}
