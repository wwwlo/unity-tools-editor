using UnityEngine;
using UnityEditor;
using System.IO;

public static class RenderTextureBaker
{
    [MenuItem("Tools/Render Texture/Bake From Camera")]
    public static void BakeFromCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            Debug.LogError("No main camera found!");
            return;
        }

        BakeFromCamera(camera, 1024, 1024, "Assets/BakedTextures/CameraBaked.png");
    }

    [MenuItem("Tools/Render Texture/Bake From Selected Camera")]
    public static void BakeFromSelectedCamera()
    {
        if (Selection.activeGameObject == null)
        {
            Debug.LogError("No object selected!");
            return;
        }

        Camera camera = Selection.activeGameObject.GetComponent<Camera>();
        if (camera == null)
        {
            Debug.LogError("Selected object is not a camera!");
            return;
        }

        BakeFromCamera(camera, 1024, 1024, $"Assets/BakedTextures/{Selection.activeGameObject.name}_Baked.png");
    }

    public static void BakeFromCamera(Camera camera, int width, int height, string savePath)
    {
        if (camera == null)
        {
            Debug.LogError("Camera is null!");
            return;
        }

        RenderTexture tempRT = RenderTexture.GetTemporary(width, height, 24);
        Texture2D bakedTexture = new Texture2D(width, height, TextureFormat.RGB24, true);

        try
        {
            CameraClearFlags originalClearFlags = camera.clearFlags;
            Color originalBackgroundColor = camera.backgroundColor;
            RenderTexture originalTargetTexture = camera.targetTexture;

            camera.targetTexture = tempRT;
            camera.Render();

            RenderTexture.active = tempRT;
            bakedTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            bakedTexture.Apply();

            SaveTexture(bakedTexture, savePath);

            Debug.Log($"Successfully baked render texture from camera: {savePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error baking render texture: {e.Message}");
        }
        finally
        {
            RenderTexture.active = null;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(tempRT);
            Object.DestroyImmediate(bakedTexture);
        }
    }

    [MenuItem("Tools/Render Texture/Bake From RenderTexture")]
    public static void BakeFromRenderTexture()
    {
        if (Selection.activeObject == null || !(Selection.activeObject is RenderTexture))
        {
            Debug.LogError("No RenderTexture selected!");
            return;
        }

        RenderTexture sourceRT = Selection.activeObject as RenderTexture;
        string savePath = $"Assets/BakedTextures/{sourceRT.name}_Baked.png";
        
        BakeFromRenderTexture(sourceRT, savePath);
    }

    public static void BakeFromRenderTexture(RenderTexture sourceRT, string savePath)
    {
        if (sourceRT == null)
        {
            Debug.LogError("Source RenderTexture is null!");
            return;
        }

        Texture2D bakedTexture = new Texture2D(sourceRT.width, sourceRT.height, TextureFormat.RGB24, true);

        try
        {
            RenderTexture.active = sourceRT;
            bakedTexture.ReadPixels(new Rect(0, 0, sourceRT.width, sourceRT.height), 0, 0);
            bakedTexture.Apply();

            SaveTexture(bakedTexture, savePath);

            Debug.Log($"Successfully baked render texture: {savePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error baking render texture: {e.Message}");
        }
        finally
        {
            RenderTexture.active = null;
            Object.DestroyImmediate(bakedTexture);
        }
    }

    private static void SaveTexture(Texture2D texture, string path)
    {
        if (texture == null)
        {
            Debug.LogError("Texture is null!");
            return;
        }

        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            byte[] bytes = texture.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            
            AssetDatabase.Refresh();
            
            Debug.Log($"Texture saved to: {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error saving texture: {e.Message}");
        }
    }

    [MenuItem("Tools/Render Texture/Create Bake Window")]
    public static void ShowBakeWindow()
    {
        RenderTextureBakerWindow.ShowWindow();
    }
}

public class RenderTextureBakerWindow : EditorWindow
{
    private Camera selectedCamera;
    private RenderTexture selectedRenderTexture;
    private int width = 1024;
    private int height = 1024;
    private string savePath = "Assets/BakedTextures/BakedTexture.png";
    private bool generateMipMaps = true;

    public static void ShowWindow()
    {
        var window = GetWindow<RenderTextureBakerWindow>("Render Texture Baker");
        window.minSize = new Vector2(400, 300);
    }

    private void OnGUI()
    {
        GUILayout.Label("Render Texture Baker", EditorStyles.boldLabel);
        GUILayout.Space(10);

        selectedCamera = (Camera)EditorGUILayout.ObjectField("Camera", selectedCamera, typeof(Camera), true);
        selectedRenderTexture = (RenderTexture)EditorGUILayout.ObjectField("Render Texture", selectedRenderTexture, typeof(RenderTexture), false);
        
        width = EditorGUILayout.IntField("Width", width);
        height = EditorGUILayout.IntField("Height", height);
        generateMipMaps = EditorGUILayout.Toggle("Generate Mip Maps", generateMipMaps);
        
        GUILayout.Space(10);
        GUILayout.Label("Save Path:", EditorStyles.label);
        savePath = EditorGUILayout.TextField(savePath);
        
        if (GUILayout.Button("Browse..."))
        {
            string directory = Path.GetDirectoryName(savePath);
            string fileName = Path.GetFileName(savePath);
            string newPath = EditorUtility.SaveFilePanel("Save Texture", directory, fileName, "png");
            
            if (!string.IsNullOrEmpty(newPath))
            {
                if (newPath.StartsWith(Application.dataPath))
                {
                    savePath = "Assets" + newPath.Substring(Application.dataPath.Length);
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Path", "Please select a path within the Assets folder.", "OK");
                }
            }
        }

        GUILayout.Space(20);

        if (GUILayout.Button("Bake from Camera", GUILayout.Height(30)))
        {
            if (selectedCamera != null)
            {
                RenderTextureBaker.BakeFromCamera(selectedCamera, width, height, savePath);
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Please select a camera!", "OK");
            }
        }

        if (GUILayout.Button("Bake from RenderTexture", GUILayout.Height(30)))
        {
            if (selectedRenderTexture != null)
            {
                RenderTextureBaker.BakeFromRenderTexture(selectedRenderTexture, savePath);
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Please select a render texture!", "OK");
            }
        }
    }
}