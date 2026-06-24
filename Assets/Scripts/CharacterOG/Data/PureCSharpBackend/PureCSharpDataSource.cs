/// <summary>
/// Pure C# data source implementation.
/// Parses Python source files using OgPyReader and converts to Unity models.
/// No Python execution - literal data only.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using CharacterOG.Models;

namespace CharacterOG.Data.PureCSharpBackend
{
    public class PureCSharpDataSource : IOgDataSource
    {
        public string BackendName => "Pure C# Parser";
        public bool IsAvailable => true;

        private Dictionary<string, List<string>> bodyShapeIndexMaps = new Dictionary<string, List<string>>();

        // ========== PHASE 1: GLOBAL DATA CACHING ==========
        // Static caches shared across all PureCSharpDataSource instances for performance
        private static Palettes s_cachedPalettes = null;
        private static Dictionary<string, BodyShapeDef> s_cachedBodyShapes = null;
        private static Dictionary<string, List<string>> s_cachedBodyShapeIndexMaps = null;
        private static Dictionary<string, ClothingCatalog> s_cachedClothingCatalogs = new Dictionary<string, ClothingCatalog>();
        private static Dictionary<string, JewelryTattooDefs> s_cachedJewelryTattoos = new Dictionary<string, JewelryTattooDefs>();
        private static Dictionary<string, FacialMorphDatabase> s_cachedFacialMorphs = new Dictionary<string, FacialMorphDatabase>();
        private static Dictionary<string, PirateDNA> s_cachedNpcDna = null;
        private static readonly object s_cacheLock = new object();

        /// <summary>Clear all static caches (useful for editor refresh)</summary>
        public static void ClearCache()
        {
            lock (s_cacheLock)
            {
                s_cachedPalettes = null;
                s_cachedBodyShapes = null;
                s_cachedBodyShapeIndexMaps = null;
                s_cachedClothingCatalogs.Clear();
                s_cachedJewelryTattoos.Clear();
                s_cachedFacialMorphs.Clear();
                s_cachedNpcDna = null;
                Debug.Log("[PureCSharpDataSource] Static caches cleared");
            }
        }

        /// <summary>Get cache statistics for debugging</summary>
        public static string GetCacheStats()
        {
            lock (s_cacheLock)
            {
                return $"Cache Stats: " +
                    $"Palettes={s_cachedPalettes != null}, " +
                    $"BodyShapes={s_cachedBodyShapes != null}, " +
                    $"ClothingCatalogs={s_cachedClothingCatalogs.Count}, " +
                    $"JewelryTattoos={s_cachedJewelryTattoos.Count}, " +
                    $"FacialMorphs={s_cachedFacialMorphs.Count}, " +
                    $"NpcDna={s_cachedNpcDna != null}";
            }
        }
        // ========== END PHASE 1 CACHING ==========

        public Dictionary<string, BodyShapeDef> LoadBodyShapes(string gender = "m")
        {
            // PHASE 1 OPTIMIZATION: Check static cache first
            lock (s_cacheLock)
            {
                if (s_cachedBodyShapes != null)
                {
                    Debug.Log("[LoadBodyShapes] Using cached body shapes");
                    CopyCachedBodyShapeIndexMaps();
                    return s_cachedBodyShapes;
                }
            }

            // Cache miss - load from disk
            Debug.Log("[LoadBodyShapes] Cache miss - loading from disk");
            var reader = new OgPyReader("", OgPaths.BodyDefs);
            var data = reader.ParseFile(OgPaths.BodyDefs);

            var shapes = new Dictionary<string, BodyShapeDef>();
            var indexMaps = new Dictionary<string, List<string>>();

            // Load MaleBodies and FemaleBodies lists (index → body shape name mapping)
            if (data.TryGetValue("MaleBodies", out var maleBodiesNode) && maleBodiesNode is PyList maleBodiesList)
            {
                var maleBodyNames = new List<string>();
                foreach (var item in maleBodiesList.items)
                {
                    if (item is PyVariable varRef)
                        maleBodyNames.Add(varRef.name);
                }
                indexMaps["m"] = maleBodyNames;
                Debug.Log($"Loaded MaleBodies mapping: {string.Join(", ", maleBodyNames)}");
            }

            if (data.TryGetValue("FemaleBodies", out var femaleBodiesNode) && femaleBodiesNode is PyList femaleBodiesList)
            {
                var femaleBodyNames = new List<string>();
                foreach (var item in femaleBodiesList.items)
                {
                    if (item is PyVariable varRef)
                        femaleBodyNames.Add(varRef.name);
                }
                indexMaps["f"] = femaleBodyNames;
                Debug.Log($"Loaded FemaleBodies mapping: {string.Join(", ", femaleBodyNames)}");
            }

            // Load all body shape definitions
            foreach (var kvp in data)
            {
                if (kvp.Value is PyDict dict)
                {
                    shapes[kvp.Key] = ConvertBodyShape(kvp.Key, dict);
                }
            }

            bodyShapeIndexMaps = CloneBodyShapeIndexMaps(indexMaps);

            // Store in cache
            lock (s_cacheLock)
            {
                s_cachedBodyShapes = shapes;
                s_cachedBodyShapeIndexMaps = CloneBodyShapeIndexMaps(indexMaps);
                Debug.Log($"[LoadBodyShapes] Cached {shapes.Count} body shapes");
            }

            return shapes;
        }

        private void EnsureBodyShapeIndexMapsLoaded()
        {
            if (bodyShapeIndexMaps.Count > 0)
                return;

            LoadBodyShapes("m");
        }

        private void CopyCachedBodyShapeIndexMaps()
        {
            if (s_cachedBodyShapeIndexMaps == null || s_cachedBodyShapeIndexMaps.Count == 0)
                return;

            bodyShapeIndexMaps = CloneBodyShapeIndexMaps(s_cachedBodyShapeIndexMaps);
        }

        private static Dictionary<string, List<string>> CloneBodyShapeIndexMaps(Dictionary<string, List<string>> source)
        {
            var clone = new Dictionary<string, List<string>>();
            if (source == null)
                return clone;

            foreach (var kvp in source)
                clone[kvp.Key] = new List<string>(kvp.Value);

            return clone;
        }

        public string GetBodyShapeNameFromIndex(string gender, int index)
        {
            EnsureBodyShapeIndexMapsLoaded();

            string genderKey = gender.ToLower();
            if (bodyShapeIndexMaps.TryGetValue(genderKey, out var list))
            {
                if (index >= 0 && index < list.Count)
                    return list[index];
            }
            // Fallback for old behavior
            return index switch
            {
                7 => genderKey == "f" ? "FemaleIdeal" : "MaleIdeal",
                _ => genderKey == "f" ? "FemaleIdeal" : "MaleIdeal"
            };
        }

