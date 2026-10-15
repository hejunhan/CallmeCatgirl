using System;
using System.Linq;
using CallmeCatgirl.Gameplay;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MemoryUpgradeConfiguration))]
public sealed class MemoryUpgradeConfigurationEditor : Editor
{
    private int paintLevel = 1, paintCompartment, previewLevel = 1, mode, splitAfter = 5;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        serializedObject.Update();
        var config = (MemoryUpgradeConfiguration)target;
        if ((config.InitialTaskMultipliers != null && !config.InitialTaskMultipliers.IsValid)
            || (config.Levels != null && config.Levels.Any(step => step?.TaskMultipliers != null && !step.TaskMultipliers.IsValid)))
            EditorGUILayout.HelpBox("新任务倍率范围无效：计算量倍率须大于0，奖励倍率不能为负，上限须大于等于下限。", MessageType.Error);
        int width = config.BoardWidth, height = config.BoardHeight, count = width * height;
        if (width < 1 || height < 1) { EditorGUILayout.HelpBox("棋盘尺寸必须大于0。", MessageType.Error); return; }
        var cells = serializedObject.FindProperty("CellUnlockLevels");
        var bags = serializedObject.FindProperty("CellCompartments");
        if (cells.arraySize != count || bags.arraySize != count)
        {
            EditorGUILayout.HelpBox("格子或分区数量与棋盘尺寸不一致。旧配置可修复为同一个背包，不改变解锁数值。", MessageType.Warning);
            if (GUILayout.Button("修复格子 / 分区数量"))
            { Resize(cells, count); Resize(bags, count); }
            serializedObject.ApplyModifiedProperties();
            return;
        }
        var choices = new[] { 0, 1, config.InitialLevel }.Concat(config.Levels == null ? Array.Empty<int>()
            : config.Levels.Where(step => step != null).Select(step => step.TargetLevel)).Distinct().OrderBy(n => n).ToArray();
        int maxLevel = choices.Max();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"内存棋盘 · {width} × {height}", EditorStyles.boldLabel);
        mode = GUILayout.Toolbar(mode, new[] { "解锁等级", "背包分区 / 隔板", "按等级预览" });
        if (mode == 0)
        {
            paintLevel = choices[GUILayout.Toolbar(Mathf.Max(0, Array.IndexOf(choices, paintLevel)),
                choices.Select(n => n == 0 ? "锁定" : n == 1 ? "初始" : n + "级").ToArray())];
            EditorGUILayout.HelpBox("先选等级，再点击或按住左键涂格子。0为永久锁定，1为初始开放。Ctrl+Z撤销。", MessageType.Info);
        }
        else if (mode == 1)
        {
            paintCompartment = Mathf.Max(0, EditorGUILayout.IntField("当前背包编号", paintCompartment));
            var used = Enumerable.Range(0, count).Select(i => bags.GetArrayElementAtIndex(i).intValue).Distinct().OrderBy(n => n).ToArray();
            EditorGUILayout.BeginHorizontal();
            foreach (int id in used.Take(8)) if (GUILayout.Button("背包 " + id)) paintCompartment = id;
            if (GUILayout.Button("新背包 +")) paintCompartment = used.Max() + 1;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("相同编号为同一个背包，不同编号间自动出现隔板。点击或拖动涂色，锁定格也可提前指定背包。升级只开放格子，不改隔板。", MessageType.Info);
            splitAfter = EditorGUILayout.IntField("快速切分位置（行 / 列）", splitAfter);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("上下切分（覆盖分区）"))
                Split(bags, width, height, Mathf.Clamp(splitAfter, 1, height - 1), false);
            if (GUILayout.Button("左右切分（覆盖分区）"))
                Split(bags, width, height, Mathf.Clamp(splitAfter, 1, width - 1), true);
            if (GUILayout.Button("合成一个背包"))
                for (int i = 0; i < count; i++) bags.GetArrayElementAtIndex(i).intValue = 0;
            EditorGUILayout.EndHorizontal();
        }
        else previewLevel = EditorGUILayout.IntSlider("预览等级", previewLevel, 1, Mathf.Max(1, maxLevel));

        float size = Mathf.Clamp(EditorGUIUtility.currentViewWidth - 48, 160, 440);
        var rect = GUILayoutUtility.GetRect(size, size * height / width, GUILayout.ExpandWidth(false));
        float cellSize = size / width;
        var labelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel); labelStyle.normal.textColor = Color.white;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int index = x + y * width;
            int level = cells.GetArrayElementAtIndex(index).intValue;
            int bag = bags.GetArrayElementAtIndex(index).intValue;
            bool open = level > 0 && (mode != 2 || level <= previewLevel);
            Color tint = Color.HSVToRGB((bag * 0.19f + .55f) % 1, .45f, open ? .72f : .26f);
            var cellRect = new Rect(rect.x + x * cellSize, rect.y + y * cellSize, cellSize - 2, cellSize - 2);
            EditorGUI.DrawRect(cellRect, mode == 0 && open ? Color.HSVToRGB((level * .16f) % 1, .6f, .8f) : tint);
            string unlock = level == 0 ? "×" : level == 1 ? "初始" : level + "级";
            GUI.Label(cellRect, new GUIContent(mode == 0 ? unlock : $"{bag}\n{unlock}", $"第{x + 1}列，第{y + 1}行 · 背包{bag}"), labelStyle);
            var e = Event.current;
            if (mode != 2 && e.button == 0 && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && cellRect.Contains(e.mousePosition))
            {
                (mode == 0 ? cells : bags).GetArrayElementAtIndex(index).intValue = mode == 0 ? paintLevel : paintCompartment;
                e.Use(); Repaint();
            }
            if (x + 1 < width && bag != bags.GetArrayElementAtIndex(index + 1).intValue)
                EditorGUI.DrawRect(new Rect(rect.x + (x + 1) * cellSize - 2, rect.y + y * cellSize, 4, cellSize), Color.white);
            if (y + 1 < height && bag != bags.GetArrayElementAtIndex(index + width).intValue)
                EditorGUI.DrawRect(new Rect(rect.x + x * cellSize, rect.y + (y + 1) * cellSize - 2, cellSize, 4), Color.white);
        }
        serializedObject.ApplyModifiedProperties();
        int shown = mode == 2 ? previewLevel : config.InitialLevel;
        var openCells = config.GetOpenCells(shown);
        if (config.CellCompartments.Any(id => id < 0))
        { EditorGUILayout.HelpBox("背包编号不能为负数，请重新涂色修复。", MessageType.Error); return; }
        var compartments = config.GetCompartments();
        EditorGUILayout.LabelField($"{shown}级：开放 {openCells.Count} / {count} 格 · {openCells.Select(c => compartments[c.X + c.Y * width]).Distinct().Count()} 个背包");
        foreach (int level in choices.Where(n => n >= 2))
            EditorGUILayout.LabelField($"{level}级：新增 {config.CellUnlockLevels.Count(n => n == level)} 格，累计 {config.GetOpenCells(level).Count} 格");
        if (config.CellUnlockLevels.Any(n => n < 0 || !choices.Contains(n)))
            EditorGUILayout.HelpBox("部分格子的等级没有对应升级档位，请补充档位或重新涂色。", MessageType.Warning);
        EditorGUILayout.HelpBox("隔板不占用格子。同一任务所有占用格必须属于同一个背包；可整体移动、旋转，不能横跨两个背包。修改配置后重开游戏生效。", MessageType.None);
    }

    private static void Resize(SerializedProperty property, int count)
    {
        int previous = property.arraySize; property.arraySize = count;
        for (int i = previous; i < count; i++) property.GetArrayElementAtIndex(i).intValue = 0;
    }
    private static void Split(SerializedProperty bags, int width, int height, int after, bool vertical)
    {
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            bags.GetArrayElementAtIndex(x + y * width).intValue = (vertical ? x : y) < after ? 0 : 1;
    }
}
