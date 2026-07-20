using UnityEngine;
using UnityEditor;

public class MeshGridSpawnerWindow : EditorWindow
{
    string searchFolder = "Assets";
    int columns = 10;
    Vector2 spacingXZ = new Vector2(2f, 2f);
    Material meshMaterial;

    [MenuItem("ObjectDistributor/Mesh Grid Spawner")]
    static void ShowWindow()
    {
        GetWindow<MeshGridSpawnerWindow>("Mesh Grid Spawner");
    }

    void OnGUI()
    {
        GUILayout.Label("Spawn Model Grid", EditorStyles.boldLabel);
        searchFolder = EditorGUILayout.TextField("Search Folder", searchFolder);
        columns = EditorGUILayout.IntField("Columns", columns);
        spacingXZ = EditorGUILayout.Vector2Field("Spacing (X,Z)", spacingXZ);
        meshMaterial = EditorGUILayout.ObjectField("Material", meshMaterial, typeof(Material), false) as Material;

        if (GUILayout.Button("Spawn Models"))
        {
            SpawnMeshes();
        }
    }

    void SpawnMeshes()
    {
        string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { searchFolder });
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("No Models Found", $"No model assets found in folder '{searchFolder}'", "OK");
            return;
        }

        GameObject parent = new GameObject("ModelGridParent");

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            EditorUtility.DisplayProgressBar("Spawning Models", prefab.name, (float)i / guids.Length);

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);

            int row = i / columns;
            int col = i % columns;
            go.transform.position = new Vector3(col * spacingXZ.x, 0f, row * spacingXZ.y);
        }

        EditorUtility.ClearProgressBar();
    }
} 