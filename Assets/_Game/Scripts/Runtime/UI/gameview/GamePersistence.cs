using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Gameplay.Training;
using CallmeCatgirl.Gameplay.Progression;
using CallmeCatgirl.Persistence;
using CallmeCatgirl.Persistence.Training;
using UnityEngine;

namespace CallmeCatgirl.Gameplay
{
    public sealed partial class CoreGameplayController : IRunSaveHost
    {
        private string runId,saveContentRevision;
        private string saveSlot="main";
        private RunSaveService saveService;
        private TrainingSaveExtension trainingSave;
        public static string SlotDirectory(string slot)
        {
            if (string.IsNullOrEmpty(slot) || !Regex.IsMatch(slot,@"\A[a-zA-Z0-9_-]{1,64}\z")) throw new ArgumentException("Invalid save slot.");
            return Path.Combine(Application.persistentDataPath,"GameJamSaves-v1",slot);
        }
        public string SavePath => Path.Combine(SlotDirectory(saveSlot),"run.json");
        private void InitializeSaveButtons()
        {
            var canvas=GameObject.Find("GameplayCanvas"); if(canvas==null)return;
            foreach(var button in canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                string command=button.name=="SaveRun"?"save":button.name=="LoadRun"?"restore":null;
                if(command==null)continue;button.onClick.RemoveAllListeners();
                button.onClick.AddListener(()=>ExecuteCommand(new GameCommand{kind=command}));
            }
        }
#if UNITY_EDITOR
        public void PrepareSaveButtons()
        {
            var canvas=GameObject.Find("GameplayCanvas");
            foreach(var entry in new[]{new[]{"SaveRun","保存游戏","470"},new[]{"LoadRun","读取存档","610"}})
                if(!canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true).Any(b=>b.name==entry[0]))
                    AddSaveButton(canvas.transform,entry[0],entry[1],float.Parse(entry[2]),()=>{});
        }
#endif
        private void AddSaveButton(Transform parent,string name,string title,float x,Action action)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button)); go.transform.SetParent(parent,false);
            var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(0,0);r.anchoredPosition=new Vector2(x,25);r.sizeDelta=new Vector2(125,38);
            go.GetComponent<UnityEngine.UI.Image>().color=new Color(.9f,.92f,.94f);go.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(()=>action());
            var child=new GameObject("Label",typeof(RectTransform),typeof(UnityEngine.UI.Text));child.transform.SetParent(go.transform,false);
            var rect=child.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var source=parent.GetComponentInChildren<UnityEngine.UI.Text>();var t=child.GetComponent<UnityEngine.UI.Text>();t.font=source?.font ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text=title;t.fontSize=18;t.color=Color.black;t.alignment=TextAnchor.MiddleCenter;
        }
        public void ConfigureSaveSlot(string slot) { SlotDirectory(slot); saveSlot=slot; saveService=null; }
        public string ContentRevision => saveContentRevision ?? (saveContentRevision = CalculateContentRevision(false));
        private string LegacyStoryV1ContentRevision => CalculateContentRevision(true);
        private string CalculateContentRevision(bool legacyStoryV1)
        {
            string content="gamejam-state-v1-xorshift32|";
            UnityEngine.Object[] sources={configuration,configuration.Story,configuration.OrdinaryDialoguePool,configuration.TaskGeneration,configuration.MemoryUpgrades,configuration.OpeningBalance,training?.Configuration};
            foreach(var source in sources) if(source != null)
            {
                string json=JsonUtility.ToJson(source);
                if(source==training?.Configuration)
                    json=Regex.Replace(json,@",""PointsPerExtraCell"":2(?:\.0+)?,""MaximumCorpusPoints"":20(?:\.0+)?","");
                if(legacyStoryV1 && source==configuration.Story) json=RunSaveValidation.LegacyStoryV1ConfigurationJson(json);
                content+=Regex.Replace(json,@"""instanceID""\s*:\s*-?\d+","\"instanceID\":0");
            }
            using(var hash=SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(content))).Replace("-","");
        }
        private void EnsureSaveService()
        {
            if(saveService != null) return;
            if(!TrainingEnabled) throw new InvalidOperationException("Training must be bound before saving.");
            var codec=new UnitySaveJsonCodec(); var session=training.Session;
            trainingSave=new TrainingSaveExtension(codec,()=>training.Session,session.Recipes,session.Architectures,session.ComputeTiers);
            var handlers=new IRunSaveExtension[]{trainingSave};
            saveService=new RunSaveService(this,new AtomicRunSaveStore(SlotDirectory(saveSlot),codec,ContentRevision,handlers,LegacyStoryV1ContentRevision),handlers);
        }
        public bool SaveRun()
        {
            if (IsMemoryInstallOpen) { Message="请先完成或跳过内存安装，再保存。"; return false; }
            try { ReadEvents(); EnsureSaveService(); saveService.Save(); Message="游戏已保存。"; return true; }
            catch(Exception error) { Message="保存失败："+error.Message; return false; }
        }
        public bool ContinueRun()
        {
            try { EnsureSaveService(); saveService.Continue(); Message="存档已恢复。"; return true; }
            catch(Exception error) { Message="读取失败："+error.Message; return false; }
        }
        public RunSaveData CaptureRun()
        {
            var policy=GetComponent<ProgressionController>(); policy.EnsureBound();
            return new RunSaveData {SchemaVersion=1,RunId=runId,ContentRevision=ContentRevision,SavedAtUtc=DateTimeOffset.UtcNow.ToString("O"),
                Simulation=Simulation.CaptureState(),Economy=Economy.CaptureState(),Story=Story.CaptureState(),Policies=policy.Tree.Capture(),Extensions=Array.Empty<SaveExtensionState>(),
                Host=new HostState {Version=2,CurrentMonth=CurrentMonth,MonthElapsed=monthTimer,SecondsPerMonth=configuration.SecondsPerGameMonth,
                    MonthlyMaintenanceFee=configuration.MonthlyMaintenanceFee,PlaybackSpeed=PlaybackSpeed,MemoryLevel=MemoryLevel,CompletedTasks=CompletedTasks,
                    AvailableAbilityPoints=AvailableAbilityPoints,RequestSerial=serial,SelectedRequestId=SelectedId,GameEnded=GameEnded,EndingText=EndingText,
                    OperatingRequestIds=operatingTasks.OrderBy(id=>id,StringComparer.Ordinal).ToArray(),
                    MemoryUpgradeOpen=IsMemoryUpgradeOpen,GenerationRandom=((SaveableRandom)requestRandom).Capture(),
                    TaskBases=taskBases.Select(p=>new TaskBasisState{RequestId=p.Key,Definition=DefinitionState.Capture(p.Value)}).ToArray()}};
        }
        public IPreparedRunRestore PrepareRestore(RunSaveData data)
        {
            EnsureSaveService(); var host=data.Host;
            if(host == null || host.SecondsPerMonth != configuration.SecondsPerGameMonth || host.MonthlyMaintenanceFee != configuration.MonthlyMaintenanceFee)
                throw new SaveCompatibilityException("Clock configuration differs.");
            var policy=GetComponent<ProgressionController>();
            var restored=RunRestoreBuilder.Build(data,ContentRevision,configuration.Story,configuration.TaskGeneration,policy.Nodes,SaveableRandom.Restore,Story.Revision,new[]{trainingSave},LegacyStoryV1ContentRevision,configuration.OrdinaryDialoguePool,configuration.InitialAffinity,configuration.SuccessAffinity,configuration.FailureAffinity);
            if(host.MemoryLevel<configuration.MemoryUpgrades.InitialLevel || host.MemoryLevel>5) throw new ArgumentException("Invalid stage.");
            var session=trainingSave.PrepareRestore(data.Extensions.Single(e=>e.Id==TrainingSaveExtension.ExtensionId),restored.Simulation,
                restored.Economy.TrySpendFunds,restored.Economy.AddFunds,
                ()=>configuration.MemoryUpgrades.GetOpenCells(ReferenceEquals(Simulation,restored.Simulation)?MemoryLevel:host.MemoryLevel),
                restoredStage:()=>ReferenceEquals(Simulation,restored.Simulation)?MemoryLevel:host.MemoryLevel);
            var active=session.Models.Single(m=>m.Id==session.ActiveModelId);
            var expected=session.Architectures.Single(a=>a.Id==active.ArchitectureId).Transform(configuration.MemoryUpgrades.GetOpenCells(host.MemoryLevel),restored.Simulation.Width,restored.Simulation.Height);
            var actual=Enumerable.Range(0,restored.Simulation.Width*restored.Simulation.Height).Select(i=>new Cell(i%restored.Simulation.Width,i/restored.Simulation.Width)).Where(restored.Simulation.IsOpen).ToArray();
            if(!new System.Collections.Generic.HashSet<Cell>(expected).SetEquals(actual)) throw new ArgumentException("Saved model outline differs from stage capacity.");
            foreach(string id in restored.Simulation.Tasks.Keys)
                if((id.StartsWith("new-") || id.StartsWith("round-")) && int.TryParse(id.Substring(id.IndexOf('-')+1),out int number) && number>host.RequestSerial)
                    throw new ArgumentException("Request serial would reuse an existing ID.");
            return new PreparedRestore(()=>{
                memoryInstallGame?.ResetGame();
                Economy.OnEconomyLog-=AddEconomyLog; training.Changed-=RefreshTraining;
                Simulation=restored.Simulation; Economy=restored.Economy; Story=restored.Story; requestRandom=restored.GenerationRandom;
                Economy.OnEconomyLog+=AddEconomyLog;
                operatingTasks.Clear(); foreach(var id in host.OperatingRequestIds ?? Array.Empty<string>()) operatingTasks.Add(id);
                taskBases.Clear(); foreach(var pair in restored.TaskBases) taskBases.Add(pair.Key,pair.Value);
                CurrentMonth=host.CurrentMonth;monthTimer=host.MonthElapsed;PlaybackSpeed=host.PlaybackSpeed;MemoryLevel=host.MemoryLevel;
                CompletedTasks=host.CompletedTasks;AvailableAbilityPoints=host.AvailableAbilityPoints;serial=host.RequestSerial;SelectedId=host.SelectedRequestId;
                GameEnded=host.GameEnded;EndingText=host.EndingText;IsMemoryUpgradeOpen=host.MemoryUpgradeOpen;runId=data.RunId;
                policy.BindRestoredTree(Simulation,restored.Policies);
                try { training.BindRestoredSession(Simulation,session,()=>!GameEnded&&!IsMemoryUpgradeOpen&&!IsMemoryInstallOpen); }
                catch(Exception error){Debug.LogWarning("Restored training observer: "+error.Message);}
                training.Changed+=RefreshTraining; observedState=null; BindDebugCommands();
                try {trainingPanel?.Close();canvasView?.CancelDrag();canvasView?.Initialize(this);canvasView?.SetManualPlanningVisual(Simulation.Paused);canvasView?.Refresh();}
                catch(Exception error){Debug.LogWarning("Restored view: "+error.Message);}
            });
        }
        private sealed class PreparedRestore : IPreparedRunRestore
        {
            private Action commit; public PreparedRestore(Action action){commit=action;}
            public void Commit(){var action=commit ?? throw new InvalidOperationException("Restore already consumed.");commit=null;action();}
            public void Dispose(){commit=null;}
        }
    }
}
