using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    /// <summary>Compact fallback management UI. Wire Open to the server drawer; production art can replace this view.</summary>
    public sealed class TrainingPanel : MonoBehaviour
    {
        [SerializeField] private TrainingController controller;
        [SerializeField] private bool showFallbackEntry = true;
        public bool IsOpen { get; private set; }
        private Vector2 scroll;
        private readonly HashSet<string> selected = new HashSet<string>();
        private string nameInput = "我的模型", recipeId, architectureId;
        private TrainingSession bound;
        [SerializeField] private CallmeCatgirl.UI.ModelGalleryView gallery;
        public void Open()
        {
            IsOpen = true;
            if (gallery == null) gallery = GetComponent<CallmeCatgirl.UI.ModelGalleryView>();
            if (gallery == null) throw new System.InvalidOperationException("Model gallery must be authored in the scene.");
            gallery.Open(controller, this);
        }
        public void Close() { IsOpen = false; if (gallery != null) gallery.Close(); }
        private void OnGUI()
        {
            if (controller == null || !controller.IsBound) return;
            if (gallery != null && gallery.IsVisible) return;
            if (!ReferenceEquals(bound, controller.Session))
            {
                bound = controller.Session; selected.Clear();
                recipeId = bound.CompactTrainingAvailable ? TrainingSession.TutorialCorpusRecipeId
                    : bound.StarterTrainingAvailable ? TrainingSession.StarterRecipeId : bound.Recipes[0].Id;
                architectureId = bound.Architectures[0].Id;
            }
            if (!IsOpen)
            {
                if (showFallbackEntry && GUI.Button(new Rect(12, 12, 150, 34), "服务器 · 训练 / 模型")) Open();
                return;
            }
            float width = Mathf.Min(760, Screen.width - 24), height = Mathf.Min(760, Screen.height - 24);
            var bounds = new Rect((Screen.width - width) / 2, 12, width, height);
            GUILayout.BeginArea(bounds, GUI.skin.box);
            GUILayout.BeginHorizontal(); GUILayout.Label("服务器抽屉 · 语料训练 / 模型目录");
            if (GUILayout.Button("关闭", GUILayout.Width(65))) Close(); GUILayout.EndHorizontal();
            GUILayout.Label("语料→能力；算力×架构×指定政策→每任务 token/s（并发不分摊，倍率暂定）");
            GUILayout.Label(controller.Simulation.Paused ? "已暂停，可选择模型。" : "换模型与升级算力前，请先暂停。");
            GUILayout.Label(controller.Message);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("语料库存（完成请求并接受回复后记录；每片段仅消费一次）");
            foreach (var record in bound.Corpus)
            {
                bool used = bound.IsConsumed(record.Id);
                GUI.enabled = !used;
                bool on = GUILayout.Toggle(selected.Contains(record.Id), record.Id + " · " + record.ConversationId
                    + "/" + record.NodeId + " · " + Points(record.Scores) + (used ? " [已消耗]" : "") + " · " + record.ScoreSource);
                if (on && !used) selected.Add(record.Id); else selected.Remove(record.Id);
            }
            GUI.enabled = controller.CanManage;
            GUILayout.Label("新模型名称（最多40字）"); nameInput = GUILayout.TextField(nameInput, 40);
            foreach (var recipe in bound.Recipes)
                if (GUILayout.Toggle(recipeId == recipe.Id, recipe.Title + " · 基础费用 " + recipe.Cost + " · 工作量 "
                    + recipe.WorkTokens + " · 占格 " + recipe.Shape.Count + "（政策修正以实际扣款为准）")) recipeId = recipe.Id;
            foreach (var architecture in bound.Architectures)
                if (GUILayout.Toggle(architectureId == architecture.Id, architecture.Title + " · 速度×" + architecture.SpeedMultiplier
                    + " · 轮廓 " + architecture.Outline)) architectureId = architecture.Id;
            if (GUILayout.Button("冻结所选语料并创建训练模块（需到棋盘摆放）"))
                controller.StartTraining(recipeId, architectureId, nameInput, selected.ToArray());
            GUILayout.Label("训练状态");
            foreach (var job in bound.Jobs)
            {
                if (!controller.Simulation.Tasks.TryGetValue(job.RequestId, out var task)) continue;
                GUILayout.Label(job.RequestId + " · " + job.Output.Name + " · " + task.State + " · "
                    + (100 * task.Progress / task.Definition.Calculation).ToString("0") + "%");
                GUI.enabled = controller.CanManage && !job.Completed && task.State == RequestState.Ready;
                if (GUILayout.Button("确认训练产物 · " + job.Output.Name)) controller.CompleteTraining(job.RequestId);
            }
            GUI.enabled = controller.CanManage && controller.Simulation.Paused;
            GUILayout.Label(bound.Models.Count == 0
                ? "尚无模型。完成第一次训练后会创建第一台模型。"
                : "模型目录（旧模型保留；换模后全部在场模块退候选，已判质结果不变）");
            foreach (var model in bound.Models)
            {
                GUILayout.Label(model.Name + " · " + model.ArchitectureId + " · 每 " + model.Definition.Interval
                    + "s 产出 " + Points(model.Definition.Points) + (bound.ActiveModelId == model.Id ? " [当前]" : ""));
                if (GUILayout.Button("选择 " + model.Name)) controller.SelectModel(model.Id);
            }
            GUILayout.Label("算力 " + bound.Compute.Id + " · 当前 " + bound.TokensPerSecond.ToString("0.##")
                + " token/s /任务" + (bound.ActiveModelId == null ? "（尚无模型，使用基础算力）" : ""));
            if (bound.NextCompute != null && GUILayout.Button("升级算力 · " + bound.NextCompute.TokensPerSecond
                + " 基础token/s · 费用 " + bound.NextCompute.UpgradeCost)) controller.UpgradeCompute();
            GUI.enabled = true; GUILayout.EndScrollView(); GUILayout.EndArea();
            // Avoid clicks falling through into the gameplay canvas beneath this fallback window.
            if (bounds.Contains(Event.current.mousePosition) && Event.current.isMouse) Event.current.Use();
        }
        private static string Points(IReadOnlyDictionary<AbilityKind, double> values)
            => string.Join(" / ", values.Select(p => p.Key + ":" + p.Value.ToString("0.##")));
    }
}
