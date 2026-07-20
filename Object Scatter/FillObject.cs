using UnityEditor;
using UnityEngine;
using VisualDesignCafe.Rendering.Nature;

public class ScenePrefabDistributor : EditorWindow
{
    // ���������
    private GameObject surfaceObject; // ����������� ��� ����������
    private GameObject scatterObject; // ������ � ���� (����)
    private float density = 1f; // ��������� �������� (������� �� ������� �������)
    private float minScale = 0.8f; // ����������� �������
    private float maxScale = 1.2f; // ����������� �������
    private float rotationVariationX = 4.1f;
    private float rotationVariationY = 180f;
    private float rotationVariationZ = 4.2f;

    // === НОВЫЕ ПАРАМЕТРЫ УГЛОВЫХ ОГРАНИЧЕНИЙ ===
    private float minAngle = 0f;      // Минимальный угол отклонения (в градусах)
    private float maxAngle = 45f;     // Максимальный угол отклонения (в градусах)
    // ============================================

    private bool matchRotation = true;   // 
    private bool matchScale = true;      //  
    private bool useNatureInstance = false; // Use NatureInstance component
    private Texture2D scaleTexture;      // Added texture field
    private Vector2 textureTiling = Vector2.one;
    private Vector2 textureOffset = Vector2.zero;
    private bool useTextureScaling = false;

    [MenuItem("ObjectDistributor/FillObject")]
    public static void ShowWindow() => GetWindow<ScenePrefabDistributor>("FillObject");

    private void OnGUI()
    {
        GUILayout.Label("FillObject", EditorStyles.boldLabel);

        surfaceObject = (GameObject)EditorGUILayout.ObjectField("Surface Object:", surfaceObject, typeof(GameObject), true);
        scatterObject = (GameObject)EditorGUILayout.ObjectField("Scatter Object:", scatterObject, typeof(GameObject), true);
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Density Settings", EditorStyles.boldLabel);
        density = EditorGUILayout.Slider("Density (Objects per Unit Area):", density, 0.1f, 10f);
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Texture Settings", EditorStyles.boldLabel);
        scaleTexture = (Texture2D)EditorGUILayout.ObjectField("Scale Texture:", scaleTexture, typeof(Texture2D), false);
        useTextureScaling = EditorGUILayout.Toggle("Use Texture for Scaling", useTextureScaling);
        if (useTextureScaling)
        {
            textureTiling = EditorGUILayout.Vector2Field("Texture Tiling:", textureTiling);
            textureOffset = EditorGUILayout.Vector2Field("Texture Offset:", textureOffset);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Instance Settings", EditorStyles.boldLabel);
        useNatureInstance = EditorGUILayout.Toggle("Use Nature Instance", useNatureInstance);
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scale Settings", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        minScale = EditorGUILayout.Slider(minScale, 0.01f, maxScale);
        maxScale = EditorGUILayout.Slider(maxScale, minScale, 5f);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rotation Settings", EditorStyles.boldLabel);
        matchRotation = EditorGUILayout.Toggle("Match Rotation", matchRotation);
        if (matchRotation)
        {
            rotationVariationX = EditorGUILayout.Slider("X Rotation Variation:", rotationVariationX, 0f, 180f);
            rotationVariationY = EditorGUILayout.Slider("Y Rotation Variation:", rotationVariationY, 0f, 180f);
            rotationVariationZ = EditorGUILayout.Slider("Z Rotation Variation:", rotationVariationZ, 0f, 180f);

            // === НОВЫЕ ПОЛЗУНКИ УГЛОВЫХ ОГРАНИЧЕНИЙ ===
            minAngle = EditorGUILayout.Slider("Min Angle (°):", minAngle, 0f, maxAngle);
            if (minAngle < 0) minAngle = 0; // Safety check
            maxAngle = EditorGUILayout.Slider("Max Angle (°):", maxAngle, minAngle, 180f);
            if (maxAngle < minAngle) maxAngle = minAngle; // Ensure order
            // ==========================================
        }

        matchScale = EditorGUILayout.Toggle("Match Scale", matchScale);

        if (GUILayout.Button("Distribute Objects", GUILayout.Height(30)))
        {
            DistributeObjects();
        }

        if (GUILayout.Button("Clear All Objects", GUILayout.Height(30)))
        {
            ClearObjects();
        }
    }

    private void DistributeObjects()
    {
        if (surfaceObject == null || scatterObject == null)
        {
            Debug.LogWarning("Both Surface Object and Scatter Object must be assigned.");
            return;
        }

        // Try to find LOD0 child first
        Transform targetTransform = null;
        foreach (Transform child in surfaceObject.transform)
        {
            if (child.name.Contains("_LOD0"))
            {
                targetTransform = child;
                break;
            }
        }

        // If no LOD0 found, try to use the surface object itself
        if (targetTransform == null)
        {
            MeshFilter directMeshFilter = surfaceObject.GetComponent<MeshFilter>();
            if (directMeshFilter != null && directMeshFilter.sharedMesh != null)
            {
                targetTransform = surfaceObject.transform;
                Debug.Log($"Using surface object directly (no LOD found): {surfaceObject.name}");
            }
        }

        if (targetTransform == null)
        {
            Debug.LogWarning("Surface Object must have either a child containing '_LOD0' or a MeshFilter component.");
            return;
        }

        MeshFilter meshFilter = targetTransform.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogWarning($"{targetTransform.name} must have a MeshFilter with a valid mesh.");
            return;
        }

        // Add mesh collider if needed
        MeshCollider meshCollider = targetTransform.GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            meshCollider = targetTransform.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            Debug.Log($"Added MeshCollider to {targetTransform.name}");
        }

        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            // Transform vertices to world space using target transform
            Vector3 v1 = targetTransform.TransformPoint(vertices[triangles[i]]);
            Vector3 v2 = targetTransform.TransformPoint(vertices[triangles[i + 1]]);
            Vector3 v3 = targetTransform.TransformPoint(vertices[triangles[i + 2]]);

