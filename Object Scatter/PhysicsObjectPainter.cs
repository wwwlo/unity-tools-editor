// PhysicsObjectPainter.cs — расширено: поддержка BoxCollider и CapsuleCollider
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class PhysicsObjectPainter : EditorWindow
{
    [MenuItem("ObjectDistributor/Physics Object Painter")]
    static void Open() => GetWindow<PhysicsObjectPainter>("PhysPainter");

    float simulationTime = 10f;
    readonly List<Transform> roots = new();
    void OnGUI()
    {
        GUILayout.Label("LOD-Group Prefabs Selected", EditorStyles.boldLabel);

        if (GUILayout.Button("Refresh Selection") || Event.current.type == EventType.Layout)
            RefreshRoots();

        foreach (var t in roots)
            EditorGUILayout.ObjectField(t.name, t, typeof(Transform), true);

        GUILayout.Space(10);
        simulationTime = EditorGUILayout.FloatField("Simulation Time (s)", simulationTime);

        GUI.enabled = roots.Count > 0;
        if (GUILayout.Button("Simulate", GUILayout.Height(30)))
            SimulateSelected();
        GUI.enabled = true;
    }
        void RefreshRoots() {
        roots.Clear();
        foreach (GameObject go in Selection.gameObjects) {
            if (go.GetComponentInParent<LODGroup>() == null) {
                roots.Add(go.transform);
                continue;
            }
            Transform root = go.transform.root;
            if (!roots.Contains(root))
                roots.Add(root);
        }
    }

    void SimulateSelected()
    {
        // 1. Сбор MeshCollider'ов — как было
        var meshCols = new List<MeshCollider>();
        foreach (var root in roots)
            meshCols.AddRange(root.GetComponentsInChildren<MeshCollider>(true));

        if (meshCols.Count == 0) {
            Debug.LogWarning("No MeshColliders found.");
            return;
        }

        // 2. Convex ON для всех меш-коллайдеров
        foreach (var mc in meshCols) {
            if (!mc.convex) {
                mc.convex = true;
                EditorUtility.SetDirty(mc);
            }
        }

        // 3. Сбор BoxCollider и CapsuleCollider — добавлено ранее
        var boxCapsColls = new List<Collider>();
        foreach (var root in roots) {
            foreach (BoxCollider bc in root.GetComponentsInChildren<BoxCollider>(true))
                if (!boxCapsColls.Contains(bc)) boxCapsColls.Add(bc);
            foreach (CapsuleCollider cc in root.GetComponentsInChildren<CapsuleCollider>(true))
                if (!boxCapsColls.Contains(cc)) boxCapsColls.Add(cc);
        }

        // 4. Добавляем Rigidbody там, где его нет
        var createdRBs = new List<Rigidbody>();

        foreach (var mc in meshCols) {
            if (!mc.GetComponent<Rigidbody>()) {
                var rb = mc.gameObject.AddComponent<Rigidbody>();
                createdRBs.Add(rb);
            }
        }

        foreach (Collider cb in boxCapsColls) {
            if (!cb.GetComponent<Rigidbody>()) {
                var rb = cb.gameObject.AddComponent<Rigidbody>();
                createdRBs.Add(rb);
            }
        }

        // 5. Снимки: теперь и для простых объектов (без LODGroup)
        var snapshots = new List<(Transform root, Vector3 rootPos, Quaternion rootRot,
                                Transform lod0Renderer, Vector3 lod0Pos, Quaternion lod0Rot)>();

        foreach (var root in roots) {
            LODGroup lg = root.GetComponent<LODGroup>();
            if (lg != null) {
                // — классический случай с LOD‑группой — берём первый рендерер из LOD0
                var lods = lg.GetLODs();
                if (lods.Length == 0 || lods[0].renderers.Length == 0) continue;

                Transform lod0Renderer = lods[0].renderers[0].transform;
                snapshots.Add((root, root.position, root.rotation,
                            lod0Renderer, lod0Renderer.position, lod0Renderer.rotation));
            } else {
                // — простой объект: нет LODGroup, берём сам корень как единственную точку отсчёта
                snapshots.Add((root, root.position, root.rotation, null, Vector3.zero, Quaternion.identity));
            }
        }

        // 6. Запуск симуляции (без изменений)
        SimulationMode previous = Physics.simulationMode;
        float dt = Time.fixedDeltaTime;
        Physics.simulationMode = SimulationMode.Script;
        for (float elapsed = 0; elapsed < simulationTime; elapsed += dt) {
            Physics.Simulate(dt);
        }
        Physics.simulationMode = previous;

        // 7. Применяем результат — теперь корректно обрабатываем оба типа снапшотов
        foreach (var snap in snapshots) {
            Vector3 deltaPos;
            Quaternion deltaRot;

            if (snap.lod0Renderer != null) {
                // LOD‑группа: смещение берём от рендерера
                deltaPos = snap.lod0Renderer.position - snap.lod0Pos;
                deltaRot = snap.lod0Renderer.rotation * Quaternion.Inverse(snap.lod0Rot);

                snap.root.position = snap.rootPos + deltaPos;
                snap.root.rotation = deltaRot * snap.rootRot;

                // сбрасываем локальные трансформации дочерних объектов (если есть)
                if (snap.lod0Renderer != null) {
                    snap.lod0Renderer.localPosition = Vector3.zero;
                    snap.lod0Renderer.localRotation = Quaternion.identity;
                }
            } else {
                // простой объект: нет child‑renderer, просто копируем текущий pose корня
                snap.root.position = snap.rootPos + (snap.root.position - snap.rootPos);
                snap.root.rotation = snap.rootRot * (snap.root.rotation * Quaternion.Inverse(snap.rootRot));

                // здесь ничего не сбрасываем — у простого объекта нет локальных дочерних трансформаций,
                // которые нужно было бы вернуть в ноль.
            }
        }

        // 8. Удаляем временные Rigidbody
        foreach (var rb in createdRBs) DestroyImmediate(rb);

        //EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Simulation finished. Simple objects now supported.");

        // 9. Возвращаем меш-коллайдеры в исходное состояние (convex = false)
        foreach (var mc in meshCols) {
            mc.convex = false;
            EditorUtility.SetDirty(mc);
        }
    }


}
