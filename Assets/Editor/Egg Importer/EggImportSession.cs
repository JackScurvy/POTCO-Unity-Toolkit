using System;
using System.Collections.Generic;
using UnityEditor;

public struct EggFileAnalysis
{
    public EggFileAnalysis(bool isAnimationOnly, bool hasSkeletalData)
    {
        IsAnimationOnly = isAnimationOnly;
        HasSkeletalData = hasSkeletalData;
    }

    public bool IsAnimationOnly { get; }
    public bool HasSkeletalData { get; }
}

public static class EggFileAnalyzer
{
    public static EggFileAnalysis Analyze(string[] lines)
    {
        bool hasBundle = false;
        bool hasVertices = false;
        bool hasPolygons = false;
        bool hasJoints = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (line.StartsWith("<Bundle>", StringComparison.Ordinal)) hasBundle = true;
            else if (line.StartsWith("<Vertex>", StringComparison.Ordinal)) hasVertices = true;
            else if (line.StartsWith("<Polygon>", StringComparison.Ordinal)) hasPolygons = true;
            else if (line.StartsWith("<Joint>", StringComparison.Ordinal)) hasJoints = true;
            else if (line.Contains("<Scalar> membership")) hasJoints = true;
            else if (line.StartsWith("<Table>", StringComparison.Ordinal) && i + 1 < lines.Length)
            {
                string nextLine = lines[i + 1];
                if (nextLine.IndexOf("joint", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasJoints = true;
                }
            }

            if (hasVertices && hasPolygons && hasJoints)
            {
                return new EggFileAnalysis(false, true);
            }
        }

        return new EggFileAnalysis(hasBundle && !hasVertices && !hasPolygons, hasJoints);
    }
}

public sealed class EggImportSessionContext
{
    private readonly Dictionary<string, EggFileAnalysis> fileAnalyses;

    public EggImportSessionContext(EggLodIndex lodIndex, IDictionary<string, EggFileAnalysis> analyses)
    {
        LodIndex = lodIndex;
        fileAnalyses = new Dictionary<string, EggFileAnalysis>(StringComparer.OrdinalIgnoreCase);

        if (analyses == null) return;

        foreach (var kvp in analyses)
        {
            fileAnalyses[NormalizeAssetPath(kvp.Key)] = kvp.Value;
        }
    }

    public EggLodIndex LodIndex { get; }

    public bool TryGetAnalysis(string assetPath, out EggFileAnalysis analysis)
    {
        return fileAnalyses.TryGetValue(NormalizeAssetPath(assetPath), out analysis);
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return string.IsNullOrEmpty(assetPath) ? string.Empty : assetPath.Replace('\\', '/');
    }
}

public static class EggImportSession
{
    private static int explicitImportDepth;
    private static readonly Stack<EggImportSessionContext> contextStack = new Stack<EggImportSessionContext>();

    public static bool IsExplicitImportActive => explicitImportDepth > 0;

    public static IDisposable BeginExplicitImport()
    {
        return BeginExplicitImport(null);
    }

    public static IDisposable BeginExplicitImport(EggImportSessionContext context)
    {
        explicitImportDepth++;
        bool pushedContext = context != null;
        if (pushedContext)
        {
            contextStack.Push(context);
        }

        return new ExplicitImportScope(pushedContext);
    }

    public static bool TryGetFileAnalysis(string assetPath, out EggFileAnalysis analysis)
    {
        foreach (EggImportSessionContext context in contextStack)
        {
            if (context.TryGetAnalysis(assetPath, out analysis))
            {
                return true;
            }
        }

        analysis = default;
        return false;
    }

    public static EggLodIndex CurrentLodIndex
    {
        get
        {
            foreach (EggImportSessionContext context in contextStack)
            {
                if (context.LodIndex != null)
                {
                    return context.LodIndex;
                }
            }

            return null;
        }
    }

    private sealed class ExplicitImportScope : IDisposable
    {
        private readonly bool pushedContext;
        private bool disposed;

        public ExplicitImportScope(bool pushedContext)
        {
            this.pushedContext = pushedContext;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (pushedContext && contextStack.Count > 0)
            {
                contextStack.Pop();
            }

            if (explicitImportDepth > 0)
            {
                explicitImportDepth--;
            }
        }
    }
}

public sealed class EggBatchImportPlan
{
    private readonly Dictionary<string, EggFileAnalysis> analyses;

