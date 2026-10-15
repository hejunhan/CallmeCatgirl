using System;
using CallmeCatgirl.Gameplay;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TaskGenerationConfiguration))]
public sealed class TaskGenerationConfigurationEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "Shapes");
        var shapes = serializedObject.FindProperty("Shapes");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"随机形状库 · 当前 {shapes.arraySize} 种", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("剧情有方块图案时优先使用剧情；没写时从本形状库等概率随机。点击小棋盘切换占格，支持Ctrl+Z；占格须上下左右连通。", MessageType.Info);
        for (int i = 0; i < shapes.arraySize; i++)
        {
            var preset = shapes.GetArrayElementAtIndex(i);
            var name = preset.FindPropertyRelative("Name"); var pattern = preset.FindPropertyRelative("Pattern");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            preset.isExpanded = EditorGUILayout.Foldout(preset.isExpanded, $"{i + 1}. {name.stringValue}", true);
            bool remove = GUILayout.Button("删除", GUILayout.Width(48));
            EditorGUILayout.EndHorizontal();
            if (remove) { shapes.DeleteArrayElementAtIndex(i); EditorGUILayout.EndVertical(); break; }
            if (preset.isExpanded)
            {
                EditorGUILayout.PropertyField(name, new GUIContent("形状名称"));
                int oldHeight = Mathf.Max(1, pattern.arraySize);
                string first = pattern.arraySize > 0 ? pattern.GetArrayElementAtIndex(0).stringValue : "#";
                int oldWidth = Mathf.Max(1, first.Length);
                EditorGUILayout.BeginHorizontal();
                int width = Mathf.Clamp(EditorGUILayout.IntField("列数", oldWidth), 1, 8);
                int height = Mathf.Clamp(EditorGUILayout.IntField("行数", oldHeight), 1, 8);
                EditorGUILayout.EndHorizontal();
                var rows = new string[height];
                for (int y = 0; y < height; y++)
                {
                    string previous = y < pattern.arraySize ? pattern.GetArrayElementAtIndex(y).stringValue : string.Empty;
                    var cells = new char[width];
                    for (int x = 0; x < width; x++) cells[x] = x < previous.Length && (previous[x] == '#' || previous[x] == '■') ? '#' : '.';
                    rows[y] = new string(cells);
                }
                bool dirty = width != oldWidth || height != oldHeight;
                Color original = GUI.backgroundColor;
                for (int y = 0; y < height; y++)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int x = 0; x < width; x++)
                    {
                        bool filled = rows[y][x] == '#';
                        GUI.backgroundColor = filled ? new Color(.5f,.3f,.9f) : new Color(.25f,.25f,.25f);
                        if (GUILayout.Button(filled ? "■" : "", GUILayout.Width(28), GUILayout.Height(28)))
                        {
                            var chars = rows[y].ToCharArray(); chars[x] = filled ? '.' : '#'; rows[y] = new string(chars); dirty = true;
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                GUI.backgroundColor = original;
                if (dirty)
                {
                    pattern.arraySize = height;
                    for (int y = 0; y < height; y++) pattern.GetArrayElementAtIndex(y).stringValue = rows[y];
                }
                try { EditorGUILayout.LabelField("占用 " + ShapePattern.Parse(rows).Length + " 格"); }
                catch (ArgumentException error) { EditorGUILayout.HelpBox(error.Message, MessageType.Warning); }
            }
            EditorGUILayout.EndVertical();
        }
        if (GUILayout.Button("新增形状"))
        {
            int index = shapes.arraySize++; var preset = shapes.GetArrayElementAtIndex(index);
            preset.FindPropertyRelative("Name").stringValue = "新形状"; preset.isExpanded = true;
            var pattern = preset.FindPropertyRelative("Pattern"); pattern.arraySize = 1; pattern.GetArrayElementAtIndex(0).stringValue = "#";
        }
        serializedObject.ApplyModifiedProperties();
    }
}
