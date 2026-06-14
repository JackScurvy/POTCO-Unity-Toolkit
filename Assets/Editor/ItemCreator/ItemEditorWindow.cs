#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace POTCO.Editor.ItemCreator
{
    public sealed class ItemEditorWindow : EditorWindow
    {
        private readonly int[] classFilterValues = { 0, 51, 52, 53, 54, 56, 57 };
        private readonly string[] classFilterLabels = { "All", "Weapons", "Clothing", "Tattoos", "Jewelry", "Charms", "Consumables" };

        private PotcoSourceIndex index;
        private ItemCardDataBuilder cardBuilder;
        private ItemPreviewResolver previewResolver;
        private PotcoItemCardRenderer cardRenderer;

        private Vector2 listScroll;
        private Vector2 editScroll;
        private string search = string.Empty;
        private int classFilterIndex;
        private ItemDataRow selectedRow;
        private string loadError;

        private readonly HashSet<int> dirtyRows = new HashSet<int>();

        [MenuItem("POTCO/Item Card Editor")]
        public static void Open()
        {
            ItemEditorWindow window = GetWindow<ItemEditorWindow>("Item Card Editor");
            window.minSize = new Vector2(1040, 620);
        }

        private void OnEnable()
        {
            previewResolver = new ItemPreviewResolver();
            cardRenderer = new PotcoItemCardRenderer(previewResolver);
            Reload();
        }

        private void OnDisable()
        {
            cardRenderer?.Dispose();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!string.IsNullOrEmpty(loadError))
            {
                EditorGUILayout.HelpBox(loadError, MessageType.Error);
                return;
            }

            if (index == null)
            {
                EditorGUILayout.HelpBox("Item data is not loaded.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawListPane();
            DrawEditPane();
            DrawCardPane();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), EditorStyles.toolbarButton, GUILayout.Width(28)))
                Reload();

            GUI.enabled = selectedRow != null;
            if (GUILayout.Button(EditorGUIUtility.IconContent("CreateAddNew"), EditorStyles.toolbarButton, GUILayout.Width(28)))
                CreateNewItem();

            if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Duplicate"), EditorStyles.toolbarButton, GUILayout.Width(28)))
                DuplicateSelectedItem();
            GUI.enabled = true;

            GUI.enabled = selectedRow != null && EditorApplication.isPlaying;
            if (GUILayout.Button("Add To Play Inventory", EditorStyles.toolbarButton, GUILayout.Width(140)))
                AddSelectedToPlayInventory();
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            GUI.enabled = dirtyRows.Count > 0;
            if (GUILayout.Button($"Save {dirtyRows.Count}", EditorStyles.toolbarButton, GUILayout.Width(90)))
                Save();
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawListPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(260));

            EditorGUILayout.LabelField("Items", EditorStyles.boldLabel);
            search = EditorGUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.textField);
            classFilterIndex = EditorGUILayout.Popup(classFilterIndex, classFilterLabels);

            List<ItemDataRow> items = FilteredItems().ToList();
            EditorGUILayout.LabelField($"{items.Count} items", EditorStyles.miniLabel);

            listScroll = EditorGUILayout.BeginScrollView(listScroll, EditorStyles.helpBox);
            foreach (ItemDataRow row in items)
            {
                string title = index.ItemNames.TryGetValue(row.ItemId, out string localized)
                    ? localized
                    : index.GetString(row, "ITEM_NAME", $"Item {row.ItemId}");
                string dirtyMark = dirtyRows.Contains(row.ItemId) ? "* " : string.Empty;
                bool selected = selectedRow != null && selectedRow.ItemId == row.ItemId;

                GUIStyle style = selected ? EditorStyles.toolbarButton : EditorStyles.miniButton;
                if (GUILayout.Button($"{dirtyMark}{row.ItemId}  {title}", style))
                    selectedRow = row;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawEditPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(390));
            EditorGUILayout.LabelField("Editor", EditorStyles.boldLabel);

            if (selectedRow == null)
            {
                EditorGUILayout.HelpBox("Select an item from the list.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Item {selectedRow.ItemId}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (dirtyRows.Contains(selectedRow.ItemId))
                EditorGUILayout.LabelField("Modified", EditorStyles.miniBoldLabel, GUILayout.Width(62));
            EditorGUILayout.EndHorizontal();

            editScroll = EditorGUILayout.BeginScrollView(editScroll, EditorStyles.helpBox);

            Dictionary<int, string> columnsByIndex = BuildColumnLabels(index);
            for (int i = 0; i < selectedRow.Values.Count; i++)
            {
                string label = columnsByIndex.TryGetValue(i, out string columnName) ? columnName : $"Column {i}";
                ItemDataValue value = selectedRow.Values[i];

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(label, GUILayout.Width(160));
                EditorGUI.BeginChangeCheck();
                string updated = EditorGUILayout.TextField(value.Raw);
                if (EditorGUI.EndChangeCheck())
                {
                    value.SetRawInferringType(updated);
                    dirtyRows.Add(selectedRow.ItemId);
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        internal static Dictionary<int, string> BuildColumnLabels(PotcoSourceIndex sourceIndex)
        {
            var labels = new Dictionary<int, string>();
            if (sourceIndex == null)
                return labels;

            foreach (IGrouping<int, KeyValuePair<string, int>> group in sourceIndex.Columns.GroupBy(pair => pair.Value))
            {
                string[] names = group
                    .Select(pair => pair.Key)
                    .Distinct()
                    .OrderBy(name => name)
                    .ToArray();
                labels[group.Key] = string.Join(" / ", names);
            }

            return labels;
        }

        private void DrawCardPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(330));
            EditorGUILayout.LabelField("Card Preview", EditorStyles.boldLabel);

            ItemCardData card = selectedRow == null ? null : cardBuilder.Build(selectedRow);
            float cardWidth = 300f;
            float cardHeight = cardRenderer.GetPreferredHeight(card, cardWidth);
            Rect cardRect = GUILayoutUtility.GetRect(cardWidth, cardHeight, GUILayout.Width(cardWidth), GUILayout.Height(cardHeight));
            cardRenderer.Draw(cardRect, card, selectedRow, index);

            EditorGUILayout.EndVertical();
        }

        private IEnumerable<ItemDataRow> FilteredItems()
        {
            IEnumerable<ItemDataRow> rows = index.Items.Values.OrderBy(row => row.ItemId);

            int selectedClass = classFilterValues[classFilterIndex];
            if (selectedClass != 0)
                rows = rows.Where(row => index.GetInt(row, "ITEM_CLASS") == selectedClass);

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                rows = rows.Where(row =>
                    row.ItemId.ToString().Contains(term) ||
                    index.GetString(row, "ITEM_NAME").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    index.GetString(row, "CONSTANT_NAME").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (index.ItemNames.TryGetValue(row.ItemId, out string localized) && localized.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            return rows;
        }

        private void Reload()
        {
            try
            {
                index = PotcoSourceIndex.LoadFromAssets();
                cardBuilder = new ItemCardDataBuilder(index);
                dirtyRows.Clear();
                selectedRow = index.Items.Values.OrderBy(row => row.ItemId).FirstOrDefault();
                loadError = null;
            }
            catch (Exception ex)
            {
                loadError = ex.Message;
                index = null;
                cardBuilder = null;
                selectedRow = null;
            }
        }

        private void CreateNewItem()
        {
            int newId = NextItemId();
            List<ItemDataValue> values = CreateTemplateValues(newId);
            var row = new ItemDataRow(newId, values);

            index.Items[newId] = row;
            selectedRow = row;
            dirtyRows.Add(newId);
        }

        private void DuplicateSelectedItem()
        {
            if (selectedRow == null)
                return;

            int newId = NextItemId();
            List<ItemDataValue> values = selectedRow.Values
                .Select(value => new ItemDataValue(value.Raw, value.IsString))
                .ToList();

            SetValue(values, "ITEM_ID", newId.ToString());
            SetValue(values, "ITEM_NAME", index.GetString(selectedRow, "ITEM_NAME") + " Copy");
            SetValue(values, "CONSTANT_NAME", index.GetString(selectedRow, "CONSTANT_NAME") + "_" + newId);

            var row = new ItemDataRow(newId, values);
            index.Items[newId] = row;
            selectedRow = row;
            dirtyRows.Add(newId);
        }

        private List<ItemDataValue> CreateTemplateValues(int itemId)
        {
            int width = index.Columns.Count == 0 ? 44 : Math.Max(44, index.Columns.Values.Max() + 1);
            var values = new List<ItemDataValue>();
            for (int i = 0; i < width; i++)
                values.Add(ItemDataValue.FromRaw("0"));

            SetValue(values, "ITEM_CLASS", ((int)PotcoItemClass.Weapon).ToString());
            SetValue(values, "VERSION", "0");
            SetValue(values, "GOLD_COST", "0");
            SetValue(values, "ITEM_ID", itemId.ToString());
            SetValue(values, "ITEM_NAME", "New Item");
            SetValue(values, "CONSTANT_NAME", $"NEW_ITEM_{itemId}");
            SetValue(values, "RARITY", "1");
            SetValue(values, "ITEM_TYPE", "1");
            SetValue(values, "ITEM_ICON", "");
            SetValue(values, "FLAVOR_TEXT", "");
            SetValue(values, "ITEM_MODEL", "");
            SetValue(values, "ITEM_SUBTYPE", "1");
            return values;
        }

        private void SetValue(List<ItemDataValue> values, string columnName, string raw)
        {
            if (!index.Columns.TryGetValue(columnName, out int column))
                return;

            while (values.Count <= column)
                values.Add(ItemDataValue.FromRaw("0"));

            values[column] = ItemDataValue.FromRaw(raw);
        }

        private int NextItemId()
        {
            return index.Items.Count == 0 ? 1 : index.Items.Keys.Max() + 1;
        }

        private void Save()
        {
            try
            {
                string path = Path.Combine(Application.dataPath, "Editor", "POTCO_Source", "inventory", "ItemData.py");
                string source = File.ReadAllText(path);

                foreach (int itemId in dirtyRows.OrderBy(id => id))
                    source = ItemDataPatchWriter.PatchItemData(source, index.Items[itemId]);

                File.WriteAllText(path, source, new UTF8Encoding(false));
                dirtyRows.Clear();
                AssetDatabase.Refresh();
                Reload();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Save Failed", ex.Message, "OK");
            }
        }

        private void AddSelectedToPlayInventory()
        {
            if (selectedRow == null)
                return;

            POTCO.Inventory.PotcoInventoryController controller = POTCO.Inventory.PotcoInventoryController.FindActive();
            if (controller == null)
            {
                EditorUtility.DisplayDialog("No Play Inventory", "No active POTCO inventory controller was found in the playing scene.", "OK");
                return;
            }

            POTCO.Inventory.PotcoInventoryAddResult result = controller.AddItemToInventory(selectedRow.ItemId);
            if (!result.Success)
            {
                EditorUtility.DisplayDialog("Add Item Failed", result.Message, "OK");
                return;
            }

            Debug.Log($"Added POTCO item {selectedRow.ItemId} to play inventory at slot {result.PrimaryLocation}.");
        }
    }
}
#endif
