#if POTCO_INVENTORY_SCREENSHOT_CAPTURE
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace POTCO.Inventory
{
    internal sealed class PotcoInventoryScreenshotCaptureRuntime : MonoBehaviour
    {
        private const int CaptureFrameDelay = 30;
        private const int CaptureTimeoutFrames = CaptureFrameDelay + 180;

        private string outputPath;
        private int frameCount;
        private bool captureRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            var runner = new GameObject("POTCO Inventory Screenshot Runtime Capture");
            DontDestroyOnLoad(runner);
            runner.AddComponent<PotcoInventoryScreenshotCaptureRuntime>();
        }

        private void Awake()
        {
            outputPath = GetArgumentValue("-potcoInventoryScreenshot");
            if (string.IsNullOrEmpty(outputPath))
                outputPath = Path.Combine(Application.dataPath, "..", "potco-inventory-current.png");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            Application.targetFrameRate = 60;
            CreateScreenshotScene();
        }

        private void Update()
        {
            frameCount++;

            if (!captureRequested && frameCount >= CaptureFrameDelay)
            {
                captureRequested = true;
                ScreenCapture.CaptureScreenshot(outputPath);
                return;
            }

            if (captureRequested && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
            {
                Debug.Log("POTCO_INVENTORY_SCREENSHOT_CAPTURED " + outputPath);
                Application.Quit(0);
            }
            else if (frameCount > CaptureTimeoutFrames)
            {
                Debug.LogError("POTCO inventory screenshot was not written: " + outputPath);
                Application.Quit(1);
            }
        }

        private static void CreateScreenshotScene()
        {
            Scene scene = SceneManager.CreateScene("POTCO Inventory Screenshot Scene");
            SceneManager.SetActiveScene(scene);

            Camera camera = new GameObject("POTCO Inventory Screenshot Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.07f, 1f);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            GameObject host = new GameObject("POTCO Inventory Screenshot Host");
            PotcoChestGui gui = host.AddComponent<PotcoChestGui>();
            PotcoInventoryController controller = host.GetComponent<PotcoInventoryController>();
            controller.EnsureLoaded();
            AddRepresentativeItems(controller);

            gui.SetOpen(true);
            FieldInfo progressField = typeof(PotcoChestGui).GetField("chestOpenProgress", BindingFlags.Instance | BindingFlags.NonPublic);
            progressField?.SetValue(gui, 1f);
        }

        private static void AddRepresentativeItems(PotcoInventoryController controller)
        {
            foreach (int itemId in new[] { 1, 15002, 31501 })
                controller.AddItemToInventory(itemId);
        }

        private static string GetArgumentValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return string.Empty;
        }
    }
}
#endif
