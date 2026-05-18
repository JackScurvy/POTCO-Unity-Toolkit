using UnityEngine;
using UnityEditor;
using POTCO.Sky;

public class SkyMenu
{
    [MenuItem("POTCO/Create Sky", false, 100)]
    public static void CreatePOTCOSky()
    {
        // Check if POTCO Sky already exists
        GameObject existingSky = GameObject.Find("POTCO Sky");
        if (existingSky != null)
        {
            bool select = EditorUtility.DisplayDialog("POTCO Sky Already Exists",
                "A 'POTCO Sky' GameObject already exists in the scene.\n\n" +
                "Would you like to select it instead of creating a new one?",
                "Select Existing", "Create New Anyway");

            if (select)
            {
                Selection.activeGameObject = existingSky;
                return;
            }
        }

        // Create POTCO Sky GameObject
        GameObject potcoSky = new GameObject("POTCO Sky");
        Undo.RegisterCreatedObjectUndo(potcoSky, "Create POTCO Sky");

        // Add SkyboxManager component
        SkyboxManager skyboxManager = Undo.AddComponent<SkyboxManager>(potcoSky);

        // Try to find and assign directional light
        Light directionalLight = FindDirectionalLight();
        if (directionalLight != null)
        {
            skyboxManager.directionalLight = directionalLight;
            skyboxManager.updateDirectionalLight = true;
        }

        // Add POTCOFogManager component (automatically syncs with skybox)
        POTCOFogManager fogManager = Undo.AddComponent<POTCOFogManager>(potcoSky);

        // Position at origin
        potcoSky.transform.position = Vector3.zero;

        // Select the new GameObject
        Selection.activeGameObject = potcoSky;

        Debug.Log("✅ Created POTCO Sky with SkyboxManager and POTCOFogManager");
        EditorUtility.DisplayDialog("POTCO Sky Created",
            "Successfully created 'POTCO Sky' GameObject with:\n\n" +
            "• SkyboxManager - Time-of-day sky system\n" +
            "• POTCOFogManager - Automatic fog sync\n\n" +
            "Click 'Create Skybox Material' in the Inspector to begin.\n" +
            "Use 'Enable Fog' toggle in POTCOFogManager to control fog.",
            "OK");
    }

    private static Light FindDirectionalLight()
    {
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (Light light in lights)
        {
            if (light.type == LightType.Directional)
            {
                return light;
            }
        }
        return null;
    }
}

