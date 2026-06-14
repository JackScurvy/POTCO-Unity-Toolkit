using System;
using UnityEngine;

namespace POTCO.Inventory
{
    [DisallowMultipleComponent]
    public sealed class PotcoInventoryController : MonoBehaviour
    {
        [SerializeField] private bool seedReferenceStarterLoadout = true;
        [SerializeField] private bool createChestGui = true;
        [SerializeField] private bool showChestOnStart;

        public PotcoRuntimeItemCatalog Catalog { get; private set; }
        public PotcoInventory Inventory { get; private set; }
        public string LoadError { get; private set; }

        public static PotcoInventoryController FindActive()
        {
            return FindAnyObjectByType<PotcoInventoryController>();
        }

        private void Awake()
        {
            EnsureLoaded();

            if (createChestGui && GetComponent<PotcoChestGui>() == null)
            {
                PotcoChestGui gui = gameObject.AddComponent<PotcoChestGui>();
                gui.SetOpen(showChestOnStart);
            }
        }

        public bool EnsureLoaded()
        {
            if (Inventory != null)
                return true;

            try
            {
                Catalog = PotcoRuntimeItemCatalog.LoadFromAssets();
                Inventory = new PotcoInventory(Catalog);
                if (seedReferenceStarterLoadout)
                    Inventory.AddReferenceStarterLoadout();
                LoadError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
                Debug.LogError($"POTCO inventory failed to load: {ex}");
                return false;
            }
        }

        public PotcoInventoryAddResult AddItemToInventory(int itemId, int quantity = 1)
        {
            if (!EnsureLoaded())
                return PotcoInventoryAddResult.Fail(LoadError);

            PotcoInventoryAddResult result = Inventory.AddItem(itemId, quantity);
            if (!result.Success)
                Debug.LogWarning(result.Message);
            return result;
        }

        public PotcoInventoryMoveResult MoveItem(int sourceLocation, int destinationLocation)
        {
            if (!EnsureLoaded())
                return PotcoInventoryMoveResult.Fail(sourceLocation, destinationLocation, LoadError);

            return Inventory.MoveItem(sourceLocation, destinationLocation);
        }
    }
}
