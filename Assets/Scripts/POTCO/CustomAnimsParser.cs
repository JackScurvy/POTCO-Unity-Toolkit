using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;

namespace POTCO
{
    /// <summary>
    /// Parses CustomAnims.py and provides AnimSet lookup
    /// </summary>
    public static class CustomAnimsParser
    {
        private static Dictionary<string, CustomAnimData> animSetDatabase;
        private static bool isInitialized = false;

        /// <summary>
        /// Parse CustomAnims.py and build the database
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            animSetDatabase = new Dictionary<string, CustomAnimData>();

            // Try loading from Resources first (Build-friendly)
            TextAsset customAnimsText = Resources.Load<TextAsset>("CustomAnims");
            if (customAnimsText != null)
            {
                Debug.Log("✅ CustomAnimsParser: Loaded CustomAnims from Resources");
                ParseCustomAnimsContent(customAnimsText.text);
                isInitialized = true;
                return;
            }

            // Fallback to direct file path (Editor-only legacy support)
            string filePath = Path.Combine(Application.dataPath, "Editor/POTCO_Source/leveleditor/CustomAnims.py");
            if (File.Exists(filePath))
            {
                Debug.Log("ℹ️ CustomAnimsParser: Loaded CustomAnims from Editor path");
                ParseCustomAnimsContent(File.ReadAllText(filePath));
                isInitialized = true;
                return;
            }

            Debug.LogError("❌ CustomAnimsParser: Could not find CustomAnims.txt in Resources or CustomAnims.py in Editor path!");
        }

