using System;
using System.Collections.Generic;
using System.Linq;

namespace POTCO.Inventory
{
    public sealed class PotcoInventory
    {
        private readonly PotcoRuntimeItemCatalog catalog;
        private readonly Dictionary<int, PotcoInventoryItemStack> byLocation = new Dictionary<int, PotcoInventoryItemStack>();

        public PotcoInventory(PotcoRuntimeItemCatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public event Action Changed;

        public IReadOnlyDictionary<int, PotcoInventoryItemStack> ItemsByLocation => byLocation;
        public int Gold { get; private set; }

        public PotcoInventoryItemStack GetItemAt(int location)
        {
            byLocation.TryGetValue(location, out PotcoInventoryItemStack item);
            return item;
        }

        public IEnumerable<PotcoInventoryItemStack> GetItemsInRange(PotcoInventoryRange range)
        {
            return byLocation.Values
                .Where(item => range.Contains(item.Location))
                .OrderBy(item => item.Location);
        }

        public PotcoInventoryAddResult AddItem(int itemId, int quantity = 1)
        {
            if (quantity <= 0)
                return PotcoInventoryAddResult.Fail("Quantity must be greater than zero.");

            if (!catalog.TryGetItem(itemId, out PotcoItemDefinition definition))
                return PotcoInventoryAddResult.Fail($"Item {itemId} was not found in the POTCO item catalog.");

            if (definition.Category == PotcoInventoryCategory.Money)
            {
                int before = Gold;
                Gold = Math.Min(PotcoInventoryLocations.GoldCap, Gold + quantity);
                NotifyChanged();
                return PotcoInventoryAddResult.Ok(PotcoInventoryLocations.Gold.First, Gold - before, new[] { PotcoInventoryLocations.Gold.First });
            }

            if (definition.IsStackable)
                return AddStackable(definition, quantity);

            return AddLocatables(definition, quantity);
        }

        public PotcoInventoryMoveResult MoveItem(int sourceLocation, int destinationLocation)
        {
            if (!byLocation.TryGetValue(sourceLocation, out PotcoInventoryItemStack source))
                return PotcoInventoryMoveResult.Fail(sourceLocation, destinationLocation, "No item exists in the source slot.");

            if (sourceLocation == destinationLocation)
                return PotcoInventoryMoveResult.Ok(sourceLocation, destinationLocation);

            if (!PotcoInventoryLocations.IsValidDestination(source.Definition, destinationLocation))
                return PotcoInventoryMoveResult.Fail(sourceLocation, destinationLocation, "The item cannot be placed in that slot.");

            if (!byLocation.TryGetValue(destinationLocation, out PotcoInventoryItemStack destination))
            {
                MoveStack(source, sourceLocation, destinationLocation);
                NotifyChanged();
                return PotcoInventoryMoveResult.Ok(sourceLocation, destinationLocation);
            }

            if (CanMerge(source, destination))
            {
                int capacity = destination.Definition.StackLimit - destination.Quantity;
                int moved = Math.Min(capacity, source.Quantity);
                destination.Quantity += moved;
                source.Quantity -= moved;
                if (source.Quantity <= 0)
                    byLocation.Remove(sourceLocation);
                NotifyChanged();
                return PotcoInventoryMoveResult.Ok(sourceLocation, destinationLocation);
            }

            if (!PotcoInventoryLocations.IsValidDestination(destination.Definition, sourceLocation))
                return PotcoInventoryMoveResult.Fail(sourceLocation, destinationLocation, "The destination item cannot be swapped into the source slot.");

            byLocation[sourceLocation] = destination;
            byLocation[destinationLocation] = source;
            destination.Location = sourceLocation;
            source.Location = destinationLocation;
            NotifyChanged();
            return PotcoInventoryMoveResult.Ok(sourceLocation, destinationLocation);
        }

        public PotcoInventoryMoveResult EquipFirstAvailable(int location)
        {
            PotcoInventoryItemStack item = GetItemAt(location);
            if (item == null)
                return PotcoInventoryMoveResult.Fail(location, PotcoInventoryLocations.InvalidLocation, "No item exists in the source slot.");

            foreach (int equipLocation in PotcoInventoryLocations.GetEquipLocations(item.Category, item.Definition.ItemType))
            {
                PotcoInventoryMoveResult result = MoveItem(location, equipLocation);
                if (result.Success)
                    return result;
            }

            return PotcoInventoryMoveResult.Fail(location, PotcoInventoryLocations.InvalidLocation, "No legal equip slot is available.");
        }

        public bool TrashItem(int location)
        {
            if (!byLocation.Remove(location))
                return false;

            NotifyChanged();
            return true;
        }

        public bool ConsumeOne(int location)
        {
            PotcoInventoryItemStack item = GetItemAt(location);
            if (item == null || item.Category != PotcoInventoryCategory.Consumable)
                return false;

            item.Quantity--;
            if (item.Quantity <= 0)
                byLocation.Remove(location);

            NotifyChanged();
            return true;
        }

        public void AddReferenceStarterLoadout()
        {
            if (GetItemAt(1) == null && catalog.TryGetItem(1, out _))
            {
                PotcoInventoryAddResult result = AddItem(1);
                if (result.Success)
                    MoveItem(result.PrimaryLocation, 1);
            }
        }

        private PotcoInventoryAddResult AddStackable(PotcoItemDefinition definition, int quantity)
        {
            int remaining = quantity;
            var touched = new List<int>();

            foreach (PotcoInventoryItemStack existing in byLocation.Values
                         .Where(item => item.ItemId == definition.ItemId && item.Quantity < definition.StackLimit)
                         .OrderBy(item => item.Location)
                         .ToArray())
            {
                int space = definition.StackLimit - existing.Quantity;
                int add = Math.Min(space, remaining);
                existing.Quantity += add;
                remaining -= add;
                touched.Add(existing.Location);
                if (remaining <= 0)
                    break;
            }

            while (remaining > 0)
            {
                int location = FindAvailableLocation(definition);
                if (location == PotcoInventoryLocations.InvalidLocation)
                    break;

                int add = Math.Min(definition.StackLimit, remaining);
                byLocation[location] = new PotcoInventoryItemStack(definition, location, add);
                touched.Add(location);
                remaining -= add;
            }

            if (touched.Count == 0)
                return PotcoInventoryAddResult.Fail("No inventory slot is available.");

            NotifyChanged();
            return remaining == 0
                ? PotcoInventoryAddResult.Ok(touched[0], quantity, touched)
                : PotcoInventoryAddResult.Fail($"Only {quantity - remaining} of {quantity} could be added.");
        }

        private PotcoInventoryAddResult AddLocatables(PotcoItemDefinition definition, int quantity)
        {
            var touched = new List<int>();
            int remaining = quantity;
            while (remaining > 0)
            {
                int location = FindAvailableLocation(definition);
                if (location == PotcoInventoryLocations.InvalidLocation)
                    break;

                byLocation[location] = new PotcoInventoryItemStack(definition, location, 1);
                touched.Add(location);
                remaining--;
            }

            if (touched.Count == 0)
                return PotcoInventoryAddResult.Fail("No inventory slot is available.");

            NotifyChanged();
            return remaining == 0
                ? PotcoInventoryAddResult.Ok(touched[0], quantity, touched)
                : PotcoInventoryAddResult.Fail($"Only {quantity - remaining} of {quantity} could be added.");
        }

        private int FindAvailableLocation(PotcoItemDefinition definition)
        {
            foreach (PotcoInventoryRange range in PotcoInventoryLocations.GetBagRanges(definition.Category))
            {
                for (int location = range.First; location <= range.Last; location++)
                {
                    if (!byLocation.ContainsKey(location))
                        return location;
                }
            }

            for (int location = PotcoInventoryLocations.Overflow.First; location <= PotcoInventoryLocations.Overflow.Last; location++)
            {
                if (!byLocation.ContainsKey(location))
                    return location;
            }

            return PotcoInventoryLocations.InvalidLocation;
        }

        private static bool CanMerge(PotcoInventoryItemStack source, PotcoInventoryItemStack destination)
        {
            return source.ItemId == destination.ItemId &&
                   source.Definition.IsStackable &&
                   destination.Quantity < destination.Definition.StackLimit;
        }

        private void MoveStack(PotcoInventoryItemStack stack, int sourceLocation, int destinationLocation)
        {
            byLocation.Remove(sourceLocation);
            stack.Location = destinationLocation;
            byLocation[destinationLocation] = stack;
        }

        private void NotifyChanged()
        {
            Changed?.Invoke();
        }
    }
}
