using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class ExportGrassInstances : EditorWindow
{
    private string exportPath = "Assets";
    private Terrain selectedTerrain;

    [MenuItem("Terrain/Export Grass Instances")]
    public static void ShowWindow()
    {
        GetWindow<ExportGrassInstances>("Export Grass Instances");
    }

    private void OnEnable()
    {
        // Try to get the currently selected terrain when the window opens
        selectedTerrain = Selection.activeObject as Terrain;
        if (selectedTerrain == null)
        {
            // Try to find any terrain in the scene
            selectedTerrain = FindObjectOfType<Terrain>();
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("Export Grass Instances", EditorStyles.boldLabel);

        // Terrain selection field
        EditorGUI.BeginChangeCheck();
        selectedTerrain = EditorGUILayout.ObjectField("Terrain:", selectedTerrain, typeof(Terrain), true) as Terrain;
        if (EditorGUI.EndChangeCheck())
        {
            // User selected a different terrain
            Repaint();
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Export Path:", GUILayout.Width(100));
        exportPath = EditorGUILayout.TextField(exportPath);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            string folder = EditorUtility.OpenFolderPanel("Select Export Folder", exportPath, "");
            if (!string.IsNullOrEmpty(folder))
            {
                exportPath = folder.Replace("\\", "/");
            }
        }
        GUILayout.EndHorizontal();

        GUI.enabled = selectedTerrain != null;
        if (GUILayout.Button("Export"))
        {
            ExportSelectedTerrain();
        }
        GUI.enabled = true;

        if (selectedTerrain == null)
        {
            EditorGUILayout.HelpBox("Please select a terrain to export grass instances.", MessageType.Warning);
        }
    }

    private void ExportSelectedTerrain()
    {
        if (selectedTerrain == null)
        {
            Debug.LogError("No terrain selected.");
            return;
        }

        TerrainData terrainData = selectedTerrain.terrainData;
        if (terrainData == null)
        {
            Debug.LogError("Terrain has no TerrainData.");
            return;
        }

        int layerCount = terrainData.detailPrototypes.Length;
        for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
        {
            int[,] detailLayer = terrainData.GetDetailLayer(0, 0, terrainData.detailWidth, terrainData.detailHeight, layerIndex);
            List<Vector3> positions = new List<Vector3>();
            List<Quaternion> rotations = new List<Quaternion>();
            List<float> scales = new List<float>();

            // Process each detail instance in the layer
            for (int y = 0; y < terrainData.detailHeight; y++)
            {
                for (int x = 0; x < terrainData.detailWidth; x++)
                {
                    int density = detailLayer[y, x];
                    if (density > 0)
                    {
                        // Convert detail map coordinates to world position
                        float xPos = x * terrainData.size.x / terrainData.detailWidth;
                        float zPos = y * terrainData.size.z / terrainData.detailHeight;
                        float yPos = selectedTerrain.SampleHeight(new Vector3(xPos, 0, zPos) + selectedTerrain.transform.position);
                        Vector3 worldPos = new Vector3(xPos, yPos, zPos) + selectedTerrain.transform.position;

                        // Add an instance for each density count
                        for (int i = 0; i < density; i++)
                        {
                            // Add some randomization to position within the cell
                            float randX = Random.Range(-0.5f, 0.5f) * terrainData.size.x / terrainData.detailWidth;
                            float randZ = Random.Range(-0.5f, 0.5f) * terrainData.size.z / terrainData.detailHeight;
                            Vector3 instancePos = new Vector3(worldPos.x + randX, worldPos.y, worldPos.z + randZ);
                            
                            positions.Add(instancePos);
                            
                            // Random rotation around Y axis
                            Quaternion rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
                            rotations.Add(rotation);
                            
                            // Random scale variation
                            float scale = Random.Range(0.8f, 1.2f);
                            scales.Add(scale);
                        }
                    }
                }
            }

            if (positions.Count > 0)
            {
                GrassInstanceData grassInstanceData = CreateInstance<GrassInstanceData>();
                
                // Get prototype properties
                var prototype = terrainData.detailPrototypes[layerIndex];
                grassInstanceData.mesh = prototype.prototype != null ? prototype.prototype.GetComponent<MeshFilter>()?.sharedMesh : null;
                grassInstanceData.material = prototype.prototypeTexture != null ? 
                    new Material(Shader.Find("Standard")) { mainTexture = prototype.prototypeTexture } : 
                    null;
                
                grassInstanceData.positions = positions;
                grassInstanceData.rotations = rotations;
                grassInstanceData.scales = scales;

                string fileName = $"GrassLayer_{layerIndex}.asset";
                string fullPath = $"{exportPath}/{fileName}";
                
                // Ensure the directory exists
                string directoryPath = System.IO.Path.GetDirectoryName(fullPath);
                if (!System.IO.Directory.Exists(directoryPath))
                {
                    System.IO.Directory.CreateDirectory(directoryPath);
                    AssetDatabase.Refresh();
                }
                
                // Make sure the path is a valid Unity asset path
                if (!fullPath.StartsWith("Assets/"))
                {
                    // Convert absolute path to relative path if needed
                    if (fullPath.Contains(Application.dataPath))
                    {
                        fullPath = "Assets" + fullPath.Substring(Application.dataPath.Length);
                    }
                    else
                    {
                        fullPath = "Assets/" + fileName;
                    }
                }
                
                fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);
                AssetDatabase.CreateAsset(grassInstanceData, fullPath);
                
                Debug.Log($"Exported grass layer {layerIndex} with {positions.Count} instances to {fullPath}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Exported {layerCount} detail layers to {exportPath}");
    }
}