        private static void ParseCustomAnimsContent(string content)
        {
            // Split content into lines for processing
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            CustomAnimData currentAnimSet = null;
            string currentProperty = null;
            bool inInteractAnims = false;

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Optimized Span-based check
                ReadOnlySpan<char> span = line.AsSpan().Trim();

                // Check for INTERACT_ANIMS start
                if (!inInteractAnims)
                {
                    if (span.StartsWith("INTERACT_ANIMS".AsSpan(), StringComparison.Ordinal))
                        inInteractAnims = true;
                    continue;
                }

                // Check for INTERACT_ANIMS end
                if (span.Length > 0 && span[0] == '}')
                {
                    if (currentAnimSet != null) animSetDatabase[currentAnimSet.animSetName] = currentAnimSet;
                    break;
                }

                // Check for AnimSet key: 'bar_wipe': {
                if (span.Length > 0 && span[0] == '\'' && span.EndsWith("': {".AsSpan(), StringComparison.Ordinal))
                {
                    if (currentAnimSet != null) animSetDatabase[currentAnimSet.animSetName] = currentAnimSet;

                    currentAnimSet = new CustomAnimData();
                    // Fast substring extraction
                    int endQuote = span.Slice(1).IndexOf('\'');
                    if (endQuote > 0)
                        currentAnimSet.animSetName = span.Slice(1, endQuote).ToString();

                    currentProperty = null;
                    continue;
                }

                // Check for property: 'idles': [
                if (span.Length > 0 && span[0] == '\'' && span.Contains("': [".AsSpan(), StringComparison.Ordinal))
                {
                    int endQuote = span.Slice(1).IndexOf('\'');
                    if (endQuote > 0)
                    {
                        currentProperty = span.Slice(1, endQuote).ToString();

                        // Check for inline array: 'idles': ['anim'],
                        if (span.Contains("]".AsSpan(), StringComparison.Ordinal))
                        {
                            ParsePropertySpan(currentAnimSet, currentProperty, span);
                            currentProperty = null;
                        }
                    }
                    continue;
                }

                // Parse values in multi-line array
                if (currentProperty != null && currentAnimSet != null)
                {
                    if (span.StartsWith("]".AsSpan(), StringComparison.Ordinal))
                    {
                        currentProperty = null;
                        continue;
                    }
                    ParsePropertySpan(currentAnimSet, currentProperty, span);
                }
            }
        }

        // Legacy method stub for compatibility if needed, though we replaced usage
        private static void ParseCustomAnimsFile(string filePath)
        {
            ParseCustomAnimsContent(File.ReadAllText(filePath));
        }

        // New Optimized Helper
        private static void ParsePropertySpan(CustomAnimData animSet, string property, ReadOnlySpan<char> lineSpan)
        {
            // Manually find single-quoted strings without Regex
            int startQuote = -1;

            for (int i = 0; i < lineSpan.Length; i++)
            {
                if (lineSpan[i] == '\'')
                {
                    if (startQuote == -1)
                    {
                        startQuote = i;
                    }
                    else
                    {
                        // Found matching quote
                        var valueSpan = lineSpan.Slice(startQuote + 1, i - startQuote - 1);
                        AddValueToProperty(animSet, property, valueSpan.ToString());
                        startQuote = -1;
                    }
                }
            }
        }

        private static void AddValueToProperty(CustomAnimData animSet, string property, string value)
        {
            switch (property)
            {
                case "idles":
                    animSet.idles.Add(value);
                    break;
                case "interactInto":
                    animSet.interactInto.Add(value);
                    break;
                case "interact":
                    animSet.interact.Add(value);
                    break;
                case "interactOutof":
                    animSet.interactOutof.Add(value);
                    break;
                case "noticeIdle":
                    animSet.noticeIdle.Add(value);
                    break;
                case "notice1":
                    animSet.notice1.Add(value);
                    break;
                case "notice2":
                    animSet.notice2.Add(value);
                    break;
                case "greeting":
                    animSet.greeting.Add(value);
                    break;
                case "props":
                    // Props are model paths
                    if (value.StartsWith("models/"))
                    {
                        animSet.props.Add(new PropData(value, 0)); // Default to DYNAMIC
                    }
                    break;
            }
        }

        /// <summary>
        /// Get AnimSet data by name (e.g., "bar_wipe")
        /// </summary>
        public static CustomAnimData GetAnimSet(string animSetName)
        {
            if (!isInitialized)
            {
                Initialize();
            }

            if (animSetDatabase != null && animSetDatabase.ContainsKey(animSetName))
            {
                return animSetDatabase[animSetName];
            }

            return null;
        }

        /// <summary>
        /// Check if an AnimSet exists
        /// </summary>
        public static bool HasAnimSet(string animSetName)
        {
            if (!isInitialized)
            {
                Initialize();
            }

            return animSetDatabase != null && animSetDatabase.ContainsKey(animSetName);
        }

        /// <summary>
        /// Get all AnimSet names
        /// </summary>
        public static List<string> GetAllAnimSetNames()
        {
            if (!isInitialized)
            {
                Initialize();
            }

            if (animSetDatabase == null) return new List<string>();
            return new List<string>(animSetDatabase.Keys);
        }

        /// <summary>
        /// Debug: Print all loaded AnimSets
        /// </summary>
        public static void DebugPrintAllAnimSets()
        {
            if (!isInitialized)
            {
                Initialize();
            }

            Debug.Log($"========== CustomAnims Database ({animSetDatabase.Count} entries) ==========");
            foreach (var kvp in animSetDatabase)
            {
                Debug.Log($"AnimSet: {kvp.Key}");
                Debug.Log($"  Idles: {string.Join(", ", kvp.Value.idles)}");
                Debug.Log($"  InteractInto: {string.Join(", ", kvp.Value.interactInto)}");
                Debug.Log($"  Interact: {string.Join(", ", kvp.Value.interact)}");
                Debug.Log($"  InteractOutof: {string.Join(", ", kvp.Value.interactOutof)}");
                Debug.Log($"  NoticeIdle: {string.Join(", ", kvp.Value.noticeIdle)}");
                Debug.Log($"  Notice1: {string.Join(", ", kvp.Value.notice1)}");
                Debug.Log($"  Notice2: {string.Join(", ", kvp.Value.notice2)}");
                Debug.Log($"  Greeting: {string.Join(", ", kvp.Value.greeting)}");
                Debug.Log($"  Props: {kvp.Value.props.Count} prop(s)");
            }
        }
    }
}
