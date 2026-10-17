using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;

namespace CallmeCatgirl.EditorTools
{
    // 策划文本直接编译成剧情资源；运行时只使用资源，不解析源文件。
    public static class StoryTextImporter
    {
        private sealed class Session
        {
            public string Title;
            public int Line;
            public Dictionary<string, string> Settings;
            public readonly List<Round> Rounds = new List<Round>();
        }
        private sealed class Round
        {
            public string Name, Message;
            public readonly List<StoryConfiguration.ReplyChoice> Choices = new List<StoryConfiguration.ReplyChoice>();
        }
        private static readonly HashSet<string> Settings = new HashSet<string>
            { "任务模板", "计算量", "忍耐时间", "计算速度", "Token收入", "达标能力", "优秀能力", "方块" };
        private static readonly Dictionary<string, AbilityKind> Kinds = new Dictionary<string, AbilityKind>
            { { "写作", AbilityKind.Writing }, { "代码", AbilityKind.Code }, { "推理", AbilityKind.Reasoning }, { "检索", AbilityKind.Retrieval } };
        private static readonly Regex Option = new Regex(@"^选项\s*\d*\s*[：:](.+)$");
        private static readonly Regex RoundHeader = new Regex(@"^(第\d+轮|轮次)(?:[：:](.+))?$");