[CustomEditor(typeof(SkyboxManager))]
public class SkyboxManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SkyboxManager skyboxManager = (SkyboxManager)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Setup Tools", EditorStyles.boldLabel);

        if (skyboxManager.skyboxMaterial == null)
        {
            EditorGUILayout.HelpBox("No skybox material assigned. Click 'Create Skybox Material' to automatically load all POTCO textures.", MessageType.Warning);

            if (GUILayout.Button("Create Skybox Material", GUILayout.Height(30)))
            {
                skyboxManager.CreateSkyboxMaterial();
                RenderSettings.skybox = skyboxManager.skyboxMaterial;
                DynamicGI.UpdateEnvironment();
                EditorUtility.SetDirty(skyboxManager);
                EditorUtility.SetDirty(RenderSettings.skybox);
                Debug.Log("SkyboxManager: Material created and assigned to scene");
            }
        }
        else
        {
            if (skyboxManager.useManualPreset)
            {
                EditorGUILayout.HelpBox("MANUAL PRESET MODE\n\n" +
                                       "Change 'Current Preset' dropdown to preview different presets.\n" +
                                       "Edit preset library values to customize each preset.\n" +
                                       "Disable 'Use Manual Preset' to switch to automatic time-based cycle.", MessageType.Info);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Quick Preset Switching", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Day")) { skyboxManager.currentPreset = POTCO.Sky.SkyboxManager.TODPreset.Day; skyboxManager.SetPreset(POTCO.Sky.SkyboxManager.TODPreset.Day); }
                if (GUILayout.Button("Sunset")) { skyboxManager.currentPreset = POTCO.Sky.SkyboxManager.TODPreset.Sunset; skyboxManager.SetPreset(POTCO.Sky.SkyboxManager.TODPreset.Sunset); }
                if (GUILayout.Button("Night")) { skyboxManager.currentPreset = POTCO.Sky.SkyboxManager.TODPreset.Night; skyboxManager.SetPreset(POTCO.Sky.SkyboxManager.TODPreset.Night); }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Stars")) { skyboxManager.currentPreset = POTCO.Sky.SkyboxManager.TODPreset.Stars; skyboxManager.SetPreset(POTCO.Sky.SkyboxManager.TODPreset.Stars); }
                if (GUILayout.Button("Overcast")) { skyboxManager.currentPreset = POTCO.Sky.SkyboxManager.TODPreset.Overcast; skyboxManager.SetPreset(POTCO.Sky.SkyboxManager.TODPreset.Overcast); }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();
                if (GUILayout.Button("Force Apply Current Preset", GUILayout.Height(25)))
                {
                    skyboxManager.SetPreset(skyboxManager.currentPreset);
                    EditorUtility.SetDirty(skyboxManager);
                    Debug.Log($"Force applied {skyboxManager.currentPreset} preset");
                }
            }
            else
            {
                EditorGUILayout.HelpBox("AUTOMATIC TIME-BASED MODE\n\n" +
                                       "Enable 'Auto Advance Time' to see continuous day/night cycle.\n" +
                                       "Sun rises at 6:00, peaks at 12:00, sets at 18:00.\n" +
                                       "Moon rises at 18:00, peaks at 0:00, sets at 6:00.\n\n" +
                                       "The cycle uses your preset library values at appropriate times.", MessageType.Info);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Quick Time Switching", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Sunrise (6:00)")) skyboxManager.timeOfDay = 6f;
                if (GUILayout.Button("Noon (12:00)")) skyboxManager.timeOfDay = 12f;
                if (GUILayout.Button("Sunset (18:00)")) skyboxManager.timeOfDay = 18f;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Midnight (0:00)")) skyboxManager.timeOfDay = 0f;
                if (GUILayout.Button("3:00 AM")) skyboxManager.timeOfDay = 3f;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Refresh Skybox Material"))
            {
                RenderSettings.skybox = skyboxManager.skyboxMaterial;
                DynamicGI.UpdateEnvironment();
                Debug.Log("SkyboxManager: Skybox refreshed");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debugging", EditorStyles.boldLabel);

            if (GUILayout.Button("Print Material Properties"))
            {
                if (skyboxManager.skyboxMaterial != null)
                {
                    Debug.Log($"Skybox Material: {skyboxManager.skyboxMaterial.name}");
                    Debug.Log($"Shader: {skyboxManager.skyboxMaterial.shader.name}");
                    Debug.Log($"Cloud Intensity: {skyboxManager.skyboxMaterial.GetFloat("_CloudIntensity")}");
                    Debug.Log($"Brightness: {skyboxManager.skyboxMaterial.GetFloat("_Brightness")}");
                    Debug.Log($"CloudLayerA: {skyboxManager.skyboxMaterial.GetTexture("_CloudLayerA")}");
                }
                else
                {
                    Debug.LogWarning("No skybox material assigned");
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Settings Management", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            try
            {
                if (GUILayout.Button("Save All Settings to JSON", GUILayout.Height(30)))
                {
                    string path = EditorUtility.SaveFilePanel("Save Skybox Settings", Application.dataPath, "SkyboxSettings", "json");
                    if (!string.IsNullOrEmpty(path))
                    {
                        skyboxManager.SaveSettingsToJson(path);
                    }
                }

                if (GUILayout.Button("Load Settings from JSON", GUILayout.Height(30)))
                {
                    string path = EditorUtility.OpenFilePanel("Load Skybox Settings", Application.dataPath, "json");
                    if (!string.IsNullOrEmpty(path))
                    {
                        skyboxManager.LoadSettingsFromJson(path);
                        EditorUtility.SetDirty(skyboxManager);
                    }
                }
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.HelpBox("Save/Load complete skybox configuration including all presets, cloud settings, light settings, and overrides.", MessageType.Info);

            EditorGUILayout.Space();
            if (GUILayout.Button("Export Current Material Settings (Copy to Clipboard)", GUILayout.Height(25)))
            {
                ExportSettings(skyboxManager);
            }

            EditorGUILayout.HelpBox("Export current material state as code to paste into preset definitions.", MessageType.Info);
        }
    }

    void ExportSettings(SkyboxManager manager)
    {
        if (manager.skyboxMaterial == null)
        {
            Debug.LogError("No skybox material assigned!");
            return;
        }

        Material mat = manager.skyboxMaterial;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        sb.AppendLine("=== SKYBOX SETTINGS (Paste these into SkyboxManager preset) ===");
        sb.AppendLine();
        sb.AppendLine($"skyColorTopA = {ColorToCode(mat.GetColor("_SkyColorTopA"))},");
        sb.AppendLine($"skyColorTopB = {ColorToCode(mat.GetColor("_SkyColorTopB"))},");
        sb.AppendLine($"skyColorHorizonA = {ColorToCode(mat.GetColor("_SkyColorHorizonA"))},");
        sb.AppendLine($"skyColorHorizonB = {ColorToCode(mat.GetColor("_SkyColorHorizonB"))},");
        sb.AppendLine($"skyColorBottomA = {ColorToCode(mat.GetColor("_SkyColorBottomA"))},");
        sb.AppendLine($"skyColorBottomB = {ColorToCode(mat.GetColor("_SkyColorBottomB"))},");
        sb.AppendLine($"stageBlend = {mat.GetFloat("_StageBlend"):F1}f,");
        sb.AppendLine($"cloudTexture = \"{GetCloudName(mat)}\",");
        sb.AppendLine($"cloudIntensity = {mat.GetFloat("_CloudIntensity"):F1}f,");
        sb.AppendLine($"cloudBlendAB = {mat.GetFloat("_CloudBlendAB"):F1}f,");
        sb.AppendLine($"starsIntensity = {mat.GetFloat("_StarsIntensity"):F2}f,");
        sb.AppendLine($"sunIntensity = {mat.GetFloat("_SunIntensity"):F1}f,");
        sb.AppendLine($"sunSize = {mat.GetFloat("_SunSize"):F2}f,");
        sb.AppendLine($"sunGlowIntensity = {mat.GetFloat("_SunGlowIntensity"):F1}f,");
        sb.AppendLine($"sunDirection = {VectorToCode(mat.GetVector("_SunDirection"))},");
        sb.AppendLine($"moonIntensity = {mat.GetFloat("_MoonIntensity"):F1}f,");
        sb.AppendLine($"moonSize = {mat.GetFloat("_MoonSize"):F3}f,");
        sb.AppendLine($"moonGlowIntensity = {mat.GetFloat("_MoonGlowIntensity"):F1}f,");
        sb.AppendLine($"moonDirection = {VectorToCode(mat.GetVector("_MoonDirection"))},");
        sb.AppendLine($"brightness = {mat.GetFloat("_Brightness"):F2}f,");
        sb.AppendLine($"exposure = {mat.GetFloat("_Exposure"):F2}f,");
        sb.AppendLine($"contrast = {mat.GetFloat("_Contrast"):F2}f");
        sb.AppendLine();
        sb.AppendLine("=== END SETTINGS ===");

        string output = sb.ToString();
        Debug.Log(output);
        EditorGUIUtility.systemCopyBuffer = output;
        Debug.Log("✓ Settings copied to clipboard!");
    }

    string ColorToCode(Color c)
    {
        return $"new Color({c.r:F2}f, {c.g:F2}f, {c.b:F2}f, {c.a:F0}f)";
    }

    string VectorToCode(Vector4 v)
    {
        return $"new Vector3({v.x:F1}f, {v.y:F1}f, {v.z:F1}f)";
    }

    string GetCloudName(Material mat)
    {
        Texture tex = mat.GetTexture("_CloudLayerA");
        if (tex != null)
        {
            if (tex.name.Contains("heavy")) return "clouds_heavy";
            if (tex.name.Contains("medium")) return "clouds_medium";
            if (tex.name.Contains("light")) return "clouds_light";
        }
        return "clouds_heavy";
    }
}