        public Palettes LoadPalettesAndDyeRules()
        {
            // PHASE 1 OPTIMIZATION: Check static cache first
            lock (s_cacheLock)
            {
                if (s_cachedPalettes != null)
                {
                    Debug.Log("[LoadPalettesAndDyeRules] Using cached palettes");
                    return s_cachedPalettes;
                }
            }

            // Cache miss - load from disk
            Debug.Log($"[LoadPalettesAndDyeRules] Cache miss - loading from {OgPaths.HumanDNA}");

            var reader = new OgPyReader("", OgPaths.HumanDNA);
            var data = reader.ParseFile(OgPaths.HumanDNA);

            Debug.Log($"LoadPalettesAndDyeRules: Parsed {data.Count} top-level variables: {string.Join(", ", data.Keys)}");

            var palettes = new Palettes();

            // Load face and iris textures for BOTH genders (since NPCs can be either)
            LoadFaceAndIrisTextures(palettes, "m");  // Male face textures
            LoadFaceAndIrisTextures(palettes, "f");  // Female face textures

            // Load skin colors
            if (data.TryGetValue("skinColors", out var skinNode) && skinNode is PyList skinList)
            {
                foreach (var item in skinList.items)
                {
                    if (item is PyFunctionCall call)
                        palettes.skin.Add(call.ToColor());
                }
            }

            // Load hair colors
            Debug.Log($"LoadPalettesAndDyeRules: Looking for hairColors...");
            if (data.TryGetValue("hairColors", out var hairNode))
            {
                Debug.Log($"Found hairColors! Type: {hairNode.GetType().Name}");
                if (hairNode is PyList hairList)
                {
                    Debug.Log($"hairColors is PyList with {hairList.items.Count} items");
                    foreach (var item in hairList.items)
                    {
                        if (item is PyFunctionCall call)
                            palettes.hair.Add(call.ToColor());
                    }
                }
            }
            else
            {
                Debug.LogWarning("hairColors NOT FOUND in parsed data");
            }

            // Load hat colors (hatColorsOld - array of arrays)
            if (data.TryGetValue("hatColorsOld", out var hatColorsNode) && hatColorsNode is PyList hatColorsList)
            {
                foreach (var item in hatColorsList.items)
                {
                    if (item is PyList colorList)
                    {
                        var colors = new List<Color>();
                        foreach (var colorItem in colorList.items)
                        {
                            if (colorItem is PyFunctionCall call)
                                colors.Add(call.ToColor());
                        }
                        palettes.hatColors.Add(colors);
                    }
                }

                // Also populate dye palette from first element for backward compatibility
                if (hatColorsList.items.Count > 0 && hatColorsList.items[0] is PyList dyeList)
                {
                    foreach (var item in dyeList.items)
                    {
                        if (item is PyFunctionCall call)
                            palettes.dye.Add(call.ToColor());
                    }
                }
            }

            // Load crazy skin colors
            if (data.TryGetValue("crazySkinColors", out var crazySkinNode) && crazySkinNode is PyList crazySkinList)
            {
                foreach (var item in crazySkinList.items)
                {
                    if (item is PyFunctionCall call)
                        palettes.crazySkin.Add(call.ToColor());
                }
            }

            // Load jewelry colors
            if (data.TryGetValue("jewelryColors", out var jewelryNode) && jewelryNode is PyList jewelryList)
            {
                foreach (var item in jewelryList.items)
                {
                    if (item is PyFunctionCall call)
                        palettes.jewelry.Add(call.ToColor());
                }
            }

            // Load top clothing colors (clothesTopColorsOld - array of arrays)
            if (data.TryGetValue("clothesTopColorsOld", out var topColorsNode) && topColorsNode is PyList topColorsList)
            {
                foreach (var item in topColorsList.items)
                {
                    if (item is PyList colorList)
                    {
                        var colors = new List<Color>();
                        foreach (var colorItem in colorList.items)
                        {
                            if (colorItem is PyFunctionCall call)
                                colors.Add(call.ToColor());
                        }
                        palettes.clothesTopColors.Add(colors);
                    }
                }
            }

            // Load bottom clothing colors (clothesBotColorsOld - array of arrays)
            if (data.TryGetValue("clothesBotColorsOld", out var botColorsNode) && botColorsNode is PyList botColorsList)
            {
                foreach (var item in botColorsList.items)
                {
                    if (item is PyList colorList)
                    {
                        var colors = new List<Color>();
                        foreach (var colorItem in colorList.items)
                        {
                            if (colorItem is PyFunctionCall call)
                                colors.Add(call.ToColor());
                        }
                        palettes.clothesBotColors.Add(colors);
                    }
                }
            }

            // Load DYE_COLOR_LEVEL
            if (data.TryGetValue("DYE_COLOR_LEVEL", out var dyeLevelNode) && dyeLevelNode is PyDict dyeLevelDict)
            {
                foreach (var kvp in dyeLevelDict.items)
                {
                    if (int.TryParse(kvp.Key, out int level) && kvp.Value is PyList levelList)
                    {
                        palettes.dyeColorLevels[level] = new List<int>();
                        foreach (var item in levelList.items)
                        {
                            if (item is PyNumber num)
                                palettes.dyeColorLevels[level].Add(num.AsInt());
                        }
                    }
                }
            }

            Debug.Log($"Loaded color palettes: {palettes.skin.Count} skin, {palettes.hair.Count} hair, {palettes.dye.Count} dye, " +
                     $"{palettes.hatColors.Count} hat sets, {palettes.crazySkin.Count} crazy skin, {palettes.jewelry.Count} jewelry, " +
                     $"{palettes.clothesTopColors.Count} top sets, {palettes.clothesBotColors.Count} bot sets");

            // Store in cache
            lock (s_cacheLock)
            {
                s_cachedPalettes = palettes;
                Debug.Log("[LoadPalettesAndDyeRules] Cached palettes");
            }

            return palettes;
        }

        public ClothingCatalog LoadClothingCatalog(string gender = "m")
        {
            // PHASE 1 OPTIMIZATION: Check static cache first
            string genderKey = gender.ToLower();
            lock (s_cacheLock)
            {
                if (s_cachedClothingCatalogs.TryGetValue(genderKey, out var cachedCatalog))
                {
                    Debug.Log($"[LoadClothingCatalog] Using cached clothing catalog for gender '{genderKey}'");
                    return cachedCatalog;
                }
            }

            // Cache miss - load from disk
            Debug.Log($"[LoadClothingCatalog] Cache miss - loading catalog for gender '{genderKey}'");
            var catalog = new ClothingCatalog();

            try
            {
                // Load ClothingGlobals.py
                Debug.Log($"Loading ClothingGlobals.py from {OgPaths.ClothingGlobals}");
                var globalsReader = new OgPyReader("", OgPaths.ClothingGlobals);
                var globalsData = globalsReader.ParseFile(OgPaths.ClothingGlobals);
                Debug.Log($"Parsed ClothingGlobals.py - Found {globalsData.Count} top-level variables: {string.Join(", ", globalsData.Keys)}");

                // Load gender-specific file
                string genderFile = OgPaths.GetPirateGenderFile(gender);
                Debug.Log($"Loading gender file {genderFile}");
                var genderReader = new OgPyReader("", genderFile);
                var genderData = genderReader.ParseFile(genderFile);
                Debug.Log($"Parsed gender file - Found {genderData.Count} top-level variables");

                // Parse CLOTHING_NAMES for display names
                if (globalsData.TryGetValue("CLOTHING_NAMES", out var clothingNamesNode) && clothingNamesNode is PyDict clothingNamesDict)
                {
                    Debug.Log($"Found CLOTHING_NAMES with {clothingNamesDict.items.Count} slots");
                    ParseClothingNames(catalog, clothingNamesDict, gender);
                }
                else
                {
                    Debug.LogWarning($"CLOTHING_NAMES not found or not a dict. Available keys: {string.Join(", ", globalsData.Keys)}");
                }

                // Parse UNDERWEAR
                if (globalsData.TryGetValue("UNDERWEAR", out var underwearNode) && underwearNode is PyDict underwearDict)
                {
                    ParseUnderwear(catalog, underwearDict);
                }

                // NEW: Parse layer lists and clothing arrays from gender file to get group patterns and body hides
                ParseClothingPatterns(catalog, genderData, gender);

                // Parse textures from ClothingGlobals.py
                if (globalsData.TryGetValue("textures", out var texturesNode) && texturesNode is PyDict texturesDict)
                {
                    Debug.Log($"Found textures dictionary");
                    ParseClothingTextures(catalog, texturesDict, gender);
                }
                else
                {
                    Debug.LogWarning($"textures not found in ClothingGlobals.py");
                }

                Debug.Log($"ClothingCatalog loaded: {catalog.variantsBySlot.Sum(kvp => kvp.Value.Count)} total variants across {catalog.variantsBySlot.Count} slots");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load ClothingCatalog: {ex.Message}\n{ex.StackTrace}");
            }

            // Store in cache
            lock (s_cacheLock)
            {
                s_cachedClothingCatalogs[genderKey] = catalog;
                Debug.Log($"[LoadClothingCatalog] Cached clothing catalog for gender '{genderKey}'");
            }

            return catalog;
        }

        public JewelryTattooDefs LoadJewelryAndTattoos(string gender = "m")
        {
            // PHASE 1 OPTIMIZATION: Check static cache first
            string genderKey = gender.ToLower();
            lock (s_cacheLock)
            {
                if (s_cachedJewelryTattoos.TryGetValue(genderKey, out var cachedDefs))
                {
                    Debug.Log($"[LoadJewelryAndTattoos] Using cached jewelry/tattoos for gender '{genderKey}'");
                    return cachedDefs;
                }
            }

            // Cache miss - load from disk
            Debug.Log($"[LoadJewelryAndTattoos] Cache miss - loading for gender '{genderKey}'");
            var defs = new JewelryTattooDefs();

            string genderFile = OgPaths.GetPirateGenderFile(gender);
            var reader = new OgPyReader("", genderFile);
            var data = reader.ParseFile(genderFile);

            // Load jewelry_geos_face (the parts array)
            var jewelryFaceParts = new List<string>();
            if (data.TryGetValue("jewelry_geos_face", out var faceNode) && faceNode is PyList faceList)
            {
                foreach (var item in faceList.items)
                {
                    if (item is PyString str)
                        jewelryFaceParts.Add(str.value);
                }
            }

            // Load jewelry_geos_body (the parts array)
            var jewelryBodyParts = new List<string>();
            if (data.TryGetValue("jewelry_geos_body", out var bodyNode) && bodyNode is PyList bodyList)
            {
                foreach (var item in bodyList.items)
                {
                    if (item is PyString str)
                        jewelryBodyParts.Add(str.value);
                }
            }

            // Load jewelry_options and build jewelrySets (PirateMale.py lines 1914-1940)
            if (data.TryGetValue("jewelry_options", out var optionsNode) && optionsNode is PyDict optionsDict)
            {
                foreach (var kvp in optionsDict.items)
                {
                    string zoneName = kvp.Key; // LEar, REar, LBrow, etc.
                    if (!(kvp.Value is PyList piecesList)) continue;

                    // Choose face or body parts based on zone
                    var parts = (zoneName == "LHand" || zoneName == "RHand") ? jewelryBodyParts : jewelryFaceParts;

                    var zoneGroups = new List<string>();
                    foreach (var pieceNode in piecesList.items)
                    {
                        if (!(pieceNode is PyList piece)) continue;

                        // Each piece is an array of indices into jewelry_geos_face/body
                        // We take the first index as the primary group name
                        if (piece.items.Count > 0 && piece.items[0] is PyNumber num)
                        {
                            int idx = num.AsInt();
                            if (idx >= 0 && idx < parts.Count)
                            {
                                zoneGroups.Add(parts[idx]);
                            }
                        }
                    }

                    defs.jewelryGroupsByZone[zoneName] = zoneGroups;
                }
            }

            // Load vector_tattoos for tattoo zone mapping (indices map to zones)
            if (data.TryGetValue("vector_tattoos", out var tattoosNode) && tattoosNode is PyList tattoosList)
            {
                for (int i = 0; i < tattoosList.items.Count; i++)
                {
                    defs.tattooZonesToBodyGroups[$"zone{i}"] = new List<string> { $"body_zone{i}" };
                }
            }

            Debug.Log($"Loaded jewelry for {gender}: {string.Join(", ", defs.jewelryGroupsByZone.Keys)}");

            // Store in cache
            lock (s_cacheLock)
            {
                s_cachedJewelryTattoos[genderKey] = defs;
                Debug.Log($"[LoadJewelryAndTattoos] Cached jewelry/tattoos for gender '{genderKey}'");
            }

            return defs;
        }

