using UnityEngine;
using UnityEditor;

public class GlobalRainController : EditorWindow
{
    // Эти имена должны совпадать с Reference Name в Shader Graph
    private const string RainKeywordName = "_RAIN";
    // Лучше задавать и Float, так как Shader Graph часто использует значение для логики внутри ветки
    private const string RainPropertyName = "_RAIN";

    [MenuItem("Materials/Global Rain Controller")]
    public static void ShowWindow()
    {
        GetWindow<GlobalRainController>("Global Rain");
    }

    void OnGUI()
    {
        GUILayout.Label("Глобальное управление дождем", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Этот скрипт устанавливает Глобальное свойство. Работает даже если Exposed=False.", MessageType.Info);
        EditorGUILayout.Space();

        // ИСПРАВЛЕНИЕ: Проверяем реальное состояние ключевого слова в системе Unity
        bool isRaining = Shader.IsKeywordEnabled(RainKeywordName);

        using (new GUILayout.HorizontalScope())
        {
            // Кнопка "Включить": зеленая, если дождь уже включен
            GUI.backgroundColor = isRaining ? Color.green : Color.white;
            if (GUILayout.Button("Включить Дождь", GUILayout.Height(40)))
            {
                SetGlobalRain(true);
            }

            // Кнопка "Выключить": красная, если дождь уже выключен
            GUI.backgroundColor = !isRaining ? Color.red : Color.white;
            if (GUILayout.Button("Выключить Дождь", GUILayout.Height(40)))
            {
                SetGlobalRain(false);
            }
            GUI.backgroundColor = Color.white; // Сброс цвета
        }

        EditorGUILayout.Space();
        // ИСПРАВЛЕНИЕ: Выводим реальное состояние
        GUILayout.Label($"Текущее состояние Keyword: {isRaining}");
    }

    private static void SetGlobalRain(bool isRaining)
    {
        float targetValue = isRaining ? 1.0f : 0.0f;

        // 1. Устанавливаем глобальное значение Float.
        // Это нужно, чтобы внутри шейдера (в узле Branch) можно было проверить условие "Если _RAIN > 0.5".
        Shader.SetGlobalFloat(RainPropertyName, targetValue);

        // 2. Управляем глобальным Keyword.
        if (isRaining)
        {
            Shader.EnableKeyword(RainKeywordName);
        }
        else
        {
            Shader.DisableKeyword(RainKeywordName);
        }

        // Принудительно обновляем виды Scene и Game, чтобы изменения отобразились сразу
        SceneView.RepaintAll();

        Debug.Log($"[GlobalRain] {(isRaining ? "Включен" : "Выключен")} (Keyword: {RainKeywordName})");
    }
}