using System;
using System.IO;
using System.Linq;
using System.Text;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.EditorTools;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StoryConfiguration))]
public sealed class StoryConfigurationEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("每个角色的会话次数、每次会话轮数、每轮选项数均独立配置。一次会话只计算一次，弹窗内连续选择直到会话结束。推荐使用中文策划文本：对白、数值和方块统一编辑。导入后重开生效。", MessageType.Info);
        if (GUILayout.Button("导入角色文本（推荐，保留其他角色）")) Import(false, true);
        if (GUILayout.Button("从角色文本创建新的剧情配置资源")) Create(true);
        if (GUILayout.Button("打开纯文字示例目录")) EditorUtility.RevealInFinder(Path.GetFullPath("Examples/StoryText"));
        EditorGUILayout.Space();
        if (GUILayout.Button("导入角色 JSON（保留其他角色）")) Import(false);
        if (GUILayout.Button("导入整份 JSON（替换全部角色）")) Import(true);
        if (GUILayout.Button("从 JSON 创建新的剧情配置资源")) Create();
        var config = (StoryConfiguration)target;
        foreach (var user in config.Users ?? Array.Empty<StoryConfiguration.UserStory>())
            if (user != null && user.HasSessions)
                EditorGUILayout.LabelField(user.DisplayName + " (" + user.UserId + ")", $"{user.Sessions.Length}次会话 / {user.AllRounds.Count()}轮 / {user.AllRounds.Sum(n => n.Choices?.Length ?? 0)}个选项");
        EditorGUILayout.Space(); DrawDefaultInspector();
    }
    public static string[] RequestIds()
    {
        var config = AssetDatabase.LoadAssetAtPath<GameplayConfiguration>("Assets/_Game/Data/TaskGrid/CoreGameplayConfiguration.asset");
        if (config == null) throw new InvalidOperationException("未找到CoreGameplayConfiguration.asset，请先配置任务模板。");
        return config.Requests.Select(r => r.Id).ToArray();
    }
    private static string PickJson() => EditorUtility.OpenFilePanel("导入对话JSON", Path.GetFullPath("Examples/StoryImport"), "json");
    private static string PickText() => EditorUtility.OpenFilePanelWithFilters("导入角色策划文本", Path.GetFullPath("Examples/StoryText"), new[] { "角色文本", "txt,md" });
    private static StoryConfiguration ParseFile(string path, bool text)
    {
        var content = File.ReadAllText(path, Encoding.UTF8);
        var main = AssetDatabase.LoadAssetAtPath<GameplayConfiguration>("Assets/_Game/Data/TaskGrid/CoreGameplayConfiguration.asset");
        var names = (main?.Story?.Users ?? Array.Empty<StoryConfiguration.UserStory>()).Where(u => u != null && !string.IsNullOrWhiteSpace(u.DisplayName))
            .GroupBy(u => u.DisplayName).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First().UserId);
        var parsed = text ? StoryTextImporter.Parse(content, RequestIds(), names) : StoryJsonImporter.Parse(content, RequestIds());
        try
        {
            if (main?.MemoryUpgrades != null)
                foreach (var session in parsed.Users.SelectMany(u => u.Sessions))
                    if (session.Task?.Shape != null && session.Task.Shape.Length > 0)
                    {
                        var cells = ShapePattern.Parse(session.Task.Shape);
                        int width = cells.Max(c => c.X) - cells.Min(c => c.X) + 1;
                        int height = cells.Max(c => c.Y) - cells.Min(c => c.Y) + 1;
                        if (!((width <= main.MemoryUpgrades.BoardWidth && height <= main.MemoryUpgrades.BoardHeight)
                            || (height <= main.MemoryUpgrades.BoardWidth && width <= main.MemoryUpgrades.BoardHeight)))
                            throw new InvalidOperationException("会话“" + session.Title + "”的方块尺寸超过棋盘。");
                    }
            return parsed;
        }
        catch { UnityEngine.Object.DestroyImmediate(parsed); throw; }
    }
    private void Import(bool replaceAll, bool text = false)
    {
        string path = text ? PickText() : PickJson(); if (string.IsNullOrEmpty(path)) return;
        try
        {
            var config = (StoryConfiguration)target;
            Undo.RegisterCompleteObjectUndo(config, "导入角色剧情");
            var parsed = ParseFile(path, text);
            try { StoryJsonImporter.ApplyParsed(parsed, config, RequestIds(), replaceAll); }
            finally { UnityEngine.Object.DestroyImmediate(parsed); }
            EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); serializedObject.Update();
            Debug.Log("角色剧情导入成功：" + Path.GetFileName(path), config);
        }
        catch (Exception e) { EditorUtility.DisplayDialog("导入失败，原配置未替换", e.Message, "确定"); }
    }
    private void Create(bool text = false)
    {
        string path = text ? PickText() : PickJson(); if (string.IsNullOrEmpty(path)) return;
        StoryConfiguration parsed = null;
        try
        {
            parsed = ParseFile(path, text);
            string destination = EditorUtility.SaveFilePanelInProject("保存剧情配置", "ImportedStoryConfiguration", "asset", "创建后将主配置中的剧情配置引用指向该资源。", "Assets/_Game/Data/TaskGrid");
            if (string.IsNullOrEmpty(destination)) return;
            AssetDatabase.CreateAsset(parsed, AssetDatabase.GenerateUniqueAssetPath(destination)); AssetDatabase.SaveAssets();
            Selection.activeObject = parsed; parsed = null;
        }
        catch (Exception e) { EditorUtility.DisplayDialog("创建失败", e.Message, "确定"); }
        finally { if (parsed != null) UnityEngine.Object.DestroyImmediate(parsed); }
    }
}
