using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using POTCO.Editor;

public sealed class EggLodIndex
{
    private static readonly Regex NumericLodRegex = new Regex(@"(.+)_(\d+)$", RegexOptions.Compiled);
    private readonly HashSet<string> fileNames;
    private readonly Dictionary<string, int> highestNumericLodByBaseName;

    private EggLodIndex(IEnumerable<string> eggFilePaths)
    {
        fileNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        highestNumericLodByBaseName = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

        if (eggFilePaths == null) return;

        foreach (string eggFilePath in eggFilePaths)
        {
            string fileName = NormalizeFileName(eggFilePath);
            if (string.IsNullOrEmpty(fileName)) continue;

            fileNames.Add(fileName);

            Match match = NumericLodRegex.Match(fileName);
            if (!match.Success || !int.TryParse(match.Groups[2].Value, out int lodValue))
            {
                continue;
            }

            string baseName = match.Groups[1].Value;
            if (!highestNumericLodByBaseName.TryGetValue(baseName, out int currentHighest) || lodValue > currentHighest)
            {
                highestNumericLodByBaseName[baseName] = lodValue;
            }
        }
    }

    public static EggLodIndex FromFilePaths(IEnumerable<string> eggFilePaths)
    {
        return new EggLodIndex(eggFilePaths);
    }

    public static EggLodIndex FromProjectFiles()
    {
        return new EggLodIndex(Directory.GetFiles(Application.dataPath, "*.egg", SearchOption.AllDirectories));
    }

    public static bool IsNumericLodName(string fileName)
    {
        return NumericLodRegex.IsMatch(NormalizeFileName(fileName));
    }

    public bool ContainsFileName(string fileName)
    {
        return fileNames.Contains(NormalizeFileName(fileName));
    }

    public bool TryGetHighestNumericLod(string baseName, out int highestLod)
    {
        return highestNumericLodByBaseName.TryGetValue(baseName, out highestLod);
    }

    public static bool TryParseNumericLod(string fileName, out string baseName, out int lodValue)
    {
        Match match = NumericLodRegex.Match(NormalizeFileName(fileName));
        if (match.Success && int.TryParse(match.Groups[2].Value, out lodValue))
        {
            baseName = match.Groups[1].Value;
            return true;
        }

        baseName = string.Empty;
        lodValue = 0;
        return false;
    }

    private static string NormalizeFileName(string fileNameOrPath)
    {
        if (string.IsNullOrEmpty(fileNameOrPath)) return string.Empty;
        return Path.GetFileNameWithoutExtension(fileNameOrPath.Replace('\\', '/')).ToLowerInvariant();
    }
}

public static class LODFilteringUtility
{
    public static bool ShouldImportHighestLODOnly(string fileName, bool hasSkeletalData = false, EggLodIndex lodIndex = null)
    {
        lodIndex ??= EggLodIndex.FromProjectFiles();
        fileName = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

        if (DebugLogger.IsEggImporterEnabled)
        {
            DebugLogger.LogEggImporter($"Checking LOD for file: {fileName} (hasSkeletalData: {hasSkeletalData})");
        }

        if (fileName.EndsWith("_hi") || fileName.EndsWith("_high"))
        {
            if (DebugLogger.IsEggImporterEnabled)
            {
                DebugLogger.LogEggImporter($"Importing character hi/high LOD: {fileName}");
            }

            return true;
        }

        if (fileName.EndsWith("_med") || fileName.EndsWith("_medium") || fileName.EndsWith("_low") || fileName.EndsWith("_super") || fileName.EndsWith("_superlow"))
        {
            string baseName = StripNamedLodSuffix(fileName);

            if (lodIndex.ContainsFileName(baseName + "_hi"))
            {
                if (DebugLogger.IsEggImporterEnabled)
                {
                    DebugLogger.LogEggImporter($"Skipping {fileName} - higher quality version exists: {baseName}_hi");
                }

                return false;
            }

            if (lodIndex.ContainsFileName(baseName + "_high"))
            {
                if (DebugLogger.IsEggImporterEnabled)
                {
                    DebugLogger.LogEggImporter($"Skipping {fileName} - higher quality version exists: {baseName}_high");
                }

                return false;
            }
        }

        if (EggLodIndex.TryParseNumericLod(fileName, out string numericBaseName, out int currentLOD) && hasSkeletalData)
        {
            if (DebugLogger.IsEggImporterEnabled)
            {
                DebugLogger.LogEggImporter($"Found numeric LOD in skeletal model: {fileName} (base: '{numericBaseName}', number: {currentLOD})");
            }

            int highestLOD = lodIndex.TryGetHighestNumericLod(numericBaseName, out int indexedHighestLOD)
                ? indexedHighestLOD
                : currentLOD;

            if (currentLOD < highestLOD)
            {
                if (DebugLogger.IsEggImporterEnabled)
                {
                    DebugLogger.LogEggImporter($"Skipping {fileName} - higher numeric LOD exists: {numericBaseName}_{highestLOD}");
                }

                return false;
            }

            if (DebugLogger.IsEggImporterEnabled)
            {
                DebugLogger.LogEggImporter($"Importing highest numeric LOD: {fileName}");
            }
        }

        return true;
    }

    private static string StripNamedLodSuffix(string fileName)
    {
        if (fileName.EndsWith("_medium")) return fileName.Substring(0, fileName.LastIndexOf("_medium"));
        if (fileName.EndsWith("_superlow")) return fileName.Substring(0, fileName.LastIndexOf("_superlow"));
        if (fileName.EndsWith("_med")) return fileName.Substring(0, fileName.LastIndexOf("_med"));
        if (fileName.EndsWith("_low")) return fileName.Substring(0, fileName.LastIndexOf("_low"));
        if (fileName.EndsWith("_super")) return fileName.Substring(0, fileName.LastIndexOf("_super"));
        return fileName;
    }
}