    public EggBatchImportPlan(string[] assetPaths, EggLodIndex lodIndex, IDictionary<string, EggFileAnalysis> fileAnalyses)
    {
        AssetPaths = assetPaths ?? Array.Empty<string>();
        LodIndex = lodIndex;
        analyses = new Dictionary<string, EggFileAnalysis>(StringComparer.OrdinalIgnoreCase);

        if (fileAnalyses == null) return;

        foreach (var kvp in fileAnalyses)
        {
            analyses[kvp.Key.Replace('\\', '/')] = kvp.Value;
        }
    }

    public string[] AssetPaths { get; }
    public EggLodIndex LodIndex { get; }
    public IReadOnlyDictionary<string, EggFileAnalysis> Analyses => analyses;

    public bool TryGetAnalysis(string assetPath, out EggFileAnalysis analysis)
    {
        return analyses.TryGetValue(assetPath.Replace('\\', '/'), out analysis);
    }

    public Dictionary<string, EggFileAnalysis> CreateAnalysisDictionary()
    {
        return new Dictionary<string, EggFileAnalysis>(analyses, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class EggBatchImportOptions
{
    public const int DefaultBatchSize = 50;

    public string ProgressTitle { get; set; } = "Importing EGG Files";
    public int BatchSize { get; set; } = DefaultBatchSize;
    public EggLodIndex LodIndex { get; set; }
    public IDictionary<string, EggFileAnalysis> FileAnalyses { get; set; }
    public Func<string, int, int, bool> ShouldCancel { get; set; }
    public Action<string> ImportAsset { get; set; }
    public Action PrewarmCaches { get; set; }
    public bool UseAssetEditing { get; set; } = true;
}

public struct EggBatchImportResult
{
    public EggBatchImportResult(int totalFiles, int importedCount, bool wasCancelled)
    {
        TotalFiles = totalFiles;
        ImportedCount = importedCount;
        WasCancelled = wasCancelled;
    }

    public int TotalFiles { get; }
    public int ImportedCount { get; }
    public bool WasCancelled { get; }
}

public static class EggBatchImporter
{
    public static EggBatchImportResult ImportFiles(IEnumerable<string> assetPaths, EggBatchImportOptions options = null)
    {
        options ??= new EggBatchImportOptions();
        string[] paths = ToArray(assetPaths);
        int totalFiles = paths.Length;
        int batchSize = Math.Max(1, options.BatchSize);
        int importedCount = 0;
        bool wasCancelled = false;

        var settings = EggImporterSettings.Instance;
        bool originalAutoImportEnabled = settings.autoImportEnabled;
        settings.autoImportEnabled = true;
        EditorUtility.SetDirty(settings);

        Action<string> importAsset = options.ImportAsset ?? (path => AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate));
        var context = new EggImportSessionContext(options.LodIndex, options.FileAnalyses);

        try
        {
            using (EggImportSession.BeginExplicitImport(context))
            {
                options.PrewarmCaches?.Invoke();

                int index = 0;
                while (index < totalFiles && !wasCancelled)
                {
                    bool assetEditingStarted = false;
                    try
                    {
                        if (options.UseAssetEditing)
                        {
                            AssetDatabase.StartAssetEditing();
                            assetEditingStarted = true;
                        }

                        int batchEnd = Math.Min(index + batchSize, totalFiles);
                        for (; index < batchEnd; index++)
                        {
                            string path = paths[index];
                            if (options.ShouldCancel != null && options.ShouldCancel(path, index, totalFiles))
                            {
                                wasCancelled = true;
                                break;
                            }

                            importAsset(path);
                            importedCount++;
                        }
                    }
                    finally
                    {
                        if (assetEditingStarted)
                        {
                            AssetDatabase.StopAssetEditing();
                        }
                    }
                }
            }
        }
        finally
        {
            settings.autoImportEnabled = originalAutoImportEnabled;
            EditorUtility.SetDirty(settings);
        }

        return new EggBatchImportResult(totalFiles, importedCount, wasCancelled);
    }

    private static string[] ToArray(IEnumerable<string> assetPaths)
    {
        if (assetPaths == null) return Array.Empty<string>();
        if (assetPaths is string[] array) return array;

        var paths = new List<string>();
        foreach (string assetPath in assetPaths)
        {
            paths.Add(assetPath);
        }

        return paths.ToArray();
    }
}
