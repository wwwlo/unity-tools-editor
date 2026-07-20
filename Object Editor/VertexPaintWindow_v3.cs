/*
 * СТРУКТУРА ФАЙЛА VertexPaintWindow_v3.cs:
 * 
 * 1. ОСНОВНОЙ КЛАСС: VertexPaintWindow_v3 : EditorWindow
 *    - Unity Editor окно для рисования по вертексам мешей
 *    - Использует additionalVertexStreams для неразрушающего редактирования
 * 
 * 2. ОСНОВНЫЕ СЕКЦИИ:
 *    ├── Настройки кисти (brushRadius, brushStrength, brushColor)
 *    ├── Цветовая палитра (colorPalette, методы сохранения/загрузки)
 *    ├── Состояние рантайма (targetObject, workingMesh, vertexColors)
 *    ├── Кэш чтения/записи (ModelImporter, additionalVertexStreams)
 *    └── Материалы и подмеши (materials, submeshTriangles)
 * 
 * 3. КЛЮЧЕВЫЕ МЕТОДЫ:
 *    ├── GUI и управление:
 *    │   ├── OnGUI() - основной интерфейс
 *    │   ├── OnSceneGUI() - рисование в Scene View
 *    │   └── DrawColorPalette() - интерфейс палитры
 *    ├── Управление объектами:
 *    │   ├── PickSelectedGameObject() - выбор цели
 *    │   ├── SetupTarget() - настройка меша
 *    │   └── FindBestMeshTarget() - поиск подходящего меша
 *    ├── Рисование:
 *    │   ├── PaintUnderCursor() - основная логика рисования
 *    │   ├── PaintVertices() - рисование по вертексам
 *    │   └── RaycastMesh() - пересечение луча с мешем
 *    ├── Специальные функции:
 *    │   ├── FillSelectedMaterial() - заливка по материалу
 *    │   ├── FillEdgeVertices() - заливка граничных вертексов
 *    │   └── ClearColors() - очистка цветов
 *    └── Сохранение:
 *        ├── UpdateAdditionalStreams() - обновление потоков
 *        ├── SaveMeshAsset() - сохранение в сцену
 *        └── SaveMeshToFolder() - экспорт в файл
 * 
 * 4. ВСПОМОГАТЕЛЬНЫЕ КЛАССЫ:
 *    └── ColorPaletteData_v3 - сериализация палитры цветов
 * 
 * 5. ОСОБЕННОСТИ РЕАЛИЗАЦИИ:
 *    - Неразрушающее редактирование через additionalVertexStreams
 *    - Поддержка рисования по материалам/подмешам
 *    - Автоматическое включение Read/Write для мешей
 *    - Физический и геометрический raycast для точности
 *    - Система палитры с сохранением/загрузкой
 */

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class VertexPaintWindow_v3 : EditorWindow
{
    [MenuItem("ObjectEditor/Vertex Paint_v3")]
    static void Open() => GetWindow<VertexPaintWindow_v3>("VP");

    // -------------------- Brush settings --------------------
    float brushRadius   = 0.25f;
    float brushStrength = 0.333f;
    Color brushColor    = Color.red;
    bool applyToCurrent = true; // New option
    string saveFolder = "Assets/VertexPaintedMeshes"; // Folder for saving meshes
    int selectedMaterialIndex = 0; // Selected material index for polygon painting
    bool paintAllMesh = false; // Checkbox to paint on entire mesh regardless of material
    
    // -------------------- Color Palette --------------------
    Color[] colorPalette = new Color[]
    {
        Color.white, Color.black, Color.red, Color.green, Color.blue,
        new Color(0f, 0.5f, 0f), // Orange
        new Color(0.5f, 0f, 0f),
        new Color(0f, 0f, 0.5f),
    };
    bool showPalette = true;
    Vector2 paletteScrollPos;

    // -------------------- Runtime state --------------------
    GameObject targetObject;
    Mesh       workingMesh;
    Color[]    vertexColors;
    Vector3[]  vertices;
    Transform  meshTransform;
    bool       isPainting;
    Material[] materials; // Materials of the target object
    int[]      submeshTriangles; // Triangles of the selected submesh

    // -------------------- Multi-mesh support ------------------
    MeshFilter[] availableMeshes; // All mesh filters in hierarchy
    int selectedMeshIndex = 0; // Currently selected mesh index
    string[] meshNames; // Names for dropdown display
    //bool showMeshSelection = false; // Show mesh selection UI

    // -------------------- Read/Write cache ------------------
    ModelImporter importer;
    bool wasReadable;
    Mesh additionalVertexStreams; // For Polybrush-like streaming

    // -------------------- Undo support ------------------
    Color[] undoVertexColors; // Backup for undo operations
    bool hasUndoData = false; // Whether we have undo data available

    
	void ClearColors()
{
	   if (workingMesh == null || vertexColors == null) return;
	   
	   // Сохраняем состояние для Undo
	   SaveUndoState("Clear Colors");
	   
	   for (int i = 0; i < vertexColors.Length; i++)
	       vertexColors[i] = Color.white;
	   UpdateAdditionalStreams();
	   SaveMeshAsset();
}

// -------------------- Undo Methods --------------------
void SaveUndoState(string operationName)
{
    if (vertexColors == null) return;
    
    // Регистрируем операцию Undo для Unity
    Undo.RegisterCompleteObjectUndo(this, operationName);
    
    // Сохраняем текущее состояние цветов
    undoVertexColors = new Color[vertexColors.Length];
    System.Array.Copy(vertexColors, undoVertexColors, vertexColors.Length);
    hasUndoData = true;
}

void PerformUndo()
{
    if (!hasUndoData || undoVertexColors == null || vertexColors == null) return;
    
    // Восстанавливаем предыдущее состояние
    System.Array.Copy(undoVertexColors, vertexColors, vertexColors.Length);
    
    // Обновляем визуализацию
    UpdateAdditionalStreams();
    SaveMeshAsset();
    
    // Перерисовываем Scene View
    SceneView.RepaintAll();
}

// --------------------------------------------------------
    
	void SaveMeshAsset()
	{
		if (workingMesh == null || vertexColors == null) return;

		// Только обновляем additionalVertexStreams, не трогаем исходный меш
		UpdateAdditionalStreams();
		
		// Помечаем сцену как измененной
		UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
			UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
	}
	
	void SaveMeshToFolder()
	{
		if (workingMesh == null || targetObject == null || vertexColors == null) return;
		
		// Применяем текущие раскрашенные цвета к рабочему мешу
		workingMesh.colors = (Color[])vertexColors.Clone();
		
		// Помечаем меш как измененный
		EditorUtility.SetDirty(workingMesh);
		
		// Сохраняем изменения
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		
		// Сбрасываем additionalVertexStreams, так как цвета теперь в самом меше
		MeshRenderer renderer = targetObject.GetComponent<MeshRenderer>();
		if (renderer != null)
		{
			renderer.additionalVertexStreams = null;
		}
		additionalVertexStreams = null;
		
		Debug.Log($"Mesh colors saved to: {AssetDatabase.GetAssetPath(workingMesh)}");
		
		// Обновляем рабочие данные
		vertexColors = (Color[])workingMesh.colors.Clone();
		vertices = workingMesh.vertices;
	}
	
	
	void OnEnable()
	{
		SceneView.duringSceneGui += OnSceneGUI;
		Selection.selectionChanged += OnSelectionChanged;
	}
	
	void OnDisable()
	{
		BakeAndSave();

		if (importer != null && !wasReadable)
		{
			importer.isReadable = false;
			AssetDatabase.ImportAsset(importer.assetPath, ImportAssetOptions.ForceUpdate);
		}

		SceneView.duringSceneGui -= OnSceneGUI;
		Selection.selectionChanged -= OnSelectionChanged;
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

    // -------------------- Color Palette Methods --------------------
    void DrawColorPalette()
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);
        
        // Palette controls
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset Palette", GUILayout.Width(100)))
        {
            ResetPalette();
        }
        if (GUILayout.Button("Save Palette", GUILayout.Width(100)))
        {
            SavePalette();
        }
        if (GUILayout.Button("Load Palette", GUILayout.Width(100)))
        {
            LoadPalette();
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(5);
        
        // Color grid
        int colorsPerRow = 8;
        int rows = Mathf.CeilToInt((float)colorPalette.Length / colorsPerRow);
        
        paletteScrollPos = EditorGUILayout.BeginScrollView(paletteScrollPos, GUILayout.Height(Mathf.Min(rows * 25 + 10, 150)));
        
        for (int row = 0; row < rows; row++)
        {
            EditorGUILayout.BeginHorizontal();
            
            for (int col = 0; col < colorsPerRow; col++)
            {
                int index = row * colorsPerRow + col;
                if (index >= colorPalette.Length) break;
                
                Color oldColor = GUI.backgroundColor;
                GUI.backgroundColor = colorPalette[index];
                
                if (GUILayout.Button("", GUILayout.Width(20), GUILayout.Height(20)))
                {
                    brushColor = colorPalette[index];
                }
                
                GUI.backgroundColor = oldColor;
                
                // Right-click to edit color
                if (Event.current.type == EventType.ContextClick)
                {
                    Rect lastRect = GUILayoutUtility.GetLastRect();
                    if (lastRect.Contains(Event.current.mousePosition))
                    {
                        EditPaletteColor(index);
                        Event.current.Use();
                    }
                }
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        EditorGUILayout.EndScrollView();
        
        // Add current color to palette
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Current Color", GUILayout.Height(20)))
        {
            AddColorToPalette(brushColor);
        }
        if (GUILayout.Button("Clear Palette", GUILayout.Height(20)))
        {
            ClearPalette();
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndVertical();
    }
    
    void ResetPalette()
    {
        colorPalette = new Color[]
        {
            Color.white, Color.black, Color.red, Color.green, Color.blue,
            Color.yellow, Color.cyan, Color.magenta, new Color(1f, 0.5f, 0f), // Orange
            new Color(0.5f, 0f, 0.5f), // Purple
            new Color(0.5f, 0.25f, 0f), // Brown
            new Color(0.75f, 0.75f, 0.75f), // Light Gray
            new Color(0.25f, 0.25f, 0.25f), // Dark Gray
            new Color(1f, 0.75f, 0.8f), // Pink
            new Color(0.5f, 1f, 0.5f), // Light Green
            new Color(0.5f, 0.5f, 1f), // Light Blue
            new Color(1f, 1f, 0.5f) // Light Yellow
        };
    }
    
    void AddColorToPalette(Color color)
    {
        // Check if color already exists
        foreach (Color c in colorPalette)
        {
            if (Mathf.Approximately(c.r, color.r) && 
                Mathf.Approximately(c.g, color.g) && 
                Mathf.Approximately(c.b, color.b) && 
                Mathf.Approximately(c.a, color.a))
            {
                return; // Color already exists
            }
        }
        
        // Add color to palette
        Color[] newPalette = new Color[colorPalette.Length + 1];
        System.Array.Copy(colorPalette, newPalette, colorPalette.Length);
        newPalette[colorPalette.Length] = color;
        colorPalette = newPalette;
    }
    
    void ClearPalette()
    {
        if (EditorUtility.DisplayDialog("Clear Palette", 
            "Are you sure you want to clear the entire color palette?", 
            "Yes", "Cancel"))
        {
            colorPalette = new Color[0];
        }
    }
    
    void EditPaletteColor(int index)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("Edit Color"), false, () => {
            colorPalette[index] = EditorGUILayout.ColorField(colorPalette[index]);
        });
        menu.AddItem(new GUIContent("Remove Color"), false, () => {
            RemoveColorFromPalette(index);
        });
        menu.ShowAsContext();
    }
    
    void RemoveColorFromPalette(int index)
    {
        if (index < 0 || index >= colorPalette.Length) return;
        
        Color[] newPalette = new Color[colorPalette.Length - 1];
        int newIndex = 0;
        for (int i = 0; i < colorPalette.Length; i++)
        {
            if (i != index)
            {
                newPalette[newIndex] = colorPalette[i];
                newIndex++;
            }
        }
        colorPalette = newPalette;
    }
    
    void SavePalette()
    {
        string path = EditorUtility.SaveFilePanel("Save Color Palette", 
            Application.dataPath, "ColorPalette", "json");
        
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                ColorPaletteData_v3 paletteData = new ColorPaletteData_v3();
                paletteData.colors = new float[colorPalette.Length * 4];
                
                for (int i = 0; i < colorPalette.Length; i++)
                {
                    paletteData.colors[i * 4] = colorPalette[i].r;
                    paletteData.colors[i * 4 + 1] = colorPalette[i].g;
                    paletteData.colors[i * 4 + 2] = colorPalette[i].b;
                    paletteData.colors[i * 4 + 3] = colorPalette[i].a;
                }
                
                string json = JsonUtility.ToJson(paletteData, true);
                File.WriteAllText(path, json);
                
                Debug.Log("Color palette saved to: " + path);
            }
            catch (System.Exception e)
            {
                Debug.LogError("Failed to save palette: " + e.Message);
            }
        }
    }
    
    void LoadPalette()
    {
        string path = EditorUtility.OpenFilePanel("Load Color Palette", 
            Application.dataPath, "json");
        
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                ColorPaletteData_v3 paletteData = JsonUtility.FromJson<ColorPaletteData_v3>(json);
                
                if (paletteData.colors != null && paletteData.colors.Length % 4 == 0)
                {
                    int colorCount = paletteData.colors.Length / 4;
                    colorPalette = new Color[colorCount];
                    
                    for (int i = 0; i < colorCount; i++)
                    {
                        colorPalette[i] = new Color(
                            paletteData.colors[i * 4],
                            paletteData.colors[i * 4 + 1],
                            paletteData.colors[i * 4 + 2],
                            paletteData.colors[i * 4 + 3]
                        );
                    }
                    
                    Debug.Log("Color palette loaded from: " + path);
                }
                else
                {
                    Debug.LogError("Invalid palette file format");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("Failed to load palette: " + e.Message);
            }
        }
    }

    // --------------------------------------------------------
    void OnGUI()
    {
        // Обработка горячих клавиш
        Event e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            if (e.control && e.keyCode == KeyCode.Z && hasUndoData)
            {
                PerformUndo();
                e.Use();
                return;
            }
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "1. Select an object in the Hierarchy.\n" +
            "2. Press «Select Action GameObject».\n" +
            "3. Paint in the Scene view with LMB.\n" +
            "4. Use Ctrl+Z to undo painting operations.",
            MessageType.Info);

        EditorGUILayout.Space();
        GameObject newTargetObject = (GameObject)EditorGUILayout.ObjectField("Target GameObject", targetObject, typeof(GameObject), true);
        
        if (newTargetObject != targetObject)
        {
            if (newTargetObject == null)
            {
                DeselectGameObject();
            }
            else
            {
                targetObject = newTargetObject;
                FindAllAvailableMeshes(targetObject.transform);
                if (availableMeshes != null && availableMeshes.Length > 0)
                {
                    selectedMeshIndex = 0;
                    SetupTarget(availableMeshes[0].gameObject);
                }
            }
        }

        // Multi-mesh selection UI
        if (availableMeshes != null && availableMeshes.Length > 1)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Multiple meshes found:", EditorStyles.boldLabel);
            
            int newSelectedIndex = EditorGUILayout.Popup("Select Mesh", selectedMeshIndex, meshNames);
            if (newSelectedIndex != selectedMeshIndex)
            {
                selectedMeshIndex = newSelectedIndex;
                if (selectedMeshIndex >= 0 && selectedMeshIndex < availableMeshes.Length)
                {
                    SetupTarget(availableMeshes[selectedMeshIndex].gameObject);
                }
            }
            
            // Show mesh info
            if (selectedMeshIndex >= 0 && selectedMeshIndex < availableMeshes.Length)
            {
                MeshFilter selectedMF = availableMeshes[selectedMeshIndex];
                EditorGUILayout.LabelField($"Vertices: {selectedMF.sharedMesh.vertexCount}");
                EditorGUILayout.LabelField($"Triangles: {selectedMF.sharedMesh.triangles.Length / 3}");
                EditorGUILayout.LabelField($"Submeshes: {selectedMF.sharedMesh.subMeshCount}");
            }
        }

        EditorGUI.BeginDisabledGroup(targetObject == null);
        {
            brushColor    = EditorGUILayout.ColorField("Color",    brushColor);
            
            // Color Palette Section
            showPalette = EditorGUILayout.Foldout(showPalette, "Color Palette");
            if (showPalette)
            {
                DrawColorPalette();
            }
            
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
            
            // Undo button
            EditorGUI.BeginDisabledGroup(!hasUndoData);
            if (GUILayout.Button("Undo", GUILayout.Height(25)))
                PerformUndo();
            EditorGUI.EndDisabledGroup();
            
            if (GUILayout.Button("Fill Edge Vertices", GUILayout.Height(25)))
                FillEdgeVertices();
            
            if (GUILayout.Button("Save Mesh to Folder", GUILayout.Height(30)))
                SaveMeshToFolder();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Target:", targetObject ? targetObject.name : "None");
        
        // Show current selection info
        if (Selection.activeTransform != null)
        {
            // Show info about all available meshes in selection
            Transform selectedTransform = Selection.activeTransform;
            MeshFilter[] allMeshes = selectedTransform.GetComponentsInChildren<MeshFilter>();
            
            if (allMeshes.Length > 0)
            {
                EditorGUILayout.LabelField($"Available meshes: {allMeshes.Length}");
                
                // Show first few mesh names as preview
                for (int i = 0; i < Mathf.Min(3, allMeshes.Length); i++)
                {
                    if (allMeshes[i].sharedMesh != null)
                    {
                        string meshInfo = $"  {allMeshes[i].gameObject.name} ({allMeshes[i].sharedMesh.vertexCount} verts)";
                        EditorGUILayout.LabelField(meshInfo);
                    }
                }
                
                if (allMeshes.Length > 3)
                {
                    EditorGUILayout.LabelField($"  ... and {allMeshes.Length - 3} more");
                }
            }
            else
            {
                EditorGUILayout.LabelField("Selected:", Selection.activeTransform.name);
                EditorGUILayout.LabelField("Status:", "No mesh found");
            }
        }
    }

    // --------------------------------------------------------
    void OnSelectionChanged()
    {
        // If we have a target object and the selection changed to a different object with a MeshFilter,
        // automatically update the target
        Transform selectedTransform = Selection.activeTransform;
        if (selectedTransform != null && targetObject != null)
        {
            // Check if the new selection contains meshes
            MeshFilter[] newMeshes = selectedTransform.GetComponentsInChildren<MeshFilter>();
            
            if (newMeshes.Length > 0)
            {
                // Check if any of the new meshes is different from current target
                bool foundDifferentMesh = true;
                foreach (MeshFilter mf in newMeshes)
                {
                    if (mf.gameObject == targetObject)
                    {
                        foundDifferentMesh = false;
                        break;
                    }
                }
                
                // Only auto-update if we found different meshes
                if (foundDifferentMesh)
                {
                    FindAllAvailableMeshes(selectedTransform);
                    if (availableMeshes != null && availableMeshes.Length > 0)
                    {
                        selectedMeshIndex = 0;
                        SetupTarget(availableMeshes[0].gameObject);
                        Repaint(); // Refresh the window UI
                    }
                }
            }
        }
    }

    // --------------------------------------------------------
    void PickSelectedGameObject()
    {
        // Use Selection.activeTransform instead of activeGameObject to properly handle
        // individual meshes within imported objects that have multiple meshes
        Transform selectedTransform = Selection.activeTransform;
        if (selectedTransform == null)
        {
            EditorUtility.DisplayDialog("Vertex Paint",
                "No object selected in the Hierarchy!", "OK");
            return;
        }

        // Find all available meshes in the selected object and its children
        FindAllAvailableMeshes(selectedTransform);
        
        if (availableMeshes == null || availableMeshes.Length == 0)
        {
            EditorUtility.DisplayDialog("Vertex Paint",
                "Selected object has no MeshFilter or mesh.", "OK");
            return;
        }

        // If only one mesh found, select it directly
        if (availableMeshes.Length == 1)
        {
            selectedMeshIndex = 0;
            SetupTarget(availableMeshes[0].gameObject);
        }
        else
        {
            // Multiple meshes found, let user choose
            selectedMeshIndex = 0; // Default to first mesh
            SetupTarget(availableMeshes[0].gameObject);
            
            Debug.Log($"Found {availableMeshes.Length} meshes in selected object. Use dropdown to switch between them.");
        }
    }

    // --------------------------------------------------------
    void FindAllAvailableMeshes(Transform selectedTransform)
    {
        GameObject go = selectedTransform.gameObject;
        
        // Get all MeshFilters in the object and its children
        MeshFilter[] allMeshFilters = go.GetComponentsInChildren<MeshFilter>();
        
        // Filter out meshes that are null or don't have valid meshes
        List<MeshFilter> validMeshFilters = new List<MeshFilter>();
        List<string> validMeshNames = new List<string>();
        
        foreach (MeshFilter mf in allMeshFilters)
        {
            if (mf != null && mf.sharedMesh != null)
            {
                validMeshFilters.Add(mf);
                
                // Create descriptive name for dropdown
                string meshName = mf.sharedMesh.name;
                string objectName = mf.gameObject.name;
                
                // If object name is different from mesh name, show both
                if (objectName != meshName)
                {
                    validMeshNames.Add($"{objectName} ({meshName})");
                }
                else
                {
                    validMeshNames.Add(objectName);
                }
            }
        }
        
        availableMeshes = validMeshFilters.ToArray();
        meshNames = validMeshNames.ToArray();
        
        Debug.Log($"Found {availableMeshes.Length} valid meshes:");
        for (int i = 0; i < availableMeshes.Length; i++)
        {
            Debug.Log($"  {i}: {meshNames[i]} - {availableMeshes[i].sharedMesh.vertexCount} vertices");
        }
    }

    // --------------------------------------------------------
    GameObject FindBestMeshTarget(Transform selectedTransform)
    {
        GameObject go = selectedTransform.gameObject;
        
        // First, check if the selected object itself has a MeshFilter
        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            return go;
        }
        
        // If not, look for MeshFilter in children
        MeshFilter[] childMeshFilters = go.GetComponentsInChildren<MeshFilter>();
        if (childMeshFilters.Length > 0)
        {
            // Return the first child with MeshFilter
            return childMeshFilters[0].gameObject;
        }
        
        return null;
    }

    // --------------------------------------------------------
    void DeselectGameObject()
    {
        // Сбрасываем Undo данные
        hasUndoData = false;
        undoVertexColors = null;
        
        targetObject = null;
        workingMesh = null;
        vertexColors = null;
        vertices = null;
        meshTransform = null;
        additionalVertexStreams = null;
        availableMeshes = null;
        meshNames = null;
        selectedMeshIndex = 0;
    }

    // --------------------------------------------------------
    void SetupTarget(GameObject go)
    {
        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        // Сбрасываем Undo данные при смене объекта
        hasUndoData = false;
        undoVertexColors = null;

        targetObject  = go;
        meshTransform = go.transform;
        Mesh originalMesh = mf.sharedMesh;

        // Debug information about selected mesh
        Debug.Log($"Selected target: {go.name}");
        Debug.Log($"Original mesh: {originalMesh.name}");
        Debug.Log($"Mesh vertex count: {originalMesh.vertexCount}");
        Debug.Log($"Mesh path: {AssetDatabase.GetAssetPath(originalMesh)}");

        // Проверяем, является ли текущий меш уже копией (имеет суффикс __painted)
        bool isAlreadyPaintedCopy = originalMesh.name.EndsWith("__painted");
        
        if (!isAlreadyPaintedCopy)
        {
            // Создаем копию меша в папке перед началом работы
            Debug.Log("Creating mesh copy to preserve original...");
            CreateAndApplyMeshCopy(originalMesh, mf);
        }
        else
        {
            Debug.Log("Already working with painted copy, continuing...");
            workingMesh = originalMesh;
        }

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
                Debug.Log($"Enabled Read/Write for mesh: {workingMesh.name}");
            }
        }

        // Инициализируем массив цветов
        if (workingMesh.colors == null || workingMesh.colors.Length == 0)
        {
            vertexColors = new Color[workingMesh.vertexCount];
            for (int i = 0; i < vertexColors.Length; i++) vertexColors[i] = Color.white;
            Debug.Log($"Initialized {vertexColors.Length} vertex colors to white");
        }
        else
        {
            // Копируем существующие цвета
            vertexColors = new Color[workingMesh.colors.Length];
            System.Array.Copy(workingMesh.colors, vertexColors, workingMesh.colors.Length);
            Debug.Log($"Copied {vertexColors.Length} existing vertex colors");
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
            selectedMaterialIndex = Mathf.Clamp(selectedMaterialIndex, 0, materials.Length - 1);
            UpdateSubmeshTriangles();
        }
        else
        {
            materials = null;
            submeshTriangles = null;
        }
    }
    
    // --------------------------------------------------------
    void CreateAndApplyMeshCopy(Mesh originalMesh, MeshFilter targetMeshFilter)
    {
        // Создаем папку если её нет
        if (!Directory.Exists(saveFolder))
        {
            Directory.CreateDirectory(saveFolder);
            AssetDatabase.Refresh();
        }
        
        // Создаем имя для копии меша
        string baseMeshName = originalMesh.name + "__painted";
        string meshFileName = baseMeshName + ".asset";
        string fullPath = Path.Combine(saveFolder, meshFileName);
        
        // Проверяем, существует ли уже копия
        Mesh existingCopy = AssetDatabase.LoadAssetAtPath<Mesh>(fullPath);
        
        if (existingCopy != null)
        {
            // Используем существующую копию
            Debug.Log($"Found existing painted copy: {fullPath}");
            workingMesh = existingCopy;
        }
        else
        {
            // Создаем новую копию меша
            Debug.Log($"Creating new mesh copy: {fullPath}");
            Mesh newMesh = Instantiate(originalMesh);
            newMesh.name = baseMeshName;
            
            // Сохраняем копию как ассет
            AssetDatabase.CreateAsset(newMesh, fullPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            workingMesh = newMesh;
            Debug.Log($"Mesh copy created and saved to: {fullPath}");
        }
        
        // Применяем копию к объекту в сцене
        MeshRenderer renderer = targetMeshFilter.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.additionalVertexStreams = null;
        }
        additionalVertexStreams = null;
        
        targetMeshFilter.sharedMesh = workingMesh;
        Debug.Log($"Applied mesh copy to scene object. Original mesh preserved.");
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
                // Сохраняем состояние для Undo при начале рисования
                SaveUndoState("Paint Vertices");
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

        // Brush preview - только для выбранного меша
        Ray cursorRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        
        Vector3 hitPoint = Vector3.zero;
        Vector3 hitNormal = Vector3.up;
        bool hasHit = false;
        
        // Используем только mesh raycast для точного попадания в выбранный объект
        if (workingMesh != null && meshTransform != null)
        {
            Ray localRay = new Ray(
                meshTransform.InverseTransformPoint(cursorRay.origin),
                meshTransform.InverseTransformDirection(cursorRay.direction)
            );
            
            if (RaycastMesh(localRay, workingMesh, out Vector3 localHitPoint, out Vector3 localHitNormal))
            {
                hitPoint = meshTransform.TransformPoint(localHitPoint);
                hitNormal = meshTransform.TransformDirection(localHitNormal).normalized;
                hasHit = true;
            }
        }
        
        // Draw brush preview if we have a hit
        if (hasHit)
        {
            Handles.color = new Color(1, 0, 0, 0.6f);
            Handles.DrawWireDisc(hitPoint, hitNormal, brushRadius);
            
            // Optional: Draw a small dot at hit point
            Handles.color = new Color(1, 0, 0, 0.8f);
            Handles.DrawSolidDisc(hitPoint, hitNormal, brushRadius * 0.05f);
            
            // Force repaint to show brush preview
            view.Repaint();
        }
    }

    // --------------------------------------------------------
    // Integrated from BrushModePaint.cs
    void PaintVertices(RaycastHit hit)
    {
        Vector3 worldPoint = hit.point;
        PaintVerticesAtPoint(worldPoint, hit.normal);
    }
    
    void PaintVerticesAtPoint(Vector3 worldPoint, Vector3 worldNormal)
    {
        float strengthModifier = 1f;
        
        // Get unique vertices from triangles for selected submesh
        HashSet<int> verticesToPaint = new HashSet<int>();
        
        if (submeshTriangles != null && !paintAllMesh)
        {
            // Add all vertices from submesh triangles
            for (int i = 0; i < submeshTriangles.Length; i++)
            {
                if (submeshTriangles[i] < vertices.Length)
                    verticesToPaint.Add(submeshTriangles[i]);
            }
        }
        else
        {
            // Paint all vertices
            for (int i = 0; i < vertices.Length; i++)
                verticesToPaint.Add(i);
        }
        
        bool hasChanges = false;
        foreach (int vertexIndex in verticesToPaint)
        {
            Vector3 vWorld = meshTransform.TransformPoint(vertices[vertexIndex]);
            float dist = Vector3.Distance(vWorld, worldPoint);
            if (dist <= brushRadius)
            {
                float falloff = Mathf.Clamp01(1f - (dist / brushRadius));
                falloff = Mathf.Pow(falloff, 2);
                Color newColor = Color.Lerp(vertexColors[vertexIndex], brushColor, falloff * brushStrength * strengthModifier);
                
                // Only update if there's a significant change
                if (Vector4.Distance(vertexColors[vertexIndex], newColor) > 0.001f)
                {
                    vertexColors[vertexIndex] = newColor;
                    hasChanges = true;
                }
            }
        }
        
        if (hasChanges)
        {
            UpdateAdditionalStreams();
            SceneView.RepaintAll(); // Force scene view repaint
        }
    }
    
    void UpdateSubmeshTriangles()
    {
        if (workingMesh == null || materials == null || selectedMaterialIndex >= materials.Length || selectedMaterialIndex < 0) 
        {
            submeshTriangles = null;
            return;
        }
        
        try
        {
            if (selectedMaterialIndex < workingMesh.subMeshCount)
            {
                submeshTriangles = workingMesh.GetTriangles(selectedMaterialIndex);
            }
            else
            {
                submeshTriangles = null;
            }
        }
        catch (System.Exception)
        {
            submeshTriangles = null;
        }
    }
    
    void FillSelectedMaterial()
    {
        if (workingMesh == null || vertexColors == null || submeshTriangles == null) return;
        
        // Сохраняем состояние для Undo
        SaveUndoState("Fill Selected Material");
        
        // Get unique vertices from submesh triangles
        HashSet<int> uniqueVertices = new HashSet<int>();
        for (int i = 0; i < submeshTriangles.Length; i++)
        {
            if (submeshTriangles[i] >= 0 && submeshTriangles[i] < vertexColors.Length)
                uniqueVertices.Add(submeshTriangles[i]);
        }
        
        // Fill all unique vertices of the selected submesh with the current brush color
        foreach (int vertexIndex in uniqueVertices)
        {
            vertexColors[vertexIndex] = brushColor;
        }
        
        UpdateAdditionalStreams();
        SaveMeshAsset();
    }
    

    
    void FillEdgeVertices()
    {
        if (workingMesh == null || vertexColors == null) return;
        
        // Сохраняем состояние для Undo
        SaveUndoState("Fill Edge Vertices");
        
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
            }
            
            // Обновляем только цвета
            additionalVertexStreams.colors = (Color[])vertexColors.Clone();
            
            // Recalculate bounds after color update
            additionalVertexStreams.RecalculateBounds();
            
            // Apply to renderer
            renderer.additionalVertexStreams = additionalVertexStreams;
            
            // Mark renderer as dirty to ensure proper update
            EditorUtility.SetDirty(renderer);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error updating additional vertex streams: {ex.Message}");
        }
    }

    void PaintUnderCursor(Event e)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        
        // Используем только mesh raycast для точного попадания в выбранный объект
        if (workingMesh != null && meshTransform != null)
        {
            Ray localRay = new Ray(
                meshTransform.InverseTransformPoint(ray.origin),
                meshTransform.InverseTransformDirection(ray.direction)
            );
            
            if (RaycastMesh(localRay, workingMesh, out Vector3 localHitPoint, out Vector3 localHitNormal))
            {
                // Convert to world space
                Vector3 worldHitPoint = meshTransform.TransformPoint(localHitPoint);
                Vector3 worldHitNormal = meshTransform.TransformDirection(localHitNormal).normalized;
                
                if (workingMesh != null)
                {
                    vertices = workingMesh.vertices;
                }
                
                // Paint vertices directly without using RaycastHit reflection
                PaintVerticesAtPoint(worldHitPoint, worldHitNormal);
            }
        }
    }

    // Manual mesh raycast implementation
    bool RaycastMesh(Ray ray, Mesh mesh, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitPoint = Vector3.zero;
        hitNormal = Vector3.up;
        
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        
        float closestDistance = float.MaxValue;
        bool hasHit = false;
        
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = vertices[triangles[i]];
            Vector3 v1 = vertices[triangles[i + 1]];
            Vector3 v2 = vertices[triangles[i + 2]];
            
            if (RayTriangleIntersect(ray, v0, v1, v2, out float distance, out Vector3 point))
            {
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    hitPoint = point;
                    hitNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                    hasHit = true;
                }
            }
        }
        
        return hasHit;
    }
    
    // Ray-triangle intersection using Möller-Trumbore algorithm
    bool RayTriangleIntersect(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float distance, out Vector3 hitPoint)
    {
        distance = 0;
        hitPoint = Vector3.zero;
        
        const float EPSILON = 0.0000001f;
        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;
        Vector3 h = Vector3.Cross(ray.direction, edge2);
        float a = Vector3.Dot(edge1, h);
        
        if (a > -EPSILON && a < EPSILON)
            return false; // Ray is parallel to triangle
            
        float f = 1.0f / a;
        Vector3 s = ray.origin - v0;
        float u = f * Vector3.Dot(s, h);
        
        if (u < 0.0f || u > 1.0f)
            return false;
            
        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(ray.direction, q);
        
        if (v < 0.0f || u + v > 1.0f)
            return false;
            
        float t = f * Vector3.Dot(edge2, q);
        
        if (t > EPSILON)
        {
            distance = t;
            hitPoint = ray.origin + ray.direction * t;
            return true;
        }
        
        return false;
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
        
        // Помечаем сцену как измененной
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }
}

// -------------------- Color Palette Data --------------------
[System.Serializable]
public class ColorPaletteData_v3
{
    public float[] colors;
}