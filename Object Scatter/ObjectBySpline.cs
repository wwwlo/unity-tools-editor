using UnityEngine;
using UnityEditor;
// using SplineMesh;  // УДАЛИ или закомментируй
using System.Collections.Generic;
using static TreeEditor.TreeGroup;
using UnityEngine.Splines;
// using UnityEngine.Splines.Utility; // если потребуется для SplineUtility
using System;
using System.Linq;

#if UNITY_EDITOR

/// <summary>
/// Режим распределения объектов относительно сплайна
/// </summary>
public enum DistributionMode
{
    /// <summary>Объекты размещаются непосредственно на кривой сплайна</summary>
    OnCurve,
    /// <summary>Объекты размещаются внутри области, ограниченной сплайном</summary>
    WithinCurve
}

/// <summary>
/// Режим распределения объектов внутри области сплайна
/// </summary>
public enum WithinCurveMode
{
    /// <summary>Объекты распределяются вокруг пути сплайна с заданной шириной и высотой</summary>
    AroundPath,
    /// <summary>Объекты заполняют всю площадь, ограниченную замкнутым сплайном</summary>
    FilledArea
}

/// <summary>
/// Основной класс редактора для распределения объектов вдоль сплайна
/// Позволяет создавать и управлять экземплярами префабов вдоль сплайна с различными режимами распределения
/// </summary>
public class ObjectBySpline : EditorWindow
{
    private SplineContainer splineContainer; // Контейнер сплайна для размещения объектов
    private List<PrefabData> prefabs = new List<PrefabData>(); // Список префабов для распределения
    private List<GameObject[]> instanceGroups = new List<GameObject[]>(); // Список групп экземпляров
    private GameObject[] instances; // Текущие экземпляры (для обратной совместимости)
    private GameObject parent; // Родительский объект для всех сгенерированных экземпляров
    private bool previewMode = false; // Режим предпросмотра в реальном времени
    private const string PrefsKeyPrefix = "SplineInstancer_"; // Префикс для сохранения настроек
    private Vector2 m_ScrollPosition = Vector2.zero; // Позиция скролла в интерфейсе
    private string parentName = "SplineInstancer"; // Имя родительского объекта
    private Dictionary<GameObject, Stack<GameObject>> objectPool = new Dictionary<GameObject, Stack<GameObject>>(); // Пул объектов для оптимизации
    private Transform poolContainer; // Контейнер для пула объектов

    /// <summary>
    /// Класс для хранения данных о префабе и его настройках распределения
    /// </summary>
    [System.Serializable]
    public class PrefabData
    {
        // Основные настройки
        public GameObject prefab; // Префаб для создания экземпляров
        public DistributionMode distributionMode = DistributionMode.OnCurve; // Режим распределения
        public WithinCurveMode withinCurveMode = WithinCurveMode.AroundPath; // Режим распределения внутри области
        
        // Настройки области распределения
        public float areaWidth = 2f; // Ширина области вокруг сплайна
        public float areaHeight = 2f; // Высота области вокруг сплайна
        public float pathDensity = 1f; // Плотность размещения объектов вдоль сплайна
        
        // Настройки ориентации и интервалов
        public bool alignToSpline = true; // Выравнивать объекты по касательной сплайна
        public bool useSpacing = false; // Использовать фиксированные интервалы между объектами
        public float spacing = 1f; // Расстояние между объектами при использовании интервалов
        
        // Настройки масштабирования на основе текстуры
        public bool scaleRelativeToTexture = false; // Масштабировать объекты на основе текстуры
        public Texture2D scaleTexture; // Текстура для определения масштаба
        public float minTextureScale = 0.5f; // Минимальный масштаб на основе текстуры
        public float maxTextureScale = 2.0f; // Максимальный масштаб на основе текстуры
        public bool useAlphaChannel = false; // Использовать альфа-канал текстуры для масштабирования
        public Vector2 textureScale = Vector2.one; // Масштаб текстуры
        public Vector2 textureOffset = Vector2.zero; // Смещение текстуры
        public bool useTextureMask = false; // Использовать текстуру как маску для размещения
        public float textureMaskThreshold = 0.5f; // Порог маски текстуры
        
        // Настройки коллизий
        public bool snapToCollision = false; // Притягивать объекты к поверхностям с коллизией
        public float maxRaycastDistance = 100f; // Максимальная дистанция для рейкаста
        
        // Настройки масштабирования по расстоянию
        public bool useDistanceBasedScaling = false; // Масштабировать объекты в зависимости от расстояния до сплайна
        public float minDistance = 0f; // Минимальное расстояние для масштабирования
        public float maxDistance = 10f; // Максимальное расстояние для масштабирования
        public float minDistanceScale = 0.5f; // Минимальный масштаб на максимальном расстоянии
        public float maxDistanceScale = 2f; // Максимальный масштаб на минимальном расстоянии
        public float distanceScaleExponent = 1f; // Экспонента для кривой масштабирования
        
        // Настройки для режима FilledArea
        public int areaResolution = 50; // Разрешение для генерации полигона из сплайна
        public float densityFactor = 1f; // Фактор плотности для заполнения области
        public float poissonMinDistance = 1f; // Минимальное расстояние между объектами при распределении Пуассона
        
        // Настройки экземпляров
        public int count = 10; // Количество экземпляров для данного префаба
        public Vector3 scale = Vector3.one; // Масштаб экземпляров
        public Vector3 rotation = Vector3.zero; // Поворот экземпляров
        public float randomPositionRange = 0f; // Диапазон случайного смещения позиции
        public float randomRotationRange = 0f; // Диапазон случайного поворота

        /// <summary>
        /// Конструктор класса PrefabData
        /// </summary>
        /// <param name="prefab">Префаб для создания экземпляров</param>
        public PrefabData(GameObject prefab)
        {
            this.prefab = prefab;
        }
        
        /// <summary>
        /// Проверяет, задан ли валидный префаб
        /// </summary>
        /// <returns>True если префаб задан, иначе False</returns>
        public bool HasValidPrefab()
        {
            return prefab != null;
        }
        
        /// <summary>
        /// Возвращает общий вес для распределения (всегда 1 для простоты)
        /// </summary>
        /// <returns>Общий вес (1.0)</returns>
        public float GetTotalWeight()
        {
            return 1f;
        }
    }

    /// <summary>
    /// Сохраняет все настройки в EditorPrefs для восстановления между сессиями редактора
    /// </summary>
    private void SavePrefs()
    {
        EditorPrefs.SetString(PrefsKeyPrefix + "ParentName", parentName);
        EditorPrefs.SetInt(PrefsKeyPrefix + "PrefabCount", prefabs.Count);
        for (int i = 0; i < prefabs.Count; i++)
        {
            var pd = prefabs[i];
            // Сохранение GUID префаба для последующей загрузки
            EditorPrefs.SetString(PrefsKeyPrefix + "Prefab_" + i, pd.prefab != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pd.prefab)) : "");
            