            // Calculate triangle area
            float triangleArea = Vector3.Cross(v2 - v1, v3 - v1).magnitude / 2f;

            // Calculate number of objects for this triangle
            int objectCount = Mathf.FloorToInt(triangleArea * density);

            for (int j = 0; j < objectCount; j++)
            {
                // Generate random point within triangle
                float r1 = Random.value;
                float r2 = Random.value;
                if (r1 + r2 > 1f)
                {
                    r1 = 1f - r1;
                    r2 = 1f - r2;
                }
                Vector3 randomPoint = v1 + r1 * (v2 - v1) + r2 * (v3 - v1);

                // Calculate normal
                Vector3 normal = Vector3.Cross(v2 - v1, v3 - v1).normalized;

                // Place object
                PlaceObjectAtPoint(randomPoint, normal);
            }
        }

        Debug.Log($"Distributed objects on {surfaceObject.name}'s {targetTransform.name}");
    }

private void PlaceObjectAtPoint(Vector3 point, Vector3 normal)
{
    GameObject newObject = null;
    
    // === 1) Базовое направление (без случайных осей) ===
    Quaternion baseRot = Quaternion.FromToRotation(Vector3.up, normal);
    float angleBase = Mathf.Acos(Mathf.Clamp(Vector3.Dot(baseRot * Vector3.up, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;

    // Если базовый угол уже выходит за пределы — сразу отказываемся от размещения
    if (matchRotation && (angleBase < minAngle || angleBase > maxAngle))
    {
        Debug.LogWarning($"Угол размещения {angleBase:F1}° вне диапазона [{minAngle}, {maxAngle}]");
        return; // Не размещаем, если проверка не пройдена
    }

    if (useNatureInstance)
    {
        newObject = new GameObject(scatterObject.name);
        var instance = newObject.AddComponent<NatureInstance>();
        instance.Prefab = scatterObject;
        newObject.transform.position = point;
        Undo.SetTransformParent(newObject.transform, surfaceObject.transform, "Parent Object");
    }
    else
    {
        newObject = Instantiate(scatterObject, point, Quaternion.identity);
        Undo.SetTransformParent(newObject.transform, surfaceObject.transform, "Parent Object");
    }

    // === 2) Применяем случайные вращающие оси (Только если базовый угол допустим) ===
    if (matchRotation)
    {
        Quaternion rotation = baseRot;

        float randX = Random.Range(-rotationVariationX, rotationVariationX);
        float randY = Random.Range(-rotationVariationY, rotationVariationY);
        float randZ = Random.Range(-rotationVariationZ, rotationVariationZ);

        // Применяем оси в порядке X → Y → Z (как было)
        rotation *= Quaternion.Euler(randX, 0f, 0f);
        rotation *= Quaternion.Euler(0f, randY, 0f);
        rotation *= Quaternion.Euler(0f, 0f, randZ);

        // Проверка после всех осей — если вышли за пределы, отменяем размещение
        float finalAngle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(rotation * Vector3.up, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
        if (finalAngle < minAngle || finalAngle > maxAngle)
        {
            Debug.LogWarning($"Итоговый угол после осей {finalAngle:F1}° выходит за пределы — размещение отменено");
            Undo.DestroyObjectImmediate(newObject);
            return;
        }

        newObject.transform.rotation = rotation;
    }

    // === 3) Масштабирование (без изменений) ===
    if (matchScale)
    {
        float randomScale;
        if (useTextureScaling && scaleTexture != null)
        {
            Vector2 uv = new Vector2(
                (point.x * textureTiling.x + textureOffset.x) % 1f,
                (point.z * textureTiling.y + textureOffset.y) % 1f
            );
            Color pixel = scaleTexture.GetPixelBilinear(uv.x, uv.y);
            float textureValue = pixel.grayscale;
            randomScale = Mathf.Lerp(minScale, maxScale, textureValue);
        }
        else
        {
            randomScale = Random.Range(minScale, maxScale);
        }

        
        newObject.transform.localScale = scatterObject.transform.localScale * randomScale;
    }

    // === 4) NatureInstance Refresh (если используется) ===
    if (useNatureInstance && newObject != null)
    {
        var instance = newObject.GetComponent<NatureInstance>();
        if (instance != null) instance.Refresh();
    }

    Undo.RegisterCreatedObjectUndo(newObject, "Instantiate Object");
}


    private void ClearObjects()
    {
        if (surfaceObject == null) return;

        // Delete all cloned objects and NatureInstance objects from Surface Object
        for (int i = surfaceObject.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = surfaceObject.transform.GetChild(i);
            if (child != null && (child.name.Contains("(Clone)") || child.GetComponent<NatureInstance>() != null))
            {
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        Debug.Log($"Cleared all cloned objects from {surfaceObject.name}");
    }
}