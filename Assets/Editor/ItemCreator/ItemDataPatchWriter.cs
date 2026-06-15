using System;
using System.Text.RegularExpressions;
using POTCO.ItemCards;

namespace POTCO.Editor.ItemCreator
{
    public static class ItemDataPatchWriter
    {
        public static string PatchItemData(string source, ItemDataRow row)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            Match existing = FindExistingEntry(source, row.ItemId);
            if (existing.Success)
            {
                int listStart = source.IndexOf('[', existing.Index, existing.Length);
                int listEnd = FindMatchingListEnd(source, listStart);
                int replaceEnd = listEnd + 1;

                if (replaceEnd < source.Length && source[replaceEnd] == ',')
                    replaceEnd++;

                string lead = existing.Groups["lead"].Value;
                string indent = existing.Groups["indent"].Value;
                string replacement = lead + row.ToPythonEntry(indent);
                return source.Substring(0, existing.Index) + replacement + source.Substring(replaceEnd);
            }

            return InsertBeforeColumnHeadings(source, row);
        }

        private static Match FindExistingEntry(string source, int itemId)
        {
            string pattern = $@"(?m)(?<lead>^|[{{,\r\n])(?<indent>[ \t]*){itemId}\s*:\s*\[";
            return Regex.Match(source, pattern);
        }

        private static int FindMatchingListEnd(string source, int listStart)
        {
            if (listStart < 0 || listStart >= source.Length || source[listStart] != '[')
                throw new FormatException("Could not find item row list start.");

            char quote = '\0';
            bool escaped = false;
            int depth = 0;

            for (int i = listStart; i < source.Length; i++)
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

                if (current == '[')
                {
                    depth++;
                    continue;
                }

                if (current == ']')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            throw new FormatException("Could not find item row list end.");
        }

        private static string InsertBeforeColumnHeadings(string source, ItemDataRow row)
        {
            Match columnHeadings = Regex.Match(source, @"(?m)^(?<indent>[ \t]*)['""]columnHeadings['""]\s*:");
            if (!columnHeadings.Success)
                throw new FormatException("Could not find columnHeadings insertion point.");

            string newline = source.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            string entry = row.ToPythonEntry(columnHeadings.Groups["indent"].Value) + newline;
            return source.Insert(columnHeadings.Index, entry);
        }
    }
}
