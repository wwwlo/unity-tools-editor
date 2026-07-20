using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using VisualDesignCafe.Rendering.Nature;

public class ObjectPainterWindow : EditorWindow
{
    // Constants
    private const float MAX_RAYCAST_DISTANCE = 20f;
    private const float MIN_MOUSE_MOVEMENT = 1f;
    
    // Settings for painting prefab objects
    [System.Serializable]
    public struct PrefabSettings
    {
        public GameObject prefab; // Prefab to paint
        public GrassInstanceData grassInstanceData; // Associated grass instance data for this prefab
        public bool useNatureInstance; // Use NatureInstance component instead of GameObject
        public Vector3 initialRotation; // Initial rotation
        public Vector3 randomRotation; // Random rotation range
        public float minPaintAngle; // Minimum paint angle
        public float maxPaintAngle; // Maximum paint angle
        public bool rotateToNormal; // Rotate to surface normal
        public float minTextureScale; // Minimum texture scale
        public float maxTextureScale; // Maximum texture scale
        public int density; // Paint density
        public float brushCenterScale; // Scale at brush center
        public float brushEdgeScale; // Scale at brush edge
        public float brushScaleExponent; // Scale exponent
    }

    // Painting settings
    private GameObject surfaceToPaintOn; // Surface to paint on
    private GameObject parentObject; // Parent object for painted objects
    private Texture2D textureForScaling; // Texture for scaling
    private List<PrefabSettings> prefabs = new List<PrefabSettings>(); // List of prefabs and their settings
    private float brushSize = 1f; // Brush size
    private float timeBetweenPaints = 0.1f; // Time between paints
    private float randomScaleOffset = 0.05f; // Random scale offset
    private bool isPainting = false;
    private bool isErasing = false; // Eraser mode
    private bool isScaling = false; // Scaling mode
    private float eraserProbability = 1f; // Eraser probability
    private float scaleMultiplier = 1.1f; // Scale multiplier for scaling mode
    private Vector3 lastPaintPosition = Vector3.zero;
    private float lastPaintTime = 0f;
    private Vector2 scrollPosition = Vector2.zero;


    [MenuItem("ObjectDistributor/Object Painter")]
    public static void ShowWindow() => GetWindow<ObjectPainterWindow>("Object Painter");

    private void OnGUI()
    {
        // Wrap the entire content in the scroll view
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Width(position.width), GUILayout.Height(position.height));

