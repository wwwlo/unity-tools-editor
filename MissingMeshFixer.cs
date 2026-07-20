using UnityEngine;
using UnityEditor;

public class MissingMeshFixer
{
    [MenuItem("Tools/Fix Missing LOD0 Meshes")]
    public static void FixMeshes()
    {
        // 1. Перебираем все объекты в сцене (включая неактивные)
        MeshFilter[] allMeshFilters = GameObject.FindObjectsOfType<MeshFilter>(true);
        int fixedCount = 0;

        foreach (MeshFilter mf in allMeshFilters)
        {
            string objName = mf.gameObject.name;

            // 1. Проверка на суффикс _LOD0
            if (!objName.EndsWith("_LOD0")) continue;

            // 2. Проверка на missing mesh
            if (mf.sharedMesh != null) continue;

            // 3. Формируем имя для поиска (убираем _LOD0 и (Clone) если есть)
            string searchName = objName.Replace("_LOD0", "").Replace("(Clone)", "").Trim();

            // Ищем меш в проекте по имени (фильтр t:Mesh ищет только файлы мешей)
            string[] guids = AssetDatabase.FindAssets(searchName + " t:Mesh");

            if (guids.Length > 0)
            {
                // Берем первый найденный вариант
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                Mesh foundMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);

                if (foundMesh != null)
                {
                    // 4. Устанавливаем меш
                    Undo.RecordObject(mf, "Fix Missing Mesh");
                    mf.sharedMesh = foundMesh;
                    EditorUtility.SetDirty(mf);

                    fixedCount++;
                    Debug.Log($"<color=#00ff00>Fixed:</color> {objName} -> {foundMesh.name}", mf.gameObject);
                }
            }
            else
            {
                Debug.LogWarning($"<color=#ffff00>Mesh not found in project for:</color> {searchName}", mf.gameObject);
            }
        }

        Debug.Log($"Job done. Fixed {fixedCount} objects.");
    }
}