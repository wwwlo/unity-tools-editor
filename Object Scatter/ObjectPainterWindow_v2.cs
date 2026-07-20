using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using VisualDesignCafe.Rendering.Nature;

public class ObjectPainterWindow_v2 : EditorWindow
{
    // Constants
    private const float MAX_RAYCAST_DISTANCE = 20f;
    private const float MIN_MOUSE_MOVEMENT = 1f;
    
    // Settings for painting prefab objects
    [System.Serializable]
    public struct PrefabSettings
    {
        public GameObject prefab; // Prefab to paint
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
    private GameObject parentObject;     // Parent object for painted objects
    private Texture2D textureForScaling; // Texture for scaling
    private List<PrefabSettings> prefabs = new List<PrefabSettings>(); // List of prefabs and their settings
    private float brushSize = 1f;        // Brush size
    private float timeBetweenPaints = 0.1f;     // Time between paints
    private float randomScaleOffset = 0.05f;    // Random scale offset
    private bool isPainting = false;
    private bool isErasing = false;      // Eraser mode
    private bool isScaling = false;      // Scaling mode
    private bool isPrimitivizing = false; // Primitivization mode (new)
    private float eraserProbability = 1f;   // Eraser probability
    private float scaleMultiplier = 1.1f;   // Scale multiplier for scaling mode
    private Vector3 lastPaintPosition = Vector3.zero;
    private float lastPaintTime = 0f;
    private Vector2 scrollPosition = Vector2.zero;


    [MenuItem("ObjectDistributor/Object Painter v2")]
    public static void ShowWindow() => GetWindow<ObjectPainterWindow_v2>("Object Painter v2");