        public Dictionary<string, PirateDNA> LoadNpcDna()
        {
            EnsureBodyShapeIndexMapsLoaded();

            // PHASE 1 OPTIMIZATION: Check static cache first
            lock (s_cacheLock)
            {
                if (s_cachedNpcDna != null)
                {
                    Debug.Log("[LoadNpcDna] Using cached NPC DNA database");
                    return s_cachedNpcDna;
                }
            }

            // Cache miss - load from disk
            Debug.Log("[LoadNpcDna] Cache miss - loading NPC DNA database");
            var npcs = new Dictionary<string, PirateDNA>();

            if (!File.Exists(OgPaths.NPCList))
            {
                Debug.LogError($"NPCList.py not found at: {OgPaths.NPCList}");
                return npcs;
            }

            // Load NPC display names from PQuestStringsEnglish.py
            var npcNames = LoadNpcNames();

            var reader = new OgPyReader("", OgPaths.NPCList);
            Dictionary<string, PyNode> data;

            try
            {
                Debug.Log("Parsing NPCList.py (35k+ lines, this may take a moment)...");
                data = reader.ParseFile(OgPaths.NPCList);
                Debug.Log($"Successfully parsed NPCList.py - Found {data.Count} top-level variables: {string.Join(", ", data.Keys)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse NPCList.py: {ex.Message}\n{ex.StackTrace}");
                return npcs;
            }

            if (!data.TryGetValue("NPC_LIST", out var npcListNode) || !(npcListNode is PyDict npcListDict))
            {
                Debug.LogWarning($"NPC_LIST not found in NPCList.py. Available keys: {string.Join(", ", data.Keys)}");
                return npcs;
            }

            foreach (var kvp in npcListDict.items)
            {
                string npcId = kvp.Key;

                if (kvp.Value is PyDict npcDict)
                {
                    try
                    {
                        var dna = ConvertNpcDict(npcId, npcDict);

                        // Use display name from PQuestStringsEnglish if available
                        if (npcNames.TryGetValue(npcId, out string displayName))
                        {
                            dna.name = displayName;
                        }

                        npcs[npcId] = dna;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to parse NPC {npcId}: {ex.Message}");
                    }
                }
            }

            Debug.Log($"Loaded {npcs.Count} NPCs, {npcNames.Count} have display names");

            // Store in cache
            lock (s_cacheLock)
            {
                s_cachedNpcDna = npcs;
                Debug.Log($"[LoadNpcDna] Cached {npcs.Count} NPC DNA entries");
            }

            return npcs;
        }

