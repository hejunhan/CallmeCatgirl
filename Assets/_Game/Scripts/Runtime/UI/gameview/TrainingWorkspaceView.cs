using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.Training;
namespace CallmeCatgirl.UI
{
    /// <summary>Normal corpus and training controls; debug children are local, folded and development gated.</summary>
    public sealed class TrainingWorkspaceView : MonoBehaviour
    {
        private const float DesignScale=1000f/720f;
        private Font font;private CoreGameplayController owner;private TrainingController controller;
        private readonly HashSet<string> selected=new HashSet<string>();
        private string recipe,architecture;private InputField modelName;
        public static void Build(RectTransform parent,TrainingController source,Font font)
        {
            var root=new GameObject("TrainingWorkspace",typeof(RectTransform)).GetComponent<RectTransform>();root.SetParent(parent,false);
            root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;
            var view=root.gameObject.AddComponent<TrainingWorkspaceView>();view.font=font;view.controller=source;view.owner=source.GetComponent<CoreGameplayController>();view.Paint();
        }
        private RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y)*DesignScale;r.sizeDelta=new Vector2(w,h)*DesignScale;return r;
        }
        private Text Text(Transform p,string caption,float x,float y,float w,float h,int size=16)
        {
            var r=Rect(p,"Text",x,y,w,h);var t=r.gameObject.AddComponent<Text>();t.font=font;t.fontSize=Mathf.RoundToInt(size*DesignScale);t.color=Color.black;t.text=caption;t.raycastTarget=false;return t;
        }
        private Button Button(Transform p,string caption,float x,float y,float w,Action action)
        {
            var r=Rect(p,caption,x,y,w,32);r.gameObject.AddComponent<Image>().color=new Color(.87f,.9f,.91f);var b=r.gameObject.AddComponent<Button>();b.onClick.AddListener(()=>action());Text(r,caption,8,5,w-12,28);return b;
        }
        private RectTransform List(Transform p,string name,float x,float y,float w,float h)
        {
            var viewport=Rect(p,name,x,y,w,h);viewport.gameObject.AddComponent<Image>().color=new Color(.96f,.96f,.96f);viewport.gameObject.AddComponent<RectMask2D>();
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;
            var list=Rect(viewport,"Rows",0,0,w,0);list.anchorMin=new Vector2(0,1);list.anchorMax=new Vector2(1,1);list.sizeDelta=Vector2.zero;
            var layout=list.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=14;layout.padding=new RectOffset(11,11,11,11);layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandHeight=false;
            var fit=list.gameObject.AddComponent<ContentSizeFitter>();fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;scroll.viewport=viewport;scroll.content=list;return list;
        }
        private RectTransform Row(RectTransform list,float height)
        {
            var r=Rect(list,"Record",0,0,0,height);r.gameObject.AddComponent<LayoutElement>().preferredHeight=height*DesignScale;return r;
        }
        private void DebugDock(RectTransform row,string kind,string id,float y)
        {
            if(!CallmeCatgirl.Debugging.DebugBuild.Enabled)return;
            var slot=Rect(row,"InlineDebugSlot",0,y,500,270);
            var fold=LocalDebugFoldout.Attach(slot,owner.DebugCommands,kind,id,font);
            if(fold!=null)
            {
                var toggle=slot.GetComponentInChildren<Button>();toggle.onClick.AddListener(()=>row.GetComponent<LayoutElement>().preferredHeight=(y+8)*DesignScale+slot.GetComponent<LayoutElement>().preferredHeight);
            }
        }
        private void Paint()
        {
            var session=controller.Session;recipe=session.Recipes[0].Id;architecture=session.Architectures[0].Id;
            Text(transform,"语料库存",28,126,520,30,22);
            var records=List(transform,"Corpus",28,166,550,460);
            foreach(var record in session.Corpus)
            {
                var row=Row(records,110);bool consumed=session.IsConsumed(record.Id);
                Text(row,record.ConversationId+" / "+record.NodeId+" · "+(consumed?"已消耗":"可训练"),0,0,500,28);
                Text(row,string.Join("  ",record.Scores.Select(p=>CoreGameplayController.AbilityText(p.Key)+" "+p.Value)),0,30,500,28);
                var button=Button(row,"选择此语料",0,62,160,()=>{});button.interactable=!consumed;
                button.onClick.AddListener(()=>{if(!selected.Add(record.Id))selected.Remove(record.Id);button.GetComponentInChildren<Text>().text=selected.Contains(record.Id)?"已选择 ✓":"选择此语料";});
                DebugDock(row,"corpus",record.Id,102);
                if(CallmeCatgirl.Debugging.DebugBuild.Enabled)row.GetComponent<LayoutElement>().preferredHeight=138*DesignScale;
            }
            if(session.Corpus.Count==0)Text(records,"完成订单并接受回复后，合格语料会出现在这里。",0,0,510,80);
            Text(transform,"训练新模型",610,126,550,30,22);
            Text(transform,"新模型名称",610,166,500,24);
            var input=Rect(transform,"NewModelName",610,194,550,34);input.gameObject.AddComponent<Image>().color=new Color(.94f,.94f,.94f);
            modelName=input.gameObject.AddComponent<InputField>();var label=Text(input,"我的模型",8,5,530,28);modelName.textComponent=label;modelName.characterLimit=40;modelName.text="我的模型";
            Text(transform,"配方："+session.Recipes[0].Title+" · 费用 "+session.Recipes[0].Cost+" · 计算量 "+session.Recipes[0].WorkTokens,610,238,550,36);
            float x=610;foreach(var arch in session.Architectures)
            {
                var a=arch;var b=Button(transform,a.Title,x,278,178,()=>{architecture=a.Id;owner.SetMessage("训练架构："+a.Title);});
                var caption=b.GetComponentInChildren<Text>();caption.resizeTextForBestFit=true;caption.resizeTextMinSize=16;caption.resizeTextMaxSize=22;
                b.interactable=a.ContentReady && a.UnlockStage<=session.CurrentStage;x+=184;
            }
            Button(transform,"生成训练模块",610,322,240,()=>owner.ExecuteCommand(new GameCommand{kind="train",recipe=recipe,architecture=architecture,displayName=modelName.text,corpusIds=selected.ToArray()}));
            Button(transform,"升级算力",880,322,200,()=>owner.ExecuteCommand(new GameCommand{kind="upgradeCompute"}));
            Text(transform,"将训练模块放入棋盘运行",610,362,550,28);
            var jobs=List(transform,"TrainingJobs",610,398,550,228);
            foreach(var job in session.Jobs)
            {
                var row=Row(jobs,128);var task=controller.Simulation.Tasks[job.RequestId];
                Text(row,job.Output.Name+" · "+CoreGameplayController.StateText(task.State)+" · "+(task.Progress/task.Definition.Calculation*100).ToString("0")+"%",0,0,500,50);
                var done=Button(row,"确认训练产物",0,54,220,()=>owner.ExecuteCommand(new GameCommand{kind="submit",id=job.RequestId}));done.interactable=!job.Completed && task.State==CallmeCatgirl.Gameplay.TaskGrid.RequestState.Ready;
                DebugDock(row,"training",job.RequestId,96);
            }
        }
    }
}
