#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class SnapToCollisionEditor : EditorWindow
{
    private Transform parentObject;
    private float rayDistance = 200f; // Делаем настраиваемой

    [MenuItem("Tools/Snap to Collision")]
    public static void ShowWindow() => GetWindow<SnapToCollisionEditor>("Snap Y");

    private void OnGUI()
    {
        GUILayout.Label("Родительский объект:", EditorStyles.boldLabel);
        parentObject = (Transform)EditorGUILayout.ObjectField(parentObject, typeof(Transform), true);

        rayDistance = EditorGUILayout.FloatField("Дистанция луча:", rayDistance);

        if (GUILayout.Button("Притянуть дочерние объекты", GUILayout.Height(30)))
        {
            if (parentObject == null) Debug.LogWarning("Укажите родительский объект!");
            else SnapAllChildren();
        }
    }

    private void SnapAllChildren()
    {
        // Включаем обнаружение внутренних сторон
        bool oldBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;

        int count = 0;
        try
        {
            foreach (Transform child in parentObject)
                if (SnapObject(child)) count++;
        }
        finally
        {
            Physics.queriesHitBackfaces = oldBackfaces; // Восстанавливаем
        }

        Debug.Log($"Обработано объектов: {count}");
    }

    private bool SnapObject(Transform obj)
    {
        Vector3 hitPoint;

        // Пробуем вниз (с приоритетом)
        if (FindNearestHit(obj.position, Vector3.down, out hitPoint))
        {
            obj.position = hitPoint;
            return true;
        }

        // Пробуем вверх
        if (FindNearestHit(obj.position, Vector3.up, out hitPoint))
        {
            obj.position = hitPoint;
            return true;
        }

        Debug.LogWarning($"Коллизия не найдена: {obj.name}");
        return false;
    }

    private bool FindNearestHit(Vector3 origin, Vector3 direction, out Vector3 hitPoint)
    {
        hitPoint = origin;

        // Используем все слои, игнорируем триггеры
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            rayDistance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore
        );

        if (hits.Length == 0) return false;

        // Находим ближайшую точку
        float minDist = float.MaxValue;
        foreach (var hit in hits)
        {
            float dist = Vector3.Distance(origin, hit.point);
            if (dist < minDist)
            {
                minDist = dist;
                hitPoint = hit.point;
            }
        }

        return true;
    }
}
#endif