    private void OnGUI()
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        try
        {
            GUILayout.Label("Object Painter Settings", EditorStyles.boldLabel);
            
            // Surface to Paint On - horizontal layout
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Surface to Paint On:", GUILayout.Width(150f));
            GameObject selectedObject = (GameObject)EditorGUILayout.ObjectField(
                GUIContent.none,
                surfaceToPaintOn,
                typeof(GameObject),
                true
            );
            if (GUILayout.Button("Select Active", GUILayout.Width(100f)))
            {
                if (Selection.activeGameObject != null)
                {
                    selectedObject = Selection.activeGameObject;
                }
                else
                {
                    Debug.LogWarning("No active GameObject selected in the hierarchy.");
                }
            }
            EditorGUILayout.EndHorizontal();
            
            // Parent Object - horizontal layout
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Parent Object:", GUILayout.Width(150f));
            parentObject = (GameObject)EditorGUILayout.ObjectField(
                GUIContent.none,
                parentObject,
                typeof(GameObject),
                true
            );
            if (GUILayout.Button("Select Active", GUILayout.Width(100f)))
            {
                if (Selection.activeGameObject != null)
                {
                    parentObject = Selection.activeGameObject;
                }
                else
                {
                    Debug.LogWarning("No active GameObject selected in the hierarchy.");
                }
            }
            EditorGUILayout.EndHorizontal();

            
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
            
            // Texture for Scaling - horizontal layout
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Texture for Scaling:", GUILayout.Width(100f));
            textureForScaling = (Texture2D)EditorGUILayout.ObjectField(
                GUIContent.none,
                textureForScaling, 
                typeof(Texture2D), 
                false
            );
            EditorGUILayout.EndHorizontal();
            
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
                
                // Prefab field - horizontal layout
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Prefab:", GUILayout.Width(200f));
                settings.prefab = (GameObject)EditorGUILayout.ObjectField(
                    GUIContent.none,
                    settings.prefab, 
                    typeof(GameObject), 
                    false
                );
                EditorGUILayout.EndHorizontal();
                
                settings.useNatureInstance = EditorGUILayout.Toggle("Use Nature Instance", settings.useNatureInstance);
                
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
                    useNatureInstance = true
                });
            }

            EditorGUILayout.LabelField("Tool Modes", EditorStyles.boldLabel);
            
            // Mode selection - only one can be active at a time
            bool newErasing = EditorGUILayout.Toggle("Enable Eraser Mode", isErasing);
            bool newScaling = EditorGUILayout.Toggle("Enable Scaling Mode", isScaling);
            bool newPrimitivizing = EditorGUILayout.Toggle("Attraction", isPrimitivizing);

            // Ensure only one mode is active
            if (newErasing && !isErasing)
            {
                isErasing = true;
                isScaling = false;
                isPrimitivizing = false;
            }
            else if (newScaling && !isScaling)
            {
                isScaling = true;
                isErasing = false;
                isPrimitivizing = false;
            }
            else if (newPrimitivizing && !isPrimitivizing)
            {
                isPrimitivizing = true;
                isErasing = false;
                isScaling = false;
            }
            else if (!newErasing && isErasing)
            {
                isErasing = false;
            }
            else if (!newScaling && isScaling)
            {
                isScaling = false;
            }
            else if (!newPrimitivizing && isPrimitivizing)
            {
                isPrimitivizing = false;
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
                
            // Show current mode status
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Current Mode", EditorStyles.boldLabel);
            string currentMode = "Painting";
            if (isErasing) currentMode = "Erasing";
            else if (isScaling) currentMode = "Scaling";
            else if (isPrimitivizing) currentMode = "Primitivization";
            EditorGUILayout.LabelField($"Active Mode: {currentMode}", EditorStyles.miniLabel);
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
        else if (isPrimitivizing) HandlePrimitivization();
        else HandlePainting();

        // Display brush preview circle
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
                else if (isPrimitivizing)
                    Handles.color = new Color(0.1f, 0.1f, 0.8f); // custom yellow for primitivization
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
        if (e.button != 0 || !e.control || !e.shift) return;
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
            if (prefabs.Count == 0)
            {
                Debug.LogWarning("No prefabs configured for painting.");
                RestoreOriginalLayer(originalLayer);
                return;
            }
            
            PrefabSettings prefabSettings = prefabs[Random.Range(0, prefabs.Count)];
            
            // Check if prefab is valid
            if (prefabSettings.prefab == null)
            {
                Debug.LogWarning("Selected prefab settings has no prefab assigned.");
                RestoreOriginalLayer(originalLayer);
                return;
            }
            
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

                    // Safely get parent transform
                    Transform parentTransform = null;
                    if (parentObject != null)
                    {
                        parentTransform = parentObject.transform;
                    }
                    else if (surfaceToPaintOn != null)
                    {
                        parentTransform = surfaceToPaintOn.transform;
                    }
                    else
                    {
                        Debug.LogError("Both parentObject and surfaceToPaintOn are null. Cannot create object.");
                        RestoreOriginalLayer(originalLayer);
                        return;
                    }
                    
                    GameObject newObject = null;
                    
                    // Check if we should use NatureInstance
                    if (prefabSettings.useNatureInstance)
                    {
                        try
                        {
                            // Create NatureInstance
                            newObject = new GameObject(prefabSettings.prefab.name);
                            if (newObject == null)
                            {
                                Debug.LogError("Failed to create GameObject for NatureInstance.");
                                RestoreOriginalLayer(originalLayer);
                                return;
                            }
                            
                            newObject.transform.SetParent(parentTransform);
                            var instance = newObject.AddComponent<NatureInstance>();
                            if (instance == null)
                            {
                                Debug.LogError("Failed to add NatureInstance component.");
                                DestroyImmediate(newObject);
                                RestoreOriginalLayer(originalLayer);
                                return;
                            }
                            
                            instance.Prefab = prefabSettings.prefab;
                            newObject.transform.position = depthHit.point;
                            Undo.RegisterCreatedObjectUndo(newObject, "Create Nature Instance");
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"Exception while creating NatureInstance: {ex.Message}");
                            if (newObject != null)
                            {
                                DestroyImmediate(newObject);
                            }
                            RestoreOriginalLayer(originalLayer);
                            return;
                        }
                    }
                    else
                    {
                        try
                        {
                            // Regular instantiate
                            newObject = (GameObject)PrefabUtility.InstantiatePrefab(prefabSettings.prefab, parentTransform);
                            if (newObject == null)
                            {
                                Debug.LogError("Failed to instantiate prefab.");
                                RestoreOriginalLayer(originalLayer);
                                return;
                            }
                            
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
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"Exception while instantiating prefab: {ex.Message}");
                            if (newObject != null)
                            {
                                DestroyImmediate(newObject);
                            }
                            RestoreOriginalLayer(originalLayer);
                            return;
                        }
                    }

                    // Verify newObject was created successfully before setting transform
                    if (newObject == null)
                    {
                        Debug.LogError("newObject is null after creation attempt.");
                        RestoreOriginalLayer(originalLayer);
                        return;
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
        if (e.button != 0 || !e.control || !e.shift) return;
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

            // Skip grass instance data erasing (removed feature)

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
        if (e.button != 0 || !e.control || !e.shift) return;
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

            // Skip grass instance scaling (removed feature)

            // Scale GameObjects within brush range
            ScaleGameObjects(hit.point);

            lastPaintPosition = e.mousePosition;
            lastPaintTime = Time.realtimeSinceStartup;
            e.Use();
        }

        RestoreOriginalLayer(originalLayer);
    }

    /// <summary>
    /// Primitivization: move objects under brush to nearest collision point along Y axis.
    /// </summary>
    private void HandlePrimitivization()
    {
        Event e = Event.current;
        if (e.button != 0 || !e.control || !e.shift) return;
        if (surfaceToPaintOn == null) return;

        int originalLayer = SetTemporaryLayer();
        int layerMask = 1 << LayerMask.NameToLayer("Ignore Raycast");

        // Only process while mouse is down/dragging with Ctrl+Shift (same as painting/erasing/scale for consistency)
        if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) return;

        if (e.type == EventType.MouseDown && e.button == 0 && e.control && e.shift)
        {
            isPainting = true;
            lastPaintPosition = e.mousePosition;
            lastPaintTime = Time.realtimeSinceStartup;
            e.Use();
        }
        else if (isPrimitivizing && e.type == EventType.MouseUp && e.button == 0)
        {
            isPainting = false;
            RestoreOriginalLayer(originalLayer);
            e.Use();
            return;
        }

        // Continue processing for drag events only when painting flag is set (i.e. mouse down happened)
        if (!isPrimitivizing || !isPainting) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (e.type == EventType.MouseUp && e.button == 0)
        {
            // On release, do nothing special for primitivization
            isPrimitivizing = false;
            RestoreOriginalLayer(originalLayer);
            return;
        }

        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, layerMask))
        {
            RestoreOriginalLayer(originalLayer);
            return;
        }
        
        // Verify the hit is on our surface (or within brush area)
        if (hit.collider.gameObject != surfaceToPaintOn && 
            Vector3.Distance(hit.point, hit.collider.transform.position) > brushSize * 3f)
        {
            RestoreOriginalLayer(originalLayer);
            return;
        }

        // Get parent transform for children iteration
        Transform parentTransform = null;
        if (parentObject != null)
        {
            parentTransform = parentObject.transform;
        }
        else if (surfaceToPaintOn != null)
        {
            parentTransform = surfaceToPaintOn.transform;
        }
        else
        {
            Debug.LogWarning("No valid parent or surface for primitivization.");
            RestoreOriginalLayer(originalLayer);
            return;
        }

        // Collect children within brush radius around hit point
        List<GameObject> objectsToPrimitivize = new List<GameObject>();
        int childCount = parentTransform.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = parentTransform.GetChild(i);
            if (child != null && child != parentTransform)
            {
                float dist = Vector3.Distance(child.position, hit.point);
                if (dist <= brushSize * 3f) // slightly generous radius for usability
                    objectsToPrimitivize.Add(child.gameObject);
            }
        }

        if (objectsToPrimitivize.Count == 0) return;

        foreach (GameObject obj in objectsToPrimitivize)
        {
            if (obj == null) continue;
            
            Transform transform = obj.transform;
            Undo.RecordObject(transform, "Primitivize Object");

            // Find nearest collision point along Y: snap to surface normal at hit, then adjust only Y
            Vector3 targetPos = hit.point + hit.normal * 0.1f; // small offset to avoid zero-length move
            float newY = Mathf.Sign(Vector3.Dot(hit.normal, Vector3.up)) * 
                         Mathf.Max(0.5f, Mathf.Abs(targetPos.y));
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
        }

        lastPaintPosition = e.mousePosition;
        lastPaintTime = Time.realtimeSinceStartup;
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
}
