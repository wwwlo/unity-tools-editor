using UnityEngine;
using UnityEditor;
using System.Collections.Generic; // Добавлено
using System.Linq; // Добавлено

public class SplatmapEditorWindow : EditorWindow
{
    private GameObject targetObject;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Vector2 scrollPosition;
#pragma warning disable UDR0001 // No method with [RuntimeInitializeOnLoadMethod] attribute
    private static bool paintingEnabled;
#pragma warning restore UDR0001
    private Material currentMaterial;

    // Texture Settings
    private Texture2D splatmap;
    private int materialIndex;
    private string[] materialNames;

    // Splatmap Type
    private enum SplatmapType { SplatMap, SplatMap2 }
    private SplatmapType currentSplatmapType = SplatmapType.SplatMap;
    private readonly string[] splatmapPropertyNames = { "_SplatMap", "_SplatMap2" };

    // Painting Settings
    private int brushSize = 16;
    //private Color brushColor = Color.red;
	
	[Range(0f,1f)] private float brushR = 1f;
	[Range(0f,1f)] private float brushG = 0f;
	[Range(0f,1f)] private float brushB = 0f;
	[Range(0f,1f)] private float brushA = 1f;
	
    private LayerMask paintMask = 0;
    private float gradientPower = 1f;
    private float brushIntensity = 0.333f; // Скорость накопления цвета
    private enum UVChannel { UV1, UV2 }
	private enum BlendMode
	{
		Normal,     // обычная замена
		Multiply,   // умножение (текущий режим)
		Add,        // добавление
		Subtract,   // вычитание
		Overlay,    // наложение
		Erase       // стираем (альфа → 0)
	}

	private BlendMode blendMode = BlendMode.Normal;
    private UVChannel uvChannel = UVChannel.UV2;

    // Auto-Save Settings
    private string lastSavedPath;
    private bool autoSave = true;
    private float autoSaveInterval = 20f;
    private float lastAutoSaveTime;

    // New fields
    private bool isSaving = false;
    private Queue<Vector2> paintQueue = new Queue<Vector2>();

    [MenuItem("Terrain/Splatmap Editor")]
    static void Init()
    {
        var window = GetWindow<SplatmapEditorWindow>("Splatmap Editor");
        window.minSize = new Vector2(350, 450);
    }

    void OnGUI()
    {
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.Space(10);
        
        // Object Selection
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Select Active GameObject", GUILayout.Height(30)))
            {
                if (Selection.activeGameObject != null)
                {
                    SetTargetObject(Selection.activeGameObject);
                }
            }
            
            if (targetObject != null && GUILayout.Button("Clear Selection", GUILayout.Height(30)))
            {
                SetTargetObject(null);
            }
        }

        if (targetObject == null)
        {
            EditorGUILayout.HelpBox("Select object with MeshRenderer and MeshCollider", MessageType.Info);
            return;
        }

