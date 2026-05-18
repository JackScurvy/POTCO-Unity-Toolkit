using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class EggImportSessionTests
{
    private const string TestFolder = "Assets/EggImportSessionTestsTemp";
    private const string SettingsAssetPath = "Assets/Resources/EggImporterSettings.asset";

    private EggImporterSettings _settings;
    private bool _originalAutoImportEnabled;
    private bool _settingsAssetExisted;

    [SetUp]
    public void SetUp()
    {
        _settingsAssetExisted = File.Exists(Path.Combine(Application.dataPath, "Resources", "EggImporterSettings.asset"));
        _settings = EggImporterSettings.Instance;
        _originalAutoImportEnabled = _settings.autoImportEnabled;
        _settings.autoImportEnabled = true;
        EditorUtility.SetDirty(_settings);

        Directory.CreateDirectory(TestFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (_settingsAssetExisted)
        {
            _settings.autoImportEnabled = _originalAutoImportEnabled;
            EditorUtility.SetDirty(_settings);
        }
        else
        {
            AssetDatabase.DeleteAsset(SettingsAssetPath);
            ClearSettingsSingleton();
        }

        AssetDatabase.DeleteAsset(TestFolder);
        AssetDatabase.Refresh();
    }

    [Test]
    public void EggImportsRequireExplicitImportSession()
    {
        string blockedPath = WriteMinimalAnimationEgg("blocked");
        AssetDatabase.ImportAsset(blockedPath, ImportAssetOptions.ForceUpdate);

        Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(blockedPath), Is.Null);
        Assert.That(EggImportSession.IsExplicitImportActive, Is.False);

        string explicitPath = WriteMinimalAnimationEgg("explicit");
        using (EggImportSession.BeginExplicitImport())
        {
            Assert.That(EggImportSession.IsExplicitImportActive, Is.True);

            using (EggImportSession.BeginExplicitImport())
            {
                Assert.That(EggImportSession.IsExplicitImportActive, Is.True);
            }

            Assert.That(EggImportSession.IsExplicitImportActive, Is.True);
            AssetDatabase.ImportAsset(explicitPath, ImportAssetOptions.ForceUpdate);
        }

        Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(explicitPath), Is.Not.Null);
        Assert.That(EggImportSession.IsExplicitImportActive, Is.False);

        string blockedAfterScopePath = WriteMinimalAnimationEgg("blocked_after_scope");
        AssetDatabase.ImportAsset(blockedAfterScopePath, ImportAssetOptions.ForceUpdate);

        Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(blockedAfterScopePath), Is.Null);
        Assert.That(EggImportSession.IsExplicitImportActive, Is.False);
    }

    private static string WriteMinimalAnimationEgg(string fileName)
    {
        string assetPath = $"{TestFolder}/{fileName}.egg";
        File.WriteAllText(assetPath, "<CoordinateSystem> { Z-Up }\n<Bundle> test {\n}\n");
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        return assetPath;
    }

    private static void ClearSettingsSingleton()
    {
        typeof(EggImporterSettings)
            .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
    }
}