        private Dictionary<string, string> LoadNpcNames()
        {
            var names = new Dictionary<string, string>();

            if (!File.Exists(OgPaths.NPCNames))
            {
                Debug.LogWarning($"PQuestStringsEnglish.py not found at: {OgPaths.NPCNames}");
                return names;
            }

            try
            {
                var reader = new OgPyReader("", OgPaths.NPCNames);
                var data = reader.ParseFile(OgPaths.NPCNames);

                // Parse from NPCNames dictionary (line 7737+)
                if (data.TryGetValue("NPCNames", out var npcNamesNode) && npcNamesNode is PyDict npcNamesDict)
                {
                    foreach (var kvp in npcNamesDict.items)
                    {
                        if (kvp.Value is PyString nameStr && !string.IsNullOrEmpty(nameStr.value))
                        {
                            names[kvp.Key] = nameStr.value;
                        }
                    }
                    Debug.Log($"Loaded {names.Count} NPC display names from NPCNames in PQuestStringsEnglish.py");
                }
                else
                {
                    Debug.LogWarning("NPCNames dictionary not found in PQuestStringsEnglish.py");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load NPC names from PQuestStringsEnglish.py: {ex.Message}");
            }

            return names;
        }

        // ===== CONVERSION HELPERS =====

        private BodyShapeDef ConvertBodyShape(string name, PyDict dict)
        {
            var shape = new BodyShapeDef(name);

            shape.headScale = dict.GetFloat("headScale", 1f);
            shape.bodyScale = dict.GetFloat("bodyScale", 1f);
            shape.heightBias = dict.GetFloat("heightBias", 0f);
            shape.frameType = dict.GetString("frameType", "");
            shape.animType = dict.GetString("AnimType", "");

            // Parse headPostion (VBase3)
            if (dict.Get<PyFunctionCall>("headPostion") is PyFunctionCall headPosCall)
            {
                shape.headPosition = headPosCall.ToVector3();
            }

            // Parse bodyTextures
            if (dict.GetList("bodyTextures") is PyList bodyTexList)
            {
                foreach (var item in bodyTexList.items)
                {
                    if (item is PyString str)
                        shape.bodyTextures.Add(str.value);
                }
            }

            // Parse boneScales
            if (dict.GetDict("boneScales") is PyDict boneScalesDict)
            {
                foreach (var boneKvp in boneScalesDict.items)
                {
                    if (boneKvp.Value is PyFunctionCall call)
                    {
                        shape.boneScales[boneKvp.Key] = call.ToVector3();
                    }
                }
            }

            // Parse bone offsets (tr_* entries in boneScales are actually offsets)
            // In OG data, tr_* bones are position offsets, not scales
            var offsetKeys = shape.boneScales.Keys.Where(k => k.StartsWith("tr_")).ToList();
            foreach (var key in offsetKeys)
            {
                shape.boneOffsets[key] = shape.boneScales[key];
                shape.boneScales.Remove(key);
            }

            return shape;
        }

        private void ParseClothingNames(ClothingCatalog catalog, PyDict clothingNamesDict, string gender)
        {
            int totalVariants = 0;
            foreach (var slotKvp in clothingNamesDict.items)
            {
                if (!int.TryParse(slotKvp.Key, out int slotNum))
                {
                    Debug.LogWarning($"Skipping non-numeric slot key: {slotKvp.Key}");
                    continue;
                }

                Slot slot = (Slot)slotNum;

                if (slotKvp.Value is PyDict genderDict)
                {
                    string genderKey = gender.ToUpper() == "F" ? "FEMALE" : "MALE";

                    if (genderDict.items.TryGetValue(genderKey, out var variantsNode) && variantsNode is PyDict variantsDict)
                    {
                        int slotVariants = 0;
                        foreach (var variantKvp in variantsDict.items)
                        {
                            if (!int.TryParse(variantKvp.Key, out int idx))
                            {
                                Debug.LogWarning($"Skipping non-numeric variant index for slot {slot}: {variantKvp.Key}");
                                continue;
                            }

                            if (variantKvp.Value is PyString nameStr)
                            {
                                var variant = new SlotVariant
                                {
                                    id = nameStr.value.ToLower().Replace(" ", "_"),
                                    displayName = nameStr.value,
                                    ogIndex = idx
                                };

                                catalog.variantsBySlot[slot].Add(variant);
                                slotVariants++;
                                totalVariants++;
                            }
                            else
                            {
                                Debug.LogWarning($"Variant value for slot {slot}[{idx}] is not a string, it's {variantKvp.Value?.GetType().Name}");
                            }
                        }
                        Debug.Log($"Loaded {slotVariants} variants for slot {slot} ({genderKey})");
                    }
                    else
                    {
                        Debug.LogWarning($"Gender key '{genderKey}' not found or not a dict for slot {slot}. Available keys: {string.Join(", ", genderDict.items.Keys)}");
                    }
                }
                else
                {
                    Debug.LogWarning($"Slot {slotNum} value is not a dict, it's {slotKvp.Value?.GetType().Name}");
                }
            }
            Debug.Log($"ParseClothingNames total: {totalVariants} variants");
        }

        private void LoadFaceAndIrisTextures(Palettes palettes, string gender)
        {
            string genderFile = OgPaths.GetPirateGenderFile(gender);
            var reader = new OgPyReader("", genderFile);
            var data = reader.ParseFile(genderFile);

            // Determine which face texture list to use based on gender
            var faceTextureList = gender.ToLower() == "f" ? palettes.femaleFaceTextures : palettes.maleFaceTextures;
            string genderLabel = gender.ToLower() == "f" ? "female" : "male";

            // Load face_textures (gender-specific)
            if (data.TryGetValue("face_textures", out var faceTexNode) && faceTexNode is PyList faceTexList)
            {
                foreach (var item in faceTexList.items)
                {
                    if (item is PyString str)
                    {
                        faceTextureList.Add(str.value);
                    }
                }
                Debug.Log($"[LoadFaceAndIrisTextures] Loaded {faceTextureList.Count} {genderLabel} face textures");
            }

            // Load eye_iris_textures (shared) - only load once
            if (palettes.irisTextures.Count == 0 && data.TryGetValue("eye_iris_textures", out var irisTexNode) && irisTexNode is PyList irisTexList)
            {
                foreach (var item in irisTexList.items)
                {
                    if (item is PyString str)
                    {
                        palettes.irisTextures.Add(str.value);
                    }
                }
                Debug.Log($"[LoadFaceAndIrisTextures] Loaded {palettes.irisTextures.Count} iris textures");
            }
        }

        private void ParseUnderwear(ClothingCatalog catalog, PyDict underwearDict)
        {
            // Map constant names to slot numbers (from ClothingGlobals.py lines 8-15)
            var constantToSlot = new Dictionary<string, int>
            {
                { "HAT", 0 },
                { "SHIRT", 1 },
                { "VEST", 2 },
                { "COAT", 3 },
                { "PANT", 4 },
                { "BELT", 5 },
                { "SOCK", 6 },
                { "SHOE", 7 }
            };

            foreach (var genderKvp in underwearDict.items)
            {
                string gender = genderKvp.Key;

                if (genderKvp.Value is PyDict slotsDict)
                {
                    var underwearSet = new Dictionary<Slot, (int idx, int texIdx, int colorIdx)>();

                    Debug.Log($"[ParseUnderwear] Gender '{gender}': Found {slotsDict.items.Count} slots, keys: {string.Join(", ", slotsDict.items.Keys)}");

                    foreach (var slotKvp in slotsDict.items)
                    {
                        int slotNum;

                        // Try to parse as number first, then try constant name
                        if (!int.TryParse(slotKvp.Key, out slotNum))
                        {
                            if (constantToSlot.TryGetValue(slotKvp.Key, out slotNum))
                            {
                                Debug.Log($"[ParseUnderwear] Gender '{gender}': Converted constant '{slotKvp.Key}' to slot number {slotNum}");
                            }
                            else
                            {
                                Debug.LogWarning($"[ParseUnderwear] Gender '{gender}': Unknown constant '{slotKvp.Key}'");
                                continue;
                            }
                        }

                        Slot slot = (Slot)slotNum;

                        if (slotKvp.Value is PyTuple tuple && tuple.Count >= 3)
                        {
                            int idx = (tuple.Get<PyNumber>(0))?.AsInt() ?? 0;
                            int texIdx = (tuple.Get<PyNumber>(1))?.AsInt() ?? 0;
                            int colorIdx = (tuple.Get<PyNumber>(2))?.AsInt() ?? 0;

                            underwearSet[slot] = (idx, texIdx, colorIdx);
                            Debug.Log($"[ParseUnderwear] Gender '{gender}': {slot} = ({idx}, {texIdx}, {colorIdx})");
                        }
                    }

                    catalog.underwear[gender] = underwearSet;
                    Debug.Log($"[ParseUnderwear] Gender '{gender}': Loaded {underwearSet.Count} underwear slots");
                }
            }
        }

        private void ParseClothingTextures(ClothingCatalog catalog, PyDict texturesDict, string gender)
        {
            // Map part names to slot numbers
            var partToSlot = new Dictionary<string, Slot>
            {
                { "HAT", Slot.Hat },
                { "SHIRT", Slot.Shirt },
                { "VEST", Slot.Vest },
                { "COAT", Slot.Coat },
                { "PANT", Slot.Pant },
                { "BELT", Slot.Belt },
                { "SOCK", Slot.Sock },
                { "SHOE", Slot.Shoe }
            };

            string genderKey = gender.ToUpper() == "F" ? "FEMALE" : "MALE";

            // Get gender-specific textures
            if (!texturesDict.items.TryGetValue(genderKey, out var genderNode) || !(genderNode is PyDict genderDict))
            {
                Debug.LogWarning($"[ParseClothingTextures] No textures found for gender '{genderKey}'");
                return;
            }

            Debug.Log($"[ParseClothingTextures] Parsing textures for gender '{genderKey}'");

            foreach (var partKvp in genderDict.items)
            {
                string partName = partKvp.Key;

                if (!partToSlot.TryGetValue(partName, out Slot slot))
                {
                    Debug.LogWarning($"[ParseClothingTextures] Unknown part '{partName}', skipping");
                    continue;
                }

                if (!(partKvp.Value is PyList modelsListNode))
                {
                    Debug.LogWarning($"[ParseClothingTextures] Part '{partName}' is not a list");
                    continue;
                }

                var variants = catalog.GetVariants(slot);

                // Iterate through models (each model index matches a variant ogIndex)
                for (int modelIdx = 0; modelIdx < modelsListNode.items.Count; modelIdx++)
                {
                    if (!(modelsListNode.items[modelIdx] is PyList texturesListNode))
                    {
                        Debug.LogWarning($"[ParseClothingTextures] {partName}[{modelIdx}] is not a list");
                        continue;
                    }

                    // Find matching variant
                    var variant = variants.FirstOrDefault(v => v.ogIndex == modelIdx);
                    if (variant == null)
                    {
                        Debug.LogWarning($"[ParseClothingTextures] No variant found for {partName} modelIdx {modelIdx}");
                        continue;
                    }

                    // Parse texture options for this model
                    foreach (var texNode in texturesListNode.items)
                    {
                        if (!(texNode is PyList texEntry) || texEntry.items.Count < 1)
                            continue;

                        // First element is texture name (possibly with '+' for dual textures)
                        if (texEntry.items[0] is PyString texNameStr)
                        {
                            variant.textureIds.Add(texNameStr.value);
                        }

                        // Second element is VBase4 lowLOD color (we can ignore for now, or store if needed)
                    }

                    Debug.Log($"[ParseClothingTextures] {partName}[{modelIdx}] '{variant.displayName}': Loaded {variant.textureIds.Count} textures");
                }
            }
        }

        private void ParseClothingPatterns(ClothingCatalog catalog, Dictionary<string, PyNode> genderData, string gender)
        {
            // Extract layer pattern lists from gender file
            // Note: These are inside class methods, so they won't be in top-level variables
            // We'll need to search for them in the source or use a different approach

            Debug.Log("ParseClothingPatterns: Building group patterns for clothing variants");

            // Add Hair/Beard/Mustache variants (not in CLOTHING_NAMES)
            AddHairBeardMustacheVariants(catalog, gender);

            // Parse hairList, layer1List from Python source
            string genderFile = OgPaths.GetPirateGenderFile(gender);
            ParseLayerListsFromSource(catalog, genderFile, gender);

            // Build patterns from variant names (fallback for items not in lists)
            BuildPatternsFromNames(catalog, gender);
        }

        private void ParseLayerListsFromSource(ClothingCatalog catalog, string genderFilePath, string gender)
        {
            var text = File.ReadAllText(genderFilePath);

            // Parse layer lists (these contain the OG patterns)
            var layer1Patterns = ExtractLayerList(text, "self.clothingsLayer1");
            var layer2Patterns = ExtractLayerList(text, "self.clothingsLayer2");
            var layer3Patterns = ExtractLayerList(text, "self.clothingsLayer3");
            
            // Try to parse self.hairPieces (Male style - direct appends)
            var hairPatterns = ExtractLayerList(text, "self.hairPieces");
            
            // If empty, try parsing 'hairList' (Female style - list variable)
            if (hairPatterns.Count == 0)
            {
                hairPatterns = ExtractLayerList(text, "hairList");
                Debug.Log("Used 'hairList' fallback for hair patterns");
            }

            Debug.Log($"Extracted layer lists: layer1={layer1Patterns.Count}, layer2={layer2Patterns.Count}, layer3={layer3Patterns.Count}, hair={hairPatterns.Count}");
            if (hairPatterns.Count > 0) Debug.Log($"First hair pattern: {hairPatterns[0]}");
            if (hairPatterns.Count > 2) Debug.Log($"3rd hair pattern: {hairPatterns[2]}");

            // Parse append() calls for each slot to get layer indices and body hides
            ParseClothingAppends(catalog, text, "Shirt", Slot.Shirt, layer1Patterns);
            ParseClothingAppends(catalog, text, "Vest", Slot.Vest, layer2Patterns);
            ParseClothingAppends(catalog, text, "Coat", Slot.Coat, layer3Patterns);
            ParseClothingAppends(catalog, text, "Pant", Slot.Pant, layer1Patterns);
            ParseClothingAppends(catalog, text, "Shoe", Slot.Shoe, layer1Patterns);
            ParseClothingAppends(catalog, text, "Belt", Slot.Belt, layer2Patterns);
            ParseClothingAppends(catalog, text, "Hat", Slot.Hat, layer1Patterns);

            // Parse hair (special case - no append(), just use hairList directly)
            ParseHairVariants(catalog, hairPatterns, text, gender);

            // Parse hat hair cuts (cuts = [...])
            ParseHairCuts(catalog, text, gender);

            // Parse beard and mustache patterns from Python source
            ParseBeardMustachePatterns(catalog, text, gender);

            // MANUAL FIX: Ensure "Long Tucked Pants" (Index 0) has the correct pattern for MALES
            // The parser sometimes fails to link the specific append index, so we force it here.
            if (gender == "m")
            {
                var pant0 = catalog.GetVariant(Slot.Pant, 0);
                if (pant0 != null)
                {
                    pant0.ogPatterns = new List<string> { "**/clothing_layer1_pant_tucked*" };
                    Debug.Log("Manually forced pattern for Male Long Tucked Pants (Pant [0])");
                }
            }

            // Build patterns from variant names (fallback for items not in lists)
            BuildPatternsFromNames(catalog, gender);
        }

        private List<string> ExtractLayerList(string text, string listName)
        {
            // 1. Try to find static list definition: listName = [ ... ]
            // Note: Escape dots in listName for regex (e.g. self.clothingsLayer1 -> self\.clothingsLayer1)
            string safeListName = Regex.Escape(listName);
            var match = Regex.Match(text, $@"{safeListName}\s*=\s*\[(.*?)\]", RegexOptions.Singleline);
            
            var patterns = new List<string>();

            if (match.Success)
            {
                patterns.AddRange(ExtractPatternList(match.Groups[1].Value));
            }

            // 2. Also find dynamic appends: listName.append(geom.findAllMatches('pattern'))
            // Regex handles both single and double quotes
            // Matches: self.clothingsLayer1.append(geom.findAllMatches('**/clothing_layer1_pant_tucked_*'))
            // Added \s* before ( to be safe, and * for content
            var appendRx = new Regex($@"{safeListName}\.append\s*\(\s*geom\.findAllMatches\s*\(\s*['""]([^'""]*)['""]", RegexOptions.Compiled);
            
            foreach (Match m in appendRx.Matches(text))
            {
                string pattern = m.Groups[1].Value;
                // Strip POTCO-specific ;+s suffix
                pattern = pattern.Replace(";+s", "");
                patterns.Add(pattern);
            }

            return patterns;
        }

        private void ParseClothingAppends(ClothingCatalog catalog, string text, string pySlotName, Slot slot, List<string> layerPatterns)
        {
            // Match: self.clothings{SlotName}.append( [ ... ] )
            // Use non-greedy matching and handle nested brackets properly
            var pattern = $@"self\.clothings{pySlotName}\.append\s*\(";
            var matches = Regex.Matches(text, pattern);

            var variants = catalog.GetVariants(slot);
            int ogIndex = 0;

            foreach (Match m in matches)
            {
                // Find the matching closing parenthesis
                int startPos = m.Index + m.Length;
                int depth = 1;
                int endPos = startPos;

                for (int i = startPos; i < text.Length && depth > 0; i++)
                {
                    if (text[i] == '(') depth++;
                    else if (text[i] == ')') depth--;
                    endPos = i;
                }

                if (depth != 0) continue; // Malformed

                var fullPayload = text.Substring(startPos, endPos - startPos);

                // Debug: Show first few raw payloads with escaped newlines
                if (ogIndex < 3)
                {
                    string displayPayload = fullPayload.Substring(0, Math.Min(150, fullPayload.Length))
                        .Replace("\r", "\\r")
                        .Replace("\n", "\\n")
                        .Replace("\t", "\\t");
                    Debug.Log($"[ParseClothingAppends] {slot}[{ogIndex}] RAW (len={fullPayload.Length}): {displayPayload}");
                }

                // Parse the structure: [ [layerIdx...], -bodyHide, ... ]
                // First, extract the inner list (layer indices)
                var innerListMatch = Regex.Match(fullPayload, @"\[\s*\[(.*?)\]", RegexOptions.Singleline);
                var layerIndices = new List<int>();

                if (innerListMatch.Success)
                {
                    var innerContent = innerListMatch.Groups[1].Value;
                    var intRx = new Regex(@"\d+");
                    foreach (Match im in intRx.Matches(innerContent))
                    {
                        if (int.TryParse(im.Value, out var n))
                            layerIndices.Add(n);
                    }
                }
                else if (ogIndex < 3)
                {
                    Debug.LogWarning($"[ParseClothingAppends] {slot}[{ogIndex}]: Inner list regex did not match! Payload: {fullPayload.Replace("\n", "\\n").Substring(0, Math.Min(100, fullPayload.Length))}");
                }

                // Then extract all negative numbers (body hides)
                var bodyHides = new List<int>();
                var negRx = new Regex(@"-\s*(\d+)");
                foreach (Match nm in negRx.Matches(fullPayload))
                {
                    if (int.TryParse(nm.Groups[1].Value, out var n))
                        bodyHides.Add(n);
                }

                // Get or create variant for this ogIndex
                SlotVariant variant = null;
                if (ogIndex < variants.Count)
                {
                    variant = variants[ogIndex];
                }
                else
                {
                    // Create placeholder variant
                    variant = new SlotVariant { ogIndex = ogIndex, id = $"{slot}_{ogIndex}", displayName = $"{slot} {ogIndex}" };
                    catalog.variantsBySlot[slot].Add(variant);
                }

                // Collect OG patterns from layer indices
                var ogPatterns = new List<string>();
                foreach (var idx in layerIndices)
                {
                    if (idx >= 0 && idx < layerPatterns.Count)
                        ogPatterns.Add(layerPatterns[idx]);
                }

                // Store OG patterns and body hides
                variant.ogPatterns = ogPatterns;
                variant.bodyHideIndices = bodyHides;

                // Enhanced logging
                string layerIdxStr = string.Join(",", layerIndices);
                string bodyHideStr = string.Join(",", bodyHides);
                string patternStr = ogPatterns.Count > 0 ? string.Join(" | ", ogPatterns.Take(2)) : "NONE";

                Debug.Log($"[ParseClothingAppends] {slot}[{ogIndex}]: LayerIdx=[{layerIdxStr}] → Patterns={ogPatterns.Count} ({patternStr}), BodyHides=[{bodyHideStr}]");

                ogIndex++;
            }

            Debug.Log($"[ParseClothingAppends] Parsed {ogIndex} {slot} variants from append() calls");
        }

        private void ParseHairVariants(ClothingCatalog catalog, List<string> hairPatterns, string text, string gender)
        {
            var variants = catalog.GetVariants(Slot.Hair);
            
            // self.hairs.append([ [indices...], -hides... ])
            // Use robust parsing to handle nested lists and newlines
            var matches = Regex.Matches(text, @"self\.hairs\.append\s*\(");
            var hairCombos = new List<List<int>>();

            foreach (Match m in matches)
            {
                // Find the matching closing parenthesis
                int startPos = m.Index + m.Length;
                int depth = 1;
                int endPos = startPos;

                for (int i = startPos; i < text.Length && depth > 0; i++)
                {
                    if (text[i] == '(') depth++;
                    else if (text[i] == ')') depth--;
                    endPos = i;
                }

                if (depth != 0) continue;

                var fullPayload = text.Substring(startPos, endPos - startPos);

                // Robustly extract list content by finding brackets manually
                // Male: [ [ 0, 1 ], ... ] (Nested)
                // Female: [ 0, 1, 2 ] (Flat)

                string indicesContent = "";
                int firstBracket = fullPayload.IndexOf('[');

                if (firstBracket >= 0)
                {
                    // Check for nested '[' (ignoring whitespace)
                    bool isNested = false;
                    int contentStart = firstBracket + 1;

                    for (int k = firstBracket + 1; k < fullPayload.Length; k++)
                    {
                        char c = fullPayload[k];
                        if (char.IsWhiteSpace(c)) continue;
                        
                        if (c == '[')
                        {
                            isNested = true;
                            contentStart = k + 1; // Start after the inner '['
                        }
                        break; 
                    }

                    // Find the matching closing ']'
                    int contentEnd = fullPayload.IndexOf(']', contentStart);
                    
                    if (contentEnd > contentStart)
                    {
                        indicesContent = fullPayload.Substring(contentStart, contentEnd - contentStart);
                    }
                }

                var indices = new List<int>();
                if (!string.IsNullOrEmpty(indicesContent))
                {
                    var numRx = new Regex(@"\d+");
                    foreach (Match nm in numRx.Matches(indicesContent))
                    {
                        if (int.TryParse(nm.Value, out var idx))
                            indices.Add(idx);
                    }
                }
                
                hairCombos.Add(indices);
            }

            // Map indices to patterns using hairList (hairPatterns)
            for (int i = 0; i < hairCombos.Count && i < variants.Count; i++)
            {
                var patterns = new List<string>();
                foreach (var idx in hairCombos[i])
                {
                    if (idx >= 0 && idx < hairPatterns.Count)
                        patterns.Add(hairPatterns[idx]);
                }
                variants[i].ogPatterns = patterns;

                if (i < 5)
                {
                    Debug.Log($"Hair[{i}]: {string.Join(", ", patterns)} (indices: {string.Join(", ", hairCombos[i])})");
                    if (patterns.Count == 0 && hairCombos[i].Count > 0)
                    {
                        Debug.LogWarning($"Hair[{i}] has indices but no patterns! Max hairPattern index: {hairPatterns.Count - 1}");
                    }
                }
            }
            Debug.Log($"Stored {hairCombos.Count} {gender} hair combinations from Python source (self.hairs.append)");
        }

        private void ParseHairCuts(ClothingCatalog catalog, string text, string gender)
        {
            // Find 'cuts = [...]' inside generateHairSets or global scope
            // Use a more robust regex to capture the entire list content, including newlines
            var match = Regex.Match(text, @"cuts\s*=\s*\[(.*?)\]", RegexOptions.Singleline);
            
            if (match.Success)
            {
                var cutList = ExtractPatternList(match.Groups[1].Value);
                
                // Clean up empty strings (POTCO uses '' for no cut)
                for (int i = 0; i < cutList.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(cutList[i]))
                        cutList[i] = "cut_none"; // Use explict 'none' for logic consistency
                }

                catalog.hairHatCuts[gender] = cutList;
                Debug.Log($"[ParseHairCuts] Parsed {cutList.Count} hair cut rules for {gender}");
                if (cutList.Count > 0) Debug.Log($"[ParseHairCuts] First rule: '{cutList[0]}'");
            }
            else
            {
                Debug.LogWarning($"[ParseHairCuts] Could not find 'cuts = [...]' list for {gender}");
            }
        }

