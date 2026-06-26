using POTCO.Sky;
using UnityEditor;
using UnityEngine;

public static class SkyMenu
{
    [MenuItem("POTCO/Create Sky", false, 100)]
    public static void CreatePOTCOSky()
    {
        GameObject existingSky = GameObject.Find("POTCO Sky");
        if (existingSky != null)
        {
            bool selectExisting = EditorUtility.DisplayDialog(
                "POTCO Sky Already Exists",
                "A 'POTCO Sky' GameObject already exists in the scene.",
                "Select Existing",
                "Create New");

            if (selectExisting)
            {
                Selection.activeGameObject = existingSky;
                return;
            }
        }

        GameObject potcoSky = new GameObject("POTCO Sky");
        Undo.RegisterCreatedObjectUndo(potcoSky, "Create POTCO Sky");

        SkyboxManager skyboxManager = Undo.AddComponent<SkyboxManager>(potcoSky);
        Light directionalLight = SkyboxManager.FindSceneDirectionalLight();
        if (directionalLight != null)
            skyboxManager.directionalLight = directionalLight;

        skyboxManager.InitializeSky();
        EditorUtility.SetDirty(skyboxManager);
        Selection.activeGameObject = potcoSky;

        Debug.Log("Created POTCO reference sky.");
        EditorUtility.DisplayDialog(
            "POTCO Sky Created",
            "Created a POTCO Sky GameObject using the reference SkyGroup model hierarchy and TOD settings.",
            "OK");
    }
}

[CustomEditor(typeof(SkyboxManager))]
public sealed class SkyboxManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SkyboxManager manager = (SkyboxManager)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Reference Sky Tools", EditorStyles.boldLabel);

        if (GUILayout.Button("Initialize / Rebuild Reference Sky", GUILayout.Height(28)))
        {
            Undo.RegisterFullObjectHierarchyUndo(manager.gameObject, "Rebuild POTCO Reference Sky");
            manager.InitializeSky();
            EditorUtility.SetDirty(manager);
        }

        EditorGUILayout.Space();
        DrawTimeOfDayPanel(manager);

        EditorGUILayout.Space();
        DrawSkyButtons(manager);

        EditorGUILayout.Space();
        DrawCloudButtons(manager);

        EditorGUILayout.Space();
        DrawMoonButtons(manager);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime State", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Last Sky", manager.LastSky.ToString());
        EditorGUILayout.LabelField("Cloud Level", manager.CurrentCloudLevel.ToString());
        EditorGUILayout.LabelField("Cloud Texture", string.IsNullOrEmpty(manager.CurrentCloudTextureName) ? "<none>" : manager.CurrentCloudTextureName);
    }

    private static void DrawTimeOfDayPanel(SkyboxManager manager)
    {
        EditorGUILayout.LabelField("Time Of Day", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        float hour = EditorGUILayout.Slider("Hour", manager.timeOfDay, 0f, 24f);
        if (EditorGUI.EndChangeCheck())
        {
            Apply(manager, () =>
            {
                manager.useManualPreset = false;
                manager.autoAdvanceTime = false;
                manager.ApplyTimeOfDay(hour);
            });
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply Hour"))
        {
            Apply(manager, () =>
            {
                manager.useManualPreset = false;
                manager.ApplyTimeOfDay(manager.timeOfDay);
            });
        }

        if (GUILayout.Button("Resume Cycle"))
        {
            Apply(manager, () =>
            {
                manager.useManualPreset = false;
                manager.autoAdvanceTime = true;
                manager.ApplyTimeOfDay(manager.timeOfDay);
            });
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        PresetButton(manager, "Day", SkyboxManager.TODPreset.Day);
        PresetButton(manager, "Sunset", SkyboxManager.TODPreset.Sunset);
        PresetButton(manager, "Night", SkyboxManager.TODPreset.Night);
        PresetButton(manager, "Stars", SkyboxManager.TODPreset.Stars);
        PresetButton(manager, "Overcast", SkyboxManager.TODPreset.Overcast);
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawSkyButtons(SkyboxManager manager)
    {
        EditorGUILayout.LabelField("Sky Presets", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        SkyButton(manager, "Off", SkyboxManager.SkyType.Off);
        SkyButton(manager, "Dawn", SkyboxManager.SkyType.Dawn);
        SkyButton(manager, "Day", SkyboxManager.SkyType.Day);
        SkyButton(manager, "Dusk", SkyboxManager.SkyType.Dusk);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        SkyButton(manager, "Night", SkyboxManager.SkyType.Night);
        SkyButton(manager, "Stars", SkyboxManager.SkyType.Stars);
        SkyButton(manager, "Swamp", SkyboxManager.SkyType.Swamp);
        SkyButton(manager, "Invasion", SkyboxManager.SkyType.Invasion);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        SkyButton(manager, "Halloween", SkyboxManager.SkyType.Halloween);
        SkyButton(manager, "Overcast", SkyboxManager.SkyType.Overcast);
        SkyButton(manager, "Overcast Night", SkyboxManager.SkyType.OvercastNight);
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button(Application.isPlaying ? "Transition To Manual Sky" : "Apply Manual Sky"))
        {
            Apply(manager, () =>
            {
                if (Application.isPlaying)
                    manager.TransitionSkyFromCurrent(manager.manualSkyType, manager.transitionDuration);
                else
                    manager.SetManualSky(manager.manualSkyType);
            });
        }
    }

    private static void DrawCloudButtons(SkyboxManager manager)
    {
        EditorGUILayout.LabelField("Cloud Level", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        CloudButton(manager, "Clear", 0);
        CloudButton(manager, "Light", 1);
        CloudButton(manager, "Medium", 2);
        CloudButton(manager, "Heavy", 3);
        EditorGUILayout.EndHorizontal();

        if (Application.isPlaying && GUILayout.Button("Transition To Default Cloud Level"))
            Apply(manager, () => manager.TransitionClouds(manager.defaultCloudLevel, manager.transitionDuration));
    }

    private static void DrawMoonButtons(SkyboxManager manager)
    {
        EditorGUILayout.LabelField("Moon", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Full"))
            Apply(manager, () => manager.SetMoonState(1f));
        if (GUILayout.Button("Half"))
            Apply(manager, () => manager.SetMoonState(0f));
        if (GUILayout.Button("Jolly On"))
            Apply(manager, () => manager.SetMoonOverlayAlpha(0.5f));
        if (GUILayout.Button("Jolly Off"))
            Apply(manager, () => manager.SetMoonOverlayAlpha(0f));
        EditorGUILayout.EndHorizontal();
    }

    private static void SkyButton(SkyboxManager manager, string label, SkyboxManager.SkyType skyType)
    {
        if (GUILayout.Button(label))
            Apply(manager, () => manager.SetManualSky(skyType));
    }

    private static void PresetButton(SkyboxManager manager, string label, SkyboxManager.TODPreset preset)
    {
        if (GUILayout.Button(label))
            Apply(manager, () => manager.SetPreset(preset));
    }

    private static void CloudButton(SkyboxManager manager, string label, int level)
    {
        if (GUILayout.Button(label))
            Apply(manager, () => manager.SetCloudLevel(level));
    }

    private static void Apply(SkyboxManager manager, System.Action action)
    {
        Undo.RegisterFullObjectHierarchyUndo(manager.gameObject, "Apply POTCO Sky Setting");
        action();
        EditorUtility.SetDirty(manager);
    }
}