            // Основные настройки экземпляров
            EditorPrefs.SetInt(PrefsKeyPrefix + "Count_" + i, pd.count);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "ScaleX_" + i, pd.scale.x);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "ScaleY_" + i, pd.scale.y);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "ScaleZ_" + i, pd.scale.z);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "RotationX_" + i, pd.rotation.x);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "RotationY_" + i, pd.rotation.y);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "RotationZ_" + i, pd.rotation.z);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "RandomPos_" + i, pd.randomPositionRange);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "RandomRot_" + i, pd.randomRotationRange);
            
            // Настройки распределения
            EditorPrefs.SetInt(PrefsKeyPrefix + "DistMode_" + i, (int)pd.distributionMode);
            EditorPrefs.SetInt(PrefsKeyPrefix + "WithinCurveMode_" + i, (int)pd.withinCurveMode);
            EditorPrefs.SetBool(PrefsKeyPrefix + "AlignToSpline_" + i, pd.alignToSpline);
            EditorPrefs.SetBool(PrefsKeyPrefix + "UseSpacing_" + i, pd.useSpacing);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "Spacing_" + i, pd.spacing);
            
            // Настройки области
            EditorPrefs.SetFloat(PrefsKeyPrefix + "AreaWidth_" + i, pd.areaWidth);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "AreaHeight_" + i, pd.areaHeight);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "PathDensity_" + i, pd.pathDensity);
            EditorPrefs.SetInt(PrefsKeyPrefix + "AreaResolution_" + i, pd.areaResolution);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "DensityFactor_" + i, pd.densityFactor);
            
            // Настройки текстуры и масштабирования
            EditorPrefs.SetBool(PrefsKeyPrefix + "ScaleRelativeToTexture_" + i, pd.scaleRelativeToTexture);
            EditorPrefs.SetString(PrefsKeyPrefix + "ScaleTexture_" + i, pd.scaleTexture != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pd.scaleTexture)) : "");
            EditorPrefs.SetFloat(PrefsKeyPrefix + "MinTextureScale_" + i, pd.minTextureScale);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "MaxTextureScale_" + i, pd.maxTextureScale);
            EditorPrefs.SetBool(PrefsKeyPrefix + "UseAlphaChannel_" + i, pd.useAlphaChannel);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "TextureScaleX_" + i, pd.textureScale.x);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "TextureScaleY_" + i, pd.textureScale.y);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "TextureOffsetX_" + i, pd.textureOffset.x);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "TextureOffsetY_" + i, pd.textureOffset.y);
            EditorPrefs.SetBool(PrefsKeyPrefix + "UseTextureMask_" + i, pd.useTextureMask);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "TextureMaskThreshold_" + i, pd.textureMaskThreshold);
            
            // Настройки коллизий
            EditorPrefs.SetBool(PrefsKeyPrefix + "SnapToCollision_" + i, pd.snapToCollision);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "MaxRaycastDistance_" + i, pd.maxRaycastDistance);
            
            // Настройки масштабирования по расстоянию
            EditorPrefs.SetBool(PrefsKeyPrefix + "UseDistanceBasedScaling_" + i, pd.useDistanceBasedScaling);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "MinDistance_" + i, pd.minDistance);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "MaxDistance_" + i, pd.maxDistance);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "MinDistanceScale_" + i, pd.minDistanceScale);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "MaxDistanceScale_" + i, pd.maxDistanceScale);
            EditorPrefs.SetFloat(PrefsKeyPrefix + "DistanceScaleExponent_" + i, pd.distanceScaleExponent);
            
            // Настройки распределения Пуассона
            EditorPrefs.SetFloat(PrefsKeyPrefix + "PoissonMinDistance_" + i, pd.poissonMinDistance);
        }
    }

    /// <summary>
    /// Загружает сохраненные настройки из EditorPrefs
    /// </summary>
    private void LoadPrefs()
    {
        parentName = EditorPrefs.GetString(PrefsKeyPrefix + "ParentName", "SplineInstancer");
        int prefabCount = EditorPrefs.GetInt(PrefsKeyPrefix + "PrefabCount", 0);
        prefabs.Clear();
        
        for (int i = 0; i < prefabCount; i++)
        {
            // Загрузка префаба по GUID
            string guid = EditorPrefs.GetString(PrefsKeyPrefix + "Prefab_" + i, "");
            GameObject prefab = string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            
            // Создание нового объекта PrefabData с загруженными настройками
            var pd = new PrefabData(prefab)
            {
                // Основные настройки экземпляров
                count = EditorPrefs.GetInt(PrefsKeyPrefix + "Count_" + i, 10),
                scale = new Vector3(
                    EditorPrefs.GetFloat(PrefsKeyPrefix + "ScaleX_" + i, 1f),
                    EditorPrefs.GetFloat(PrefsKeyPrefix + "ScaleY_" + i, 1f),
                    EditorPrefs.GetFloat(PrefsKeyPrefix + "ScaleZ_" + i, 1f)
                ),
                rotation = new Vector3(
                    EditorPrefs.GetFloat(PrefsKeyPrefix + "RotationX_" + i, 0f),
                    EditorPrefs.GetFloat(PrefsKeyPrefix + "RotationY_" + i, 0f),
                    EditorPrefs.GetFloat(PrefsKeyPrefix + "RotationZ_" + i, 0f)
                ),
                randomPositionRange = EditorPrefs.GetFloat(PrefsKeyPrefix + "RandomPos_" + i, 0f),
                randomRotationRange = EditorPrefs.GetFloat(PrefsKeyPrefix + "RandomRot_" + i, 0f),
                
                // Настройки распределения
                distributionMode = (DistributionMode)EditorPrefs.GetInt(PrefsKeyPrefix + "DistMode_" + i, 0),
                withinCurveMode = (WithinCurveMode)EditorPrefs.GetInt(PrefsKeyPrefix + "WithinCurveMode_" + i, 0),
                alignToSpline = EditorPrefs.GetBool(PrefsKeyPrefix + "AlignToSpline_" + i, true),
                useSpacing = EditorPrefs.GetBool(PrefsKeyPrefix + "UseSpacing_" + i, false),
                spacing = EditorPrefs.GetFloat(PrefsKeyPrefix + "Spacing_" + i, 1f),
                
                // Настройки области
                areaWidth = EditorPrefs.GetFloat(PrefsKeyPrefix + "AreaWidth_" + i, 2f),
                areaHeight = EditorPrefs.GetFloat(PrefsKeyPrefix + "AreaHeight_" + i, 2f),
                pathDensity = EditorPrefs.GetFloat(PrefsKeyPrefix + "PathDensity_" + i, 1f),
                areaResolution = EditorPrefs.GetInt(PrefsKeyPrefix + "AreaResolution_" + i, 50),
                densityFactor = EditorPrefs.GetFloat(PrefsKeyPrefix + "DensityFactor_" + i, 1f),
                
                // Настройки текстуры и масштабирования
                scaleRelativeToTexture = EditorPrefs.GetBool(PrefsKeyPrefix + "ScaleRelativeToTexture_" + i, false),
                scaleTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(PrefsKeyPrefix + "ScaleTexture_" + i, ""))),
                minTextureScale = EditorPrefs.GetFloat(PrefsKeyPrefix + "MinTextureScale_" + i, 0.5f),
                maxTextureScale = EditorPrefs.GetFloat(PrefsKeyPrefix + "MaxTextureScale_" + i, 2.0f),
                useAlphaChannel = EditorPrefs.GetBool(PrefsKeyPrefix + "UseAlphaChannel_" + i, false),
                textureScale = new Vector2(
                    EditorPrefs.GetFloat(PrefsKeyPrefix + "TextureScaleX_" + i, 1f),
                    EditorPrefs.GetFloat(PrefsKeyPrefix + "TextureScaleY_" + i, 1f)
                ),
                textureOffset = new Vector2(
                    EditorPrefs.GetFloat(PrefsKeyPrefix + "TextureOffsetX_" + i, 0f),
                    EditorPrefs.GetFloat(PrefsKeyPrefix + "TextureOffsetY_" + i, 0f)
                ),
                useTextureMask = EditorPrefs.GetBool(PrefsKeyPrefix + "UseTextureMask_" + i, false),
                textureMaskThreshold = EditorPrefs.GetFloat(PrefsKeyPrefix + "TextureMaskThreshold_" + i, 0.5f),
                
                // Настройки коллизий
                snapToCollision = EditorPrefs.GetBool(PrefsKeyPrefix + "SnapToCollision_" + i, false),
                maxRaycastDistance = EditorPrefs.GetFloat(PrefsKeyPrefix + "MaxRaycastDistance_" + i, 100f),
                
                // Настройки масштабирования по расстоянию
                useDistanceBasedScaling = EditorPrefs.GetBool(PrefsKeyPrefix + "UseDistanceBasedScaling_" + i, false),
                minDistance = EditorPrefs.GetFloat(PrefsKeyPrefix + "MinDistance_" + i, 0f),
                maxDistance = EditorPrefs.GetFloat(PrefsKeyPrefix + "MaxDistance_" + i, 10f),
                minDistanceScale = EditorPrefs.GetFloat(PrefsKeyPrefix + "MinDistanceScale_" + i, 0.5f),
                maxDistanceScale = EditorPrefs.GetFloat(PrefsKeyPrefix + "MaxDistanceScale_" + i, 2f),
                distanceScaleExponent = EditorPrefs.GetFloat(PrefsKeyPrefix + "DistanceScaleExponent_" + i, 1f),
                
                // Настройки распределения Пуассона
                poissonMinDistance = EditorPrefs.GetFloat(PrefsKeyPrefix + "PoissonMinDistance_" + i, 1f)
            };
            prefabs.Add(pd);
        }
    }

    /// <summary>
    /// Метод инициализации окна редактора
    /// </summary>
    [MenuItem("ObjectDistributor/ObjectBySpline")]
    static void Init()
    {
        var window = GetWindow<ObjectBySpline>("ObjectBySpline");
        window.minSize = new Vector2(350, 550);
        window.LoadPrefs();
    }

    /// <summary>
    /// Основной метод отрисовки интерфейса редактора
    /// </summary>
    void OnGUI()
    {
        GUILayout.Label("ObjectBySpline", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        
        // Поле выбора контейнера сплайна
        splineContainer = (SplineContainer)EditorGUILayout.ObjectField("Spline Container", splineContainer, typeof(SplineContainer), true);
        EditorGUILayout.Space();
        
        // Поле ввода имени родительского объекта
        parentName = EditorGUILayout.TextField("Parent Name", parentName);
        EditorGUILayout.Space();
        
        // Область прокрутки для длинных списков префабов
        m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition, GUILayout.ExpandHeight(true));
        
        EditorGUILayout.LabelField("Prefabs", EditorStyles.boldLabel);
        for (int i = 0; i < prefabs.Count; i++)
        {
            var pd = prefabs[i];
            EditorGUILayout.BeginVertical(GUI.skin.box);
            pd.prefab = (GameObject)EditorGUILayout.ObjectField("Prefab " + (i + 1), pd.prefab, typeof(UnityEngine.GameObject), false);
            pd.distributionMode = (DistributionMode)EditorGUILayout.EnumPopup("Distribution Mode", pd.distributionMode);
            if (pd.distributionMode == DistributionMode.OnCurve || pd.distributionMode == DistributionMode.WithinCurve)
            {
                EditorGUILayout.LabelField("Settings for Prefab " + (i + 1), EditorStyles.boldLabel);
                if (pd.distributionMode == DistributionMode.OnCurve)
                {
                    pd.alignToSpline = EditorGUILayout.Toggle("Align To Spline", pd.alignToSpline);
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Instance Settings", EditorStyles.boldLabel);
                    pd.count = EditorGUILayout.IntField("Count", pd.count);
                    pd.scale = EditorGUILayout.Vector3Field("Scale", pd.scale);
                    pd.rotation = EditorGUILayout.Vector3Field("Rotation", pd.rotation);
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Randomization", EditorStyles.boldLabel);
                    pd.randomPositionRange = EditorGUILayout.FloatField("Random Position Range", pd.randomPositionRange);
                    pd.randomRotationRange = EditorGUILayout.FloatField("Random Rotation Range", pd.randomRotationRange);
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Spacing", EditorStyles.boldLabel);
                    pd.useSpacing = EditorGUILayout.Toggle("Use Object Spacing", pd.useSpacing);
                    if (pd.useSpacing)
                    {
                        pd.spacing = EditorGUILayout.FloatField("Spacing Distance", pd.spacing);
                    }
                }
                else if (pd.distributionMode == DistributionMode.WithinCurve)
                {
                    pd.withinCurveMode = (WithinCurveMode)EditorGUILayout.EnumPopup("Within Curve Mode", pd.withinCurveMode);
                    pd.count = EditorGUILayout.IntField("Count", pd.count);
                    if (pd.withinCurveMode == WithinCurveMode.AroundPath)
                    {
                        pd.areaWidth = EditorGUILayout.FloatField("Width", pd.areaWidth);
                        pd.areaHeight = EditorGUILayout.FloatField("Height", pd.areaHeight);
                        pd.pathDensity = EditorGUILayout.Slider("Плотность", pd.pathDensity, 0.01f, 10f);
                        EditorGUILayout.HelpBox("Чем выше значение, тем больше объектов будет размещено вдоль сплайна.", MessageType.Info);
                    }
                    else // FilledArea
                    {
                        EditorGUILayout.HelpBox("Distribute objects within the area enclosed by a closed curve. Works best with closed curves.", MessageType.Info);
                        pd.areaResolution = EditorGUILayout.IntSlider("Area Resolution", pd.areaResolution, 10, 200);
                        pd.densityFactor = EditorGUILayout.Slider("Density", pd.densityFactor, 0.01f, 50f);
                        pd.poissonMinDistance = EditorGUILayout.FloatField("Poisson Min Distance", pd.poissonMinDistance);
                    }
                    pd.alignToSpline = EditorGUILayout.Toggle("Align To Spline", pd.alignToSpline);
                }
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Collision Settings", EditorStyles.boldLabel);
                pd.snapToCollision = EditorGUILayout.Toggle("Притягивать к коллизии", pd.snapToCollision);
                if (pd.snapToCollision)
                {
                    pd.maxRaycastDistance = EditorGUILayout.FloatField("Макс. дистанция луча", pd.maxRaycastDistance);
                }
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Texture-Based Scaling", EditorStyles.boldLabel);
                pd.scaleRelativeToTexture = EditorGUILayout.Toggle("Scale Relative To Texture", pd.scaleRelativeToTexture);
                pd.useTextureMask = EditorGUILayout.Toggle("Use Texture Mask", pd.useTextureMask);
                if (pd.useTextureMask)
                    pd.textureMaskThreshold = EditorGUILayout.Slider("Mask Threshold", pd.textureMaskThreshold, 0f, 1f);
                if (pd.scaleRelativeToTexture)
                {
                    pd.scaleTexture = (Texture2D)EditorGUILayout.ObjectField("Scale Texture", pd.scaleTexture, typeof(Texture2D), false);
                    pd.useAlphaChannel = EditorGUILayout.Toggle("Use Alpha Channel", pd.useAlphaChannel);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Scale Range", GUILayout.Width(80));
                    pd.minTextureScale = EditorGUILayout.FloatField(pd.minTextureScale, GUILayout.Width(50));
                    EditorGUILayout.LabelField("to", GUILayout.Width(20));
                    pd.maxTextureScale = EditorGUILayout.FloatField(pd.maxTextureScale, GUILayout.Width(50));
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.LabelField("Texture Settings", EditorStyles.boldLabel);
                    pd.textureScale = EditorGUILayout.Vector2Field("Texture Scale", pd.textureScale);
                    pd.textureOffset = EditorGUILayout.Vector2Field("Texture Offset", pd.textureOffset);
                    
                    if (pd.scaleTexture == null)
                    {
                        EditorGUILayout.HelpBox("Please assign a texture for scaling.", MessageType.Warning);
                    }
                }
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Distance-Based Scaling", EditorStyles.boldLabel);
                pd.useDistanceBasedScaling = EditorGUILayout.Toggle("Use Distance Scaling", pd.useDistanceBasedScaling);
                if (pd.useDistanceBasedScaling)
                {
                    pd.minDistance = EditorGUILayout.FloatField("Min Distance", pd.minDistance);
                    pd.maxDistance = EditorGUILayout.FloatField("Max Distance", pd.maxDistance);
                    pd.minDistanceScale = EditorGUILayout.FloatField("Min Scale", pd.minDistanceScale);
                    pd.maxDistanceScale = EditorGUILayout.FloatField("Max Scale", pd.maxDistanceScale);
                    pd.distanceScaleExponent = EditorGUILayout.FloatField("Exponent", pd.distanceScaleExponent);
                }
            }
            if (GUILayout.Button("Remove Prefab", GUILayout.Width(100)))
            {
                prefabs.RemoveAt(i);
                break;
            }
            EditorGUILayout.EndVertical();
        }
        if (GUILayout.Button("Add Prefab"))
        {
            prefabs.Add(new PrefabData(null));
        }
        EditorGUILayout.Space();
        
        // Завершение области прокрутки
        EditorGUILayout.EndScrollView();
        
        if (EditorGUI.EndChangeCheck())
        {
            SavePrefs();
            if (previewMode) UpdateInstances();
        }
        
        // Элементы управления вне области прокрутки
        EditorGUILayout.Space();
        previewMode = EditorGUILayout.Toggle("Live Preview", previewMode);
        EditorGUILayout.Space();
        
        // Кнопки действий (вне области прокрутки для удобства)
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Генерировать", GUILayout.Height(30)))
        {
            GenerateInstances();
        }
        if (GUILayout.Button("Добавить", GUILayout.Height(30)))
        {
            AddInstances();
        }
        if (GUILayout.Button("Очистить", GUILayout.Height(30)))
        {
            ClearInstances();
        }
        if (GUILayout.Button("Обновить(10+раз)", GUILayout.Height(30)))
        {
            UpdateInstances();
        }
        EditorGUILayout.EndHorizontal();

        // Check if the selected object is a SplineContainer
        if (splineContainer is SplineContainer container)
        {
            foreach (var s in container.Splines)
            {
                // Process each spline as needed
            }
        }
    }

    /// <summary>
    /// Генерирует новые экземпляры объектов вдоль сплайна
    /// Создает новый родительский объект и размещает все экземпляры в нем
    /// </summary>
    void GenerateInstances()
    {
        ClearInstances();
        if (prefabs.Count == 0)
        {
            Debug.LogWarning("Prefab должен быть задан");
            return;
        }
        if (splineContainer == null || splineContainer.Splines.Count == 0)
        {
            Debug.LogWarning("SplineContainer должен быть задан и содержать хотя бы один сплайн");
            return;
        }
        var spline = splineContainer.Splines[0];
        parent = new GameObject(parentName);
        Undo.RegisterCreatedObjectUndo(parent, "Create Spline Instances");
        parent.transform.position = splineContainer.transform.position;
        
        // Создаем новые экземпляры с учетом per-prefab counts
        int totalInstances = 0;
        foreach (var pd in prefabs)
            if (pd.HasValidPrefab())
                totalInstances += pd.count;
        instances = new GameObject[totalInstances];
        instanceGroups.Add(instances);
        
        // Генерируем экземпляры с помощью общего метода
        GenerateInstancesInternal(instances);
        
        Selection.activeGameObject = parent;
    }

    /// <summary>
    /// Добавляет новые экземпляры объектов к существующим
    /// Создает новую группу экземпляров с отдельным родительским объектом
    /// </summary>
    void AddInstances()
    {
        if (splineContainer == null || splineContainer.Splines.Count == 0 || prefabs.Count == 0)
        {
            Debug.LogWarning("Необходим SplineContainer и Prefab");
            return;
        }
        var spline = splineContainer.Splines[0];
        int instanceCount = instanceGroups.Count + 1;
        parent = new GameObject($"{parentName}_{instanceCount}");
        Undo.RegisterCreatedObjectUndo(parent, "Create Spline Instances");
        parent.transform.position = splineContainer.transform.position;
        
        // Создаем новые экземпляры и добавляем их в список групп с учетом per-prefab counts
        int totalInstancesNew = 0;
        foreach (var pd in prefabs)
            if (pd.HasValidPrefab())
                totalInstancesNew += pd.count;
        GameObject[] newInstances = new GameObject[totalInstancesNew];
        instanceGroups.Add(newInstances);
        instances = newInstances;
        
        // Генерируем новые экземпляры
        GenerateInstancesInternal(newInstances);
        
        Selection.activeGameObject = parent;
    }
    
    /// <summary>
    /// Обновляет существующие экземпляры объектов
    /// Пересоздает экземпляры с текущими настройками, сохраняя структуру групп
    /// </summary>
    void UpdateInstances()
    {
        if (splineContainer == null || splineContainer.Splines.Count == 0 || prefabs.Count == 0 || parent == null)
        {
            ClearInstances();
            Debug.LogWarning("Необходимы SplineContainer, Prefab и корректное количество экземпляров");
            return;
        }
        var spline = splineContainer.Splines[0];
        // Очистка существующих экземпляров в текущей группе
        if (instances != null)
        {
            foreach (var go in instances)
                if (go) UnityEngine.Object.DestroyImmediate(go);
        }
        
        // Если группа экземпляров пуста, создаем новую с учетом per-prefab counts
        if (instanceGroups.Count == 0 || !instanceGroups.Contains(instances))
        {
            int totalInstancesUpd = 0;
            foreach (var pd in prefabs)
                if (pd.HasValidPrefab())
                    totalInstancesUpd += pd.count;
            instances = new GameObject[totalInstancesUpd];
            instanceGroups.Add(instances);
        }
        
        // Генерируем экземпляры
        GenerateInstancesInternal(instances);
    }
    
    /// <summary>
    /// Внутренний метод для генерации экземпляров
    /// Распределяет объекты вдоль сплайна в соответствии с настройками каждого префаба
    /// </summary>
    /// <param name="targetInstances">Массив для хранения созданных экземпляров</param>
    private void GenerateInstancesInternal(GameObject[] targetInstances)
    {
        if (splineContainer == null || splineContainer.Splines.Count == 0) return;
        var spline = splineContainer.Splines[0];
        
        // Проверка на наличие валидных префабов
        List<PrefabData> validPrefabs = new List<PrefabData>();
        foreach (var pd in prefabs)
        {
            if (pd.HasValidPrefab())
            {
                validPrefabs.Add(pd);
            }
        }
        
        if (validPrefabs.Count == 0)
        {
            Debug.LogWarning("Нет валидных префабов для создания");
            return;
        }
        
        // Распределение с учетом настроек каждого префаба
        int instanceIndex = 0;
        foreach (var pd in validPrefabs)
        {
            float splineLength = SplineUtility.CalculateLength(spline, splineContainer.transform.localToWorldMatrix);
            int countForThisPrefab = Mathf.CeilToInt(splineLength * pd.pathDensity);
            if (countForThisPrefab < 1) countForThisPrefab = 1;
            
            for (int i = 0; i < countForThisPrefab; i++)
            {
                if (pd.distributionMode == DistributionMode.OnCurve)
                    DistributeOnCurve(spline, i, pd, countForThisPrefab, instanceIndex);
                else
                    DistributeWithinCurve(spline, i, pd, countForThisPrefab, instanceIndex);
                instanceIndex++;
            }
        }
    }

    void DistributeOnCurve(UnityEngine.Splines.Spline spline, int index, PrefabData pd, int countForThisPrefab, int globalIndex)
    {
        if (spline == null || pd.prefab == null || parent == null || countForThisPrefab <= 0)
        {
            Debug.LogWarning("Невозможно распределить объекты: отсутствуют необходимые компоненты");
            return;
        }
        Undo.RegisterFullObjectHierarchyUndo(parent, "Distribute Objects On Curve");
        float splineLength = SplineUtility.CalculateLength(spline, splineContainer.transform.localToWorldMatrix);
        if (splineLength <= 0)
        {
            Debug.LogWarning("Длина сплайна равна нулю или отрицательна");
            return;
        }
        // Compute position and rotation using mesh spline sampling
        float t = (countForThisPrefab == 1) ? 0.5f : (float)index / (countForThisPrefab - 1);
        Vector3 pos = SplineUtility.EvaluatePosition(spline, t);
        Vector3 tangent = SplineUtility.EvaluateTangent(spline, t);
        Vector3 up = Vector3.up;
        Quaternion rot = Quaternion.LookRotation(tangent, up);
        if (pd.randomPositionRange > 0)
        {
            pos += UnityEngine.Random.insideUnitSphere * pd.randomPositionRange;
        }
        if (pd.randomRotationRange > 0)
        {
            rot *= Quaternion.Euler(
                UnityEngine.Random.Range(-pd.randomRotationRange, pd.randomRotationRange),
                UnityEngine.Random.Range(-pd.randomRotationRange, pd.randomRotationRange),
                UnityEngine.Random.Range(-pd.randomRotationRange, pd.randomRotationRange)
            );
        }
        if (pd.useTextureMask && pd.scaleTexture != null)
        {
            Vector3 worldPos = parent.transform.TransformPoint(pos);
            float u = Mathf.Repeat(worldPos.x * pd.textureScale.x / 10f + pd.textureOffset.x, 1f);
            float v = Mathf.Repeat(worldPos.z * pd.textureScale.y / 10f + pd.textureOffset.y, 1f);
            Color pixelColor = pd.scaleTexture.GetPixelBilinear(u, v);
            float maskValue = pd.useAlphaChannel ? pixelColor.a : (pixelColor.r * 0.299f + pixelColor.g * 0.587f + pixelColor.b * 0.114f);
            if (maskValue < pd.textureMaskThreshold)
                return;
        }
        var go = GetPooledInstance(pd.prefab, parent.transform);
        if (go != null)
        {
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            
            // Притягиваем к коллизии, если включено
            if (pd.snapToCollision)
            {
                Vector3 worldPos = parent.transform.TransformPoint(pos);
                RaycastHit hit;
                if (Physics.Raycast(worldPos, Vector3.down, out hit, pd.maxRaycastDistance))
                {
                    Vector3 localHitPos = parent.transform.InverseTransformPoint(hit.point);
                    go.transform.localPosition = localHitPos;
                }
            }
            go.transform.localScale = pd.scale;
            
            // Применяем масштабирование на основе текстуры, если это включено
            Vector3 finalScale = pd.scale;
            if (pd.scaleRelativeToTexture && pd.scaleTexture != null)
            {
                // Преобразуем мировую позицию в координаты текстуры (0-1) с учетом масштаба и сдвига
                Vector3 worldPos = parent.transform.TransformPoint(pos);
                float u = Mathf.Repeat(worldPos.x * pd.textureScale.x / 10f + pd.textureOffset.x, 1f);
                float v = Mathf.Repeat(worldPos.z * pd.textureScale.y / 10f + pd.textureOffset.y, 1f);
                Color pixelColor = pd.scaleTexture.GetPixelBilinear(u, v);
                float scaleFactor = pd.useAlphaChannel ? pixelColor.a : (pixelColor.r * 0.299f + pixelColor.g * 0.587f + pixelColor.b * 0.114f);
                float textureScale = Mathf.Lerp(pd.minTextureScale, pd.maxTextureScale, scaleFactor);
                finalScale *= textureScale;
            }
            
            // Применяем масштабирование по расстоянию от кривой, если это включено
            if (pd.useDistanceBasedScaling)
            {
                float dist = Vector3.Distance(pos, SplineUtility.EvaluatePosition(spline, t));
                float norm = Mathf.InverseLerp(pd.minDistance, pd.maxDistance, dist);
                norm = Mathf.Clamp01(norm);
                float factorExp = Mathf.Pow(norm, pd.distanceScaleExponent);
                float distanceScale = Mathf.Lerp(pd.maxDistanceScale, pd.minDistanceScale, factorExp);
                finalScale *= distanceScale;
            }
            
            go.transform.localScale = finalScale;
            if (instances != null && globalIndex >= 0 && globalIndex < instances.Length)
                instances[globalIndex] = go;
            Undo.RegisterCreatedObjectUndo(go, "Create Instance");
        }
    }

    void DistributeWithinCurve(UnityEngine.Splines.Spline spline, int index, PrefabData pd, int countForThisPrefab, int globalIndex)
    {
        if (spline == null || pd.prefab == null || parent == null || countForThisPrefab <= 0)
        {
            Debug.LogWarning("Невозможно распределить объекты: отсутствуют необходимые компоненты");
            return;
        }
        // Регистрация операции для возможности отмены
        Undo.RegisterFullObjectHierarchyUndo(parent, "Distribute Objects Within Curve");
        if (pd.withinCurveMode == WithinCurveMode.AroundPath)
        {
            DistributeAroundPath(spline, index, pd, countForThisPrefab, globalIndex);
        }
        else
        {
            DistributeWithinArea(spline, index, pd, countForThisPrefab, globalIndex);
        }
    }

    void DistributeAroundPath(UnityEngine.Splines.Spline spline, int index, PrefabData pd, int countForThisPrefab, int globalIndex)
    {
        if (spline == null || pd.prefab == null || parent == null || countForThisPrefab <= 0)
        {
            Debug.LogWarning("Невозможно распределить объекты вокруг пути: отсутствуют необходимые компоненты");
            return;
        }
        // t равномерно по кривой
        float t = (countForThisPrefab == 1) ? 0.5f : (float)index / (countForThisPrefab - 1);
        t = Mathf.Clamp01(t + UnityEngine.Random.Range(-0.02f, 0.02f));
        int segmentCount = spline.Count - 1;
        float sampleTime = t * segmentCount;
        Vector3 curvePos = SplineUtility.EvaluatePosition(spline, sampleTime);
        Vector3 tangent = SplineUtility.EvaluateTangent(spline, sampleTime);
        Vector3 up = Vector3.up;
        Vector3 right = Vector3.Cross(up, tangent).normalized;
        // Смещение по ширине и высоте
        float offsetW = UnityEngine.Random.Range(-pd.areaWidth * 0.5f, pd.areaWidth * 0.5f);
        float offsetH = UnityEngine.Random.Range(-pd.areaHeight * 0.5f, pd.areaHeight * 0.5f);
        curvePos += right * offsetW + up * offsetH;
        Quaternion rot = pd.alignToSpline ? Quaternion.LookRotation(tangent, up) : Quaternion.Euler(pd.rotation);
        if (pd.randomPositionRange > 0)
            curvePos += UnityEngine.Random.insideUnitSphere * pd.randomPositionRange;
        if (pd.randomRotationRange > 0)
            rot *= Quaternion.Euler(
                UnityEngine.Random.Range(-pd.randomRotationRange, pd.randomRotationRange),
                UnityEngine.Random.Range(-pd.randomRotationRange, pd.randomRotationRange),
                UnityEngine.Random.Range(-pd.randomRotationRange, pd.randomRotationRange)
            );
        if (pd.useTextureMask && pd.scaleTexture != null)
        {
            Vector3 worldPos = parent.transform.TransformPoint(curvePos);
            float u = Mathf.Repeat(worldPos.x * pd.textureScale.x / 10f + pd.textureOffset.x, 1f);
            float v = Mathf.Repeat(worldPos.z * pd.textureScale.y / 10f + pd.textureOffset.y, 1f);
            Color pixelColor = pd.scaleTexture.GetPixelBilinear(u, v);
            float maskValue = pd.useAlphaChannel ? pixelColor.a : (pixelColor.r * 0.299f + pixelColor.g * 0.587f + pixelColor.b * 0.114f);
            if (maskValue < pd.textureMaskThreshold)
                return;
        }
        var go = GetPooledInstance(pd.prefab, parent.transform);
        if (go != null)
        {
            go.transform.localPosition = curvePos;
            go.transform.localRotation = rot;
            if (pd.snapToCollision)
            {
                Vector3 worldPos = parent.transform.TransformPoint(curvePos);
                RaycastHit hit;
                if (Physics.Raycast(worldPos, Vector3.down, out hit, pd.maxRaycastDistance))
                {
                    Vector3 localHitPos = parent.transform.InverseTransformPoint(hit.point);
                    go.transform.localPosition = localHitPos;
                }
            }
            Vector3 finalScale = pd.scale;
            if (pd.scaleRelativeToTexture && pd.scaleTexture != null)
            {
                Vector3 worldPos = parent.transform.TransformPoint(curvePos);
                float u = Mathf.Repeat(worldPos.x * pd.textureScale.x / 10f + pd.textureOffset.x, 1f);
                float v = Mathf.Repeat(worldPos.z * pd.textureScale.y / 10f + pd.textureOffset.y, 1f);
                Color pixelColor = pd.scaleTexture.GetPixelBilinear(u, v);
                float scaleFactor = pd.useAlphaChannel ? pixelColor.a : (pixelColor.r * 0.299f + pixelColor.g * 0.587f + pixelColor.b * 0.114f);
                float textureScale = Mathf.Lerp(pd.minTextureScale, pd.maxTextureScale, scaleFactor);
                finalScale *= textureScale;
            }
            if (pd.useDistanceBasedScaling)
            {
                float dist = Vector3.Distance(curvePos, SplineUtility.EvaluatePosition(spline, sampleTime));
                float norm = Mathf.InverseLerp(pd.minDistance, pd.maxDistance, dist);
                norm = Mathf.Clamp01(norm);
                float factor = Mathf.Pow(norm, pd.distanceScaleExponent);
                float distanceScale = Mathf.Lerp(pd.maxDistanceScale, pd.minDistanceScale, factor);
                finalScale *= distanceScale;
            }
            go.transform.localScale = finalScale;
            if (instances != null && globalIndex >= 0 && globalIndex < instances.Length)
                instances[globalIndex] = go;
            Undo.RegisterCreatedObjectUndo(go, "Create Instance Around Path");
        }
    }

    void DistributeWithinArea(UnityEngine.Splines.Spline spline, int index, PrefabData pd, int countForThisPrefab, int globalIndex)
    {
        if (spline == null || pd.prefab == null || parent == null)
        {
            Debug.LogWarning("Невозможно распределить объекты внутри области: отсутствуют необходимые компоненты");
            return;
        }

        // Create polygon from spline with adaptive sampling
        List<Vector3> polygonPoints = new List<Vector3>();
        float splineLength = SplineUtility.CalculateLength(spline, splineContainer.transform.localToWorldMatrix);
        int sampleCount = Mathf.Max(pd.areaResolution, Mathf.CeilToInt(splineLength * 2f));
        float step = 1.0f / sampleCount;
        
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i * step;
            Vector3 pt = SplineUtility.EvaluatePosition(spline, t);
            polygonPoints.Add(pt);
        }
        if (!spline.Closed)
            polygonPoints.Add(polygonPoints[0]);

        // Convert to 2D for point distribution
        List<Vector2> polygon2D = polygonPoints.ConvertAll(p => new Vector2(p.x, p.z));

        // Calculate area and adjust point count based on density
        float area = CalculatePolygonArea(polygon2D);
        float baseDensity = pd.densityFactor * 5f;
        int targetPointCount = Mathf.Max(1, Mathf.RoundToInt(baseDensity * (area / 50f)));

        // Generate points within polygon using improved Poisson disk sampling
        List<Vector2> points = GeneratePointsInPolygon(polygon2D, targetPointCount, pd.poissonMinDistance);
        if (index >= points.Count) return;

        // Get position and calculate height using improved interpolation
        Vector2 pos2D = points[index];
        Vector3 pos = new Vector3(pos2D.x, 0, pos2D.y);

        // Calculate height using improved interpolation
        float height = CalculateHeightAtPoint(pos2D, polygonPoints, spline);
        pos.y = height;

        // Calculate rotation with improved alignment
        Quaternion rot;
        if (pd.alignToSpline)
        {
            float closestT = FindClosestPointOnSpline(spline, pos);
            Vector3 tangent = SplineUtility.EvaluateTangent(spline, closestT);
            Vector3 normal = Vector3.Cross(tangent, Vector3.up).normalized;
            
            // Calculate slope-based rotation
            Vector3 slope = CalculateSlopeAtPoint(pos2D, polygonPoints);
            float slopeAngle = Vector3.Angle(slope, Vector3.up);
            Quaternion slopeRotation = Quaternion.FromToRotation(Vector3.up, slope);
            
            rot = Quaternion.LookRotation(tangent, normal) * slopeRotation;
        }
        else
        {
            rot = Quaternion.Euler(pd.rotation);
        }

        // Apply random variations with improved distribution
        if (pd.randomPositionRange > 0)
        {
            Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * pd.randomPositionRange;
            randomOffset.y *= 0.5f; // Reduce vertical randomness
            pos += randomOffset;
        }

        if (pd.randomRotationRange > 0)
        {
            float randomYaw = UnityEngine.Random.Range(-pd.randomRotationRange, pd.randomRotationRange);
            float randomPitch = UnityEngine.Random.Range(-pd.randomRotationRange * 0.5f, pd.randomRotationRange * 0.5f);
            float randomRoll = UnityEngine.Random.Range(-pd.randomRotationRange * 0.25f, pd.randomRotationRange * 0.25f);
            rot *= Quaternion.Euler(randomPitch, randomYaw, randomRoll);
        }

        // Create instance with improved placement
        var go = GetPooledInstance(pd.prefab, parent.transform);
        if (go != null)
        {
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;

            if (pd.snapToCollision)
            {
                Vector3 worldPos = parent.transform.TransformPoint(pos);
                RaycastHit hit;
                if (Physics.Raycast(worldPos, Vector3.down, out hit, pd.maxRaycastDistance))
                {
                    Vector3 localHitPos = parent.transform.InverseTransformPoint(hit.point);
                    go.transform.localPosition = localHitPos;
                    
                    // Adjust rotation based on surface normal
                    if (pd.alignToSpline)
                    {
                        Quaternion surfaceRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
                        go.transform.localRotation = surfaceRotation * rot;
                    }
                }
            }

            Vector3 finalScale = pd.scale;
            if (pd.scaleRelativeToTexture && pd.scaleTexture != null)
            {
                Vector3 worldPos = parent.transform.TransformPoint(pos);
                float u = Mathf.Repeat(worldPos.x * pd.textureScale.x / 10f + pd.textureOffset.x, 1f);
                float v = Mathf.Repeat(worldPos.z * pd.textureScale.y / 10f + pd.textureOffset.y, 1f);
                Color pixelColor = pd.scaleTexture.GetPixelBilinear(u, v);
                float scaleFactor = pd.useAlphaChannel ? pixelColor.a : (pixelColor.r * 0.299f + pixelColor.g * 0.587f + pixelColor.b * 0.114f);
                float textureScale = Mathf.Lerp(pd.minTextureScale, pd.maxTextureScale, scaleFactor);
                finalScale *= textureScale;
            }

            if (pd.useDistanceBasedScaling)
            {
                float closestT = FindClosestPointOnSpline(spline, pos);
                Vector3 nearestPos = SplineUtility.EvaluatePosition(spline, closestT);
                float dist = Vector3.Distance(pos, nearestPos);
                float norm = Mathf.InverseLerp(pd.minDistance, pd.maxDistance, dist);
                norm = Mathf.Clamp01(norm);
                float factorExp = Mathf.Pow(norm, pd.distanceScaleExponent);
                float distanceScale = Mathf.Lerp(pd.maxDistanceScale, pd.minDistanceScale, factorExp);
                finalScale *= distanceScale;
            }

            go.transform.localScale = finalScale;
            if (instances != null && globalIndex >= 0 && globalIndex < instances.Length)
                instances[globalIndex] = go;
            Undo.RegisterCreatedObjectUndo(go, "Create Instance Within Area");
        }
    }

    private float CalculatePolygonArea(List<Vector2> polygon)
    {
        float area = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            int j = (i + 1) % polygon.Count;
            area += polygon[i].x * polygon[j].y;
            area -= polygon[j].x * polygon[i].y;
        }
        return Mathf.Abs(area) * 0.5f;
    }

    private List<Vector2> GeneratePointsInPolygon(List<Vector2> polygon, int targetCount, float minDistance)
    {
        // Настоящий Bridson's Poisson Disk Sampling
        float cellSize = minDistance / Mathf.Sqrt(2);
        float minX = polygon.Min(p => p.x);
        float minY = polygon.Min(p => p.y);
        float maxX = polygon.Max(p => p.x);
        float maxY = polygon.Max(p => p.y);

        int gridWidth = Mathf.CeilToInt((maxX - minX) / cellSize);
        int gridHeight = Mathf.CeilToInt((maxY - minY) / cellSize);
        List<Vector2> points = new List<Vector2>();
        List<Vector2> active = new List<Vector2>();
        Vector2?[,] grid = new Vector2?[gridWidth, gridHeight];

        // Helper for grid index
        Func<Vector2, (int, int)> GridIdx = (Vector2 pt) =>
        {
            int gx = (int)((pt.x - minX) / cellSize);
            int gy = (int)((pt.y - minY) / cellSize);
            return (gx, gy);
        };

        // Стартовая точка
        for (int tries = 0; tries < 1000; tries++)
        {
            float x = UnityEngine.Random.Range(minX, maxX);
            float y = UnityEngine.Random.Range(minY, maxY);
            Vector2 pt = new Vector2(x, y);
            if (IsPointInPolygon2D(pt, polygon))
            {
                points.Add(pt);
                active.Add(pt);
                var (gx, gy) = GridIdx(pt);
                grid[gx, gy] = pt;
                break;
            }
        }

        int k = 30; // попыток вокруг каждой активной точки

        while (active.Count > 0)
        {
            int idx = UnityEngine.Random.Range(0, active.Count);
            Vector2 center = active[idx];
            bool found = false;

            for (int i = 0; i < k; i++)
            {
                float angle = UnityEngine.Random.value * Mathf.PI * 2;
                float radius = UnityEngine.Random.Range(minDistance, 2 * minDistance);
                Vector2 candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

                if (candidate.x < minX || candidate.x > maxX || candidate.y < minY || candidate.y > maxY)
                    continue;
                if (!IsPointInPolygon2D(candidate, polygon))
                    continue;

                var (cgx, cgy) = GridIdx(candidate);
                bool ok = true;
                for (int ix = Mathf.Max(0, cgx - 2); ix <= Mathf.Min(gridWidth - 1, cgx + 2); ix++)
                {
                    for (int iy = Mathf.Max(0, cgy - 2); iy <= Mathf.Min(gridHeight - 1, cgy + 2); iy++)
                    {
                        if (grid[ix, iy].HasValue && Vector2.Distance(candidate, grid[ix, iy].Value) < minDistance)
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (!ok) break;
                }
                if (ok)
                {
                    points.Add(candidate);
                    active.Add(candidate);
                    grid[cgx, cgy] = candidate;
                    found = true;
                    break;
                }
            }
            if (!found)
                active.RemoveAt(idx);
        }
        return points;
    }

    private float CalculateHeightAtPoint(Vector2 point, List<Vector3> polygonPoints, UnityEngine.Splines.Spline spline)
    {
        // Find the closest points for interpolation
        List<(Vector3 point, float distance)> sortedPoints = new List<(Vector3 point, float distance)>();
        foreach (var p in polygonPoints)
        {
            float dist = Vector2.Distance(new Vector2(p.x, p.z), point);
            sortedPoints.Add((p, dist));
        }
        sortedPoints.Sort((a, b) => a.distance.CompareTo(b.distance));

        // Use the closest points for interpolation
        Vector3 p1 = sortedPoints[0].point;
        Vector3 p2 = sortedPoints[1].point;
        Vector3 p3 = sortedPoints[2].point;

        // Calculate barycentric coordinates
        Vector2 v0 = new Vector2(p2.x - p1.x, p2.z - p1.z);
        Vector2 v1 = new Vector2(p3.x - p1.x, p3.z - p1.z);
        Vector2 v2 = new Vector2(point.x - p1.x, point.y - p1.z);

        float d00 = Vector2.Dot(v0, v0);
        float d01 = Vector2.Dot(v0, v1);
        float d11 = Vector2.Dot(v1, v1);
        float d20 = Vector2.Dot(v2, v0);
        float d21 = Vector2.Dot(v2, v1);

        float denom = d00 * d11 - d01 * d01;
        float v = (d11 * d20 - d01 * d21) / denom;
        float w = (d00 * d21 - d01 * d20) / denom;
        float u = 1.0f - v - w;

        // Interpolate height with distance-based weights
        float totalWeight = 0f;
        float weightedHeight = 0f;
        
        for (int i = 0; i < 3; i++)
        {
            float weight = 1f / (sortedPoints[i].distance + 0.001f);
            totalWeight += weight;
            weightedHeight += sortedPoints[i].point.y * weight;
        }

        // Blend between barycentric and weighted interpolation
        float barycentricHeight = u * p1.y + v * p2.y + w * p3.y;
        float weightedInterpolatedHeight = weightedHeight / totalWeight;
        
        return Mathf.Lerp(barycentricHeight, weightedInterpolatedHeight, 0.5f);
    }

    private Vector3 CalculateSlopeAtPoint(Vector2 point, List<Vector3> polygonPoints)
    {
        // Find nearby points for slope calculation
        List<(Vector3 point, float distance)> nearbyPoints = new List<(Vector3 point, float distance)>();
        foreach (var p in polygonPoints)
        {
            float dist = Vector2.Distance(new Vector2(p.x, p.z), point);
            if (dist < 2f) // Only consider points within 2 units
            {
                nearbyPoints.Add((p, dist));
            }
        }

        if (nearbyPoints.Count < 3)
            return Vector3.up;

        // Sort by distance
        nearbyPoints.Sort((a, b) => a.distance.CompareTo(b.distance));

        // Calculate average normal from nearby points
        Vector3 averageNormal = Vector3.zero;
        for (int i = 0; i < nearbyPoints.Count - 2; i++)
        {
            Vector3 p1 = nearbyPoints[i].point;
            Vector3 p2 = nearbyPoints[i + 1].point;
            Vector3 p3 = nearbyPoints[i + 2].point;

            Vector3 normal = Vector3.Cross(p2 - p1, p3 - p1).normalized;
            averageNormal += normal;
        }

        averageNormal /= (nearbyPoints.Count - 2);
        return averageNormal.normalized;
    }

    // 2D Point in Polygon
    bool IsPointInPolygon2D(Vector2 point, List<Vector2> polygon)
    {
        int crossings = 0;
        for (int i = 0; i < polygon.Count - 1; i++)
        {
            Vector2 p1 = polygon[i];
            Vector2 p2 = polygon[i + 1];
            bool cond1 = (p1.y > point.y) != (p2.y > point.y);
            float atX = (p2.x - p1.x) * (point.y - p1.y) / (p2.y - p1.y + 1e-6f) + p1.x;
            if (cond1 && point.x < atX)
                crossings++;
        }
        return (crossings % 2) == 1;
    }

    // Helper method to find the closest point on a spline
    float FindClosestPointOnSpline(UnityEngine.Splines.Spline spline, Vector3 point)
    {
        if (spline == null)
            return 0.5f;
        float closestT = 0;
        float minDist = float.MaxValue;
        int steps = 100;
        float step = 1.0f / steps;
        for (int i = 0; i <= steps; i++)
        {
            float t = i * step;
            Vector3 pos = SplineUtility.EvaluatePosition(spline, t);
            float dist = Vector3.Distance(pos, point);
            if (dist < minDist)
            {
                minDist = dist;
                closestT = t;
            }
        }
        return closestT;
    }

    void ClearInstances()
    {
        // Pool all existing instances instead of destroying
        PoolAllInstances();
        instanceGroups.Clear();
        instances = null;
        if (parent != null)
        {
            UnityEngine.Object.DestroyImmediate(parent);
            parent = null;
        }
        SavePrefs();
    }

    // Pooling helper methods
    private void PoolAllInstances()
    {
        foreach (var group in instanceGroups)
        {
            if (group != null)
            {
                foreach (var go in group)
                {
                    if (go) UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }
    }

    private void PoolInstance(GameObject go)
    {
        var sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(go) as UnityEngine.GameObject;
        if (sourcePrefab == null)
        {
            UnityEngine.Object.DestroyImmediate(go);
            return;
        }
        if (!objectPool.TryGetValue(sourcePrefab, out var stack))
        {
            stack = new Stack<GameObject>();
            objectPool[sourcePrefab] = stack;
        }
        go.SetActive(false);
        go.transform.SetParent(GetPoolContainer());
        stack.Push(go);
    }

    private Transform GetPoolContainer()
    {
        if (poolContainer == null)
        {
            var go = GameObject.Find("[SplineInstancerPool]");
            if (go == null)
            {
                go = new GameObject("[SplineInstancerPool]");
                go.hideFlags = HideFlags.HideAndDontSave;
            }
            poolContainer = go.transform;
        }
        return poolContainer;
    }

    private GameObject GetPooledInstance(GameObject prefab, Transform parentTransform)
    {
        if (objectPool.TryGetValue(prefab, out var stack) && stack.Count > 0)
        {
            var go = stack.Pop();
            go.SetActive(true);
            go.transform.SetParent(parentTransform);
            return go;
        }
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parentTransform);
    }
}
#endif