        private void ParseBeardMustachePatterns(ClothingCatalog catalog, string text, string gender)
        {
            // Extract beard patterns from self.beards.append() calls (PirateMale.py lines 1829-1839)
            var beardPatterns = new List<string>();
            // Robust regex: handle whitespace, single/double quotes, empty strings
            var beardRx = new Regex(@"self\.beards\.append\s*\(\s*geom\.findAllMatches\s*\(\s*['""]([^'""]*)['""]", RegexOptions.Compiled);
            foreach (Match m in beardRx.Matches(text))
            {
                beardPatterns.Add(m.Groups[1].Value);
            }

            // Extract mustache patterns from self.mustaches.append() calls (PirateMale.py lines 1844-1850)
            var mustachePatterns = new List<string>();
            var mustacheRx = new Regex(@"self\.mustaches\.append\s*\(\s*geom\.findAllMatches\s*\(\s*['""]([^'""]*)['""]", RegexOptions.Compiled);
            foreach (Match m in mustacheRx.Matches(text))
            {
                mustachePatterns.Add(m.Groups[1].Value);
            }

            // Apply beard patterns to variants
            var beardVariants = catalog.GetVariants(Slot.Beard);
            for (int i = 0; i < beardPatterns.Count && i < beardVariants.Count; i++)
            {
                beardVariants[i].ogPatterns = new List<string> { beardPatterns[i] };
            }

            // Apply mustache patterns to variants
            var mustacheVariants = catalog.GetVariants(Slot.Mustache);
            for (int i = 0; i < mustachePatterns.Count && i < mustacheVariants.Count; i++)
            {
                mustacheVariants[i].ogPatterns = new List<string> { mustachePatterns[i] };
            }

            Debug.Log($"Loaded {beardPatterns.Count} beard and {mustachePatterns.Count} mustache patterns from {gender} Python source");
        }

