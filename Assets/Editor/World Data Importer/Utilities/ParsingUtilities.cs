using UnityEngine;
using System.Text.RegularExpressions;

namespace WorldDataImporter.Utilities
{
    public static class ParsingUtilities
    {
        private static readonly Regex objIdRegex = new(@"^\s*'(\d+\.\d+\w*)':\s*{");
        private static readonly Regex propRegex = new(@"^\s*'([^']+)':\s*(.*)");
        private static readonly Regex modelPathRegex = new(@"'([^']+)'");
        private static readonly Regex colorRegex = new(@"\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*\)");
        private static readonly Regex boolRegex = new(@"(True|False)");

        public static bool IsObjectId(string line, out string objectId)
        {
            objectId = null;
            if (!objIdRegex.IsMatch(line)) return false;
            
            Match m = objIdRegex.Match(line);
            objectId = m.Groups[1].Value;
            return true;
        }

        public static bool IsProperty(string line, out string key, out string value)
        {
            key = null;
            value = null;
            if (!propRegex.IsMatch(line)) return false;
            
            Match pm = propRegex.Match(line);
            key = pm.Groups[1].Value;
            value = pm.Groups[2].Value.Trim().TrimEnd(',');
            return true;
        }

        public static bool ExtractModelPath(string value, out string modelPath)
        {
            modelPath = null;
            Match modelMatch = modelPathRegex.Match(value);
            if (!modelMatch.Success) return false;
            
            modelPath = modelMatch.Groups[1].Value;
            return true;
        }

        public static Vector3 ParseVector3(string val, Vector3 fallback = default)
        {
            Match m = Regex.Match(val, @"\(?\s*([-+]?[0-9]*\.?[0-9]+)[f]?\s*,\s*([-+]?[0-9]*\.?[0-9]+)[f]?\s*,\s*([-+]?[0-9]*\.?[0-9]+)[f]?\s*\)?");
            if (!m.Success)
            {
                return fallback == default ? Vector3.zero : fallback;
            }

            float x = float.Parse(m.Groups[1].Value);
            float y = float.Parse(m.Groups[2].Value);
            float z = float.Parse(m.Groups[3].Value);

            return new Vector3(x, z, y);
        }

        public static bool ParseColor(string value, out Color color)
        {
            color = Color.white;
            Match m = colorRegex.Match(value);
            if (!m.Success) return false;

            try
            {
                float r = float.Parse(m.Groups[1].Value);
                float g = float.Parse(m.Groups[2].Value);
                float b = float.Parse(m.Groups[3].Value);
                float a = float.Parse(m.Groups[4].Value);
                color = new Color(r, g, b, a);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool ParseBool(string value, out bool result)
        {
            result = false;
            Match m = boolRegex.Match(value);
            if (!m.Success) return false;

            result = m.Groups[1].Value == "True";
            return true;
        }

        public static string ExtractStringValue(string value)
        {
            return value.Trim('\'', '"', ' ');
        }
    }
}
