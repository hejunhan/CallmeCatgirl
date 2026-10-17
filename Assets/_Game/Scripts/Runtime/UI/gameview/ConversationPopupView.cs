using System;
using System.Linq;
using System.Collections.Generic;
using CallmeCatgirl.Gameplay;
using UnityEngine;
using UnityEngine.UI;
namespace CallmeCatgirl.UI
{
 public sealed class ConversationPopupView:MonoBehaviour
 {
  [SerializeField] private GameObject panel;
  [SerializeField] private Text userLabel,resultLabel,historyLabel;
  [SerializeField] private Image avatar;
  [SerializeField] private ScrollRect historyScroll;
  [SerializeField] private RectTransform choiceContent;
  [SerializeField] private Button choiceTemplate;
  [SerializeField] private ScrollRect choiceScroll;
  [SerializeField] private Button closeButton;
  [SerializeField] private Sprite windowBacking,incomingBubble,outgoingBubble,replyIdle,replyHover,replyPressed,replyDisabled,closeIcon,selfPortrait,avatarFrame;
  [SerializeField] private RectTransform incomingTemplate,outgoingTemplate,systemTemplate;
  private readonly List<Button> buttons=new List<Button>();
  private ConversationStory displayed;
  private int revision=-1;
  private bool dismissed;
  private bool readyChoices;
  public void Reveal(){dismissed=false;}
  public void Refresh(CoreGameplayController owner,Sprite portrait,UIFeedbackConfiguration feedback=null)
  {
   var story=owner.Story;var options=owner.ConversationReplyChoices;bool ready=options.Count>0;bool changed=story!=displayed||revision!=story.Revision||ready!=readyChoices;
   if(changed)dismissed=false;
   panel.SetActive(story.IsOpen&&!dismissed);
   if(!story.IsOpen||dismissed)return;
   choiceScroll.gameObject.SetActive(ready);Canvas.ForceUpdateCanvases();if(!changed)return;
   displayed=story;revision=story.Revision;readyChoices=ready;
   userLabel.text=story.Name(story.UserId)+$" · 亲密度 {story.Affinity(story.UserId):0.##}";
   resultLabel.text=story.IsReply?story.RequestId+" · "+CoreGameplayController.QualityText(story.Result)+" · "+story.Progress(story.UserId):story.Progress(story.UserId);
   avatar.sprite=portrait;avatar.gameObject.SetActive(false);
   historyLabel.text=story.History(story.UserId);RenderHistory(historyLabel.text,portrait);
   closeButton.onClick.RemoveAllListeners();closeButton.onClick.AddListener(()=>{if(story.IsReply){dismissed=true;panel.SetActive(false);}else owner.CloseConversationHistory();});
   foreach(var b in buttons){b.gameObject.SetActive(false);Destroy(b.gameObject);}buttons.Clear();choiceTemplate.gameObject.SetActive(false);
   if(ready)for(int i=0;i<options.Count;i++)
   {
    int index=i,expected=story.Revision;var b=Instantiate(choiceTemplate,choiceContent);b.name="ReplyChoice_"+i;
    var text=b.GetComponentInChildren<Text>(true);text.text=options[i].Label;
    var element=b.GetComponent<LayoutElement>();if(element==null)element=b.gameObject.AddComponent<LayoutElement>();element.preferredHeight=Mathf.Max(choiceTemplate.GetComponent<LayoutElement>().preferredHeight,text.preferredHeight+24);
    b.onClick.RemoveAllListeners();b.onClick.AddListener(()=>owner.ChooseReplyAtRevision(index,expected));b.gameObject.SetActive(true);buttons.Add(b);
   }
   Canvas.ForceUpdateCanvases();historyScroll.verticalNormalizedPosition=0;choiceScroll.verticalNormalizedPosition=1;
  }
#if UNITY_EDITOR
  public void PrepareSceneLayout()
  {
   if(incomingTemplate!=null)return;
   bool visible=panel.activeSelf;panel.SetActive(true);LayoutWindow(null,true);
   var window=(RectTransform)userLabel.transform.parent;
   var body=Rect("MessageBody",window);Stretch(body,16,16,16,108);
   var stack=body.gameObject.AddComponent<VerticalLayoutGroup>();stack.spacing=16;stack.childControlWidth=stack.childControlHeight=true;stack.childForceExpandHeight=false;
   historyScroll.transform.SetParent(body,false);choiceScroll.transform.SetParent(body,false);
   var historySize=historyScroll.gameObject.GetComponent<LayoutElement>()??historyScroll.gameObject.AddComponent<LayoutElement>();
   historySize.minHeight=100;historySize.flexibleHeight=1;
   var choiceSize=choiceScroll.gameObject.GetComponent<LayoutElement>()??choiceScroll.gameObject.AddComponent<LayoutElement>();choiceSize.preferredHeight=240;
   var rows=historyScroll.content.GetComponent<VerticalLayoutGroup>()??historyScroll.content.gameObject.AddComponent<VerticalLayoutGroup>();
   rows.spacing=12;rows.childControlWidth=rows.childControlHeight=true;rows.childForceExpandHeight=false;rows.padding=new RectOffset(8,8,8,8);
   var fit=historyScroll.content.GetComponent<ContentSizeFitter>()??historyScroll.content.gameObject.AddComponent<ContentSizeFitter>();fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
   historyLabel.enabled=false;
   var templates=Rect("ConversationTemplates",window);templates.gameObject.SetActive(false);
   incomingTemplate=BuildMessageTemplate(templates,false,false);incomingTemplate.name="IncomingMessageTemplate";
   outgoingTemplate=BuildMessageTemplate(templates,true,false);outgoingTemplate.name="OutgoingMessageTemplate";
   systemTemplate=BuildMessageTemplate(templates,false,true);systemTemplate.name="SystemMessageTemplate";
   var image=choiceTemplate.GetComponent<Image>();image.sprite=replyIdle;image.type=Image.Type.Sliced;image.color=Color.white;
   choiceTemplate.targetGraphic=image;choiceTemplate.transition=Selectable.Transition.SpriteSwap;
   choiceTemplate.spriteState=new SpriteState{highlightedSprite=replyHover,selectedSprite=replyHover,pressedSprite=replyPressed,disabledSprite=replyDisabled};
   var text=choiceTemplate.GetComponentInChildren<Text>(true);text.fontSize=26;text.resizeTextForBestFit=false;text.color=new Color32(22,22,22,255);
   var size=choiceTemplate.GetComponent<LayoutElement>()??choiceTemplate.gameObject.AddComponent<LayoutElement>();size.preferredHeight=64;
   choiceTemplate.gameObject.SetActive(false);
   Canvas.ForceUpdateCanvases();RenderHistory("[对话记录]\n\n联系人：用户消息预览\n\n我：回复消息预览",null);
   panel.SetActive(visible);
  }
  private RectTransform BuildMessageTemplate(Transform parent,bool outgoing,bool system)
  {
   var row=Rect("MessageTemplate",parent);row.anchorMin=new Vector2(0,1);row.anchorMax=new Vector2(1,1);row.pivot=new Vector2(.5f,1);row.sizeDelta=new Vector2(0,64);
   var height=row.gameObject.AddComponent<LayoutElement>();height.minHeight=system?36:48;height.preferredHeight=system?36:64;
   if(system){var status=Label(row,"系统提示",22);status.alignment=TextAnchor.MiddleCenter;Stretch(status.rectTransform,0,0,0,0);return row;}
   var bubble=Rect(outgoing?"Outgoing":"Incoming",row);bubble.anchorMin=Vector2.zero;bubble.anchorMax=Vector2.one;
   bubble.offsetMin=new Vector2(outgoing?40:60,0);bubble.offsetMax=new Vector2(outgoing?-60:-40,0);
   var image=bubble.gameObject.AddComponent<Image>();image.sprite=outgoing?outgoingBubble:incomingBubble;image.type=Image.Type.Sliced;image.color=Color.white;image.raycastTarget=false;
   var body=Label(bubble,"消息内容",26);Stretch(body.rectTransform,12,12,12,12);InnerBorderGraphic.Attach(bubble,1,BorderEdges.All,new Color32(111,111,111,255));
   var face=Rect("Avatar",row);face.anchorMin=face.anchorMax=face.pivot=new Vector2(outgoing?1:0,1);face.sizeDelta=new Vector2(48,48);
   var frame=face.gameObject.AddComponent<Image>();frame.sprite=avatarFrame;frame.type=Image.Type.Sliced;frame.raycastTarget=false;
   var p=Rect("Portrait",face);Stretch(p,3,3,3,3);var portrait=p.gameObject.AddComponent<Image>();portrait.sprite=outgoing?selfPortrait:null;portrait.preserveAspect=true;portrait.raycastTarget=false;
   return row;
  }
  private void LayoutWindow(ConversationStory story,bool ready)
  {
   var overlay=panel.GetComponent<Image>();if(overlay!=null){overlay.color=Color.clear;overlay.raycastTarget=false;}
   var window=(RectTransform)userLabel.transform.parent;window.anchorMin=window.anchorMax=window.pivot=Vector2.one;window.anchoredPosition=new Vector2(-48,-136);window.sizeDelta=new Vector2(640,680);
   var paper=window.GetComponent<Image>();paper.raycastTarget=true;paper.sprite=windowBacking;paper.type=Image.Type.Sliced;paper.color=new Color32(244,244,244,255);
   var border=window.GetComponent<Outline>();if(border!=null)border.enabled=false;InnerBorderGraphic.Attach(window,1,BorderEdges.All,new Color32(82,82,82,255));
   var artwork=window.Find("WindowArtwork");if(artwork!=null)artwork.gameObject.SetActive(false);
   var hint=window.Find("ChoiceHint");if(hint!=null)hint.gameObject.SetActive(false);
   Place(userLabel.rectTransform,20,12,540,40);userLabel.fontSize=28;userLabel.color=new Color32(22,22,22,255);
   Place(resultLabel.rectTransform,20,58,588,40);resultLabel.fontSize=22;resultLabel.resizeTextForBestFit=false;
   closeButton.gameObject.SetActive(true);Place((RectTransform)closeButton.transform,584,12,44,44);
   var closeImage=closeButton.GetComponent<Image>();closeImage.sprite=closeIcon;closeImage.type=Image.Type.Simple;closeImage.color=Color.white;
   foreach(Transform child in closeButton.transform)child.gameObject.SetActive(false);
   bool replies=ready;float replyHeight=replies?240:0;
   choiceScroll.gameObject.SetActive(replies);
   var choices=(RectTransform)choiceScroll.transform;choices.anchorMin=new Vector2(0,0);choices.anchorMax=new Vector2(1,0);choices.pivot=new Vector2(.5f,0);choices.anchoredPosition=new Vector2(0,16);choices.sizeDelta=new Vector2(-32,replyHeight);
   var history=(RectTransform)historyScroll.transform;history.anchorMin=Vector2.zero;history.anchorMax=Vector2.one;history.offsetMin=new Vector2(16,replies?replyHeight+32:16);history.offsetMax=new Vector2(-16,-108);
  }
#endif
  private void RenderHistory(string history,Sprite portrait)
  {
   var content=historyScroll.content;
   foreach(var child in content.Cast<Transform>().ToArray()){child.gameObject.SetActive(false);if(Application.isPlaying)Destroy(child.gameObject);else DestroyImmediate(child.gameObject);}
   foreach(string entry in history.Split(new[]{"\n\n"},StringSplitOptions.RemoveEmptyEntries))
   {
    bool outgoing=entry.StartsWith("我："),system=entry.StartsWith("[");string text=entry;int colon=text.IndexOf('：');if(!system&&colon>=0)text=text.Substring(colon+1);
    var row=Instantiate(system?systemTemplate:outgoing?outgoingTemplate:incomingTemplate,content);row.name="Message";row.gameObject.SetActive(true);
    var body=row.GetComponentInChildren<Text>(true);body.text=text;
    var size=row.GetComponent<LayoutElement>();size.preferredHeight=Mathf.Max(size.minHeight,body.preferredHeight+(system?0:24));
    if(!system)row.Find("Avatar/Portrait").GetComponent<Image>().sprite=outgoing?selfPortrait:portrait;
   }
  }
  private Text Label(Transform parent,string text,int size){var t=Rect("Text",parent).gameObject.AddComponent<Text>();t.font=historyLabel.font;t.text=text;t.fontSize=size;t.color=new Color32(22,22,22,255);t.raycastTarget=false;return t;}
  private static RectTransform Rect(string name,Transform parent){var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);return r;}
  private static void Place(RectTransform r,float x,float y,float w,float h){r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);}
  private static void Stretch(RectTransform r,float left,float bottom,float right,float top){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(left,bottom);r.offsetMax=new Vector2(-right,-top);}
 }
}
