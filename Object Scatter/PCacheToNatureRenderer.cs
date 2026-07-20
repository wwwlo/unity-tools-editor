using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class PCacheToNatureRenderer : EditorWindow
{
    [SerializeField] private UnityEngine.Object pCacheAsset;
    [SerializeField] private GameObject vegetationPrefab;
    [SerializeField] private Transform parentObject;
    
    [SerializeField] private bool useCustomNormal = true;
    [SerializeField] private bool invertNormals = false;
    [SerializeField] private bool randomizeRotation = false;
    [SerializeField] private bool randomizeScale = true;
    [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.2f);
    [SerializeField] private Vector3 positionOffset = Vector3.zero;
    [SerializeField] private float positionScale = 1f;
    [SerializeField] private bool deleteParentChildrenFirst = false;

    [MenuItem("ObjectDistributor/Point Cache to Nature Renderer")]
    static void Init()
    {
        var window = GetWindow<PCacheToNatureRenderer>("pCache → Nature Renderer");
        window.minSize = new Vector2(420, 480);
        window.titleContent = new GUIContent("pCache to NR", EditorGUIUtility.IconContent("Prefab Icon").image);
    }

    void OnGUI()
    {
        GUILayout.Label("Blender PCACHE → Nature Renderer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Converts a Blender-exported PCACHE binary file into NatureInstance objects. " +
            "Uses VisualDesignCafe.Rendering.Nature. Orient by custom_normal attribute.", 
            MessageType.Info);
        EditorGUILayout.Space();

        pCacheAsset = EditorGUILayout.ObjectField(
            new GUIContent("PCACHE Asset", "The .pCache or .asset file from Project window"), 
            pCacheAsset, typeof(UnityEngine.Object), false);
            
        vegetationPrefab = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Vegetation Prefab", "Nature Renderer-compatible prefab"), 
            vegetationPrefab, typeof(GameObject), false);
            
        parentObject = (Transform)EditorGUILayout.ObjectField(
            new GUIContent("Parent Object", "All instances will be parented here"), 
            parentObject, typeof(Transform), true);

        EditorGUILayout.Space();
        GUILayout.Label("Transform Options", EditorStyles.boldLabel);
        
        useCustomNormal = EditorGUILayout.Toggle(
            new GUIContent("Orient by custom_normal", "Use custom_normal from pCache for rotation"), 
            useCustomNormal);
        
        if (useCustomNormal)
        {
            invertNormals = EditorGUILayout.Toggle("Invert Normals", invertNormals);
        }
        else
        {
            randomizeRotation = EditorGUILayout.Toggle("Randomize Y Rotation", randomizeRotation);
        }
        
        randomizeScale = EditorGUILayout.Toggle("Randomize Scale", randomizeScale);
        if (randomizeScale)
            scaleRange = EditorGUILayout.Vector2Field("Scale Range (Min/Max)", scaleRange);
        
        positionOffset = EditorGUILayout.Vector3Field("Position Offset", positionOffset);
        positionScale = EditorGUILayout.FloatField("Position Scale", positionScale);
        
        deleteParentChildrenFirst = EditorGUILayout.Toggle("Clear Parent First", deleteParentChildrenFirst);

        EditorGUILayout.Space();
        
        bool hasNatureInstance = System.Type.GetType(
            "VisualDesignCafe.Rendering.Nature.NatureInstance, VisualDesignCafe.Rendering.Nature") != null;
        if (!hasNatureInstance)
        {
            EditorGUILayout.HelpBox("NatureInstance not found. Install Nature Renderer.", MessageType.Error);
        }
        
        GUI.enabled = pCacheAsset != null && vegetationPrefab != null && hasNatureInstance;
        
        Color prevColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f);
        
        if (GUILayout.Button("Convert pCache to Nature Renderer Objects", GUILayout.Height(45)))
        {
            ConvertPCache();
        }
        
        GUI.backgroundColor = prevColor;
        GUI.enabled = true;
    }

    void ConvertPCache()
    {
        if (pCacheAsset == null || vegetationPrefab == null)
        {
            EditorUtility.DisplayDialog("Missing References", 
                "Please assign both a PCACHE Asset and a Vegetation Prefab.", "OK");
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(pCacheAsset);
        if (string.IsNullOrEmpty(assetPath))
        {
            EditorUtility.DisplayDialog("Invalid Asset", "Could not get asset path.", "OK");
            return;
        }

        // Parse the PCACHE file directly from disk
        PCACHEParser.PointData[] points;
        try
        {
            points = PCACHEParser.Parse(assetPath);
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Parse Error", 
                $"Failed to parse PCACHE file:\n{ex.Message}", "OK");
            return;
        }

        if (points == null || points.Length == 0)
        {
            EditorUtility.DisplayDialog("No Data", "No points found in PCACHE file.", "OK");
            return;
        }

        Debug.Log($"[pCache→NR] Parsed {points.Length} points from '{pCacheAsset.name}'");

        // Create parent if not assigned
        if (parentObject == null)
        {
            GameObject parentGo = new GameObject("NatureRenderer_" + pCacheAsset.name);
            Undo.RegisterCreatedObjectUndo(parentGo, "Create NR Parent");
            parentObject = parentGo.transform;
            Selection.activeGameObject = parentGo;
        }

        // Optional: clear existing children
        if (deleteParentChildrenFirst && parentObject.childCount > 0)
        {
            if (EditorUtility.DisplayDialog("Confirm Delete", 
                $"Delete {parentObject.childCount} existing children?", "Yes", "Cancel"))
            {
                for (int i = parentObject.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(parentObject.GetChild(i).gameObject);
            }
        }

        int spawnedCount = 0;
        Undo.SetCurrentGroupName("pCache to Nature Renderer Conversion");
        int undoGroup = Undo.GetCurrentGroup();

        System.Type natureInstanceType = System.Type.GetType(
            "VisualDesignCafe.Rendering.Nature.NatureInstance, VisualDesignCafe.Rendering.Nature");
        if (natureInstanceType == null)
        {
            EditorUtility.DisplayDialog("Nature Renderer Not Found", 
                "VisualDesignCafe.Rendering.Nature.NatureInstance not found.", "OK");
            return;
        }

        try
        {
            for (int i = 0; i < points.Length; i++)
            {
                if (i % 100 == 0)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Converting pCache...", 
                        $"Spawning {i}/{points.Length}...", 
                        (float)i / points.Length))
                    {
                        Debug.LogWarning("[pCache→NR] Cancelled by user.");
                        break;
                    }
                }

                Vector3 pos = points[i].position * positionScale + positionOffset;

                GameObject instanceGO = new GameObject(vegetationPrefab.name + "_" + i);
                Undo.RegisterCreatedObjectUndo(instanceGO, "Create NatureInstance");
                
                Component natureInstance = Undo.AddComponent(instanceGO, natureInstanceType);
                
                var prefabProp = natureInstanceType.GetProperty("Prefab");
                if (prefabProp != null)
                    prefabProp.SetValue(natureInstance, vegetationPrefab);
                
                instanceGO.transform.position = pos;
                
                // Orient by custom_normal from the pCache file
                if (useCustomNormal)
                {
                    Vector3 normal = points[i].customNormal;
                    if (invertNormals) normal = -normal;
                    
                    if (normal.sqrMagnitude > 0.0001f)
                    {
                        instanceGO.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal.normalized);
                    }
                }
                else if (randomizeRotation)
                {
                    instanceGO.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                }
                
                if (randomizeScale)
                {
                    float s = Random.Range(scaleRange.x, scaleRange.y);
                    instanceGO.transform.localScale = Vector3.one * s;
                }
                
                // Parent under the specified object
                Undo.SetTransformParent(instanceGO.transform, parentObject, "Parent to NR");
                
                // Refresh NatureInstance to cache transform
                var refreshMethod = natureInstanceType.GetMethod("Refresh");
                if (refreshMethod != null)
                    refreshMethod.Invoke(natureInstance, null);

                spawnedCount++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Undo.CollapseUndoOperations(undoGroup);
        }

        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"[pCache→NR] Spawned {spawnedCount} NatureInstance objects under '{parentObject.name}'.");
        Selection.activeTransform = parentObject;
        EditorGUIUtility.PingObject(parentObject);
    }

    /// <summary>
    /// Direct parser for Blender-exported PCACHE files (binary format).
    /// Reads position and custom_normal attributes directly as raw floats.
    /// </summary>
    public static class PCACHEParser
    {
        public struct PointData
        {
            public Vector3 position;
            public Vector3 customNormal;
        }

        public static PointData[] Parse(string assetPath)
        {
            // Unity asset paths are relative to project root
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"PCACHE file not found: {fullPath}");

            byte[] fileBytes = File.ReadAllBytes(fullPath);
            
            // Find "end_header" in the byte array
            int headerEnd = -1;
            int dataStart = -1;
            
            for (int i = 0; i < fileBytes.Length - 10; i++)
            {
                if (fileBytes[i] == 'e' && 
                    fileBytes[i+1] == 'n' && 
                    fileBytes[i+2] == 'd' &&
                    fileBytes[i+3] == '_' &&
                    fileBytes[i+4] == 'h' &&
                    fileBytes[i+5] == 'e' &&
                    fileBytes[i+6] == 'a' &&
                    fileBytes[i+7] == 'd' &&
                    fileBytes[i+8] == 'e' &&
                    fileBytes[i+9] == 'r')
                {
                    headerEnd = i;
                    dataStart = i + 10;
                    // Skip newline(s) after end_header
                    while (dataStart < fileBytes.Length && 
                           (fileBytes[dataStart] == '\n' || fileBytes[dataStart] == '\r'))
                        dataStart++;
                    break;
                }
            }
            
            if (headerEnd < 0)
                throw new System.Exception("Could not find 'end_header' in PCACHE file.");
            
            // Parse header text
            string header = System.Text.Encoding.ASCII.GetString(fileBytes, 0, headerEnd);
            var lines = header.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            
            int elementCount = 0;
            List<string> properties = new List<string>();
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("elements "))
                {
                    int.TryParse(trimmed.Substring(9), out elementCount);
                }
                else if (trimmed.StartsWith("property float "))
                {
                    properties.Add(trimmed.Substring(15)); // e.g., "position.x", "custom_normal.x"
                }
            }
            
            if (elementCount == 0)
                throw new System.Exception("No element count found in PCACHE header.");
            
            if (properties.Count == 0)
                throw new System.Exception("No float properties found in PCACHE header.");
            
            // Map property names to indices
            int posX = properties.IndexOf("position.x");
            int posY = properties.IndexOf("position.y");
            int posZ = properties.IndexOf("position.z");
            int cnX = properties.IndexOf("custom_normal.x");
            int cnY = properties.IndexOf("custom_normal.y");
            int cnZ = properties.IndexOf("custom_normal.z");
            
            PointData[] points = new PointData[elementCount];
            
            using (MemoryStream ms = new MemoryStream(fileBytes, dataStart, fileBytes.Length - dataStart))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                for (int i = 0; i < elementCount; i++)
                {
                    // Read all float values for this point in declared order
                    float[] values = new float[properties.Count];
                    for (int p = 0; p < properties.Count; p++)
                        values[p] = reader.ReadSingle();
                    
                    // Extract position
                    if (posX >= 0) points[i].position.x = values[posX];
                    if (posY >= 0) points[i].position.y = values[posY];
                    if (posZ >= 0) points[i].position.z = values[posZ];
                    
                    // Extract custom_normal
                    if (cnX >= 0) points[i].customNormal.x = values[cnX];
                    if (cnY >= 0) points[i].customNormal.y = values[cnY];
                    if (cnZ >= 0) points[i].customNormal.z = values[cnZ];
                    
                    points[i].customNormal = points[i].customNormal.normalized;
                }
            }
            
            return points;
        }
    }
}