using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace POTCO.Editor.ItemCreator
{
    public sealed class PotcoSourceIndex
    {
        private readonly Dictionary<string, int> itemSymbols = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> itemConstants = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> enemySkillSymbols = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> inventoryTypeSymbols = new Dictionary<string, int>(StringComparer.Ordinal);

        public Dictionary<int, ItemDataRow> Items { get; } = new Dictionary<int, ItemDataRow>();
        public Dictionary<string, int> Columns { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<int, string> ItemNames { get; } = new Dictionary<int, string>();
        public Dictionary<int, string> InventoryTypeNames { get; } = new Dictionary<int, string>();
        public Dictionary<int, string> RarityNames { get; } = new Dictionary<int, string>();
        public Dictionary<int, string> SubtypeNames { get; } = new Dictionary<int, string>();
        public Dictionary<int, string> AttributeNames { get; } = new Dictionary<int, string>();
        public Dictionary<int, string> AttributeDescriptions { get; } = new Dictionary<int, string>();
        public Dictionary<int, string> AttributeIcons { get; } = new Dictionary<int, string>();
        public Dictionary<int, string> SkillDescriptions { get; } = new Dictionary<int, string>();
        public Dictionary<int, SkillInfoRow> Skills { get; } = new Dictionary<int, SkillInfoRow>();
        public Dictionary<string, ItemModelPose> ModelPosHpr { get; } = new Dictionary<string, ItemModelPose>(StringComparer.Ordinal);
        public float GoldSaleMultiplier { get; private set; } = 0.05f;

        public static PotcoSourceIndex LoadFromAssets()
        {
            return LoadFromAssetsPath(FindAssetsPath());
        }

        public static PotcoSourceIndex LoadFromAssetsPath(string assetsPath)
        {
            var index = new PotcoSourceIndex();
            string sourceRoot = Path.Combine(assetsPath, "Editor", "POTCO_Source");

            index.LoadItemConstants(Path.Combine(sourceRoot, "inventory", "ItemConstants.py"));
            index.LoadAttributeIcons(Path.Combine(sourceRoot, "inventory", "ItemGlobals.py"));
            index.LoadInventoryTypes(Path.Combine(sourceRoot, "uberdog", "UberDogGlobals.py"));
            index.LoadEnemySkills(Path.Combine(sourceRoot, "battle", "EnemySkills.py"));
            index.LoadItemData(Path.Combine(sourceRoot, "inventory", "ItemData.py"));
            index.LoadSkillInfo(Path.Combine(sourceRoot, "battle", "SkillInfo.py"));
            index.LoadLocalization(Path.Combine(sourceRoot, "PLocalizerEnglish.py"));
            index.ApplyFallbackNames();

            return index;
        }

        public static PotcoSourceIndex CreateForTests(ItemDataRow row)
        {
            var index = new PotcoSourceIndex();
            index.Items[row.ItemId] = row;
            index.Columns["ITEM_CLASS"] = 0;
            index.Columns["GOLD_COST"] = 2;
            index.Columns["ITEM_ID"] = 3;
            index.Columns["ITEM_NAME"] = 4;
            index.Columns["CONSTANT_NAME"] = 5;
            index.Columns["RARITY"] = 6;
            index.Columns["ITEM_TYPE"] = 7;
            index.Columns["ITEM_ICON"] = 15;
            index.Columns["FLAVOR_TEXT"] = 16;
            index.Columns["ATTRIBUTE_1_RANK"] = 21;
            index.Columns["ATTRIBUTE_1"] = 22;
            index.Columns["ATTRIBUTE_2_RANK"] = 23;
            index.Columns["ATTRIBUTE_2"] = 24;
            index.Columns["ATTRIBUTE_3_RANK"] = 25;
            index.Columns["ATTRIBUTE_3"] = 26;
            index.Columns["SKILLBOOST_1_RANK"] = 27;
            index.Columns["SKILLBOOST_1"] = 28;
            index.Columns["SKILLBOOST_2_RANK"] = 29;
            index.Columns["SKILLBOOST_2"] = 30;
            index.Columns["SKILLBOOST_3_RANK"] = 31;
            index.Columns["SKILLBOOST_3"] = 32;
            index.Columns["ITEM_MODEL"] = 34;
            index.Columns["ITEM_SUBTYPE"] = 35;
            index.Columns["POWER"] = 37;
            index.Columns["BARRELS"] = 38;
            index.Columns["SPECIAL_ATTACK_RANK"] = 39;
            index.Columns["SPECIAL_ATTACK"] = 40;
            index.AttributeIcons[105] = "buff_stun";
            index.ApplyFallbackNames();
            return index;
        }

        public int GetInt(ItemDataRow row, string columnName, int fallback = 0)
        {
            return TryGetValue(row, columnName, out ItemDataValue value) ? value.AsInt(fallback) : fallback;
        }

        public string GetString(ItemDataRow row, string columnName, string fallback = "")
        {
            return TryGetValue(row, columnName, out ItemDataValue value) ? value.Raw : fallback;
        }

        public string GetAttributeName(int itemId, int attributeId)
        {
            if (itemId == 858 && attributeId == 105)
                return "Triton's Vengeance";

            return AttributeNames.TryGetValue(attributeId, out string name) ? name : $"Attribute {attributeId}";
        }

        public string GetAttributeDescription(int itemId, int attributeId)
        {
            if (itemId == 858 && attributeId == 105)
                return "Deals bonus damage against Jumbees.";

            return AttributeDescriptions.TryGetValue(attributeId, out string description) ? description : string.Empty;
        }

        private bool TryGetValue(ItemDataRow row, string columnName, out ItemDataValue value)
        {
            value = null;
            if (!Columns.TryGetValue(columnName, out int index))
                return false;

            if (index < 0 || index >= row.Values.Count)
                return false;

            value = row.Values[index];
            return true;
        }

        private void LoadItemData(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("ItemData.py not found.", path);

            string body = ExtractDictionaryBody(File.ReadAllText(path), "itemInfo");
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(body))
            {
                string key = TrimPythonString(entry.Key);
                if (string.Equals(key, "columnHeadings", StringComparison.Ordinal))
                {
                    foreach (KeyValuePair<string, string> heading in ParseDictionaryEntries(TrimEnclosure(entry.Value, '{', '}')))
                    {
                        if (int.TryParse(heading.Value.Trim(), out int index))
                            Columns[TrimPythonString(heading.Key)] = index;
                    }
                    continue;
                }

                if (!int.TryParse(key, out int itemId))
                    continue;

                var row = new ItemDataRow(itemId, PotcoPythonValueParser.ParseList(entry.Value));
                Items[itemId] = row;

                string constantName = row.Values.Count > 5 ? row.Values[5].Raw : string.Empty;
                if (!string.IsNullOrEmpty(constantName))
                    itemSymbols[constantName] = itemId;
            }
        }

        private void LoadItemConstants(string path)
        {
            if (!File.Exists(path))
                return;

            foreach (Match match in Regex.Matches(File.ReadAllText(path), @"(?m)^(?<name>[A-Z][A-Z0-9_]*)\s*=\s*(?<value>-?\d+)\s*$"))
                itemConstants[match.Groups["name"].Value] = int.Parse(match.Groups["value"].Value);
        }

        private void LoadInventoryTypes(string path)
        {
            if (!File.Exists(path))
                return;

            foreach (Match match in Regex.Matches(File.ReadAllText(path), @"(?m)^\s*(?<name>[A-Za-z][A-Za-z0-9_]*)\s*=\s*(?<value>-?\d+)\s*$"))
                inventoryTypeSymbols[match.Groups["name"].Value] = int.Parse(match.Groups["value"].Value);
        }

        private void LoadAttributeIcons(string path)
        {
            if (!File.Exists(path))
                return;

            string text = File.ReadAllText(path);
            Match saleMultiplier = Regex.Match(text, @"(?m)^GOLD_SALE_MULTIPLIER\s*=\s*(?<value>[0-9.]+)\s*$");
            if (saleMultiplier.Success && float.TryParse(saleMultiplier.Groups["value"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float multiplier))
                GoldSaleMultiplier = multiplier;

            string body = ExtractDictionaryBody(text, "AttributeIcons");
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(body))
            {
                if (!TryResolveItemConstant(entry.Key.Trim(), out int attributeId))
                    continue;

                string iconName = TrimPythonString(entry.Value);
                if (!string.IsNullOrEmpty(iconName))
                    AttributeIcons[attributeId] = iconName;
            }

            LoadModelPosHpr(text);
        }

        private void LoadModelPosHpr(string itemGlobalsText)
        {
            string body = ExtractDictionaryBody(itemGlobalsText, "ModelPosHpr");
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(body))
            {
                string modelName = TrimPythonString(entry.Key);
                if (string.IsNullOrEmpty(modelName))
                    continue;

                List<ItemDataValue> values = PotcoPythonValueParser.ParseList(entry.Value);
                if (values.Count < 6)
                    continue;

                ModelPosHpr[modelName] = new ItemModelPose(
                    values[0].AsFloat(),
                    values[1].AsFloat(),
                    values[2].AsFloat(),
                    values[3].AsFloat(),
                    values[4].AsFloat(),
                    values[5].AsFloat());
            }
        }

        private void LoadEnemySkills(string path)
        {
            if (!File.Exists(path))
                return;

            foreach (Match match in Regex.Matches(File.ReadAllText(path), @"(?m)^\s*(?<name>[A-Z][A-Z0-9_]*)\s*=\s*(?<value>-?\d+)\s*$"))
                enemySkillSymbols[match.Groups["name"].Value] = int.Parse(match.Groups["value"].Value);
        }

        private void LoadSkillInfo(string path)
        {
            if (!File.Exists(path))
                return;

            string body = ExtractDictionaryBody(File.ReadAllText(path), "skillInfo");
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(body))
            {
                if (!int.TryParse(entry.Key.Trim(), out int skillId))
                    continue;

                List<ItemDataValue> values = PotcoPythonValueParser.ParseList(entry.Value);
                string icon = values.Count > 50 ? values[50].Raw : string.Empty;
                int track = values.Count > 39 ? values[39].AsInt() : 0;
                Skills[skillId] = new SkillInfoRow(skillId, icon, track);
            }
        }

        private void LoadLocalization(string path)
        {
            if (!File.Exists(path))
                return;

            string text = File.ReadAllText(path);
            ParseStringMap(text, "InventoryTypeNames", InventoryTypeNames, 0);
            ParseStringMap(text, "InventoryTypeNames", ItemNames, 0);
            ParseStringMap(text, "ItemRarityNames", RarityNames, 0);
            ParseStringMap(text, "ItemSubtypeNames", SubtypeNames, 0);
            ParseStringMap(text, "ItemAttributeNames", AttributeNames, 0);
            ParseStringMap(text, "ItemAttributeDescriptions", AttributeDescriptions, 0);
            ParseStringMap(text, "SkillDescriptions", SkillDescriptions, 0);
        }

        private void ParseStringMap(string source, string dictionaryName, Dictionary<int, string> target, int stringIndex)
        {
            string body = ExtractDictionaryBody(source, dictionaryName);
            foreach (KeyValuePair<string, string> entry in ParseDictionaryEntries(body))
            {
                if (!TryResolveSymbol(entry.Key.Trim(), out int id))
                    continue;

                List<string> strings = ExtractStringLiterals(entry.Value);
                if (strings.Count > stringIndex)
                    target[id] = strings[stringIndex];
            }
        }

        private bool TryResolveSymbol(string expression, out int value)
        {
            value = 0;
            expression = expression.Trim();
            if (int.TryParse(expression, out value))
                return true;

            Match match = Regex.Match(expression, @"^(?<scope>ItemGlobals|EnemySkills|InventoryType)\.(?<name>[A-Za-z0-9_]+)$");
            if (!match.Success)
                return false;

            string scope = match.Groups["scope"].Value;
            string name = match.Groups["name"].Value;

            if (scope == "ItemGlobals")
                return itemSymbols.TryGetValue(name, out value) || itemConstants.TryGetValue(name, out value);

            if (scope == "EnemySkills")
                return enemySkillSymbols.TryGetValue(name, out value);

            return inventoryTypeSymbols.TryGetValue(name, out value);
        }

        private bool TryResolveItemConstant(string expression, out int value)
        {
            value = 0;
            expression = expression.Trim();
            if (int.TryParse(expression, out value))
                return true;

            Match match = Regex.Match(expression, @"^(?<scope>ItemGlobals\.)?(?<name>[A-Za-z0-9_]+)$");
            if (!match.Success)
                return false;

            string name = match.Groups["name"].Value;
            return itemConstants.TryGetValue(name, out value) || itemSymbols.TryGetValue(name, out value);
        }

        private void ApplyFallbackNames()
        {
            RarityNames[1] = "Crude";
            RarityNames[2] = "Common";
            RarityNames[3] = "Rare";
            RarityNames[4] = "Famed";
            RarityNames[5] = "Legendary";

            SubtypeNames[1] = "Cutlass";
            SubtypeNames[2] = "Sabre";
            SubtypeNames[3] = "Broadsword";
            SubtypeNames[6] = "Pistol";
            SubtypeNames[31] = "Potion";
            SubtypeNames[39] = "Grenades";
        }

        private static string FindAssetsPath()
        {
            string envPath = Environment.GetEnvironmentVariable("POTCO_UNITY_ASSETS_PATH");
            if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
                return envPath;

            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                string assetsPath = directory.Name.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                    ? directory.FullName
                    : Path.Combine(directory.FullName, "Assets");

                if (Directory.Exists(Path.Combine(assetsPath, "Editor", "POTCO_Source")))
                    return assetsPath;

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate Assets/Editor/POTCO_Source.");
        }

        private static string ExtractDictionaryBody(string source, string dictionaryName)
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

                if (current == '\'' || current == '"')
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

            throw new FormatException($"Could not find matching '{close}'.");
        }

        private static IEnumerable<KeyValuePair<string, string>> ParseDictionaryEntries(string body)
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

        private static List<string> SplitTopLevel(string text, char separator)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            char quote = '\0';
            bool escaped = false;
            int round = 0;
            int square = 0;
            int curly = 0;

            foreach (char c in text)
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

                if (c == '\'' || c == '"')
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

                if (c == '\'' || c == '"')
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

        private static string TrimEnclosure(string value, char open, char close)
        {
            value = value.Trim();
            if (value.Length >= 2 && value[0] == open && value[value.Length - 1] == close)
                return value.Substring(1, value.Length - 2);
            return value;
        }

        private static string TrimPythonString(string value)
        {
            value = value.Trim();
            if (value.StartsWith("u'", StringComparison.Ordinal) || value.StartsWith("u\"", StringComparison.Ordinal))
                value = value.Substring(1);

            if (value.Length >= 2 && ((value[0] == '\'' && value[value.Length - 1] == '\'') || (value[0] == '"' && value[value.Length - 1] == '"')))
            {
                int index = 0;
                return ParseStringLiteral(value, ref index);
            }

            return value;
        }

        private static List<string> ExtractStringLiterals(string value)
        {
            var strings = new List<string>();
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == 'u' && i + 1 < value.Length && (value[i + 1] == '\'' || value[i + 1] == '"'))
                    i++;

                if (value[i] == '\'' || value[i] == '"')
                    strings.Add(ParseStringLiteral(value, ref i));
            }
            return strings;
        }

        private static string ParseStringLiteral(string text, ref int index)
        {
            char quote = text[index++];
            var builder = new StringBuilder();
            while (index < text.Length)
            {
                char c = text[index++];
                if (c == quote)
                {
                    index--;
                    return builder.ToString();
                }

                if (c == '\\' && index < text.Length)
                    c = text[index++];

                builder.Append(c);
            }

            return builder.ToString();
        }
    }

    public sealed class SkillInfoRow
    {
        public SkillInfoRow(int skillId, string iconName, int track)
        {
            SkillId = skillId;
            IconName = iconName ?? string.Empty;
            Track = track;
        }

        public int SkillId { get; }
        public string IconName { get; }
        public int Track { get; }
    }
}
