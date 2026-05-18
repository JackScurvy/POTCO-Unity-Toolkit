using UnityEngine;

/// <summary>
/// Simple FPS counter displayed in the top-right corner of the screen.
/// Automatically attaches to any GameObject in the scene.
/// </summary>
public class FPSCounter : MonoBehaviour
{
    private float deltaTime = 0.0f;
    private GUIStyle style;
    private Rect rect;
    private string fpsText = "";
    private float updateTimer = 0.0f;
    private const float UpdateFrequency = 0.5f;

    void Awake()
    {
        // Setup text style
        style = new GUIStyle();
        style.alignment = TextAnchor.UpperRight;
        style.fontSize = 24;
        style.normal.textColor = Color.white;
        
        // Cache rect
        int w = Screen.width;
        int h = 30;
        rect = new Rect(w - 100, 10, 90, h);
    }

    void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        
        updateTimer -= Time.unscaledDeltaTime;
        if (updateTimer <= 0f)
        {
            float fps = 1.0f / deltaTime;
            fpsText = string.Format("{0:0.} FPS", fps);
            updateTimer = UpdateFrequency;
        }
    }

    void OnGUI()
    {
        // Draw shadow for better visibility
        GUI.color = Color.black;
        GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), fpsText, style);

        // Draw main text
        GUI.color = Color.white;
        GUI.Label(rect, fpsText, style);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // Auto-create FPS counter on play
        if (FindAnyObjectByType<FPSCounter>() == null)
        {
            GameObject fpsObj = new GameObject("FPS Counter");
            fpsObj.AddComponent<FPSCounter>();
            DontDestroyOnLoad(fpsObj);
            Debug.Log("FPS Counter created automatically");
        }
    }
}
