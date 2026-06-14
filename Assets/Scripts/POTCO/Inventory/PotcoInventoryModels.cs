using System;
using System.Collections.Generic;
using UnityEngine;

namespace POTCO.Inventory
{
    public enum PotcoInventoryCategory
    {
        Unknown = 0,
        Weapon = 51,
        Clothing = 52,
        Tattoo = 53,
        Jewelry = 54,
        Music = 55,
        Charm = 56,
        Consumable = 57,
        Money = 58,
        MoneyWagered = 59
    }

    public readonly struct PotcoInventoryRange
    {
        public PotcoInventoryRange(int first, int last)
        {
            First = first;
            Last = last;
        }

        public int First { get; }
        public int Last { get; }
        public int Count => Last >= First ? Last - First + 1 : 0;

        public bool Contains(int location)
        {
            return location >= First && location <= Last;
        }

        public override string ToString()
        {
            return First == Last ? First.ToString() : $"{First}-{Last}";
        }
    }

    public sealed class PotcoItemDefinition
    {
        public int ItemId { get; set; }
        public PotcoInventoryCategory Category { get; set; }
        public int GoldCost { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string ConstantName { get; set; } = string.Empty;
        public int Rarity { get; set; }
        public int ItemType { get; set; }
        public int Subtype { get; set; }
        public int ItemColor { get; set; }
        public string IconName { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public int LandInfamyRequirement { get; set; }
        public int SeaInfamyRequirement { get; set; }
        public int QuestRequirement { get; set; }
        public int WeaponRequirement { get; set; }
        public int Power { get; set; }
        public int Barrels { get; set; }
        public int StackLimit { get; set; }
        public int UseSkill { get; set; }

        public bool IsStackable => Category == PotcoInventoryCategory.Consumable && StackLimit > 1;

        public string EffectiveDisplayName => string.IsNullOrEmpty(DisplayName) ? $"Item {ItemId}" : DisplayName;
    }

    [Serializable]
    public sealed class PotcoInventoryItemStack
    {
        public PotcoInventoryItemStack(PotcoItemDefinition definition, int location, int quantity)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            ItemId = definition.ItemId;
            Category = definition.Category;
            Location = location;
            Quantity = Mathf.Max(1, quantity);
        }

        public int ItemId { get; }
        public PotcoInventoryCategory Category { get; }
        public int Location { get; internal set; }
        public int Quantity { get; internal set; }
        public PotcoItemDefinition Definition { get; }

        public PotcoInventoryItemStack CloneAt(int location)
        {
            return new PotcoInventoryItemStack(Definition, location, Quantity);
        }
    }

    public sealed class PotcoInventoryAddResult
    {
        private PotcoInventoryAddResult(bool success, int primaryLocation, int quantityAdded, string message, IReadOnlyList<int> locations)
        {
            Success = success;
            PrimaryLocation = primaryLocation;
            QuantityAdded = quantityAdded;
            Message = message ?? string.Empty;
            Locations = locations ?? Array.Empty<int>();
        }

        public bool Success { get; }
        public int PrimaryLocation { get; }
        public int QuantityAdded { get; }
        public string Message { get; }
        public IReadOnlyList<int> Locations { get; }

        public static PotcoInventoryAddResult Ok(int primaryLocation, int quantityAdded, IReadOnlyList<int> locations)
        {
            return new PotcoInventoryAddResult(true, primaryLocation, quantityAdded, string.Empty, locations);
        }

        public static PotcoInventoryAddResult Fail(string message)
        {
            return new PotcoInventoryAddResult(false, PotcoInventoryLocations.InvalidLocation, 0, message, Array.Empty<int>());
        }
    }

    public sealed class PotcoInventoryMoveResult
    {
        private PotcoInventoryMoveResult(bool success, int sourceLocation, int destinationLocation, string message)
        {
            Success = success;
            SourceLocation = sourceLocation;
            DestinationLocation = destinationLocation;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public int SourceLocation { get; }
        public int DestinationLocation { get; }
        public string Message { get; }

        public static PotcoInventoryMoveResult Ok(int sourceLocation, int destinationLocation)
        {
            return new PotcoInventoryMoveResult(true, sourceLocation, destinationLocation, string.Empty);
        }

        public static PotcoInventoryMoveResult Fail(int sourceLocation, int destinationLocation, string message)
        {
            return new PotcoInventoryMoveResult(false, sourceLocation, destinationLocation, message);
        }
    }
}