        try
        {
            GUILayout.Label("Object Painter Settings", EditorStyles.boldLabel);
            GameObject selectedObject = (GameObject)EditorGUILayout.ObjectField("Surface to Paint On:", surfaceToPaintOn, typeof(GameObject), true);
            parentObject = (GameObject)EditorGUILayout.ObjectField("Parent Object for Painted Objects:", parentObject, typeof(GameObject), true);
            

            
            // If a new object is selected, try to find its LOD0 child
            if (selectedObject != surfaceToPaintOn)
            {
                if (selectedObject != null)
                {
                    Transform lod0Transform = selectedObject.transform.Find(selectedObject.name + "_LOD0");
                    if (lod0Transform != null)
                    {
                        surfaceToPaintOn = lod0Transform.gameObject;
                        // Ensure the LOD0 object has a collider
                        if (!surfaceToPaintOn.TryGetComponent<Collider>(out _))
                        {
                            MeshFilter meshFilter = surfaceToPaintOn.GetComponent<MeshFilter>();
                            if (meshFilter != null && meshFilter.sharedMesh != null)
                            {
                                MeshCollider meshCollider = surfaceToPaintOn.AddComponent<MeshCollider>();
                                meshCollider.sharedMesh = meshFilter.sharedMesh;
                            }
                        }
                    }
                    else
                    {
                        surfaceToPaintOn = selectedObject;
                    }
                }
                else
                {
                    surfaceToPaintOn = null;
                }
            }
            
            textureForScaling = (Texture2D)EditorGUILayout.ObjectField("Texture for Scaling:", textureForScaling, typeof(Texture2D), false);
            brushSize = EditorGUILayout.Slider("Brush Size", brushSize, 0.1f, 50f);
            timeBetweenPaints = EditorGUILayout.FloatField("Time Between Paints", timeBetweenPaints);
            randomScaleOffset = EditorGUILayout.Slider("Random Scale Offset", randomScaleOffset, 0f, 1f);

            EditorGUILayout.LabelField("Prefab Settings", EditorStyles.boldLabel);

            // Store the index to remove (if any) outside the GUI loop
            int indexToRemove = -1;

            for (int i = 0; i < prefabs.Count; i++)
            {
                EditorGUILayout.BeginVertical("Box");
                PrefabSettings settings = prefabs[i];
                
                EditorGUILayout.LabelField($"Prefab {i + 1}", EditorStyles.boldLabel);
                settings.prefab = (GameObject)EditorGUILayout.ObjectField("Prefab:", settings.prefab, typeof(GameObject), false);
                settings.grassInstanceData = (GrassInstanceData)EditorGUILayout.ObjectField("Grass Instance Data:", settings.grassInstanceData, typeof(GrassInstanceData), false);
                settings.useNatureInstance = EditorGUILayout.Toggle("Use Nature Instance", settings.useNatureInstance);
                
                // Show warning if prefab is set but no grass instance data
                if (settings.prefab != null && settings.grassInstanceData == null && !settings.useNatureInstance)
                {
                    EditorGUILayout.HelpBox("Assign a GrassInstanceData asset to paint this prefab as grass instances. Leave empty to paint as GameObjects.", MessageType.Info);
                }
                
                // Show info about NatureInstance mode
                if (settings.useNatureInstance && settings.prefab != null)
                {
                    EditorGUILayout.HelpBox("Nature Instance mode: Objects will be rendered using Nature Renderer for optimal performance.", MessageType.Info);
                }
                
                EditorGUILayout.Space(5);
                settings.initialRotation = EditorGUILayout.Vector3Field("Initial Rotation", settings.initialRotation);
                settings.randomRotation = EditorGUILayout.Vector3Field("Random Rotation", settings.randomRotation);
                settings.minPaintAngle = EditorGUILayout.FloatField("Min Paint Angle", settings.minPaintAngle);
                settings.maxPaintAngle = EditorGUILayout.FloatField("Max Paint Angle", settings.maxPaintAngle);
                settings.rotateToNormal = EditorGUILayout.Toggle("Rotate To Normal", settings.rotateToNormal);
                settings.minTextureScale = EditorGUILayout.FloatField("Min Texture Scale", settings.minTextureScale);
                settings.maxTextureScale = EditorGUILayout.FloatField("Max Texture Scale", settings.maxTextureScale);
                settings.density = EditorGUILayout.IntSlider("Density", settings.density, 0, 300);
                settings.brushCenterScale = EditorGUILayout.FloatField("Brush Center Scale", settings.brushCenterScale);
                settings.brushEdgeScale = EditorGUILayout.FloatField("Brush Edge Scale", settings.brushEdgeScale);
                settings.brushScaleExponent = EditorGUILayout.FloatField("Brush Scale Exponent", settings.brushScaleExponent);
                prefabs[i] = settings;

                if (GUILayout.Button("Remove Prefab"))
                {
                    indexToRemove = i;
                }
                EditorGUILayout.EndVertical();
            }

            // Handle removal outside the GUI loop
            if (indexToRemove >= 0)
            {
                prefabs.RemoveAt(indexToRemove);
                GUIUtility.ExitGUI(); // Properly exit the GUI loop
            }

            if (GUILayout.Button("Add Prefab"))
            {
                prefabs.Add(new PrefabSettings
                {
                    initialRotation = Vector3.zero,
                    randomRotation = new Vector3(8f, 360f, 8f),
                    minPaintAngle = -180f,
                    maxPaintAngle = 180f,
                    rotateToNormal = false,
                    minTextureScale = 0.75f,
                    maxTextureScale = 1f,
                    density = 3,
                    brushCenterScale = 1.25f,
                    brushEdgeScale = 0.75f,
                    brushScaleExponent = 1f,
                    useNatureInstance = false
                });
            }

            EditorGUILayout.LabelField("Tool Modes", EditorStyles.boldLabel);
            
            // Mode selection - only one can be active at a time
            bool newErasing = EditorGUILayout.Toggle("Enable Eraser Mode", isErasing);
            bool newScaling = EditorGUILayout.Toggle("Enable Scaling Mode", isScaling);
            
            // Ensure only one mode is active
            if (newErasing && !isErasing)
            {
                isErasing = true;
                isScaling = false;
            }
            else if (newScaling && !isScaling)
            {
                isScaling = true;
                isErasing = false;
            }
            else if (!newErasing && isErasing)
            {
                isErasing = false;
            }
            else if (!newScaling && isScaling)
            {
                isScaling = false;
            }
            
            if (isErasing)
            {
                eraserProbability = EditorGUILayout.Slider("Eraser Probability", eraserProbability, 0f, 1f);
            }
            
            if (isScaling)
            {
                scaleMultiplier = EditorGUILayout.Slider("Scale Multiplier", scaleMultiplier, 0.5f, 2f);
                EditorGUILayout.HelpBox("Ctrl+Shift+Drag to scale existing objects. Values > 1.0 increase size, < 1.0 decrease size.", MessageType.Info);
            }

            if (GUILayout.Button("Clear All Painted Objects"))
                ClearPaintedObjects();

            if (GUILayout.Button("Convert GameObjects to Grass Instances"))
                ConvertGameObjectsToGrassInstances();
                
            EditorGUILayout.Space(5);
            if (GUILayout.Button("Rebuild All Grass Chunks"))
            {
                RebuildAllGrassInstancerChunks();
                EditorGUIUtility.ExitGUI();
            }
                
            // Show current mode status
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Current Mode", EditorStyles.boldLabel);
            string currentMode = "Painting";
            if (isErasing) currentMode = "Erasing";
            else if (isScaling) currentMode = "Scaling";
            EditorGUILayout.LabelField($"Active Mode: {currentMode}", EditorStyles.miniLabel);
            
            // Show information about current grass data usage
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Grass Data Status", EditorStyles.boldLabel);
            ShowGrassDataStatus();
        }
        finally
        {
            // Ensure the scroll view is always properly ended
            GUILayout.EndScrollView();
        }
    }

    private void OnEnable() 
    {
        SceneView.duringSceneGui += OnSceneGUI;
        
        // Set default texture for scaling if not already set
        if (textureForScaling == null)
        {
            textureForScaling = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Def_Noise.tga");
        }
    }
    private void OnDisable() 
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        // Clear cache when window is disabled
        cachedGrassInstancers = null;
    }

    private void OnDestroy()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        HandleBrushSizeChange();
        
        if (isErasing) HandleErasing();
        else if (isScaling) HandleScaling();
        else HandlePainting();

        // Display brush size circle
        DrawBrushPreview();
    }
    
    /// <summary>
    /// Draws the brush preview circle in the scene view
    /// </summary>
    private void DrawBrushPreview()
    {
        if (surfaceToPaintOn != null && Event.current.type == EventType.Repaint)
        {
            int originalLayer = SetTemporaryLayer();
            
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            int layerMask = 1 << LayerMask.NameToLayer("Ignore Raycast");
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, layerMask) && hit.collider.gameObject == surfaceToPaintOn)
            {
                if (isErasing)
                    Handles.color = Color.red;
                else if (isScaling)
                    Handles.color = Color.yellow;
                else
                    Handles.color = Color.green;
                    
                Handles.DrawWireDisc(hit.point, hit.normal, brushSize);
            }
            
            RestoreOriginalLayer(originalLayer);
        }
    }
    
    /// <summary>
    /// Sets the surface to a temporary layer for raycasting
    /// </summary>
    /// <returns>The original layer</returns>
    private int SetTemporaryLayer()
    {
        if (surfaceToPaintOn == null) return -1;
        
        int originalLayer = surfaceToPaintOn.layer;
        surfaceToPaintOn.layer = LayerMask.NameToLayer("Ignore Raycast");
        return originalLayer;
    }
    
    /// <summary>
    /// Restores the original layer of the surface
    /// </summary>
    /// <param name="originalLayer">The original layer to restore</param>
    private void RestoreOriginalLayer(int originalLayer)
    {
        if (surfaceToPaintOn != null && originalLayer != -1)
        {
            surfaceToPaintOn.layer = originalLayer;
        }
    }

    private void HandleBrushSizeChange()
    {
        Event e = Event.current;
        if (e.type == EventType.ScrollWheel)
        {
            brushSize = Mathf.Clamp(brushSize - e.delta.y * 0.1f, 0.1f, 50f);
            e.Use();
        }
    }

    private void HandlePainting()
    {
        Event e = Event.current;
        if (!CanPaint()) return;
        
        int originalLayer = SetTemporaryLayer();
        int layerMask = 1 << LayerMask.NameToLayer("Ignore Raycast");

        if (e.type == EventType.MouseDown && e.button == 0 && e.control && e.shift)
        {
            isPainting = true;
            lastPaintPosition = e.mousePosition;
            lastPaintTime = Time.realtimeSinceStartup;
            e.Use();
        }
        if (e.type == EventType.MouseUp && e.button == 0 && isPainting)
        {
            isPainting = false;
            RestoreOriginalLayer(originalLayer);
            
            // Mark all used grass data as dirty and rebuild chunks
            MarkAllGrassDataDirty();
            RebuildAllGrassInstancerChunks();
            e.Use();
            return;
        }
        
        if (isPainting && e.type == EventType.MouseDrag && 
            Vector2.Distance(lastPaintPosition, e.mousePosition) > MIN_MOUSE_MOVEMENT && 
            Time.realtimeSinceStartup - lastPaintTime >= timeBetweenPaints)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, layerMask) || hit.collider.gameObject != surfaceToPaintOn) 
            {
                RestoreOriginalLayer(originalLayer);
                return;
            }

            // Get selected prefab from list
            PrefabSettings prefabSettings = prefabs[Random.Range(0, prefabs.Count)];
            // Check surface angle
            if (prefabSettings.minPaintAngle <= Vector3.SignedAngle(hit.normal, Vector3.up, Vector3.right) &&
                Vector3.SignedAngle(hit.normal, Vector3.up, Vector3.right) <= prefabSettings.maxPaintAngle)
            {
                for (int i = 0; i < prefabSettings.density; i++)
                {
                    Vector3 randomOffset = Random.insideUnitCircle * brushSize;
                    Vector3 randomPosition = hit.point + new Vector3(randomOffset.x, 0, randomOffset.y);

                    if (!Physics.Raycast(randomPosition + Vector3.up * 1f, Vector3.down, out RaycastHit depthHit, MAX_RAYCAST_DISTANCE, layerMask) ||
                        depthHit.collider.gameObject != surfaceToPaintOn) continue;

                    float distanceFromCenter = randomOffset.magnitude / brushSize;
                    float scaleFactor = Mathf.Pow(distanceFromCenter, prefabSettings.brushScaleExponent);
                    float brushScale = Mathf.Lerp(prefabSettings.brushCenterScale, prefabSettings.brushEdgeScale, scaleFactor) + Random.Range(-randomScaleOffset, randomScaleOffset);
                    float textureScale = textureForScaling ? CalculateScaleFromScreenSpace(depthHit.point, prefabSettings) : 1f;
                    float objectScale = brushScale * textureScale;

                    // Check if this prefab has its own grass instance data
                    GrassInstanceData targetGrassData = prefabSettings.grassInstanceData;
                    
                    if (targetGrassData != null && IsGrassDataValid(targetGrassData))
                    {
                        targetGrassData.positions.Add(depthHit.point);
                        // compute base rotation and apply initial rotation offset
                        Quaternion baseRot = prefabSettings.rotateToNormal
                            ? Quaternion.FromToRotation(Vector3.up, depthHit.normal)
                            : Quaternion.Euler(0, Random.Range(-prefabSettings.randomRotation.y, prefabSettings.randomRotation.y), 0);
                        Quaternion instanceRot = baseRot * Quaternion.Euler(prefabSettings.initialRotation);
                        targetGrassData.rotations.Add(instanceRot);
                        targetGrassData.scales.Add(objectScale);
                        continue; // Don't instantiate GameObject!
                    }

                    GameObject newObject;
                    Transform parentTransform = parentObject != null ? parentObject.transform : surfaceToPaintOn.transform;
                    
                    // Check if we should use NatureInstance
                    if (prefabSettings.useNatureInstance)
                    {
                        // Create NatureInstance
                        newObject = new GameObject(prefabSettings.prefab.name);
                        newObject.transform.SetParent(parentTransform);
                        var instance = newObject.AddComponent<NatureInstance>();
                        instance.Prefab = prefabSettings.prefab;
                        newObject.transform.position = depthHit.point;
                        Undo.RegisterCreatedObjectUndo(newObject, "Create Nature Instance");
                    }
                    else
                    {
                        // Regular instantiate
                        newObject = (GameObject)PrefabUtility.InstantiatePrefab(prefabSettings.prefab, parentTransform);
                        newObject.transform.position = depthHit.point;
                        Debug.Log($"Instantiated object at: {newObject.transform.position}");
                        Undo.RegisterCreatedObjectUndo(newObject, "Instantiate Object");

                        // Find LOD0 mesh and add mesh collider if missing
                        Transform lod0Transform = newObject.transform.Find(prefabSettings.prefab.name + "_LOD0");
                        if (lod0Transform != null)
                        {
                            GameObject lod0Object = lod0Transform.gameObject;
                            if (!lod0Object.TryGetComponent<MeshCollider>(out _))
                            {
                                MeshFilter meshFilter = lod0Object.GetComponent<MeshFilter>();
                                if (meshFilter != null && meshFilter.sharedMesh != null)
                                {
                                    MeshCollider meshCollider = lod0Object.AddComponent<MeshCollider>();
                                    meshCollider.sharedMesh = meshFilter.sharedMesh;
                                    Undo.RegisterCreatedObjectUndo(meshCollider, "Add Mesh Collider");
                                }
                            }
                        }
                    }

                    Quaternion rotation = prefabSettings.rotateToNormal
                        ? Quaternion.FromToRotation(Vector3.up, depthHit.normal)
                        : Quaternion.Euler(0, Random.Range(-prefabSettings.randomRotation.y, prefabSettings.randomRotation.y), 0);
                    rotation *= Quaternion.Euler(prefabSettings.initialRotation);
                    rotation *= Quaternion.Euler(
                        Random.Range(-prefabSettings.randomRotation.x, prefabSettings.randomRotation.x),
                        Random.Range(-prefabSettings.randomRotation.y, prefabSettings.randomRotation.y),
                        Random.Range(-prefabSettings.randomRotation.z, prefabSettings.randomRotation.z)
                    );
                    newObject.transform.rotation = rotation;
                    newObject.transform.localScale = Vector3.one * objectScale;
                    
                    // Refresh NatureInstance after setting transform
                    if (prefabSettings.useNatureInstance)
                    {
                        var instance = newObject.GetComponent<NatureInstance>();
                        if (instance != null)
                        {
                            instance.Refresh();
                        }
                    }
                }
                
                // Rebuild chunks immediately for real-time visual feedback
                RebuildAllGrassInstancerChunks();
                
                lastPaintPosition = e.mousePosition;
                lastPaintTime = Time.realtimeSinceStartup;
                e.Use();
            }
        }
        
        RestoreOriginalLayer(originalLayer);
    }

    private void HandleErasing()
    {
        Event e = Event.current;
        if (surfaceToPaintOn == null) return;

        int originalLayer = SetTemporaryLayer();
        int layerMask = 1 << LayerMask.NameToLayer("Ignore Raycast");

        if (e.type == EventType.MouseDown && e.button == 0 && e.control && e.shift)
        {
            isPainting = true;
            lastPaintPosition = e.mousePosition;
            lastPaintTime = Time.realtimeSinceStartup;
            e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0 && isPainting)
        {
            isPainting = false;
            RestoreOriginalLayer(originalLayer);
            
            // Mark all used grass data as dirty and rebuild chunks
            MarkAllGrassDataDirty();
            RebuildAllGrassInstancerChunks();
            e.Use();
            return;
        }

        if (isPainting && e.type == EventType.MouseDrag &&
            Vector2.Distance(lastPaintPosition, e.mousePosition) > MIN_MOUSE_MOVEMENT &&
            Time.realtimeSinceStartup - lastPaintTime >= timeBetweenPaints)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, layerMask) || hit.collider.gameObject != surfaceToPaintOn)
            {
                RestoreOriginalLayer(originalLayer);
                return;
            }

            // Erase from all grass instance data
            bool anyGrassDataRemoved = EraseFromAllGrassInstances(hit.point);
            if (anyGrassDataRemoved)
            {
                // Rebuild chunks immediately for real-time visual feedback
                RebuildAllGrassInstancerChunks();
                lastPaintPosition = e.mousePosition;
                lastPaintTime = Time.realtimeSinceStartup;
                e.Use();
                RestoreOriginalLayer(originalLayer);
                return;
            }

            EraseGameObjects(hit.point);

            lastPaintPosition = e.mousePosition;
            lastPaintTime = Time.realtimeSinceStartup;
            e.Use();
        }

        RestoreOriginalLayer(originalLayer);
    }

    private void HandleScaling()
    {
        Event e = Event.current;
        if (surfaceToPaintOn == null) return;

        int originalLayer = SetTemporaryLayer();
        int layerMask = 1 << LayerMask.NameToLayer("Ignore Raycast");

        if (e.type == EventType.MouseDown && e.button == 0 && e.control && e.shift)
        {
            isPainting = true;
            lastPaintPosition = e.mousePosition;
            lastPaintTime = Time.realtimeSinceStartup;
            e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0 && isPainting)
        {
            isPainting = false;
            RestoreOriginalLayer(originalLayer);
            
            // Mark all used grass data as dirty and rebuild chunks
            MarkAllGrassDataDirty();
            RebuildAllGrassInstancerChunks();
            e.Use();
            return;
        }

        if (isPainting && e.type == EventType.MouseDrag &&
            Vector2.Distance(lastPaintPosition, e.mousePosition) > MIN_MOUSE_MOVEMENT &&
            Time.realtimeSinceStartup - lastPaintTime >= timeBetweenPaints)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, layerMask) || hit.collider.gameObject != surfaceToPaintOn)
            {
                RestoreOriginalLayer(originalLayer);
                return;
            }

            // Scale grass instances within brush range
            bool anyGrassDataScaled = ScaleAllGrassInstances(hit.point);
            if (anyGrassDataScaled)
            {
                // Rebuild chunks immediately for real-time visual feedback
                RebuildAllGrassInstancerChunks();
            }

            // Scale GameObjects within brush range
            ScaleGameObjects(hit.point);

            lastPaintPosition = e.mousePosition;
            lastPaintTime = Time.realtimeSinceStartup;
            e.Use();
        }

        RestoreOriginalLayer(originalLayer);
    }

    /// <summary>
    /// Scales grass instances within brush range from all grass instance data
    /// </summary>
    /// <param name="hitPoint">The center point for scaling</param>
    /// <returns>True if any instances were scaled</returns>
    private bool ScaleAllGrassInstances(Vector3 hitPoint)
    {
        bool scaled = false;
        
        // Scale from all prefab-specific grass data
        foreach (var prefab in prefabs)
        {
            if (prefab.grassInstanceData != null && IsGrassDataValid(prefab.grassInstanceData))
            {
                scaled |= ScaleGrassInstances(prefab.grassInstanceData, hitPoint);
            }
        }
        
        return scaled;
    }
    
    /// <summary>
    /// Scales grass instances within brush range from specific grass data
    /// </summary>
    /// <param name="data">The grass data to scale</param>
    /// <param name="hitPoint">The center point for scaling</param>
    /// <returns>True if any instances were scaled</returns>
    private bool ScaleGrassInstances(GrassInstanceData data, Vector3 hitPoint)
    {
        bool scaled = false;
        
        // Scale all elements that fall under the brush
        for (int i = 0; i < data.positions.Count; i++)
        {
            if (i < data.scales.Count &&
                Vector3.Distance(data.positions[i], hitPoint) <= brushSize)
            {
                data.scales[i] *= scaleMultiplier;
                // Clamp scale to reasonable values
                data.scales[i] = Mathf.Clamp(data.scales[i], 0.1f, 10f);
                scaled = true;
            }
        }
        
        return scaled;
    }
    
    /// <summary>
    /// Scales GameObjects within brush range
    /// </summary>
    /// <param name="hitPoint">The center point for scaling</param>
    private void ScaleGameObjects(Vector3 hitPoint)
    {
        Transform parentTransform = parentObject != null ? parentObject.transform : (surfaceToPaintOn != null ? surfaceToPaintOn.transform : null);
        if (parentTransform == null) return;

        // Get all child transforms safely
        List<GameObject> objectsToScale = new List<GameObject>();

        // First pass: identify objects to scale
        for (int i = 0; i < parentTransform.childCount; i++)
        {
            Transform child = parentTransform.GetChild(i);
            if (child != null &&
                child != parentTransform &&
                Vector3.Distance(child.position, hitPoint) <= brushSize)
            {
                objectsToScale.Add(child.gameObject);
            }
        }

        // Second pass: perform scaling
        foreach (GameObject obj in objectsToScale)
        {
            if (obj != null) // Double-check it still exists
            {
                Undo.RecordObject(obj.transform, "Scale Object");
                Vector3 newScale = obj.transform.localScale * scaleMultiplier;
                // Clamp scale to reasonable values
                newScale = Vector3.ClampMagnitude(newScale, 10f);
                if (newScale.magnitude < 0.1f)
                    newScale = Vector3.one * 0.1f;
                obj.transform.localScale = newScale;
            }
        }
    }

    /// <summary>
    /// Clears all painted objects from the scene
    /// </summary>
    private void ClearPaintedObjects()
    {
        // Clear all grass instance data
        ClearAllGrassInstanceData();

        Transform parentTransform = parentObject != null ? parentObject.transform : (surfaceToPaintOn != null ? surfaceToPaintOn.transform : null);
        if (parentTransform == null) return;

        // Process children in reverse order to avoid modifying the hierarchy while iterating
        for (int i = parentTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = parentTransform.GetChild(i);
            if (child != null && child != parentTransform)
            {
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }
    }

    private float CalculateScaleFromScreenSpace(Vector3 worldPosition, PrefabSettings prefabSettings)
    {
        if (textureForScaling == null || Camera.current == null) return prefabSettings.minTextureScale;
        Vector3 screenPosition = Camera.current.WorldToScreenPoint(worldPosition);
        Vector2 uv = new Vector2(
            Mathf.Clamp01(screenPosition.x / Camera.current.pixelWidth),
            Mathf.Clamp01(screenPosition.y / Camera.current.pixelHeight)
        );
        Color pixelColor = textureForScaling.GetPixelBilinear(uv.x, uv.y);
        return Mathf.Lerp(prefabSettings.minTextureScale, prefabSettings.maxTextureScale, pixelColor.grayscale);
    }

    /// <summary>
    /// Converts GameObjects to grass instances
    /// </summary>
    private void ConvertGameObjectsToGrassInstances()
    {
        if (surfaceToPaintOn == null)
        {
            Debug.LogWarning("Surface is not assigned.");
            return;
        }

        // Check if we have any grass instance data configured
        bool hasAnyGrassData = false;
        foreach (var prefab in prefabs)
        {
            if (prefab.grassInstanceData != null)
            {
                hasAnyGrassData = true;
                break;
            }
        }

        if (!hasAnyGrassData)
        {
            Debug.LogWarning("No GrassInstanceData assigned. Configure grass data for prefabs.");
            return;
        }

        int converted = 0;
        int skipped = 0;
        
        Transform parentTransform = parentObject != null ? parentObject.transform : (surfaceToPaintOn != null ? surfaceToPaintOn.transform : null);
        if (parentTransform == null) return;

        for (int i = parentTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = parentTransform.GetChild(i);
            if (child == null || child == parentTransform) continue;

            // Try to find matching prefab settings for this GameObject
            PrefabSettings? matchingPrefab = FindMatchingPrefabForGameObject(child.gameObject);
            
            if (matchingPrefab.HasValue)
            {
                GrassInstanceData targetGrassData = matchingPrefab.Value.grassInstanceData;
                
                if (targetGrassData != null && IsGrassDataValid(targetGrassData))
                {
                    targetGrassData.positions.Add(child.position);
                    targetGrassData.rotations.Add(child.rotation);
                    targetGrassData.scales.Add(child.localScale.x); // assume uniform scale

                    Undo.DestroyObjectImmediate(child.gameObject);
                    converted++;
                }
                else
                {
                    skipped++;
                }
            }
            else
            {
                // No matching prefab found, skip this object
                skipped++;
            }
        }
        
        MarkAllGrassDataDirty();
        AssetDatabase.SaveAssets();
        RebuildAllGrassInstancerChunks();
        
        string message = $"Converted {converted} GameObjects to GrassInstanceData.";
        if (skipped > 0)
        {
            message += $" Skipped {skipped} objects (no matching grass data found).";
        }
        Debug.Log(message);
    }
    

    
    /// <summary>
    /// Checks if specific grass data is valid and properly initialized
    /// </summary>
    /// <param name="data">The grass data to validate</param>
    /// <returns>True if grass data is valid</returns>
    private bool IsGrassDataValid(GrassInstanceData data)
    {
        return data != null && 
               data.positions != null && 
               data.rotations != null && 
               data.scales != null &&
               data.positions.Count == data.rotations.Count &&
               data.positions.Count == data.scales.Count;
    }
    
    /// <summary>
    /// Checks if painting is possible
    /// </summary>
    /// <returns>True if painting can be performed</returns>
    private bool CanPaint()
    {
        if (surfaceToPaintOn == null)
        {
            //Debug.LogWarning("No surface assigned for painting.");
            return false;
        }
        
        if (prefabs.Count == 0)
        {
            Debug.LogWarning("No prefabs configured for painting.");
            return false;
        }
        
        if (!surfaceToPaintOn.TryGetComponent(out Collider collider))
        {
            Debug.LogWarning("Surface must have a collider for painting.");
            return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Erases grass instances within brush range from all grass instance data
    /// </summary>
    /// <param name="hitPoint">The center point for erasing</param>
    /// <returns>True if any instances were removed</returns>
    private bool EraseFromAllGrassInstances(Vector3 hitPoint)
    {
        bool removed = false;
        
        // Erase from all prefab-specific grass data
        foreach (var prefab in prefabs)
        {
            if (prefab.grassInstanceData != null && IsGrassDataValid(prefab.grassInstanceData))
            {
                removed |= EraseGrassInstances(prefab.grassInstanceData, hitPoint);
            }
        }
        
        return removed;
    }
    
    /// <summary>
    /// Erases grass instances within brush range from specific grass data
    /// </summary>
    /// <param name="data">The grass data to erase from</param>
    /// <param name="hitPoint">The center point for erasing</param>
    /// <returns>True if any instances were removed</returns>
    private bool EraseGrassInstances(GrassInstanceData data, Vector3 hitPoint)
    {
        bool removed = false;
        
        // Remove all elements that fall under the brush
        for (int i = data.positions.Count - 1; i >= 0; i--)
        {
            if (i < data.rotations.Count && i < data.scales.Count &&
                Vector3.Distance(data.positions[i], hitPoint) <= brushSize &&
                Random.value <= eraserProbability)
            {
                data.positions.RemoveAt(i);
                data.rotations.RemoveAt(i);
                data.scales.RemoveAt(i);
                removed = true;
            }
        }
        
        return removed;
    }
    
    /// <summary>
    /// Erases GameObjects within brush range
    /// </summary>
    /// <param name="hitPoint">The center point for erasing</param>
    private void EraseGameObjects(Vector3 hitPoint)
    {
        Transform parentTransform = parentObject != null ? parentObject.transform : (surfaceToPaintOn != null ? surfaceToPaintOn.transform : null);
        if (parentTransform == null) return;

        // Get all child transforms safely
        List<GameObject> objectsToErase = new List<GameObject>();

        // First pass: identify objects to erase
        for (int i = 0; i < parentTransform.childCount; i++)
        {
            Transform child = parentTransform.GetChild(i);
            if (child != null &&
                child != parentTransform &&
                Vector3.Distance(child.position, hitPoint) <= brushSize)
            {
                if (Random.value <= eraserProbability)
                {
                    objectsToErase.Add(child.gameObject);
                }
            }
        }

        // Second pass: perform erasure
        foreach (GameObject obj in objectsToErase)
        {
            if (obj != null) // Double-check it still exists
            {
                Undo.DestroyObjectImmediate(obj);
            }
        }
    }
    
    /// <summary>
    /// Marks all grass instance data as dirty for saving
    /// </summary>
    private void MarkAllGrassDataDirty()
    {
        // Mark all prefab-specific grass data as dirty
        foreach (var prefab in prefabs)
        {
            if (prefab.grassInstanceData != null)
            {
                EditorUtility.SetDirty(prefab.grassInstanceData);
            }
        }
    }
    
    /// <summary>
    /// Clears all grass instance data
    /// </summary>
    private void ClearAllGrassInstanceData()
    {
        // Clear all prefab-specific grass data
        foreach (var prefab in prefabs)
        {
            if (prefab.grassInstanceData != null && IsGrassDataValid(prefab.grassInstanceData))
            {
                prefab.grassInstanceData.positions.Clear();
                prefab.grassInstanceData.rotations.Clear();
                prefab.grassInstanceData.scales.Clear();
                EditorUtility.SetDirty(prefab.grassInstanceData);
            }
        }
        
        AssetDatabase.SaveAssets();
        RebuildAllGrassInstancerChunks();
    }
    
    /// <summary>
    /// Finds matching prefab settings for a GameObject based on prefab reference
    /// </summary>
    /// <param name="gameObject">The GameObject to find a match for</param>
    /// <returns>Matching prefab settings if found</returns>
    private PrefabSettings? FindMatchingPrefabForGameObject(GameObject gameObject)
    {
        // Get the prefab asset from the GameObject
        GameObject prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
        if (prefabAsset == null) return null;
        
        // Find matching prefab in our settings
        foreach (var prefab in prefabs)
        {
            if (prefab.prefab == prefabAsset)
            {
                return prefab;
            }
        }
        
        return null;
    }
    
    // Cache for grass instancers to avoid repeated FindObjectsOfType calls
    private GrassInstancer[] cachedGrassInstancers;
    private float lastInstancerCacheTime = 0f;
    private const float INSTANCER_CACHE_DURATION = 1f; // Cache for 1 second
    
    /// <summary>
    /// Rebuilds chunks for all GrassInstancer components that use the modified grass data
    /// </summary>
    private void RebuildAllGrassInstancerChunks()
    {
        // Cache grass instancers to improve performance during real-time painting
        if (cachedGrassInstancers == null || Time.realtimeSinceStartup - lastInstancerCacheTime > INSTANCER_CACHE_DURATION)
        {
            cachedGrassInstancers = FindObjectsOfType<GrassInstancer>();
            lastInstancerCacheTime = Time.realtimeSinceStartup;
        }
        
        HashSet<GrassInstanceData> modifiedGrassData = new HashSet<GrassInstanceData>();
        
        // Collect all grass data that might have been modified
        foreach (var prefab in prefabs)
        {
            if (prefab.grassInstanceData != null)
            {
                modifiedGrassData.Add(prefab.grassInstanceData);
            }
        }
        
        // Rebuild chunks for instancers that use any of the modified grass data
        int rebuiltCount = 0;
        foreach (var instancer in cachedGrassInstancers)
        {
            if (instancer != null && instancer.data != null && modifiedGrassData.Contains(instancer.data))
            {
#if UNITY_EDITOR
                instancer.RebuildChunksExternal();
                rebuiltCount++;
#endif
            }
        }
        
        // Only log during non-painting operations to avoid console spam
        if (!isPainting && rebuiltCount > 0)
        {
            Debug.Log($"Rebuilt chunks for {rebuiltCount} GrassInstancer components.");
        }
    }
    
    /// <summary>
    /// Shows status information about grass data usage
    /// </summary>
    private void ShowGrassDataStatus()
    {
        int totalInstances = 0;
        int grassDataCount = 0;
        
        // Count prefab-specific grass data
        foreach (var prefab in prefabs)
        {
            if (prefab.grassInstanceData != null && IsGrassDataValid(prefab.grassInstanceData))
            {
                totalInstances += prefab.grassInstanceData.positions.Count;
                grassDataCount++;
                string prefabName = prefab.prefab != null ? prefab.prefab.name : "Unknown";
                EditorGUILayout.LabelField($"{prefabName}: {prefab.grassInstanceData.positions.Count} instances", EditorStyles.miniLabel);
            }
        }
        
        if (grassDataCount == 0)
        {
            EditorGUILayout.HelpBox("No grass instance data configured. Painted objects will be created as GameObjects.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField($"Total: {totalInstances} grass instances across {grassDataCount} data assets", EditorStyles.boldLabel);
        }
    }
}