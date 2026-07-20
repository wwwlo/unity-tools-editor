using UnityEngine;
using UnityEditor;

public class ReplaceObjectsWindow : EditorWindow
{
    private GameObject replacementPrefab;
    private bool keepOriginalName = false;

    [MenuItem("Level Desing/Replace Objects &#r", false, 0)] // �����: Tools -> Replace Objects ��� Ctrl+Shift+R
    public static void ShowWindow()
    {
        GetWindow<ReplaceObjectsWindow>("Replace Objects");
    }

    private void OnGUI()
    {
        GUILayout.Label("Replace Selected Objects", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // ���� ��� ���������� �������-���������� (������ �� Project ����)
        replacementPrefab = (GameObject)EditorGUILayout.ObjectField("Replacement Prefab", replacementPrefab, typeof(GameObject), false);

        GUILayout.Space(5);
        keepOriginalName = EditorGUILayout.Toggle("Keep Original Name", keepOriginalName);

        GUILayout.Space(20);

        // ������ ������
        GUI.enabled = replacementPrefab != null && Selection.gameObjects.Length > 0;
        if (GUILayout.Button("Replace Selected", GUILayout.Height(30)))
        {
            ReplaceSelected();
        }
        GUI.enabled = true;

        GUILayout.Space(10);

        // �������������� ������
        EditorGUILayout.HelpBox("1. Select objects in the Hierarchy.\n2. Assign a Prefab from the Project window.\n3. Click 'Replace Selected'.", MessageType.Info);
    }

    private void ReplaceSelected()
    {
        if (replacementPrefab == null)
        {
            Debug.LogWarning("Replacement Prefab is not assigned!");
            return;
        }

        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects.Length == 0)
        {
            Debug.LogWarning("No objects selected to replace.");
            return;
        }

        // ���������� �������� ��� ����� ������ (Ctrl+Z)
        Undo.SetCurrentGroupName("Replace Objects");
        int undoGroup = Undo.GetCurrentGroup();

        try
        {
            // ��������� ������� �����, ����� �������� ����� ������� ����� ������
            Object[] newSelection = new Object[selectedObjects.Length];

            for (int i = 0; i < selectedObjects.Length; i++)
            {
                GameObject oldObject = selectedObjects[i];
                Transform oldTransform = oldObject.transform;

                // ���������� ������ ������� �������
                Vector3 pos = oldTransform.position;
                Quaternion rot = oldTransform.rotation;
                Vector3 scale = oldTransform.localScale;
                Transform parent = oldTransform.parent;
                int siblingIndex = oldTransform.GetSiblingIndex();
                string originalName = oldObject.name;

                // ������� ����� ������
                GameObject newObject;
                if (PrefabUtility.IsPartOfPrefabAsset(replacementPrefab))
                {
                    // ���� ���������� - ������, ��������� ����� � ���
                    newObject = (GameObject)PrefabUtility.InstantiatePrefab(replacementPrefab);
                }
                else
                {
                    // ���� ���������� - ������ ������ �� ����� (�� ������)
                    newObject = Instantiate(replacementPrefab);
                }

                // ������������ �������� ��� ������� ������
                Undo.RegisterCreatedObjectUndo(newObject, "Create Replacement Object");

                // ��������� ������ ������� ������� � ������
                Undo.RecordObject(newObject.transform, "Apply Old Transform");
                newObject.transform.SetParent(parent);
                newObject.transform.position = pos;
                newObject.transform.rotation = rot;
                newObject.transform.localScale = scale;
                newObject.transform.SetSiblingIndex(siblingIndex);

                if (keepOriginalName)
                {
                    newObject.name = originalName;
                }

                // ������� ������ ������
                Undo.DestroyObjectImmediate(oldObject);

                newSelection[i] = newObject;
            }

            // �������� ����� ������� � ���������
            Selection.objects = newSelection;
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
        }
    }

    // ��������� ���� ��� ��������� ��������� � �����, ����� ������ �������������/���������������� ���������
    private void OnSelectionChange()
    {
        Repaint();
    }
}