        public static StoryConfiguration Parse(string text, IEnumerable<string> requestIds, IDictionary<string, string> nameIds = null)
        {
            var known = new HashSet<string>(requestIds ?? throw new ArgumentNullException(nameof(requestIds)));
            var names = nameIds ?? new Dictionary<string, string> { { "林小满", "A" }, { "周序", "B" }, { "苏禾", "C" } };
            string name = null, id = null, history = null;
            var kind = StoryConfiguration.ConversationKind.Ordinary;
            var defaults = new Dictionary<string, string>(); var sessions = new List<Session>();
            Session session = null; Round round = null; StoryConfiguration.ReplyChoice choice = null;
            Func<string> currentText = null; Action<string> setText = null;
            Dictionary<string, string> shape = null;
            var assigned = new HashSet<string>();
            string[] lines = (text ?? string.Empty).TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim(); string where = "第" + (i + 1) + "行";
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
                if ((line.StartsWith("【") && line.EndsWith("】")) || (line.StartsWith("[") && line.EndsWith("]")))
                {
                    string header = line.Substring(1, line.Length - 2).Trim();
                    if (header.StartsWith("会话：") || header.StartsWith("会话:"))
                    {
                        Require(!string.IsNullOrWhiteSpace(name), where + "：请先填写角色。");
                        session = new Session { Title = header.Substring(3).Trim(), Line = i + 1, Settings = new Dictionary<string, string>(defaults) };
                        Require(session.Title.Length > 0, where + "：会话标题为空。");
                        sessions.Add(session); round = null;
                    }
                    else
                    {
                        var match = RoundHeader.Match(header);
                        Require(match.Success && session != null, where + "：标题应为【会话：标题】、【第1轮】或【轮次：名称】。");
                        string label = match.Groups[1].Value == "轮次" ? match.Groups[2].Value.Trim() : match.Groups[1].Value;
                        Require(label.Length > 0 && !label.Contains("/") && label != "下一轮" && label != "结束本次会话",
                            where + "：轮次名称为空或使用了保留名称。");
                        Require(!session.Rounds.Any(r => r.Name == label), where + "：同一次会话的轮次名称重复：" + label);
                        round = new Round { Name = label }; session.Rounds.Add(round);
                    }
                    choice = null; shape = null; currentText = null; setText = null; assigned.Clear(); continue;
                }
                var option = Option.Match(line);
                if (option.Success)
                {
                    Require(round != null, where + "：选项必须写在对话轮次内。");
                    choice = new StoryConfiguration.ReplyChoice { Label = option.Groups[1].Value.Trim(), PlayerReply = null, NextNodeId = "下一轮" };
                    round.Choices.Add(choice); shape = null; currentText = null; setText = null; assigned.Clear(); continue;
                }
                if (line == "结束本次会话")
                {
                    Require(choice != null, where + "：结束本次会话必须写在选项内。");
                    choice.NextNodeId = line; currentText = null; setText = null; shape = null; continue;
                }
                int colon = line.IndexOfAny(new[] { '：', ':' });
                string field = colon >= 0 ? line.Substring(0, colon).Trim() : null;
                string value = colon >= 0 ? line.Substring(colon + 1).Trim() : null;
                bool recognized = field != null && (Settings.Contains(field) || new[] { "角色", "角色ID", "角色类型", "过往对话", "用户", "玩家回复", "用户回应", "条件", "跳转到", "下一步" }.Contains(field));
                if (!recognized)
                {
                    if (shape != null)
                    {
                        Require(line.All(c => "■□#.".Contains(c)), where + "：方块只能使用 ■/□ 或 #/.，格子之间不要加空格。");
                        shape["方块"] += (shape["方块"].Length == 0 ? "" : "\n") + line; continue;
                    }
                    Require(setText != null, where + "：无法识别，请检查字段名称或先填写正文标签。");
                    setText(currentText() + "\n" + line); continue;
                }
                Require(assigned.Add(field), where + "：同一段内字段重复：" + field);
                shape = null; currentText = null; setText = null;
                if (field == "角色")
                {
                    Require(name == null && sessions.Count == 0 && value.Length > 0, where + "：一个文件只填写一个角色。"); name = value;
                }
                else if (field == "角色ID")
                { Require(session == null, where + "：角色ID必须在第一场会话之前填写。"); id = value; }
                else if (field == "角色类型")
                {
                    Require(session == null && (value == "普通" || value == "剧情"), where + "：角色类型须在会话前填写普通或剧情。");
                    kind = value == "剧情" ? StoryConfiguration.ConversationKind.Scripted : StoryConfiguration.ConversationKind.Ordinary;
                }
                else if (field == "过往对话")
                {
                    Require(session == null, where + "：过往对话必须在第一场会话之前填写。");
                    history = value; currentText = () => history; setText = v => history = v;
                }
                else if (Settings.Contains(field))
                {
                    Require(round == null, where + "：会话参数必须写在对话轮次之前。");
                    var settings = session?.Settings ?? defaults; settings[field] = value;
                    if (field == "方块") shape = settings;
                }
                else if (field == "用户")
                {
                    Require(round != null && choice == null, where + "：用户发言必须位于轮次标题之后、选项之前。");
                    round.Message = value; var target = round; currentText = () => target.Message; setText = v => target.Message = v;
                }
                else
                {
                    Require(choice != null, where + "：" + field + "必须写在选项内。");
                    var target = choice;
                    if (field == "玩家回复") { choice.PlayerReply = value; currentText = () => target.PlayerReply; setText = v => target.PlayerReply = v; }
                    else if (field == "用户回应") { choice.UserReply = value; currentText = () => target.UserReply; setText = v => target.UserReply = v; }
                    else if (field == "条件")
                    {
                        Require(new[] { "不限", "达标", "优秀" }.Contains(value), where + "：条件只能为不限、达标、优秀。");
                        choice.Requirement = value == "优秀" ? StoryConfiguration.ReplyRequirement.ExcellentOnly : value == "达标" ? StoryConfiguration.ReplyRequirement.CompleteOrBetter : StoryConfiguration.ReplyRequirement.Any;
                    }
                    else { Require(value.Length > 0, where + "：跳转目标为空。"); choice.NextNodeId = value; }
                }
            }
            Require(!string.IsNullOrWhiteSpace(name) && sessions.Count > 0, "文件需要角色名称和至少一次会话。");
            if (string.IsNullOrEmpty(id)) Require(names.TryGetValue(name, out id), "未知角色名称，请在文件开头填写角色ID：A（或主配置中对应的请求ID）。");
            Require(known.Contains(id), "角色ID没有对应任务模板：" + id);
            var user = new StoryConfiguration.UserStory { Kind = kind, UserId = id, DisplayName = name, InitialHistory = history,
                Sessions = new StoryConfiguration.ConversationSession[sessions.Count] };
            for (int s = 0; s < sessions.Count; s++)
            {
                var draft = sessions[s]; string location = "第" + draft.Line + "行的会话“" + draft.Title + "”";
                Require(draft.Rounds.Count > 0, location + "没有对话轮次。");
                user.Sessions[s] = new StoryConfiguration.ConversationSession { Id = "s" + (s + 1), Title = draft.Title,
                    RequestId = draft.Settings.TryGetValue("任务模板", out var template) ? template : id,
                    Task = Difficulty(draft.Settings, location), Rounds = new StoryConfiguration.StoryNode[draft.Rounds.Count] };
                for (int r = 0; r < draft.Rounds.Count; r++)
                {
                    var node = draft.Rounds[r]; Require(!string.IsNullOrWhiteSpace(node.Message), location + " / " + node.Name + "缺少用户发言。");
                    Require(node.Choices.Count > 0, location + " / " + node.Name + "至少需要一个选项。");
                    foreach (var item in node.Choices)
                    {
                        string next = item.NextNodeId;
                        if (next == "下一轮" && r + 1 < draft.Rounds.Count) next = "s" + (s + 1) + "/" + draft.Rounds[r + 1].Name;
                        else if (next == "下一轮" || next == "结束本次会话")
                        {
                            Require(s + 1 >= sessions.Count || sessions[s + 1].Rounds.Count > 0, location + "的下一次会话缺少轮次。");
                            next = s + 1 < sessions.Count ? "s" + (s + 2) + "/" + sessions[s + 1].Rounds[0].Name : null;
                        }
                        else next = "s" + (s + 1) + "/" + next;
                        item.NextNodeId = next;
                        if (string.IsNullOrEmpty(item.PlayerReply)) item.PlayerReply = item.Label;
                    }
                    user.Sessions[s].Rounds[r] = new StoryConfiguration.StoryNode { Id = "s" + (s + 1) + "/" + node.Name, UserMessage = node.Message, Choices = node.Choices.ToArray() };
                }
            }
            var config = ScriptableObject.CreateInstance<StoryConfiguration>();
            try { config.Users = new[] { user }; config.Validate(known); return config; }
            catch { UnityEngine.Object.DestroyImmediate(config); throw; }
        }
        public static void Apply(string text, StoryConfiguration target, IEnumerable<string> requestIds, IDictionary<string, string> nameIds = null)
        {
            var parsed = Parse(text, requestIds, nameIds);
            try { StoryJsonImporter.ApplyParsed(parsed, target, requestIds, false); }
            finally { UnityEngine.Object.DestroyImmediate(parsed); }
        }
        private static StoryConfiguration.RequestDifficulty Difficulty(Dictionary<string, string> data, string where)
        {
            var result = new StoryConfiguration.RequestDifficulty
            {
                Calculation = Number(data, "计算量", 0, where), Patience = Number(data, "忍耐时间", 0, where),
                Speed = Number(data, "计算速度", 0, where), Tokens = Number(data, "Token收入", -1, where)
            };
            if (data.TryGetValue("方块", out var shape)) result.Shape = shape.Split('\n');
            bool hasMinimum = data.TryGetValue("达标能力", out var min), hasExcellent = data.TryGetValue("优秀能力", out var excellent);
            Require(hasMinimum == hasExcellent, where + "：达标能力与优秀能力需要一起填写，或都省略以沿用模板。");
            if (hasMinimum)
            {
                var minimums = Abilities(min, where); var excellents = Abilities(excellent, where);
                Require(minimums.Keys.OrderBy(k => k).SequenceEqual(excellents.Keys.OrderBy(k => k)), where + "：达标能力和优秀能力必须包含相同的能力类型。");
                result.Abilities = minimums.Select(p => new StoryConfiguration.AbilityTarget { Kind = p.Key, Minimum = p.Value, Excellent = excellents[p.Key] }).ToArray();
            }
            try { result.Validate(); return result; }
            catch (Exception e) { throw new FormatException(where + "：" + e.Message, e); }
        }
        private static Dictionary<AbilityKind, float> Abilities(string text, string where)
        {
            var result = new Dictionary<AbilityKind, float>();
            foreach (var entry in text.Split(new[] { '，', ',', '、' }))
            {
                var match = Regex.Match(entry.Trim(), @"^(写作|代码|推理|检索)\s*[：:=]?\s*(.+)$");
                Require(match.Success, where + "：能力请写成“写作4，代码1，推理1”。");
                var kind = Kinds[match.Groups[1].Value]; Require(!result.ContainsKey(kind), where + "：能力类型重复。");
                result.Add(kind, Number(match.Groups[2].Value, where + "的能力"));
            }
            return result;
        }
        private static float Number(Dictionary<string, string> data, string key, float fallback, string where)
            => data.TryGetValue(key, out var text) ? Number(text, where + "的" + key) : fallback;
        private static float Number(string text, string where)
        {
            Require(float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0, where + "必须是非负有限数值（小数点使用 .）。");
            return value;
        }
        private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
    }
}
