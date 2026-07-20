using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor.SceneManagement;

public class ReplaceWithPrefabWindow : EditorWindow
{
    private GameObject targetObject;
    private GameObject replacementPrefab;

    [MenuItem("Tools/Replace Object and Duplicates with Prefab")]
    public static void ShowWindow()
    {
        GetWindow<ReplaceWithPrefabWindow>("Replace with Prefab");
    }

    private void OnGUI()
    {
        GUILayout.Label("Select target object and replacement prefab", EditorStyles.boldLabel);

        targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true);
        replacementPrefab = (GameObject)EditorGUILayout.ObjectField("Replacement Prefab", replacementPrefab, typeof(GameObject), false);

        if (GUILayout.Button("Replace All Instances"))
        {
            if (targetObject == null || replacementPrefab == null)
            {
                Debug.LogWarning("Please assign both target object and replacement prefab.");
                return;
            }

            if (!PrefabUtility.IsPartOfPrefabAsset(replacementPrefab))
            {
                Debug.LogError("Replacement must be a valid prefab asset.");
                return;
            }

            ReplaceAllInstances();
        }
    }

    private void ReplaceAllInstances()
    {
        string baseName = Regex.Replace(targetObject.name, @"\.(\d{3})$", "");

        GameObject[] allObjects = Object.FindObjectsByType<GameObject>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        List<GameObject> objectsToReplace = new List<GameObject>();

        foreach (var obj in allObjects)
        {
            if (obj == null) continue;
            string cleanName = Regex.Replace(obj.name, @"\.(\d{3})$", "");
            if (cleanName == baseName)
            {
                objectsToReplace.Add(obj);
            }
        }

        if (objectsToReplace.Count == 0)
        {
            Debug.Log("No matching objects found.");
            return;
        }

        // Отмечаем сцену как изменённую
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        foreach (var obj in objectsToReplace)
        {
            if (obj == null) continue;

            Vector3 position = obj.transform.position;
            Quaternion rotation = obj.transform.rotation;
            Vector3 scale = obj.transform.localScale;
            Transform parent = obj.transform.parent;

            // Создаём новый объект из префаба
            GameObject newObject = PrefabUtility.InstantiatePrefab(replacementPrefab) as GameObject;
            if (newObject != null)
            {
                // Регистрируем создание для Undo
                Undo.RegisterCreatedObjectUndo(newObject, "Replace with Prefab");

                newObject.transform.SetParent(parent);
                newObject.transform.position = position;
                newObject.transform.rotation = rotation;
                newObject.transform.localScale = scale;
                newObject.name = obj.name;
            }

            // ✅ Ключевое исправление: используем Undo.DestroyObjectImmediate
            Undo.DestroyObjectImmediate(obj);
        }

        Debug.Log($"Replaced {objectsToReplace.Count} instances with prefab.");
    }
}