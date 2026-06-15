using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace POTCO.ItemCards
{
    public sealed class ItemDataValue
    {
        public ItemDataValue(string raw, bool isString)
        {
            Raw = raw ?? string.Empty;
            IsString = isString;
        }

        public string Raw { get; set; }
        public bool IsString { get; set; }

        public static ItemDataValue FromRaw(string raw)
        {
            if (raw == null)
                raw = string.Empty;

            if (string.Equals(raw, "None", StringComparison.Ordinal))
                return new ItemDataValue(raw, false);

            bool numeric = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
                           float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            return new ItemDataValue(raw, !numeric);
        }

        public void SetRawInferringType(string raw)
        {
            ItemDataValue inferred = FromRaw(raw);
            Raw = inferred.Raw;
            IsString = inferred.IsString;
        }

        public string ToPythonLiteral()
        {
            if (!IsString)
                return Raw;

            if (Raw.Length == 0)
                return "''";

            string escaped = Raw.Replace("\\", "\\\\").Replace("'", "\\'");
            return $"u'{escaped}'";
        }

        public int AsInt(int fallback = 0)
        {
            return int.TryParse(Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        public float AsFloat(float fallback = 0f)
        {
            return float.TryParse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }
    }

    public sealed class ItemDataRow
    {
        public ItemDataRow(int itemId, IEnumerable<string> values)
            : this(itemId, values.Select(CreateValue))
        {
        }

        public ItemDataRow(int itemId, IEnumerable<ItemDataValue> values)
        {
            ItemId = itemId;
            Values = values.ToList();
        }

        public int ItemId { get; set; }
        public List<ItemDataValue> Values { get; }

        public string ToPythonEntry(string indent)
        {
            return $"{indent}{ItemId}: [{string.Join(", ", Values.Select(value => value.ToPythonLiteral()))}],";
        }

        private static ItemDataValue CreateValue(string raw)
        {
            if (raw == null)
                raw = string.Empty;
            return ItemDataValue.FromRaw(raw);
        }
    }

    public enum PotcoItemClass
    {
        Weapon = 51,
        Clothing = 52,
        Tattoo = 53,
        Jewelry = 54,
        Music = 55,
        Charm = 56,
        Consumable = 57
    }

    public enum ItemPreviewMode
    {
        Icon,
        Model,
        GenericPlayer
    }

    public sealed class ItemCardLine
    {
        public string Title { get; set; } = string.Empty;
        public string Rank { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconName { get; set; } = string.Empty;
    }

    public sealed class ItemCardData
    {
        public int ItemId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string GoldCost { get; set; } = string.Empty;
        public string AttackPower { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string IconName { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public int Rarity { get; set; }
        public PotcoItemClass ItemClass { get; set; }
        public ItemPreviewMode PreviewMode { get; set; } = ItemPreviewMode.Icon;
        public List<ItemCardLine> Lines { get; } = new List<ItemCardLine>();
    }

    public sealed class ItemModelPose
    {
        public ItemModelPose(float x, float y, float z, float h, float p, float r)
        {
            X = x;
            Y = y;
            Z = z;
            H = h;
            P = p;
            R = r;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float H { get; }
        public float P { get; }
        public float R { get; }
    }
}