        // Material selection if multiple materials
        if (materialNames != null && materialNames.Length > 1)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Select Material", EditorStyles.boldLabel);
            materialIndex = EditorGUILayout.Popup("Material", materialIndex, materialNames);
            UpdateCurrentMaterial();
        }

        if (currentMaterial == null)
        {
            EditorGUILayout.HelpBox("No valid material found", MessageType.Error);
            return;
        }

        // Splatmap Type Selection
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Splatmap Type", EditorStyles.boldLabel);
        currentSplatmapType = (SplatmapType)EditorGUILayout.EnumPopup("Splatmap Type", currentSplatmapType);
        
        string currentPropertyName = splatmapPropertyNames[(int)currentSplatmapType];
        
        if (!currentMaterial.HasProperty(currentPropertyName))
        {
            EditorGUILayout.HelpBox($"Material '{currentMaterial.name}' doesn't have {currentPropertyName} property", MessageType.Error);
            return;
        }

        // Splatmap texture
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Splatmap Settings", EditorStyles.boldLabel);
        
        splatmap = (Texture2D)EditorGUILayout.ObjectField(
            "Splatmap Texture", 
            currentMaterial.GetTexture(currentPropertyName), 
            typeof(Texture2D), 
            false
        );

        if (splatmap == null)
        {
            EditorGUILayout.HelpBox("Please assign a splatmap texture", MessageType.Warning);
        }

        // Painting Settings
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Painting Settings", EditorStyles.boldLabel);
        brushSize = EditorGUILayout.IntSlider("Brush Size", brushSize, 1, 128);
        //brushColor = EditorGUILayout.ColorField("Brush Color", brushColor);
		
		EditorGUILayout.LabelField("Brush Channels", EditorStyles.boldLabel);
		brushR = EditorGUILayout.Slider("R", brushR, 0f, 1f);
		brushG = EditorGUILayout.Slider("G", brushG, 0f, 1f);
		brushB = EditorGUILayout.Slider("B", brushB, 0f, 1f);
		brushA = EditorGUILayout.Slider("A", brushA, 0f, 1f);
		
        gradientPower = EditorGUILayout.Slider("Gradient Power", gradientPower, 0.1f, 5f);
        brushIntensity = EditorGUILayout.Slider("Brush Intensity", brushIntensity, 0.01f, 1f);
		blendMode = (BlendMode)EditorGUILayout.EnumPopup("Blend Mode", blendMode);
        uvChannel = (UVChannel)EditorGUILayout.EnumPopup("UV Channel", uvChannel);
        paintMask = EditorGUILayout.LayerField("Paint Mask", paintMask);

        // Auto-Save Settings
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Auto-Save Settings", EditorStyles.boldLabel);
        autoSave = EditorGUILayout.Toggle("Auto Save", autoSave);
        if (autoSave)
        {
            autoSaveInterval = EditorGUILayout.Slider("Save Interval (sec)", autoSaveInterval, 1f, 60f);
        }

        if (GUILayout.Button("Save Now"))
        {
            SaveSplatmapToDisk();
        }

        if (GUILayout.Button("Change Save Location"))
        {
            lastSavedPath = "";
            SaveSplatmapToDisk();
        }

        // Apply button
        if (EditorGUI.EndChangeCheck() && splatmap != null)
        {
            EnableTextureReadWrite(splatmap);
            currentMaterial.SetTexture(currentPropertyName, splatmap);
            EditorUtility.SetDirty(currentMaterial);
        }

        // Start/Stop Painting
        EditorGUILayout.Space(10);
        GUI.backgroundColor = paintingEnabled ? Color.red : Color.green;
        if (GUILayout.Button(paintingEnabled ? "Stop Painting" : "Start Painting", GUILayout.Height(40)))
        {
            paintingEnabled = !paintingEnabled;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = Color.white;

        if (splatmap == null && paintingEnabled)
        {
            EditorGUILayout.HelpBox("Cannot paint without a splatmap texture", MessageType.Error);
        }
    }

    private void SetTargetObject(GameObject obj)
    {
        targetObject = obj;
        
        if (obj != null)
        {
            meshRenderer = obj.GetComponent<MeshRenderer>();
            meshCollider = obj.GetComponent<MeshCollider>();
            
            if (meshRenderer == null || meshCollider == null)
            {
                Debug.LogError("Object must have both MeshRenderer and MeshCollider");
                targetObject = null;
                meshRenderer = null;
                meshCollider = null;
                return;
            }
            
            // Setup materials dropdown
            Material[] mats = meshRenderer.sharedMaterials;
            materialNames = new string[mats.Length];
            
            for (int i = 0; i < mats.Length; i++)
            {
                materialNames[i] = mats[i] != null ? mats[i].name : "NULL";
            }
            
            materialIndex = 0;
            UpdateCurrentMaterial();
        }
        else
        {
            meshRenderer = null;
            meshCollider = null;
            currentMaterial = null;
            materialNames = null;
        }
    }

    private void UpdateCurrentMaterial()
    {
        if (meshRenderer != null && meshRenderer.sharedMaterials.Length > materialIndex)
        {
            currentMaterial = meshRenderer.sharedMaterials[materialIndex];
            
            string currentPropertyName = splatmapPropertyNames[(int)currentSplatmapType];
            
            if (currentMaterial != null && currentMaterial.HasProperty(currentPropertyName))
            {
                splatmap = currentMaterial.GetTexture(currentPropertyName) as Texture2D;
                if (splatmap != null)
                {
                    EnableTextureReadWrite(splatmap);
                }
            }
        }
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        lastAutoSaveTime = Time.realtimeSinceStartup;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        paintingEnabled = false;
        paintQueue.Clear();
    }

    private void OnDestroy()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!paintingEnabled || targetObject == null || splatmap == null) 
            return;

        // Захватываем контроль над вводом в сцене
        int controlID = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlID);

        Event e = Event.current;
        
        // Используем текущую камеру сцены для создания луча
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

        // Добавляем дебаг для проверки
        //Debug.DrawRay(ray.origin, ray.direction * 1000f, Color.red);

        // Проверяем попадание луча в объект
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Debug.Log($"Hit object: {hit.collider.gameObject.name}"); // Дебаг

		   
		    // 1. Позиция кисти = точка попадания без смещения
			Vector3 brushCenter = hit.point;

			// 2. Радиус кисти в мировых координатах (фиксированный размер)
			float brushWorldRadius = brushSize * 0.01f; // Простой масштаб для визуализации

			// 3. Рисуем кисть
			Color brushColor = new Color(brushR, brushG, brushB, brushA);
			Handles.color = new Color(brushR, brushG, brushB, 0.5f);
			Handles.DrawWireDisc(brushCenter, hit.normal, brushWorldRadius);
			Handles.DrawSolidDisc(brushCenter, hit.normal, brushWorldRadius * 0.5f);

		   
		   

            // Обработка кликов мыши
            if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
            {
                if (e.button == 0 && !e.alt && !e.control && !e.shift)
                {
                    Debug.Log("Attempting to paint..."); // Дебаг
                    Vector2 uv = GetUVCoordinates(hit);
                    Debug.Log($"UV coordinates: {uv}"); // Дебаг

                    if (uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1)
                    {
                        PaintAtUV(uv);
                        sceneView.Repaint();
                        e.Use(); // Важно: помечаем событие как использованное
                    }
                }
            }

            // Форсируем перерисовку сцены
            if (e.type == EventType.Layout)
                HandleUtility.Repaint();
        }
    }

    private Vector2 GetUVCoordinates(RaycastHit hit)
    {
        if (uvChannel == UVChannel.UV1)
        {
            return hit.textureCoord;
        }
        else
        {
            if (meshCollider == null || meshCollider.sharedMesh == null) 
                return Vector2.zero;
                
            Vector2[] uvs2 = meshCollider.sharedMesh.uv2;
            
            if (uvs2 == null || uvs2.Length == 0)
            {
                Debug.LogWarning("Mesh doesn't have UV2, using UV1 instead");
                return hit.textureCoord;
            }
            
            int triangleIndex = hit.triangleIndex;
            int[] triangles = meshCollider.sharedMesh.triangles;
            Vector3 baryCoords = hit.barycentricCoordinate;
            
            int index1 = triangles[triangleIndex * 3];
            int index2 = triangles[triangleIndex * 3 + 1];
            int index3 = triangles[triangleIndex * 3 + 2];
            
            return uvs2[index1] * baryCoords.x +
                   uvs2[index2] * baryCoords.y +
                   uvs2[index3] * baryCoords.z;
        }
    }

    private void PaintAtUV(Vector2 uv)
    {
        if (splatmap == null)
        {
            Debug.LogError("No splatmap texture assigned!");
            return;
        }

        if (isSaving)
        {
            paintQueue.Enqueue(uv); // Сохраняем координаты для последующей отрисовки
            return;
        }

        // Создаем новую текстуру если её нет или она не читаема
        if (!splatmap.isReadable)
        {
            string path = AssetDatabase.GetAssetPath(splatmap);
            if (string.IsNullOrEmpty(path))
            {
                // Создаем новую текстуру если старой нет в ассетах
                Texture2D newTexture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
                newTexture.name = "NewSplatmap";
                
                // Заполняем белым цветом
                Color[] pixels = new Color[1024 * 1024];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = Color.white;
                
                newTexture.SetPixels(pixels);
                newTexture.Apply();

                // Сохраняем в ассеты
                string newPath = AssetDatabase.GenerateUniqueAssetPath("Assets/NewSplatmap.png");
                byte[] bytes = newTexture.EncodeToPNG();
                System.IO.File.WriteAllBytes(newPath, bytes);
                AssetDatabase.ImportAsset(newPath);

                // Настраиваем импорт
                TextureImporter importer = AssetImporter.GetAtPath(newPath) as TextureImporter;
                if (importer != null)
                {
                    importer.isReadable = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }

                // Загружаем текстуру и присваиваем материалу
                splatmap = AssetDatabase.LoadAssetAtPath<Texture2D>(newPath);
                string currentPropertyName = splatmapPropertyNames[(int)currentSplatmapType];
                currentMaterial.SetTexture(currentPropertyName, splatmap);
                EditorUtility.SetDirty(currentMaterial);
            }
            else
            {
                // Включаем Read/Write для существующей текстуры
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.isReadable = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                    
                    // Перезагружаем текстуру
                    splatmap = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    string currentPropertyName = splatmapPropertyNames[(int)currentSplatmapType];
                    currentMaterial.SetTexture(currentPropertyName, splatmap);
                    EditorUtility.SetDirty(currentMaterial);
                }
            }

            // Если все еще не читаема, выходим
            if (!splatmap.isReadable)
            {
                Debug.LogError("Failed to make texture readable!");
                return;
            }
        }

        // Рисуем
        int x = Mathf.FloorToInt(uv.x * splatmap.width);
        int y = Mathf.FloorToInt(uv.y * splatmap.height);
        int halfBrush = brushSize / 2;

        try
        {
            for (int i = -halfBrush; i < halfBrush; i++)
            {
                for (int j = -halfBrush; j < halfBrush; j++)
                {
                    int px = Mathf.Clamp(x + i, 0, splatmap.width - 1);
                    int py = Mathf.Clamp(y + j, 0, splatmap.height - 1);

                    float dist = Mathf.Sqrt(i * i + j * j);
                    float radius = halfBrush;
                    float t = Mathf.Clamp01(1f - dist / radius);
                    t = Mathf.Pow(t, gradientPower);
                    t *= brushIntensity; // Применяем интенсивность кисти

					Color orig = splatmap.GetPixel(px, py);
					Color brushColor = new Color(brushR, brushG, brushB, brushA);

					Color blended = blendMode switch
					{
						BlendMode.Normal   => Color.Lerp(orig, brushColor, t),
						BlendMode.Multiply => Color.Lerp(orig, orig * brushColor, t),
						BlendMode.Add      => Color.Lerp(orig, orig + brushColor, t),
						BlendMode.Subtract => Color.Lerp(orig, orig - brushColor, t),
						BlendMode.Overlay  => Color.Lerp(orig, Color.Lerp(orig, brushColor, orig.grayscale), t),
						BlendMode.Erase    => Color.Lerp(orig, new Color(orig.r, orig.g, orig.b, orig.a * (1 - t)), t),
						_                  => Color.Lerp(orig, brushColor, t)
					};

                    splatmap.SetPixel(px, py, blended);
                }
            }

            splatmap.Apply(true);
            EditorUtility.SetDirty(splatmap);

            if (autoSave && Time.realtimeSinceStartup - lastAutoSaveTime > autoSaveInterval)
            {
                SaveSplatmapToDisk();
                
                // Обрабатываем отложенные мазки кисти
                while (paintQueue.Count > 0)
                {
                    Vector2 queuedUV = paintQueue.Dequeue();
                    PaintAtUV(queuedUV);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to paint: {e.Message}");
        }
    }

	private void SaveSplatmapToDisk()
	{
		if (splatmap == null || isSaving) return;

		try
		{
			isSaving = true;

			// 1. Если путь ещё не задан — спрашиваем у пользователя
			if (string.IsNullOrEmpty(lastSavedPath))
			{
				lastSavedPath = AssetDatabase.GetAssetPath(splatmap);
				if (string.IsNullOrEmpty(lastSavedPath))
				{
					// текстура создана в памяти, выбираем куда сохранить
					lastSavedPath = EditorUtility.SaveFilePanelInProject(
						"Save Splatmap",
						$"splatmap_{System.DateTime.Now:yyyyMMdd_HHmmss}.png",
						"png",
						"Please select a location to save the splatmap");
				}

				if (string.IsNullOrEmpty(lastSavedPath))
					return; // пользователь нажал Cancel
			}

			// 2. Создаём временную копию текущей текстуры
			Texture2D temp = new Texture2D(splatmap.width, splatmap.height, TextureFormat.RGBA32, false);
			Color[] pixels = splatmap.GetPixels();   // уже линейные, если importer.sRGB=false
			temp.SetPixels(pixels);
			temp.Apply();

			// 3. Сохраняем на диск
			byte[] bytes = temp.EncodeToPNG();
			System.IO.File.WriteAllBytes(lastSavedPath, bytes);
			Object.DestroyImmediate(temp);

			AssetDatabase.ImportAsset(lastSavedPath, ImportAssetOptions.ForceUpdate);

			// 4. Настраиваем импорт единожды
			TextureImporter importer = AssetImporter.GetAtPath(lastSavedPath) as TextureImporter;
			if (importer != null)
			{
				importer.isReadable            = true;
				importer.textureCompression    = TextureImporterCompression.Uncompressed;
				importer.sRGBTexture           = false;   // без гамма-преобразования
				importer.alphaIsTransparency   = true;
				importer.filterMode            = FilterMode.Point;
				importer.SaveAndReimport();
			}

			// 5. Подменяем ссылку в материале (без потери кисти)
			Texture2D reloaded = AssetDatabase.LoadAssetAtPath<Texture2D>(lastSavedPath);
			if (reloaded)
			{
				string propertyName = splatmapPropertyNames[(int)currentSplatmapType];
				currentMaterial.SetTexture(propertyName, reloaded);
				splatmap = reloaded;               // продолжаем рисовать в новой копии
				EnableTextureReadWrite(splatmap);  // проверяем readable
				EditorUtility.SetDirty(currentMaterial);
			}

			Debug.Log($"Splatmap saved to: {lastSavedPath}");
			lastAutoSaveTime = Time.realtimeSinceStartup;
		}
		catch (System.Exception e)
		{
			Debug.LogError($"Failed to save splatmap: {e}");
		}
		finally
		{
			isSaving = false;
		}
	}

    private void EnableTextureReadWrite(Texture2D texture)
    {
        if (texture == null) return;
        
        string path = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(path)) return;

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            bool needsReimport = false;
            
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                needsReimport = true;
            }
            
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                needsReimport = true;
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
                Debug.Log($"Updated import settings for texture: {path}");
            }
        }
    }
}