        private List<string> ExtractPatternList(string listContent)
        {
            var patterns = new List<string>();
            // Match quoted strings like '**/hair_base' or '**/clothing_layer1_hat_tricorn;+s'
            // Changed + to * to match empty strings ''
            var stringRx = new Regex(@"['""]([^'""]*)['""]", RegexOptions.Compiled);

            foreach (Match m in stringRx.Matches(listContent))
            {
                string pattern = m.Groups[1].Value;
                // Strip POTCO-specific ;+s suffix (show groups marker)
                pattern = pattern.Replace(";+s", "");
                patterns.Add(pattern);
            }

            return patterns;
        }

        private void AddHairBeardMustacheVariants(ClothingCatalog catalog, string gender)
        {
            // Hair styles - Males have 14 (indices 0-13), Females have 20 (indices 0-19)
            // Create enough slots to cover both genders
            int hairCount = gender.ToLower() == "f" ? 20 : 14;

            for (int i = 0; i < hairCount; i++)
            {
                catalog.variantsBySlot[Slot.Hair].Add(new SlotVariant
                {
                    id = $"hair_{i}",
                    displayName = $"Hair {i}",
                    ogIndex = i
                });
            }

            Debug.Log($"AddHairBeardMustacheVariants: Created {hairCount} hair slots for gender '{gender}'");

            // Beard styles (placeholder - actual count varies, increase to 20 to be safe)
            for (int i = 0; i < 20; i++)
            {
                catalog.variantsBySlot[Slot.Beard].Add(new SlotVariant
                {
                    id = $"beard_{i}",
                    displayName = i == 0 ? "None" : $"Beard {i}",
                    ogIndex = i
                });
            }

            // Mustache styles (placeholder - actual count varies, increase to 10 to be safe)
            for (int i = 0; i < 10; i++)
            {
                catalog.variantsBySlot[Slot.Mustache].Add(new SlotVariant
                {
                    id = $"mustache_{i}",
                    displayName = i == 0 ? "None" : $"Mustache {i}",
                    ogIndex = i
                });
            }

            Debug.Log($"AddHairBeardMustacheVariants: Created Beard: 20, Mustache: 10 variants");
        }

        private void BuildPatternsFromNames(ClothingCatalog catalog, string gender)
        {
            // Build patterns for each slot based on naming conventions
            // Only builds patterns for variants that don't already have one from ParseLayerListsFromSource
            int totalPatterns = 0;
            foreach (var slotKvp in catalog.variantsBySlot)
            {
                Slot slot = slotKvp.Key;
                var variants = slotKvp.Value;

                foreach (var variant in variants)
                {
                    if (variant.ogIndex < 0)
                        continue;

                    // Skip if patterns already set by ParseLayerListsFromSource
                    if (variant.ogPatterns != null && variant.ogPatterns.Count > 0)
                        continue;

                    string pattern = BuildPatternForVariant(slot, variant, gender);
                    if (!string.IsNullOrEmpty(pattern))
                    {
                        variant.ogPatterns = new List<string> { pattern };
                        totalPatterns++;

                        // Debug log first few patterns
                        if (totalPatterns <= 10)
                        {
                            Debug.Log($"Pattern: {slot}[{variant.ogIndex}] '{variant.displayName}' → '{pattern}'");
                        }
                    }
                }
            }
            Debug.Log($"BuildPatternsFromNames: Generated {totalPatterns} fallback patterns");
        }

