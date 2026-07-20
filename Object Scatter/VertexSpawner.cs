#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using VisualDesignCafe.Rendering.Nature;
using System.Collections;
using System.Collections.Generic;

public class VertexSpawner : EditorWindow
{
    public GameObject targetObject;
    public GameObject prefabToSpawn;
    public Transform parentContainer;
    public bool randomRotationY = true;
    public bool useNatureRenderer = false;
    public int batchSize = 1000;
    public float minDistance = 0.1f;
    public bool cullDuplicates = true;
    
    private bool isSpawning = false;
    private float progress = 0f;
    private string statusText = "";

    [MenuItem("ObjectDistributor/Vertex Spawner")]
    public static void ShowWindow()
    {
        GetWindow<VertexSpawner>("Vertex Spawner");
    }

    void OnGUI()
    {
        GUILayout.Label("Спавнинг объектов на вершинах (Оптимизированный)", EditorStyles.boldLabel);
        
        EditorGUI.BeginDisabledGroup(isSpawning);
        
        targetObject = (GameObject)EditorGUILayout.ObjectField("Целевой объект", targetObject, typeof(GameObject), true);
        prefabToSpawn = (GameObject)EditorGUILayout.ObjectField("Префаб для спавна", prefabToSpawn, typeof(GameObject), false);
        parentContainer = (Transform)EditorGUILayout.ObjectField("Родитель (опционально)", parentContainer, typeof(Transform), true);

        EditorGUILayout.Space();
        randomRotationY = EditorGUILayout.Toggle("Случайная ротация по Y", randomRotationY);
        useNatureRenderer = EditorGUILayout.Toggle("Использовать Nature Renderer", useNatureRenderer);
        
        EditorGUILayout.Space();
        GUILayout.Label("Оптимизация", EditorStyles.boldLabel);
        batchSize = EditorGUILayout.IntSlider("Размер батча", batchSize, 100, 5000);
        minDistance = EditorGUILayout.FloatField("Мин. расстояние между объектами", minDistance);
        cullDuplicates = EditorGUILayout.Toggle("Удалять дубликаты вершин", cullDuplicates);
        
        EditorGUI.EndDisabledGroup();

        if (isSpawning)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Статус:", statusText);
            EditorGUI.ProgressBar(GUILayoutUtility.GetRect(18, 18), progress, $"Прогресс: {(progress * 100):F1}%");
            
            if (GUILayout.Button("Отменить"))
            {
                isSpawning = false;
                EditorApplication.update -= UpdateSpawning;
            }
        }
        else
        {
            if (GUILayout.Button("Создать на вершинах"))
            {
                if (targetObject != null && prefabToSpawn != null)
                {
                    StartSpawning();
                }
                else
                {
                    EditorUtility.DisplayDialog("Ошибка", "Выберите объект и префаб.", "OK");
                }
            }
        }
    }

    private IEnumerator spawningCoroutine;
    private List<Vector3> processedVertices;
    private int currentBatchIndex;
    private int totalBatches;

    void StartSpawning()
    {
        MeshFilter meshFilter = targetObject.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            EditorUtility.DisplayDialog("Ошибка", "В объекте нет MeshFilter или меша.", "OK");
            return;
        }

        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Transform targetTransform = targetObject.transform;

        // Преобразуем вершины в мировые координаты и удаляем дубликаты
        processedVertices = ProcessVertices(vertices, targetTransform);
        
        if (processedVertices.Count == 0)
        {
            EditorUtility.DisplayDialog("Ошибка", "Нет подходящих вершин для спавна.", "OK");
            return;
        }

        totalBatches = Mathf.CeilToInt((float)processedVertices.Count / batchSize);
        currentBatchIndex = 0;
        progress = 0f;
        isSpawning = true;
        
        statusText = $"Обработка {processedVertices.Count} вершин в {totalBatches} батчах...";
        
        Undo.SetCurrentGroupName("Spawn Prefabs on Vertices (Optimized)");
        
        EditorApplication.update += UpdateSpawning;
    }

    void UpdateSpawning()
    {
        if (!isSpawning || currentBatchIndex >= totalBatches)
        {
            FinishSpawning();
            return;
        }

        // Обрабатываем один батч за кадр
        ProcessBatch();
        
        currentBatchIndex++;
        progress = (float)currentBatchIndex / totalBatches;
        statusText = $"Батч {currentBatchIndex}/{totalBatches} - Создано объектов: {currentBatchIndex * batchSize}";
        
        Repaint();
    }

    void ProcessBatch()
    {
        int startIndex = currentBatchIndex * batchSize;
        int endIndex = Mathf.Min(startIndex + batchSize, processedVertices.Count);
        
        for (int i = startIndex; i < endIndex; i++)
        {
            Vector3 worldPos = processedVertices[i];
            
            GameObject spawned = CreateInstance(worldPos);
            
            if (parentContainer != null)
                spawned.transform.SetParent(parentContainer);
                
            Undo.RegisterCreatedObjectUndo(spawned, "Spawned Prefab Batch");
        }
    }

    GameObject CreateInstance(Vector3 worldPos)
    {
        GameObject spawned;
        
        if (useNatureRenderer)
        {
            var instance = new GameObject($"NatureInstance_{processedVertices.Count}").AddComponent<NatureInstance>();
            instance.Prefab = prefabToSpawn;
            instance.transform.position = worldPos;
            spawned = instance.gameObject;
            
            if (randomRotationY)
            {
                float randomY = Random.Range(0f, 360f);
                spawned.transform.rotation = Quaternion.Euler(0, randomY, 0);
            }
            
            instance.Refresh();
        }
        else
        {
            spawned = (GameObject)PrefabUtility.InstantiatePrefab(prefabToSpawn);
            spawned.transform.position = worldPos;
            
            if (randomRotationY)
            {
                float randomY = Random.Range(0f, 360f);
                spawned.transform.rotation = Quaternion.Euler(0, randomY, 0);
            }
        }
        
        return spawned;
    }

    List<Vector3> ProcessVertices(Vector3[] vertices, Transform targetTransform)
    {
        var processed = new List<Vector3>();
        var positionSet = new HashSet<Vector3>();
        
        statusText = "Обработка вершин...";
        
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldPos = targetTransform.TransformPoint(vertices[i]);
            
            if (cullDuplicates)
            {
                // Округляем позицию для удаления близких дубликатов
                Vector3 roundedPos = new Vector3(
                    Mathf.Round(worldPos.x / minDistance) * minDistance,
                    Mathf.Round(worldPos.y / minDistance) * minDistance,
                    Mathf.Round(worldPos.z / minDistance) * minDistance
                );
                
                if (!positionSet.Contains(roundedPos))
                {
                    positionSet.Add(roundedPos);
                    processed.Add(worldPos);
                }
            }
            else
            {
                processed.Add(worldPos);
            }
        }
        
        return processed;
    }

    void FinishSpawning()
    {
        isSpawning = false;
        EditorApplication.update -= UpdateSpawning;
        
        int totalCreated = processedVertices?.Count ?? 0;
        statusText = $"Завершено! Создано {totalCreated} объектов.";
        
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        
        EditorUtility.DisplayDialog("Успех", 
            $"Создано {totalCreated} объектов из {processedVertices?.Count ?? 0} уникальных вершин.", "OK");
        
        processedVertices = null;
    }

    void OnDestroy()
    {
        if (isSpawning)
        {
            EditorApplication.update -= UpdateSpawning;
        }
    }

    void SpawnPrefabsOnVertices()
    {
        // Оставляем старый метод для совместимости, но рекомендуем использовать новый
        EditorUtility.DisplayDialog("Рекомендация", 
            "Для больших объемов данных используйте оптимизированный метод выше.", "OK");
            
        StartSpawning();
    }
}
#endif