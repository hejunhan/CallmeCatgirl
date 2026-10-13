using System.Linq;
using CallmeCatgirl.Gameplay;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MemoryUpgradeConfiguration))]
public sealed class MemoryUpgradeConfigurationEditor : Editor
{
    private int paintLevel = 1;
    private int previewLevel = 1;
    private int mode;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        var config = (MemoryUpgradeConfiguration)target;
        var cells = serializedObject.FindProperty("CellUnlockLevels");
        int count = config.BoardWidth * config.BoardHeight;
        if (cells.arraySize != count)
        {
            EditorGUILayout.HelpBox("格子数据与棋盘尺寸不一致，请修复后编辑。", MessageType.Error);
            if (GUILayout.Button("修复格子数量"))
            {
                int previous = cells.arraySize;
                cells.arraySize = count;
                for (int i = previous; i < count; i++) cells.GetArrayElementAtIndex(i).intValue = 0;
            }
            serializedObject.ApplyModifiedProperties();
            return;
        }
        int maxLevel = Mathf.Max(config.InitialLevel, config.Levels == null ? 1
            : config.Levels.Where(level => level != null).Select(level => level.TargetLevel).DefaultIfEmpty(1).Max());
        var choices = new[] { 0, 1 }.Concat(config.Levels == null ? new int[0]
            : config.Levels.Where(level => level != null).Select(level => level.TargetLevel)).Distinct().OrderBy(level => level).ToArray();
        var labels = choices.Select(level => level == 0 ? "锁定" : level == 1 ? "初始" : level + "级").ToArray();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"内存棋盘 · {config.BoardWidth} × {config.BoardHeight}", EditorStyles.boldLabel);
        mode = GUILayout.Toolbar(mode, new[] { "编辑布局", "预览解锁" });
        if (mode == 0)
        {
            int selected = System.Array.IndexOf(choices, paintLevel);
            paintLevel = choices[GUILayout.Toolbar(Mathf.Max(0, selected), labels)];
            EditorGUILayout.HelpBox("先选等级，再点击格子涂色。初始表示1级开放；锁定表示任何等级都不开放。支持 Ctrl+Z 撤销。", MessageType.Info);
        }
        else previewLevel = EditorGUILayout.IntSlider("预览等级", previewLevel, 1, maxLevel);
        float size = Mathf.Clamp(EditorGUIUtility.currentViewWidth - 48, 160, 440);
        var rect = GUILayoutUtility.GetRect(size, size * config.BoardHeight / config.BoardWidth, GUILayout.ExpandWidth(false));
        float cellSize = size / config.BoardWidth;
        Color original = GUI.backgroundColor;
        for (int y = 0; y < config.BoardHeight; y++)
            for (int x = 0; x < config.BoardWidth; x++)
            {
                var property = cells.GetArrayElementAtIndex(x + y * config.BoardWidth);
                int level = property.intValue;
                bool open = level > 0 && (mode == 0 || level <= previewLevel);
                GUI.backgroundColor = open ? Color.HSVToRGB((level * 0.16f) % 1, 0.6f, 0.9f) : new Color(0.35f, 0.35f, 0.35f);
                string text = level == 0 ? "×" : level == 1 ? "初始" : level + "级";
                var cellRect = new Rect(rect.x + x * cellSize, rect.y + y * cellSize, cellSize - 2, cellSize - 2);
                if (GUI.Button(cellRect, new GUIContent(text, $"第{x + 1}列，第{y + 1}行")) && mode == 0)
                    property.intValue = paintLevel;
            }
        GUI.backgroundColor = original;
        serializedObject.ApplyModifiedProperties();
        int shownLevel = mode == 0 ? config.InitialLevel : previewLevel;
        EditorGUILayout.LabelField($"{shownLevel}级共开放 {config.GetOpenCells(shownLevel).Count} / {count} 格");
        foreach (var level in choices.Where(level => level >= 2))
            EditorGUILayout.LabelField($"{level}级：新增 {config.CellUnlockLevels.Count(value => value == level)} 格，累计 {config.GetOpenCells(level).Count} 格");
        if (config.CellUnlockLevels.Any(value => value < 0 || !choices.Contains(value)))
            EditorGUILayout.HelpBox("部分格子的等级没有对应升级档位，请补充档位或重新涂色。", MessageType.Warning);
    }
}
