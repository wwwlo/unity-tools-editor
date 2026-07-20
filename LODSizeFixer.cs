using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class LODSizeFixer : EditorWindow
{
    private string prefabsPath = "Assets/Prefabs";
    private float sizeThreshold = 1.5f;
    private float sizeMultiplier = 3f;
    private Vector2 scrollPosition;
    private List<string> modifiedPrefabs = new List<string>();

    [MenuItem("Tools/LOD Size Fixer")]
    public static void ShowWindow()
    {
        GetWindow<LODSizeFixer>("LOD Size Fixer");
    }

    void OnGUI()
    {
        GUILayout.Label("LOD Size Fixer", EditorStyles.boldLabel);
        
        prefabsPath = EditorGUILayout.TextField("Prefabs Path", prefabsPath);
        sizeThreshold = EditorGUILayout.FloatField("Size Threshold", sizeThreshold);
        sizeMultiplier = EditorGUILayout.FloatField("Size Multiplier", sizeMultiplier);

        if (GUILayout.Button("Process Prefabs"))
        {
            ProcessPrefabs();
        }

        if (modifiedPrefabs.Count > 0)
        {
            GUILayout.Space(10);
            GUILayout.Label("Modified Prefabs:", EditorStyles.boldLabel);
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (string prefab in modifiedPrefabs)
            {
                EditorGUILayout.LabelField(prefab);
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void ProcessPrefabs()
    {
        modifiedPrefabs.Clear();
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabsPath });
        
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            
            if (prefab != null)
            {
                bool modified = ProcessPrefab(prefab, path);
                if (modified)
                {
                    modifiedPrefabs.Add(path);
                }
            }
        }
        
        AssetDatabase.SaveAssets();
        Debug.Log($"Processed {prefabGuids.Length} prefabs. Modified {modifiedPrefabs.Count} prefabs.");
    }

    private bool ProcessPrefab(GameObject prefab, string path)
    {
        bool modified = false;
        LODGroup[] lodGroups = prefab.GetComponentsInChildren<LODGroup>(true);
        
        foreach (LODGroup lodGroup in lodGroups)
        {
            if (lodGroup.size < sizeThreshold)
            {
                // Создаем временный экземпляр префаба для модификации
                GameObject tempInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                LODGroup tempLodGroup = tempInstance.GetComponentInChildren<LODGroup>(true);
                
                if (tempLodGroup != null)
                {
                    // Модифицируем размер
                    tempLodGroup.size *= sizeMultiplier;
                    
                    // Сохраняем изменения обратно в префаб
                    PrefabUtility.SaveAsPrefabAsset(tempInstance, path);
                    modified = true;
                }
                
                // Уничтожаем временный экземпляр
                DestroyImmediate(tempInstance);
            }
        }
        
        return modified;
    }
} 