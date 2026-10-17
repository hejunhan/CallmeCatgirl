using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CallmeCatgirl.Gameplay.Training
{
    /// <summary>Dedicated model plaza view; no extra scene, clock, or main-console ID label.</summary>
    public sealed class ModelPlazaPanel : MonoBehaviour
    {
        [SerializeField] private TrainingController controller;
        public bool IsOpen { get; private set; }
        private Vector2 scroll;
        private TrainingSession bound;
        private readonly Dictionary<string,string> renameInputs = new Dictionary<string,string>();
        private readonly HashSet<string> details = new HashSet<string>();
        public void Open() => IsOpen = true;
        public void Close() => IsOpen = false;
        private void OnGUI()
        {
            if (!IsOpen || controller == null || !controller.IsBound) return;
            if (!ReferenceEquals(bound,controller.Session)) { bound=controller.Session;renameInputs.Clear();details.Clear(); }
            float width=Mathf.Min(740,Screen.width-24),height=Mathf.Min(760,Screen.height-24);
            var bounds=new Rect((Screen.width-width)/2,12,width,height);
            var nameStyle=new GUIStyle(GUI.skin.label) { fontSize=20,fontStyle=FontStyle.Bold };
            var idStyle=new GUIStyle(GUI.skin.label) { fontSize=10 };
            GUILayout.BeginArea(bounds,GUI.skin.box);
            GUILayout.BeginHorizontal();GUILayout.Label("模型广场");
            if(GUILayout.Button("关闭",GUILayout.Width(65)))Close();GUILayout.EndHorizontal();
            GUILayout.Label(controller.Message);
            scroll=GUILayout.BeginScrollView(scroll);
            foreach(var model in bound.Models)
            {
                var architecture=bound.Architectures.First(a=>a.Id==model.ArchitectureId);
                var availability=bound.ArchitectureAvailability(model.ArchitectureId);
                GUILayout.BeginVertical(GUI.skin.box);
                // Required hierarchy: name, immediately its small Model ID, then attributes/speed/architecture.
                GUILayout.Label(model.DisplayName,nameStyle);
                GUILayout.Label("MODEL ID · "+model.ShortModelId,idStyle);
                GUILayout.Label("属性 · "+string.Join(" / ",model.Definition.Points.Select(p=>p.Key+":"+p.Value.ToString("0.##")))
                    +"（每 "+model.Definition.Interval.ToString("0.##")+"s）");
                GUILayout.Label("速度 · "+bound.PreviewTokensPerSecond(model.Id).ToString("0.##")+" token/s /任务（当前算力与政策）");
                GUILayout.Label("架构 · "+architecture.Title+" · 轮廓 "+architecture.Outline+" · 阶段 "+architecture.UnlockStage);
                if(!string.IsNullOrEmpty(architecture.TechnicalDescription))GUILayout.Label(architecture.TechnicalDescription);
                if(!renameInputs.ContainsKey(model.Id))renameInputs.Add(model.Id,model.DisplayName);
                GUI.enabled=controller.CanManage;
                GUILayout.BeginHorizontal();renameInputs[model.Id]=GUILayout.TextField(renameInputs[model.Id],ModelNames.MaximumLength);
                if(GUILayout.Button("改名",GUILayout.Width(60)))controller.RenameModel(model.Id,renameInputs[model.Id]);GUILayout.EndHorizontal();
                bool expanded=GUILayout.Toggle(details.Contains(model.Id),"详情 / 完整 Model ID");
                if(expanded)details.Add(model.Id);else details.Remove(model.Id);
                if(expanded)
                {
                    GUILayout.TextField(model.ModelId);
                    if(GUILayout.Button("复制完整 Model ID"))GUIUtility.systemCopyBuffer=model.ModelId;
                }
                GUI.enabled=controller.CanManage && controller.Simulation.Paused && availability==TrainingResult.Success && bound.ActiveModelId!=model.Id;
                if(GUILayout.Button(bound.ActiveModelId==model.Id?"当前模型":"选择模型"))controller.SelectModel(model.Id);
                GUI.enabled=true;GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
            if(bounds.Contains(Event.current.mousePosition)&&Event.current.isMouse)Event.current.Use();
        }
    }
}
