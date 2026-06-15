using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace POTCO.ItemCards
{
    public static class PotcoPythonValueParser
    {
        public static List<ItemDataValue> ParseList(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<ItemDataValue>();

            int index = 0;
            SkipWhitespace(text, ref index);
            if (index >= text.Length || text[index] != '[')
                throw new FormatException("Python list must start with '['.");

            index++;
            var values = new List<ItemDataValue>();

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

                throw new FormatException($"Unexpected character '{text[index]}' in Python list.");
            }

            throw new FormatException("Python list did not end with ']'.");
        }

        private static ItemDataValue ParseValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);

            if (index < text.Length - 1 && text[index] == 'u' && (text[index + 1] == '\'' || text[index + 1] == '"'))
            {
                index++;
                return new ItemDataValue(ParseString(text, ref index), true);
            }

            if (index < text.Length && (text[index] == '\'' || text[index] == '"'))
                return new ItemDataValue(ParseString(text, ref index), true);

            int start = index;
            while (index < text.Length && text[index] != ',' && text[index] != ']')
                index++;

            string token = text.Substring(start, index - start).Trim();
            if (string.Equals(token, "None", StringComparison.Ordinal))
                return new ItemDataValue(token, false);

            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
                float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return new ItemDataValue(token, false);

            return new ItemDataValue(token, true);
        }

        private static string ParseString(string text, ref int index)
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
                    char escaped = text[index++];
                    builder.Append(escaped);
                    continue;
                }

                builder.Append(current);
            }

            throw new FormatException("Python string did not end with a matching quote.");
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
        }
    }
}
