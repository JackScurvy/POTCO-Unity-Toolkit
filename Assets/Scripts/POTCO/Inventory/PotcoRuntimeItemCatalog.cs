using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace POTCO.Inventory
{
    public sealed class PotcoRuntimeItemCatalog
    {
        private readonly Dictionary<int, PotcoItemDefinition> items = new Dictionary<int, PotcoItemDefinition>();
        private readonly Dictionary<string, int> itemSymbols = new Dictionary<string, int>(StringComparer.Ordinal);

        public IReadOnlyDictionary<int, PotcoItemDefinition> Items => items;
        public IReadOnlyDictionary<string, string> Strings => strings;
        public IReadOnlyDictionary<int, string> RarityNames => rarityNames;

        private readonly Dictionary<string, string> strings = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<int, string> rarityNames = new Dictionary<int, string>();
        private readonly Dictionary<string, int> columns = new Dictionary<string, int>(StringComparer.Ordinal);

        public static PotcoRuntimeItemCatalog LoadFromAssets()
        {
            return LoadFromAssetsPath(Application.dataPath);
        }

        public static PotcoRuntimeItemCatalog LoadFromAssetsPath(string assetsPath)
        {
            if (string.IsNullOrEmpty(assetsPath))
                throw new ArgumentException("Assets path is required.", nameof(assetsPath));

            var catalog = new PotcoRuntimeItemCatalog();
            string sourceRoot = Path.Combine(assetsPath, "Editor", "POTCO_Source");
            catalog.LoadItemData(Path.Combine(sourceRoot, "inventory", "ItemData.py"));
            catalog.LoadLocalization(Path.Combine(sourceRoot, "PLocalizerEnglish.py"));
            catalog.ApplyFallbacks();
            return catalog;
        }

        public bool TryGetItem(int itemId, out PotcoItemDefinition definition)
        {
            return items.TryGetValue(itemId, out definition);
        }

        public PotcoItemDefinition GetItemOrNull(int itemId)
        {
            items.TryGetValue(itemId, out PotcoItemDefinition definition);
            return definition;
        }

        public string GetString(string key, string fallback)
        {
            return strings.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : fallback;
        }

        public string GetRarityName(int rarity)
        {
            return rarityNames.TryGetValue(rarity, out string value) ? value : string.Empty;
        }

        private void LoadItemData(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("ItemData.py not found.", path);

            string body = PythonText.ExtractDictionaryBody(File.ReadAllText(path), "itemInfo");
            List<KeyValuePair<string, string>> entries = PythonText.ParseDictionaryEntries(body).ToList();
            foreach (KeyValuePair<string, string> entry in entries)
            {
                string key = PythonText.TrimPythonString(entry.Key);
                if (string.Equals(key, "columnHeadings", StringComparison.Ordinal))
                    LoadColumnHeadings(entry.Value);
            }

            foreach (KeyValuePair<string, string> entry in entries)
            {
                string key = PythonText.TrimPythonString(entry.Key);
                if (string.Equals(key, "columnHeadings", StringComparison.Ordinal))
                    continue;

                if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemId))
                    continue;

                List<PythonValue> values = PythonText.ParseList(entry.Value);
                var category = (PotcoInventoryCategory)ReadInt(values, "ITEM_CLASS");
                var definition = new PotcoItemDefinition
                {
                    ItemId = itemId,
                    Category = category,
                    GoldCost = ReadInt(values, "GOLD_COST"),
                    DisplayName = ReadString(values, "ITEM_NAME"),
                    ConstantName = ReadString(values, "CONSTANT_NAME"),
                    Rarity = ReadInt(values, "RARITY"),
                    ItemType = ReadInt(values, "ITEM_TYPE"),
                    Subtype = ReadInt(values, "ITEM_SUBTYPE", ReadInt(values, "SUBTYPE")),
                    ItemColor = ReadInt(values, "ITEM_COLOR"),
                    IconName = ReadString(values, "ITEM_ICON"),
                    ModelName = ReadString(values, "ITEM_MODEL"),
                    FlavorText = ReadString(values, "FLAVOR_TEXT"),
                    LandInfamyRequirement = ReadInt(values, "ITEM_LAND_INFAMY_REQ"),
                    SeaInfamyRequirement = ReadInt(values, "ITEM_SEA_INFAMY_REQ"),
                    QuestRequirement = ReadInt(values, "QUEST_REQ"),
                    WeaponRequirement = ReadInt(values, "WEAPON_REQ"),
                    Power = category == PotcoInventoryCategory.Weapon ? ReadInt(values, "POWER") : 0,
                    Barrels = category == PotcoInventoryCategory.Weapon ? ReadInt(values, "BARRELS") : 0,
                    StackLimit = category == PotcoInventoryCategory.Consumable ? ReadInt(values, "STACK_LIMIT", 1) : 1,
                    UseSkill = category == PotcoInventoryCategory.Consumable ? ReadInt(values, "USE_SKILL") : 0
                };

                if (definition.StackLimit <= 0)
                    definition.StackLimit = 1;

                items[itemId] = definition;

                if (!string.IsNullOrEmpty(definition.ConstantName))
                    itemSymbols[definition.ConstantName] = itemId;
            }
        }

        private void LoadColumnHeadings(string dictionaryLiteral)
        {
            string body = PythonText.TrimEnclosure(dictionaryLiteral, '{', '}');
            foreach (KeyValuePair<string, string> heading in PythonText.ParseDictionaryEntries(body))
            {
                string name = PythonText.TrimPythonString(heading.Key);
                if (int.TryParse(heading.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                    columns[name] = index;
            }
        }

        private void LoadLocalization(string path)
        {
            if (!File.Exists(path))
                return;

            string text = File.ReadAllText(path);
            LoadSimpleStrings(text);
            LoadItemNames(text);
            LoadRarityNames(text);
        }

        private void LoadSimpleStrings(string text)
        {
            string[] keys =
            {
                "Treasure",
                "HowToDrinkPotion",
                "DrinkPotion",
                "OverflowHint",
                "InventoryPageTitle",
                "InventoryPageWeapons",
                "InventoryPageClothing",
                "InventoryPageJewelry",
                "InventoryPagePotions",
                "InventoryPageTreasure",
                "InventoryPageItemSlot",
                "InventoryRedeemCode",
                "InventoryFaceCamera",
                "ItemAttackStrength",
                "ItemRank",
                "ItemBoost",
                "ItemBarrels",
                "ItemLevelRequirement",
                "ItemTrainingRequirement",
                "UnlimitedAccessRequirement",
                "WeaponSkill",
                "BreakAttackSkill",
                "DefenseSkill"
            };

            foreach (string key in keys)
            {
                Match match = Regex.Match(text, $@"(?m)^{Regex.Escape(key)}\s*=\s*(?<value>u?['""][\s\S]*?['""])\s*$");
                if (match.Success)
                    strings[key] = PythonText.TrimPythonString(match.Groups["value"].Value);
            }
        }

        private void LoadItemNames(string text)
        {
            string body = PythonText.ExtractDictionaryBody(text, "ItemNames");
            foreach (KeyValuePair<string, string> entry in PythonText.ParseDictionaryEntries(body))
            {
                if (!TryResolveItemKey(entry.Key.Trim(), out int itemId))
                    continue;

                string name = PythonText.ExtractFirstStringLiteral(entry.Value);
                if (!string.IsNullOrEmpty(name) && items.TryGetValue(itemId, out PotcoItemDefinition definition))
                    definition.DisplayName = name;
            }
        }

        private void LoadRarityNames(string text)
        {
            string body = PythonText.ExtractDictionaryBody(text, "ItemRarityNames");
            foreach (KeyValuePair<string, string> entry in PythonText.ParseDictionaryEntries(body))
            {
                if (!int.TryParse(entry.Key.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rarity))
                    continue;

                string name = PythonText.ExtractFirstStringLiteral(entry.Value);
                if (!string.IsNullOrEmpty(name))
                    rarityNames[rarity] = name;
            }
        }

        private bool TryResolveItemKey(string expression, out int itemId)
        {
            itemId = 0;
            expression = expression.Trim();
            if (int.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId))
                return true;

            Match match = Regex.Match(expression, @"^(?<scope>ItemGlobals\.)?(?<name>[A-Za-z0-9_]+)$");
            return match.Success && itemSymbols.TryGetValue(match.Groups["name"].Value, out itemId);
        }

        private int ReadInt(IReadOnlyList<PythonValue> values, string columnName, int fallback = 0)
        {
            if (!columns.TryGetValue(columnName, out int index) || index < 0 || index >= values.Count)
                return fallback;
            return values[index].AsInt(fallback);
        }

        private string ReadString(IReadOnlyList<PythonValue> values, string columnName, string fallback = "")
        {
            if (!columns.TryGetValue(columnName, out int index) || index < 0 || index >= values.Count)
                return fallback;
            return values[index].Raw ?? fallback;
        }

        private void ApplyFallbacks()
        {
            if (!rarityNames.ContainsKey(1))
                rarityNames[1] = "Crude";
            if (!rarityNames.ContainsKey(2))
                rarityNames[2] = "Common";
            if (!rarityNames.ContainsKey(3))
                rarityNames[3] = "Rare";
            if (!rarityNames.ContainsKey(4))
                rarityNames[4] = "Famed";
            if (!rarityNames.ContainsKey(5))
                rarityNames[5] = "Legendary";

            AddStringFallback("InventoryPageTitle", "Inventory");
            AddStringFallback("InventoryPageWeapons", "Weapon Belt");
            AddStringFallback("InventoryPageClothing", "Garb");
            AddStringFallback("InventoryPageJewelry", "Jewelry & Tattoos");
            AddStringFallback("InventoryPagePotions", "Potions Pouch");
            AddStringFallback("InventoryPageTreasure", "Treasure");
            AddStringFallback("InventoryPageItemSlot", "Item");
            AddStringFallback("InventoryRedeemCode", "Redeem Code");
            AddStringFallback("InventoryFaceCamera", "Face Camera");
            AddStringFallback("HowToDrinkPotion", "Drop a potion here to drink it.");
            AddStringFallback("DrinkPotion", "Drink\nPotion");
        }

        private void AddStringFallback(string key, string value)
        {
            if (!strings.ContainsKey(key))
                strings[key] = value;
        }

        private readonly struct PythonValue
        {
            public PythonValue(string raw, bool isString)
            {
                Raw = raw ?? string.Empty;
                IsString = isString;
            }

            public string Raw { get; }
            public bool IsString { get; }

            public int AsInt(int fallback = 0)
            {
                return int.TryParse(Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
            }
        }

        private static class PythonText
        {
            public static string ExtractDictionaryBody(string source, string dictionaryName)
            {
                Match nameMatch = Regex.Match(source, $@"(?m)^(?<indent>\s*){Regex.Escape(dictionaryName)}\s*=");
                if (!nameMatch.Success)
                    return string.Empty;

                int openBrace = source.IndexOf('{', nameMatch.Index);
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

            public static List<PythonValue> ParseList(string text)
            {
                var values = new List<PythonValue>();
                if (string.IsNullOrWhiteSpace(text))
                    return values;

                int index = 0;
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != '[')
                    return values;

                index++;
                while (index < text.Length)
                {
                    SkipWhitespace(text, ref index);
                    if (index < text.Length && text[index] == ']')
                        return values;

                    values.Add(ParseValue(text, ref index));
                    SkipWhitespace(text, ref index);

                    if (index < text.Length && text[index] == ',')
                    {
                        index++;
                        continue;
                    }

                    if (index < text.Length && text[index] == ']')
                        return values;
                }

                return values;
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

            public static string ExtractFirstStringLiteral(string value)
            {
                value = value ?? string.Empty;
                for (int i = 0; i < value.Length; i++)
                {
                    if (value[i] == 'u' && i + 1 < value.Length && IsQuote(value[i + 1]))
                        i++;

                    if (IsQuote(value[i]))
                        return ParseStringLiteral(value, ref i);
                }

                return string.Empty;
            }

            public static string TrimEnclosure(string value, char open, char close)
            {
                value = (value ?? string.Empty).Trim();
                if (value.Length >= 2 && value[0] == open && value[value.Length - 1] == close)
                    return value.Substring(1, value.Length - 2);
                return value;
            }

            private static PythonValue ParseValue(string text, ref int index)
            {
                SkipWhitespace(text, ref index);

                if (index < text.Length - 1 && text[index] == 'u' && IsQuote(text[index + 1]))
                {
                    index++;
                    return new PythonValue(ParseStringLiteral(text, ref index), true);
                }

                if (index < text.Length && IsQuote(text[index]))
                    return new PythonValue(ParseStringLiteral(text, ref index), true);

                int start = index;
                while (index < text.Length && text[index] != ',' && text[index] != ']')
                    index++;

                string token = text.Substring(start, index - start).Trim();
                bool numeric = int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
                               float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                return new PythonValue(token, !numeric && !string.Equals(token, "None", StringComparison.Ordinal));
            }

            private static string ParseStringLiteral(string text, ref int index)
            {
                char quote = text[index++];
                var builder = new StringBuilder();

                while (index < text.Length)
                {
                    char current = text[index++];
                    if (current == quote)
                        return builder.ToString();

                    if (current == '\\' && index < text.Length)
                    {
                        builder.Append(UnescapePythonCharacter(text[index++]));
                        continue;
                    }

                    builder.Append(current);
                }

                return builder.ToString();
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

            private static void SkipWhitespace(string text, ref int index)
            {
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;
            }

            private static bool IsQuote(char c)
            {
                return c == '\'' || c == '"';
            }

            private static char UnescapePythonCharacter(char c)
            {
                switch (c)
                {
                    case 'n':
                        return '\n';
                    case 'r':
                        return '\r';
                    case 't':
                        return '\t';
                    case '0':
                        return '\0';
                    case '\\':
                        return '\\';
                    case '\'':
                        return '\'';
                    case '"':
                        return '"';
                    default:
                        return c;
                }
            }
        }
    }
}