        private string BuildPatternForVariant(Slot slot, SlotVariant variant, string gender)
        {
            // Convert display name to pattern-friendly format
            // Handle special cases like "Bandana Full" → "bandanna_full", "EITC" → "eitc"
            string nameLower = variant.displayName.ToLower()
                .Replace(" ", "_")
                .Replace("bandana", "bandanna") // POTCO uses "bandanna" spelling
                .Replace("regular", "reg");      // "Regular" abbreviated to "reg"

            switch (slot)
            {
                case Slot.Hat:
                    if (variant.ogIndex == 0 || variant.displayName == "None")
                        return null; // No pattern for "None"
                    // POTCO uses ;+s to mark "show" groups, we use * wildcard
                    return $"**/clothing_layer1_hat_{nameLower}*";

                case Slot.Shirt:
                    // Shirts are complex - use generic pattern for now
                    // TODO: Parse actual shirt group mappings
                    if (variant.ogIndex == 0)
                        return null;
                    return "**/clothing_layer1_shirt_common*";

                case Slot.Vest:
                    if (variant.ogIndex == 0 || variant.displayName == "None")
                        return "**/clothing_layer2_vest_none*";
                    if (nameLower.Contains("long"))
                        return "**/clothing_layer2_vest_long_closed*";
                    if (nameLower.Contains("open"))
                        return "**/clothing_layer2_vest_open*";
                    return "**/clothing_layer2_vest_closed*";

                case Slot.Coat:
                    if (variant.ogIndex == 0 || variant.displayName == "None")
                        return "**/clothing_layer3_coat_none*";
                    if (nameLower.Contains("long"))
                        return "**/clothing_layer3_coat_long*";
                    if (nameLower.Contains("short"))
                        return "**/clothing_layer3_coat_short*";
                    if (nameLower.Contains("navy"))
                        return "**/clothing_layer3_coat_navy*";
                    if (nameLower.Contains("eitc"))
                        return "**/clothing_layer3_coat_eitc*";
                    return "**/clothing_layer3_coat*";

                case Slot.Pant:
                    // Pants are complex - use generic pattern for now
                    return "**/clothing_layer1_pant*";

                case Slot.Belt:
                    if (variant.ogIndex == 0 || variant.displayName == "None")
                        return "**/clothing_layer2_belt_none*";
                    if (nameLower.Contains("sash"))
                        return "**/clothing_layer2_belt_sash*";
                    if (nameLower.Contains("oval"))
                        return "**/clothing_layer2_belt*oval*";
                    if (nameLower.Contains("square"))
                        return "**/clothing_layer2_belt*square*";
                    return "**/clothing_layer2_belt*";

                case Slot.Shoe:
                    // Shoes are complex - use generic pattern for now
                    if (variant.ogIndex == 0)
                        return "**/clothing_layer1_shoe_none*";
                    return "**/clothing_layer1_shoe*";

                case Slot.Hair:
                    // Hair will need special handling - multiple groups per style
                    if (variant.ogIndex == 0)
                        return "**/hair_none*";
                    // Safe fallback: don't show everything if pattern is missing
                    return null;

                case Slot.Beard:
                    if (variant.ogIndex == 0)
                        return "**/beard_none*";
                    return null;

                case Slot.Mustache:
                    if (variant.ogIndex == 0)
                        return "**/mustache_none*";
                    return null;

                default:
                    return null;
            }
        }

        private PirateDNA ConvertNpcDict(string npcId, PyDict npcDict)
        {
            var dna = new PirateDNA { name = npcId };

            // Iterate through function calls and apply them
            foreach (var kvp in npcDict.items)
            {
                string funcName = kvp.Key;
                PyNode argsNode = kvp.Value;

                // Handle both tuple and non-tuple values
                PyTuple tuple;
                if (argsNode is PyTuple t)
                {
                    tuple = t;
                }
                else if (argsNode is PyNull)
                {
                    // Skip None values (from unresolvable expressions like PLocalizer.NPCNames['id'])
                    continue;
                }
                else
                {
                    // Wrap bare values in a single-element tuple
                    tuple = new PyTuple();
                    tuple.items.Add(argsNode);
                }

                ApplyNpcFunction(dna, funcName, tuple);
            }

            return dna;
        }

        private void ApplyNpcFunction(PirateDNA dna, string funcName, PyTuple args)
        {
            switch (funcName)
            {
                case "setName":
                    if (args.Get<PyString>(0) is PyString nameStr)
                        dna.name = nameStr.value;
                    break;

                case "setGender":
                    if (args.Get<PyString>(0) is PyString genderStr)
                    {
                        dna.gender = genderStr.value;
                        Debug.Log($"[ApplyNpcFunction] Set gender to '{dna.gender}' for NPC '{dna.name}'");
                    }
                    break;

                case "setBodyShape":
                    // Body shape can be either a string (direct name) or number (index into MaleBodies/FemaleBodies)
                    Debug.Log($"[ApplyNpcFunction] setBodyShape called for '{dna.name}', gender='{dna.gender}', arg type={args.items[0]?.GetType().Name}");
                    if (args.Get<PyString>(0) is PyString shapeStr)
                    {
                        dna.bodyShape = shapeStr.value;
                        Debug.Log($"[ApplyNpcFunction] Set body shape to string '{dna.bodyShape}'");
                    }
                    else if (args.Get<PyNumber>(0) is PyNumber shapeNum)
                    {
                        int shapeIndex = shapeNum.AsInt();
                        dna.bodyShape = GetBodyShapeNameFromIndex(dna.gender, shapeIndex);
                        Debug.Log($"[ApplyNpcFunction] Mapped body shape index {shapeIndex} (gender '{dna.gender}') → '{dna.bodyShape}'");
                    }
                    else
                    {
                        Debug.LogWarning($"[ApplyNpcFunction] setBodyShape received unexpected type: {args.items[0]?.GetType().Name}");
                    }
                    break;

                case "setBodyHeight":
                    if (args.Get<PyNumber>(0) is PyNumber heightNum)
                        dna.bodyHeight = heightNum.AsFloat();
                    break;

                case "setBodyColor":
                    // setBodyColor controls the skin color palette index (self.body.color in Python)
                    if (args.Get<PyNumber>(0) is PyNumber skinNum)
                        dna.skinColorIdx = skinNum.AsInt();
                    break;

                case "setBodySkin":
                    // setBodySkin is the skin texture index (self.body.skin in Python) - not used for color
                    // Ignoring this to prevent overwriting skinColorIdx
                    break;

                case "setClothesHat":
                case "setHatIdx":
                    if (args.Get<PyNumber>(0) is PyNumber hatIdx)
                        dna.hat = hatIdx.AsInt();
                    if (args.Count > 1 && args.Get<PyNumber>(1) is PyNumber hatTex)
                        dna.hatTex = hatTex.AsInt();
                    break;

                case "setClothesShirt":
                    if (args.Get<PyNumber>(0) is PyNumber shirtIdx)
                        dna.shirt = shirtIdx.AsInt();
                    if (args.Count > 1 && args.Get<PyNumber>(1) is PyNumber shirtTex)
                        dna.shirtTex = shirtTex.AsInt();
                    break;

                case "setClothesVest":
                    if (args.Get<PyNumber>(0) is PyNumber vestIdx)
                    {
                        dna.vest = vestIdx.AsInt();
                        int texValue = args.Count > 1 ? (args.Get<PyNumber>(1)?.AsInt() ?? -1) : -1;
                        Debug.Log($"[ApplyNpcFunction] setClothesVest: vest={dna.vest}, tex={texValue}");
                    }
                    if (args.Count > 1 && args.Get<PyNumber>(1) is PyNumber vestTex)
                        dna.vestTex = vestTex.AsInt();
                    break;

                case "setClothesCoat":
                    if (args.Get<PyNumber>(0) is PyNumber coatIdx)
                        dna.coat = coatIdx.AsInt();
                    if (args.Count > 1 && args.Get<PyNumber>(1) is PyNumber coatTex)
                        dna.coatTex = coatTex.AsInt();
                    break;

                case "setClothesPant":
                    if (args.Get<PyNumber>(0) is PyNumber pantIdx)
                        dna.pants = pantIdx.AsInt();
                    if (args.Count > 1 && args.Get<PyNumber>(1) is PyNumber pantTex)
                        dna.pantsTex = pantTex.AsInt();
                    break;

                case "setClothesBelt":
                    if (args.Get<PyNumber>(0) is PyNumber beltIdx)
                        dna.belt = beltIdx.AsInt();
                    if (args.Count > 1 && args.Get<PyNumber>(1) is PyNumber beltTex)
                        dna.beltTex = beltTex.AsInt();
                    break;

                case "setClothesShoe":
                    if (args.Get<PyNumber>(0) is PyNumber shoeIdx)
                        dna.shoes = shoeIdx.AsInt();
                    if (args.Count > 1 && args.Get<PyNumber>(1) is PyNumber shoeTex)
                        dna.shoesTex = shoeTex.AsInt();
                    break;

                case "setClothesTopColor":
                    if (args.Get<PyNumber>(0) is PyNumber topColor)
                        dna.topColorIdx = topColor.AsInt();
                    break;

                case "setClothesBotColor":
                    if (args.Get<PyNumber>(0) is PyNumber botColor)
                        dna.botColorIdx = botColor.AsInt();
                    break;

                case "setHatColor":
                    if (args.Get<PyNumber>(0) is PyNumber hatColor)
                        dna.hatColorIdx = hatColor.AsInt();
                    break;

                case "setHairHair":
                    if (args.Get<PyNumber>(0) is PyNumber hairIdx)
                        dna.hair = hairIdx.AsInt();
                    break;

                case "setHairBeard":
                    if (args.Get<PyNumber>(0) is PyNumber beardIdx)
                        dna.beard = beardIdx.AsInt();
                    break;

                case "setHairMustache":
                    if (args.Get<PyNumber>(0) is PyNumber mustacheIdx)
                        dna.mustache = mustacheIdx.AsInt();
                    break;

                case "setHairColor":
                    if (args.Get<PyNumber>(0) is PyNumber hairColor)
                        dna.hairColorIdx = hairColor.AsInt();
                    break;

                case "setHeadTexture":
                    if (args.Get<PyNumber>(0) is PyNumber headTex)
                        dna.headTexture = headTex.AsInt();
                    break;

                case "setEyesColor":
                    if (args.Get<PyNumber>(0) is PyNumber eyeColor)
                        dna.eyeColorIdx = eyeColor.AsInt();
                    break;

                case string s when s.StartsWith("setTattoo"):
                    ParseTattoo(dna, funcName, args);
                    break;

                case string s when s.StartsWith("setJewelry"):
                    ParseJewelry(dna, funcName, args);
                    break;

                // Facial morphs (setHeadWidth, setJawWidth, etc.)
                case "setHeadWidth":
                case "setHeadHeight":
                case "setHeadSize":
                case "setHeadRoundness":
                case "setJawWidth":
                case "setJawRoundness":
                case "setJawAngle":
                case "setJawLength":
                case "setJawChinSize":
                case "setMouthWidth":
                case "setMouthLipThickness":
                case "setMouthFrown":
                case "setCheekBoneHeight":
                case "setCheekBoneWidth":
                case "setCheekFat":
                case "setBrowWidth":
                case "setBrowProtruding":
                case "setBrowAngle":
                case "setBrowHeight":
                case "setEyeCorner":
                case "setEyeOpeningSize":
                case "setEyeBulge":
                case "setNoseBridgeWidth":
                case "setNoseNostrilWidth":
                case "setNoseLength":
                case "setNoseBump":
                case "setNoseNostrilHeight":
                case "setNoseNostrilAngle":
                case "setNoseNostrilIndent":
                case "setNoseBridgeBroke":
                case "setNoseNostrilBroke":
                case "setEarScale":
                case "setEarFlapAngle":
                case "setEarPosition":
                case "setEarLobe":
                    ParseFacialMorph(dna, funcName, args);
                    break;
            }
        }

