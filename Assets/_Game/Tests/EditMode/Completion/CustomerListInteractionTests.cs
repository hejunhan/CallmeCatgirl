using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.Gameplay.Narrative;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CallmeCatgirl.Tests
{
    /// <summary>Play-mode checks for the ordinary customer list; the view lives in Assembly-CSharp, so access is reflective.</summary>
    public sealed class CustomerListInteractionTests
    {
        [UnityTest] public IEnumerator VisitorsOwnRowsWhilePinnedContactsStayFixed()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner=Find("CoreGameplayController");
            Call(owner,"ConfigureSaveSlot","customer-list-qa-"+Guid.NewGuid().ToString("N"));
            Get<TaskSimulation>(owner,"Simulation").Paused=true;
            CloseNarrative(Get<NarrativeSession>(owner,"Narrative")); yield return null;

            var scroll=GameObject.Find("ConversationScroll").GetComponent<ScrollRect>();
            var pinned=GameObject.Find("PinnedContacts").transform;
            var content=scroll.content;
            Assert.That(content.name,Is.EqualTo("ConversationRows"));
            Assert.That(pinned.IsChildOf(content),Is.False,"Pinned contacts must not scroll with ordinary rows.");
            Assert.That(content.childCount,Is.Zero,"The tutorial starts without ordinary customers.");
            Assert.That(pinned.GetComponentsInChildren<Button>(true).Length,Is.GreaterThan(0),"The pinned doctor row must exist.");

            var simulation=Get<TaskSimulation>(owner,"Simulation");
            AddVisitors(simulation,1,6,"顾客");
            yield return null;
            Assert.That(content.childCount,Is.EqualTo(6),"One row per ordinary visitor request.");
            Assert.That(Rows(content).Select(r=>RowText(r,"Name")).ToArray(),Is.EquivalentTo(new[]{"顾客1","顾客2","顾客3","顾客4","顾客5","顾客6"}));
            foreach(var row in Rows(content))
            {
                Assert.That(RowText(row,"Summary"),Does.Contain("亲密度").And.Contain("1 请求"),"Each row shows affinity and request count.");
                Assert.That(((RectTransform)row).rect.height,Is.EqualTo(112).Within(.01f),"Row height must stay stable for long text.");
            }

            AddVisitors(simulation,7,6,"顾客");
            yield return null;
            Assert.That(content.childCount,Is.EqualTo(12),"Rows are data-driven, not fixed to a hard-coded customer set.");
            Assert.That(content.rect.height,Is.GreaterThan(scroll.viewport.rect.height),"A full list must be scrollable.");

            scroll.verticalNormalizedPosition=1;
            scroll.OnScroll(new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-1)});
            Assert.That(scroll.verticalNormalizedPosition,Is.LessThan(.999f),"The mouse wheel scrolls the ordinary list.");

            scroll.verticalNormalizedPosition=1;
            var firstRow=(RectTransform)Rows(content)[0];
            var forwardedWheel=new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-1),
                position=RectTransformUtility.WorldToScreenPoint(null,firstRow.TransformPoint(firstRow.rect.center))};
            ExecuteEvents.Execute(firstRow.gameObject,forwardedWheel,ExecuteEvents.scrollHandler);
            Assert.That(scroll.verticalNormalizedPosition,Is.LessThan(.999f),"A row must forward the desktop wheel to the list ScrollRect.");

            var workspace=Find("ConversationWorkspaceView");
            var associationRow=Rows(content).Single(r=>r.name=="Conversation_filler-2-3");
            ExecuteEvents.Execute(associationRow.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);
            yield return null;
            Assert.That(LineCount(workspace),Is.EqualTo(1),"A filler row may only connect to its own request, not every filler block.");
            var canvas=Find("CoreGameplayCanvasView");
            var cards=(System.Collections.IDictionary)canvas.GetType().GetField("cards",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(canvas);
            var matchingCard=(MonoBehaviour)cards["filler-2-5"];
            ExecuteEvents.Execute(matchingCard.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);
            yield return null;
            Assert.That(LineCount(workspace),Is.EqualTo(1),"Hovering a task block may only connect to its matching row.");

            scroll.verticalNormalizedPosition=1;
            var drag=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=new Vector2(300,600)};
            drag.pressPosition=drag.position;
            scroll.OnBeginDrag(drag);
            drag.position=new Vector2(300,720);
            scroll.OnDrag(drag);
            scroll.OnEndDrag(drag);
            scroll.StopMovement();
            Assert.That(scroll.verticalNormalizedPosition,Is.LessThan(.999f),"Left-button dragging scrolls the ordinary list.");
            Assert.That(Get<bool>(owner,"IsModalOpen"),Is.False);
            Assert.That(Get<bool>(Find("CoreGameplayCanvasView"),"IsDragging"),Is.False,"List dragging must not start a board/task drag.");

            var pinnedBefore=((RectTransform)pinned.GetChild(0)).position;
            scroll.verticalNormalizedPosition=0;
            Assert.That((((RectTransform)pinned.GetChild(0)).position-pinnedBefore).magnitude,Is.LessThan(.001f),"Pinned contacts stay fixed at any scroll position.");

            var rowToClick=Rows(content).Single(r=>r.name=="Conversation_filler-2-3");
            Assert.That(ExecuteEvents.GetEventHandler<IBeginDragHandler>(rowToClick.gameObject),Is.SameAs(scroll.gameObject),
                "Row drags are handled by the list ScrollRect, never by a task card or board module.");
            Assert.That(EventSystem.current.pixelDragThreshold,Is.GreaterThan(0),"The pointer drag threshold must suppress accidental row clicks.");
            ExecuteEvents.Execute(rowToClick.gameObject,new PointerEventData(EventSystem.current)
                {button=PointerEventData.InputButton.Left,clickCount=1},ExecuteEvents.pointerClickHandler);
            Assert.That(Get<TaskInstance>(owner,"SelectedTask").Id,Is.EqualTo("filler-2-3"),"Clicking a row selects that visitor request.");
            yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator LongNamesAndRebuildsKeepScrollPosition()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner=Find("CoreGameplayController");
            Call(owner,"ConfigureSaveSlot","customer-list-long-"+Guid.NewGuid().ToString("N"));
            Get<TaskSimulation>(owner,"Simulation").Paused=true;
            CloseNarrative(Get<NarrativeSession>(owner,"Narrative")); yield return null;
            var simulation=Get<TaskSimulation>(owner,"Simulation");
            var scroll=GameObject.Find("ConversationScroll").GetComponent<ScrollRect>();
            var content=scroll.content;

            AddVisitors(simulation,1,12,"顾客");
            yield return null;
            string longName="一位名字特别长而且需要换行或者省略显示的普通顾客";
            AddVisitors(simulation,13,1,longName);
            yield return null;
            var row=Rows(content).Single(r=>r.name=="Conversation_filler-2-13");
            Assert.That(RowText(row,"Name"),Is.EqualTo(longName));
            var name=row.Find("Name").GetComponent<Text>();
            Assert.That(name.horizontalOverflow,Is.EqualTo(HorizontalWrapMode.Wrap));
            Assert.That(name.verticalOverflow,Is.EqualTo(VerticalWrapMode.Truncate));
            Assert.That(((RectTransform)row).rect.height,Is.EqualTo(112).Within(.01f),"Long names cannot grow the row.");

            scroll.verticalNormalizedPosition=.5f;
            AddVisitors(simulation,14,1,"顾客");
            yield return null;
            Assert.That(content.childCount,Is.EqualTo(14));
            Assert.That(scroll.verticalNormalizedPosition,Is.EqualTo(.5f).Within(.02f),"A rebuild preserves the scroll position.");
            yield return new ExitPlayMode();
        }

        private static void AddVisitors(TaskSimulation simulation,int start,int count,string namePrefix)
        {
            var definition=new TaskDefinition("customer-list-test","测试委托",new[]{new Cell(0,0)},10,1,600,12,
                new[]{new AbilityRequirement(AbilityKind.Writing,1,2)});
            for(int i=0;i<count;i++)
            {
                int serial=start+i;
                Assert.That(simulation.TryCreate("filler-2-"+serial,VisitorQueuePolicy.ConversationId,"filler-2-"+serial,
                    definition,new VisitorIdentity(namePrefix+(count==1?string.Empty:serial.ToString()),"帮我看看这段内容。","A","A"),out _),Is.True);
            }
        }
        private static Transform[] Rows(Transform content)=>content.Cast<Transform>().ToArray();
        private static int LineCount(object workspace)
        {
            var lines=workspace.GetType().GetField("lines",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(workspace);
            var paths=(System.Collections.IList)lines.GetType().GetField("paths",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(lines);
            return paths.Count;
        }
        private static string RowText(Transform row,string child)=>row.Find(child).GetComponent<Text>().text;
        private static object Find(string typeName)=>UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Single(component=>component.GetType().Name==typeName);
        private static T Get<T>(object instance,string name)
        {
            var type=instance.GetType();var property=type.GetProperty(name);
            return (T)(property!=null?property.GetValue(instance):type.GetField(name).GetValue(instance));
        }
        private static object Call(object instance,string name,params object[] args)
            => instance.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .Single(method=>method.Name==name&&method.GetParameters().Length==args.Length).Invoke(instance,args);
        private static void CloseNarrative(NarrativeSession narrative)
        {
            for(int i=0;i<8&&narrative.CurrentFrame!=null;i++)
            {
                if(narrative.CurrentFrame.CanClose&&narrative.CloseConversation())break;
                if(narrative.CurrentFrame.Choices.Count==0)break;
                Assert.That(narrative.Choose(narrative.CurrentFrame.Choices[0].Id),Is.True);
            }
        }
        [UnityTearDown] public IEnumerator Cleanup(){if(EditorApplication.isPlaying)yield return new ExitPlayMode();}
    }
}

