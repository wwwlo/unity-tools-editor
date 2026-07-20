using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;

public class Removebadprefab : EditorWindow
{
    [MenuItem("Fix/Remove Broken Prefabs")]
    public static void RemoveBrokenPrefabs()
    {
        var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        var brokenPrefabs = allObjects.Where(obj => PrefabUtility.GetPrefabAssetType(obj) == PrefabAssetType.MissingAsset).ToList();
        
        foreach (var brokenPrefab in brokenPrefabs)
        {
            Debug.Log($"Removing broken prefab: {brokenPrefab.name}");
            DestroyImmediate(brokenPrefab);
        }
        
        Debug.Log($"Removed {brokenPrefabs.Count} broken prefabs");
    }
}