        private void ParseFacialMorph(PirateDNA dna, string funcName, PyTuple args)
        {
            // Convert setter name to morph name (setHeadWidth → headWidth)
            string morphName = funcName.Substring(3); // Remove "set" prefix
            morphName = char.ToLower(morphName[0]) + morphName.Substring(1); // Lowercase first char

            if (args.Get<PyNumber>(0) is PyNumber value)
            {
                float rawValue = value.AsFloat();

                // Clamp to Custom NPC Creator's slider ranges
                GetMorphSliderRange(morphName, out float min, out float max);
                float clampedValue = Mathf.Clamp(rawValue, min, max);

                dna.headMorphs[morphName] = clampedValue;

                // Log if clamping occurred
                if (!Mathf.Approximately(rawValue, clampedValue))
                {
                    Debug.Log($"[NPC Facial Morph] Clamped {morphName}: {rawValue} → {clampedValue} (range: {min} to {max})");
                }
            }
        }

        /// <summary>Get Custom NPC Creator's slider range for a morph (matches CustomNPCCreatorWindow)</summary>
        private void GetMorphSliderRange(string morphName, out float min, out float max)
        {
            // Custom ranges per morph (matching CustomNPCCreatorWindow.cs lines 714-760)
            switch (morphName)
            {
                case "jawWidth":
                    min = -0.2f; max = 0.2f; return;
                case "browProtruding":
                    min = -1.0f; max = 1.0f; return;
                case "headRoundness":
                    min = -0.05f; max = 0.05f; return;
                case "cheekFat":
                    min = -0.2f; max = 0.2f; return;
                case "earPosition":
                    min = -0.1f; max = 0.1f; return;
                case "earScale":
                    min = -0.2f; max = 0.2f; return;
                case "earFlap":
                    min = -0.2f; max = 0.2f; return;
                case "eyeOpeningSize":
                    min = -0.2f; max = 0.2f; return;
                case "eyeSpacing":
                    min = -1.0f; max = 1.0f; return;
                case "headHeight":
                    min = -0.05f; max = 0.05f; return;
                case "headWidth":
                    min = -0.1f; max = 0.1f; return;
                case "jawChinAngle":
                    min = -0.1f; max = 0.1f; return;
                case "jawChinSize":
                    min = -0.1f; max = 0.1f; return;
                case "jawLength":
                    min = -0.1f; max = 0.1f; return;
                case "mouthLipThickness":
                    min = -0.1f; max = 0.1f; return;
                case "mouthWidth":
                    min = -0.1f; max = 0.1f; return;
                case "noseBump":
                    min = -0.1f; max = 0.1f; return;
                case "noseLength":
                    min = -0.1f; max = 0.1f; return;
                case "noseNostrilAngle":
                    min = -1.0f; max = 1.0f; return;
                case "noseNostrilWidth":
                    min = -0.2f; max = 0.2f; return;
                case "noseBridgeWidth":
                    min = -0.2f; max = 0.2f; return;
                case "noseNostrilHeight":
                    min = -0.2f; max = 0.2f; return;
            }

            // Default fallback ranges
            min = -0.5f;
            max = 0.5f;
        }

        private void ParseTattoo(PirateDNA dna, string funcName, PyTuple args)
        {
            // Extract zone from function name (e.g., setTattooZone2 → 2)
            string zoneStr = funcName.Replace("setTattoo", "").Replace("Zone", "").Replace("Chest", "0");
            int zone = int.TryParse(zoneStr, out int z) ? z : 0;

            if (args.Count >= 6)
            {
                int idx = args.Get<PyNumber>(0)?.AsInt() ?? 0;
                float u = args.Get<PyNumber>(1)?.AsFloat() ?? 0f;
                float v = args.Get<PyNumber>(2)?.AsFloat() ?? 0f;
                float scale = args.Get<PyNumber>(3)?.AsFloat() ?? 1f;
                float rot = args.Get<PyNumber>(4)?.AsFloat() ?? 0f;
                int colorIdx = args.Get<PyNumber>(5)?.AsInt() ?? 0;

                dna.tattoos.Add(new TattooSpec(zone, idx, u, v, scale, rot, colorIdx));
            }
        }

        private void ParseJewelry(PirateDNA dna, string funcName, PyTuple args)
        {
            // Extract zone number and map to zone name (PirateMale.py lines 3327-3334)
            // Zone1=LEar, Zone2=REar, Zone3=LBrow, Zone4=RBrow, Zone5=Nose, Zone6=Mouth, Zone7=LHand, Zone8=RHand
            string zoneNum = funcName.Replace("setJewelryZone", "");
            string zoneName = zoneNum switch
            {
                "1" => "LEar",
                "2" => "REar",
                "3" => "LBrow",
                "4" => "RBrow",
                "5" => "Nose",
                "6" => "Mouth",
                "7" => "LHand",
                "8" => "RHand",
                _ => $"zone{zoneNum}"
            };

            if (args.Get<PyNumber>(0) is PyNumber idxNum)
            {
                dna.jewelry[zoneName] = idxNum.AsInt();
            }
        }

        // ===== FACIAL MORPHS =====

        public FacialMorphDatabase LoadFacialMorphs(string gender = "m")
        {
            // PHASE 1 OPTIMIZATION: Check static cache first
            string genderKey = gender.ToLower();
            lock (s_cacheLock)
            {
                if (s_cachedFacialMorphs.TryGetValue(genderKey, out var cachedMorphs))
                {
                    Debug.Log($"[LoadFacialMorphs] Using cached facial morphs for gender '{genderKey}'");
                    return cachedMorphs;
                }
            }

            // Cache miss - load from disk
            Debug.Log($"[LoadFacialMorphs] Cache miss - loading for gender '{genderKey}'");
            string filePath = genderKey == "f" ? OgPaths.PirateFemale : OgPaths.PirateMale;
            var morphDatabase = FacialMorphParser.ParseFromFile(filePath, gender);

            // Store in cache
            lock (s_cacheLock)
            {
                s_cachedFacialMorphs[genderKey] = morphDatabase;
                Debug.Log($"[LoadFacialMorphs] Cached facial morphs for gender '{genderKey}'");
            }

            return morphDatabase;
        }

        // ===== BODY HIDE MASK PARSING =====

        private static readonly Dictionary<string, Slot> _slotByPyName = new()
        {
            { "Shirt", Slot.Shirt },
            { "Vest",  Slot.Vest  },
            { "Coat",  Slot.Coat  },
            { "Pant",  Slot.Pant },
            { "Shoe",  Slot.Shoe },
            { "Belt",  Slot.Belt  },
        };

    }
}
