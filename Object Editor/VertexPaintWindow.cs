// VertexPaintWindow.cs
// 2024-07-19 – Minimal vertex-paint tool without cloning meshes
// Automatically toggles ModelImporter.isReadable when needed.

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class VertexPaintWindow : EditorWindow
{
    [MenuItem("ObjectEditor/Vertex Paint")]
    static void Open() => GetWindow<VertexPaintWindow>("VP");

    // -------------------- Brush settings --------------------
    float brushRadius   = 1f;
    float brushStrength = 0.5f;
    Color brushColor    = Color.red;
    bool applyToCurrent = true; // New option
    string saveFolder = "Assets/VertexPaintedMeshes"; // Folder for saving meshes
    int selectedMaterialIndex = 0; // Selected material index for polygon painting
    bool paintAllMesh = false; // Checkbox to paint on entire mesh regardless of material

    // -------------------- Runtime state --------------------
    GameObject targetObject;
    Mesh       workingMesh;
    Color[]    vertexColors;
    Vector3[]  vertices;
    Transform  meshTransform;
    bool       isPainting;
    Material[] materials; // Materials of the target object
    int[]      submeshTriangles; // Triangles of the selected submesh

    // -------------------- Read/Write cache ------------------
    ModelImporter importer;
    bool wasReadable;
    Mesh additionalVertexStreams; // For Polybrush-like streaming

    
	void ClearColors()
{
	   if (workingMesh == null || vertexColors == null) return;
	   for (int i = 0; i < vertexColors.Length; i++)
	       vertexColors[i] = Color.white;
	   UpdateAdditionalStreams();
	   SaveMeshAsset();
}

// --------------------------------------------------------
    
	void SaveMeshAsset()
	{
		if (workingMesh == null || vertexColors == null) return;

		// Только обновляем additionalVertexStreams, не трогаем исходный меш
		UpdateAdditionalStreams();
		
		// Помечаем сцену как измененную
		UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
			UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
	}
	
	void SaveMeshToFolder()
	{
		if (workingMesh == null || targetObject == null || vertexColors == null) return;
		
		// Создаем папку если её нет
		if (!Directory.Exists(saveFolder))
		{
			Directory.CreateDirectory(saveFolder);
			AssetDatabase.Refresh();
		}
		
		// Создаем новый меш с раскрашенными вертексами
		// Используем Instantiate для полного копирования всех данных меша
		Mesh newMesh = Instantiate(workingMesh);
		newMesh.name = workingMesh.name + "_painted";
		
		// Применяем раскрашенные цвета
		newMesh.colors = (Color[])vertexColors.Clone();
		
		// Формируем путь для сохранения
		string meshFileName = newMesh.name + ".asset";
		string fullPath = Path.Combine(saveFolder, meshFileName);
		
		// Проверяем существует ли уже такой меш в папке
		Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(fullPath);
		if (existingMesh != null)
		{
			// Удаляем старый меш и создаем новый
			AssetDatabase.DeleteAsset(fullPath);
		}
		
		// Создаем новый ассет
		AssetDatabase.CreateAsset(newMesh, fullPath);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		
		Debug.Log($"Mesh saved to: {fullPath}");
		
		// Только обновляем меш в сцене, если пользователь хочет
		MeshFilter mf = targetObject.GetComponent<MeshFilter>();
		if (mf != null && EditorUtility.DisplayDialog("Replace Mesh",
			"Do you want to replace the current mesh in the scene with the saved painted mesh?",
			"Yes", "No"))
		{
			// Сбрасываем additionalVertexStreams перед заменой меша
			MeshRenderer renderer = targetObject.GetComponent<MeshRenderer>();
			if (renderer != null)
			{
				renderer.additionalVertexStreams = null;
			}
			additionalVertexStreams = null;
			
			// Заменяем меш
			mf.sharedMesh = newMesh;
			
			// Обновляем рабочие данные
			workingMesh = newMesh;
			vertexColors = (Color[])newMesh.colors.Clone();
			vertices = newMesh.vertices;
		}
	}
	
	
	void OnEnable()  => SceneView.duringSceneGui += OnSceneGUI;
	void OnDisable()
	{
		BakeAndSave();

		if (importer != null && !wasReadable)
		{
			importer.isReadable = false;
			AssetDatabase.ImportAsset(importer.assetPath, ImportAssetOptions.ForceUpdate);
		}

		SceneView.duringSceneGui -= OnSceneGUI;
	}

	void OnDestroy()
	{
		// Ensure callback is unregistered even if OnDisable isn't called
		SceneView.duringSceneGui -= OnSceneGUI;
		
		// Очищаем additionalVertexStreams при закрытии окна
		CleanupAdditionalStreams();
	}
	
	void CleanupAdditionalStreams()
	{
		if (targetObject != null)
		{
			MeshRenderer renderer = targetObject.GetComponent<MeshRenderer>();
			if (renderer != null && renderer.additionalVertexStreams != null)
			{
				// Не удаляем additionalVertexStreams, так как в них хранятся наши изменения
				// renderer.additionalVertexStreams = null;
			}
		}
	}

    // --------------------------------------------------------
    void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "1. Select an object in the Hierarchy.\n" +
            "2. Press «Select Action GameObject».\n" +
            "3. Paint in the Scene view with LMB.",
            MessageType.Info);

        EditorGUILayout.Space();
        if (targetObject == null)
        {
            if (GUILayout.Button("Select Action GameObject", GUILayout.Height(30)))
                PickSelectedGameObject();
        }
        else
        {
            if (GUILayout.Button("Deselect Action GameObject", GUILayout.Height(30)))
                DeselectGameObject();
        }

        EditorGUI.BeginDisabledGroup(targetObject == null);
        {
            brushColor    = EditorGUILayout.ColorField("Color",    brushColor);
            brushRadius   = EditorGUILayout.Slider("Radius",   brushRadius,   0.05f, 5f);
            brushStrength = EditorGUILayout.Slider("Strength", brushStrength, 0f,    1f);
            applyToCurrent = EditorGUILayout.Toggle("Apply to Current Mesh", applyToCurrent);
            
            EditorGUILayout.Space();
            saveFolder = EditorGUILayout.TextField("Save Folder", saveFolder);
            
            EditorGUILayout.Space();
            
            // Material selection for polygon painting
            if (materials != null && materials.Length > 0)
            {
                string[] materialNames = new string[materials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    materialNames[i] = materials[i] != null ? materials[i].name : "Material " + i;
                }
                
                // Checkbox to paint on entire mesh
                paintAllMesh = EditorGUILayout.Toggle("Paint All Mesh", paintAllMesh);
                
                // Only show material selection if not painting all mesh
                if (!paintAllMesh)
                {
                    int newSelectedIndex = EditorGUILayout.Popup("Paint by Material", selectedMaterialIndex, materialNames);
                    
                    // Update submesh triangles if material selection changed
                    if (newSelectedIndex != selectedMaterialIndex)
                    {
                        selectedMaterialIndex = newSelectedIndex;
                        UpdateSubmeshTriangles();
                    }
                    
                    if (GUILayout.Button("Fill Selected Material", GUILayout.Height(25)))
                        FillSelectedMaterial();
                }
            }
            
            EditorGUILayout.Space();
            if (GUILayout.Button("Clear Colors"))
                ClearColors();
            
            if (GUILayout.Button("Fill Edge Vertices", GUILayout.Height(25)))
                FillEdgeVertices();
            
            if (GUILayout.Button("Save Mesh to Folder", GUILayout.Height(30)))
                SaveMeshToFolder();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Target:", targetObject ? targetObject.name : "None");
    }

    // --------------------------------------------------------
    void PickSelectedGameObject()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("Vertex Paint",
                "No object selected in the Hierarchy!", "OK");
            return;
        }

        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            EditorUtility.DisplayDialog("Vertex Paint",
                "Selected object has no MeshFilter or mesh.", "OK");
            return;
        }

        SetupTarget(go);
    }

    // --------------------------------------------------------
    void DeselectGameObject()
    {
        targetObject = null;
        workingMesh = null;
        vertexColors = null;
        vertices = null;
        meshTransform = null;
        additionalVertexStreams = null;
    }

    // --------------------------------------------------------
    void SetupTarget(GameObject go)
    {
        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        targetObject  = go;
        meshTransform = go.transform;
        workingMesh   = mf.sharedMesh;

        // Enable Read/Write if necessary
        string path = AssetDatabase.GetAssetPath(workingMesh);
        importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer != null)
        {
            wasReadable = importer.isReadable;
            if (!wasReadable)
            {
                importer.isReadable = true;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                workingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                mf.sharedMesh = workingMesh;
            }
        }

        // Инициализируем массив цветов, не изменяя исходный меш
        if (workingMesh.colors == null || workingMesh.colors.Length == 0)
        {
            vertexColors = new Color[workingMesh.vertexCount];
            for (int i = 0; i < vertexColors.Length; i++) vertexColors[i] = Color.white;
        }
        else
        {
            // Копируем существующие цвета
            vertexColors = new Color[workingMesh.colors.Length];
            System.Array.Copy(workingMesh.colors, vertexColors, workingMesh.colors.Length);
        }
        vertices     = workingMesh.vertices;
        
        // Ensure MeshRenderer exists for additionalVertexStreams
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null)
        {
            renderer = go.AddComponent<MeshRenderer>();
        }
        
        // Get materials from MeshRenderer
        if (renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0)
        {
            materials = renderer.sharedMaterials;
            UpdateSubmeshTriangles();
        }
    }

    // --------------------------------------------------------
    void OnSceneGUI(SceneView view)
    {
        if (targetObject == null || workingMesh == null) return;

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        Event e = Event.current;

        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && !e.alt:
                isPainting = true;
                PaintUnderCursor(e);
                e.Use();
                break;

            case EventType.MouseDrag when e.button == 0 && isPainting:
                PaintUnderCursor(e);
                e.Use();
                break;

            case EventType.MouseUp when e.button == 0:
                isPainting = false;
                e.Use();
                break;
        }

        // Brush preview gizmo
        Ray cursorRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        if (Physics.Raycast(cursorRay, out RaycastHit h) && h.collider.gameObject == targetObject)
        {
            Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, 0.4f);
            Handles.DrawWireDisc(h.point, h.normal, brushRadius);
        }
    }

    // --------------------------------------------------------
    // Integrated from BrushModePaint.cs
    void PaintVertices(RaycastHit hit)
    {
        // Improved painting logic with weights and falloff
        Vector3 worldPoint = hit.point;
        float strengthModifier = 1f / 2f; // Увеличили с 1/8 до 1/2 для видимого эффекта
        
        // If painting by material and not painting all mesh, only paint vertices of selected submesh
        int[] verticesToPaint = (submeshTriangles != null && !paintAllMesh) ? submeshTriangles : Enumerable.Range(0, vertices.Length).ToArray();
        
        for (int i = 0; i < verticesToPaint.Length; i++)
        {
            int vertexIndex = verticesToPaint[i];
            Vector3 vWorld = meshTransform.TransformPoint(vertices[vertexIndex]);
            float dist = Vector3.Distance(vWorld, worldPoint);
            if (dist <= brushRadius)
            {
                float falloff = Mathf.Clamp01(1f - (dist / brushRadius)); // Linear, but can improve
                falloff = Mathf.Pow(falloff, 2); // Quadratic for less uncertainty
                vertexColors[vertexIndex] = Color.Lerp(vertexColors[vertexIndex], brushColor, falloff * brushStrength * strengthModifier);
            }
        }
        // Update streams without saving
        UpdateAdditionalStreams();
    }
    
    void UpdateSubmeshTriangles()
    {
        if (workingMesh == null || materials == null || selectedMaterialIndex >= materials.Length) return;
        
        // Get triangles for the selected submesh
        submeshTriangles = workingMesh.GetTriangles(selectedMaterialIndex);
    }
    
    void FillSelectedMaterial()
    {
        if (workingMesh == null || vertexColors == null || submeshTriangles == null) return;
        
        // Fill all vertices of the selected submesh with the current brush color
        for (int i = 0; i < submeshTriangles.Length; i++)
        {
            vertexColors[submeshTriangles[i]] = brushColor;
        }
        
        UpdateAdditionalStreams();
        SaveMeshAsset();
    }
    

    
    void FillEdgeVertices()
    {
        if (workingMesh == null || vertexColors == null) return;
        
        Vector3[] vertices = workingMesh.vertices;
        
        // Determine which triangles to process based on material selection and paintAllMesh checkbox
        int[] trianglesToProcess;
        
        if (materials != null && materials.Length > 1 && submeshTriangles != null && !paintAllMesh)
        {
            // Process only selected material's triangles
            trianglesToProcess = submeshTriangles;
        }
        else
        {
            // Process all triangles
            trianglesToProcess = workingMesh.triangles;
        }
        
        // Create a dictionary to count how many triangles each edge belongs to
        Dictionary<string, int> edgeCount = new Dictionary<string, int>();
        HashSet<int> edgeVertices = new HashSet<int>();
        
        // Process triangles
        for (int i = 0; i < trianglesToProcess.Length; i += 3)
        {
            int v1 = trianglesToProcess[i];
            int v2 = trianglesToProcess[i + 1];
            int v3 = trianglesToProcess[i + 2];
            
            // Check all three edges of the triangle
            AddEdge(edgeCount, v1, v2);
            AddEdge(edgeCount, v2, v3);
            AddEdge(edgeCount, v3, v1);
        }
        
        // Find edges that belong to only one triangle (border edges)
        foreach (var kvp in edgeCount)
        {
            if (kvp.Value == 1) // Edge belongs to only one triangle = border edge
            {
                string[] vertexIndices = kvp.Key.Split('-');
                edgeVertices.Add(int.Parse(vertexIndices[0]));
                edgeVertices.Add(int.Parse(vertexIndices[1]));
            }
        }
        
        // Color all edge vertices
        foreach (int vertexIndex in edgeVertices)
        {
            vertexColors[vertexIndex] = brushColor;
        }
        
        UpdateAdditionalStreams();
        SaveMeshAsset();
    }
    
    void AddEdge(Dictionary<string, int> edgeCount, int v1, int v2)
    {
        // Create a consistent edge key (smaller index first)
        string edgeKey = v1 < v2 ? $"{v1}-{v2}" : $"{v2}-{v1}";
        
        if (edgeCount.ContainsKey(edgeKey))
        {
            edgeCount[edgeKey]++;
        }
        else
        {
            edgeCount[edgeKey] = 1;
        }
    }

    void UpdateAdditionalStreams()
    {
        if (targetObject == null || workingMesh == null || vertexColors == null) return;
        
        MeshRenderer renderer = targetObject.GetComponent<MeshRenderer>();
        if (renderer == null) return;
        
        try
        {
            if (additionalVertexStreams == null)
            {
                additionalVertexStreams = new Mesh();
                additionalVertexStreams.name = workingMesh.name + "_AdditionalStreams";
                
                // Безопасно копируем данные
                additionalVertexStreams.vertices = (Vector3[])workingMesh.vertices.Clone();
                if (workingMesh.normals != null && workingMesh.normals.Length > 0)
                    additionalVertexStreams.normals = (Vector3[])workingMesh.normals.Clone();
                if (workingMesh.tangents != null && workingMesh.tangents.Length > 0)
                    additionalVertexStreams.tangents = (Vector4[])workingMesh.tangents.Clone();
                if (workingMesh.uv != null && workingMesh.uv.Length > 0)
                    additionalVertexStreams.uv = (Vector2[])workingMesh.uv.Clone();
                if (workingMesh.uv2 != null && workingMesh.uv2.Length > 0)
                    additionalVertexStreams.uv2 = (Vector2[])workingMesh.uv2.Clone();
                if (workingMesh.uv3 != null && workingMesh.uv3.Length > 0)
                    additionalVertexStreams.uv3 = (Vector2[])workingMesh.uv3.Clone();
                if (workingMesh.uv4 != null && workingMesh.uv4.Length > 0)
                    additionalVertexStreams.uv4 = (Vector2[])workingMesh.uv4.Clone();
                
                additionalVertexStreams.subMeshCount = workingMesh.subMeshCount;
                for (int i = 0; i < workingMesh.subMeshCount; i++)
                {
                    additionalVertexStreams.SetIndices(workingMesh.GetIndices(i), workingMesh.GetTopology(i), i);
                }
                
                additionalVertexStreams.RecalculateBounds();
            }
            
            // Обновляем только цвета
            additionalVertexStreams.colors = (Color[])vertexColors.Clone();
            renderer.additionalVertexStreams = additionalVertexStreams;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error updating additional vertex streams: {ex.Message}");
        }
    }

    void PaintUnderCursor(Event e)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;
        if (hit.collider.gameObject != targetObject) return;

        // Обновляем vertices массив перед рисованием
        if (workingMesh != null)
        {
            vertices = workingMesh.vertices;
        }
        
        PaintVertices(hit); // Use new painting
        // No real-time save
    }


    // Integrated baking from BakeAdditionalVertexStreams.cs
    void BakeAndSave()
    {
        if (targetObject == null || workingMesh == null || vertexColors == null) return;

        MeshRenderer renderer = targetObject.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        // Только обновляем additionalVertexStreams, не трогаем исходный меш
        // Это безопасно и не ломает исходные данные
        UpdateAdditionalStreams();
        
        // Помечаем сцену как измененную
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }
}