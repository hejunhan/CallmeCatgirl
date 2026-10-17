using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class CoreGameplayCanvasView : MonoBehaviour
    {
        [SerializeField] private CallmeCatgirl.Presentation.PlanningPresentation planningPresentation;
        public void SetManualPlanningVisual(bool paused) => planningPresentation?.SetPlanning(paused);
        public void RevealConversation() => conversationPopup?.Reveal();
        [SerializeField] private UIFeedbackConfiguration feedback;
        [SerializeField] private Font workspaceHeadingFont,workspaceNumberFont;
        public UIFeedbackConfiguration Feedback => feedback;
        public double VisualGameTime => controller == null ? 0 : controller.Simulation.GameTime;
        [SerializeField] private UIFeedbackLabel settlementFeedback;
        private int settlementFrom, settlementTo;
        private float settlementAt = -100;
        public void ShowSettlement(TaskEvent item, int income)
        {
            if (controller == null || feedback == null || !feedback.Animate) return;
            settlementFrom = controller.Funds - income; settlementTo = controller.Funds; settlementAt = Time.unscaledTime;
            bool timedOut = item.Kind == TaskEventKind.TimedOut;
            string result = timedOut ? "超时结算" : CoreGameplayController.QualityText(item.Result.Overall);
            Color color = timedOut ? feedback.Error : feedback.ResultColor(item.Result.Overall);
            if (settlementFeedback != null) settlementFeedback.Play(item.RequestId+" · "+result+"   收入 +"+income,
                color,feedback.SettlementSeconds,feedback.FloatingTextRise*.4f,.06f);
        }
        private int DisplayFunds
        {
            get
            {
                if (feedback == null || !feedback.Animate || controller.Funds != settlementTo) return controller.Funds;
                float t = Mathf.Clamp01((Time.unscaledTime-settlementAt)/Mathf.Max(.1f,feedback.SettlementSeconds*.55f));
                return settlementFrom + (int)System.Math.Round((settlementTo-(double)settlementFrom)*(1-(1-t)*(1-t)));
            }
        }
        private int lastFunds;
        private float fundsAt = -100, rejectedAt = -100;
        private Color fundsIdle, messageIdle, modelIdle;
        [Header("Scene layout — edit these objects directly")]
        [SerializeField] private RectTransform board;
        [SerializeField] private RectTransform boardModules;
        [SerializeField] private RectTransform candidateContent;
        [SerializeField] private RectTransform dragLayer;
        [SerializeField] private GridCellView[] gridCells;
        [SerializeField] private MemoryPartitionView partitions;
        [Header("Replaceable prefabs")]
        [SerializeField] private TaskCardView candidatePrefab;
        [SerializeField] private TaskModuleView modulePrefab;
        [Header("Texts and actions")]
        [SerializeField] private Text clockLabel;
        [SerializeField] private Text messageLabel;
        [SerializeField] private Text logLabel;
        [SerializeField] private Text pauseLabel;

        [SerializeField] private Button[] speedButtons;
        [SerializeField, Tooltip("与速度按钮一一对应的倍率。")] private float[] speedValues = { 1, 2, 4 };
        [SerializeField] private Color speedActiveColor = new Color(0.32f, 0.12f, 0.9f, 1f);
        private Color[] speedIdleColors;
        [SerializeField] private Color pauseActiveColor = new Color(0.32f, 0.12f, 0.9f, 1f);
        private Button pauseButton;
        private Color pauseIdleColor;
        [SerializeField] private GameObject infoPanel;
        private LocalDebugFoldout taskDebug;
        [SerializeField] private Text infoTitle;
        [SerializeField] private Text infoStatus;
        [SerializeField] private Text abilitiesLabel;
        [SerializeField] private Text qualityLabel;
        [SerializeField] private Button rotateButton;
        [SerializeField] private Button withdrawButton;
        [SerializeField] private Button submitButton;
        [SerializeField] private Button nextRoundButton;
        [SerializeField] private Text occupancyLabel;
        [SerializeField] private Text modelLabel;
        [SerializeField] private Text conversationLabel;
        [Header("User avatars — A, B, C, then new users" )]
        [SerializeField] private Sprite[] userAvatars;
        [SerializeField] private Image conversationAvatar;
        [SerializeField] private Button memoryUpgradeButton;
        [SerializeField] private Text memoryUpgradeLabel;
        [SerializeField] private Text memoryUpgradeButtonLabel;
        [SerializeField] private Text economyStatusLabel;
        [SerializeField] private GameObject endingPanel;
        [SerializeField] private Text endingLabel;
        [SerializeField] private ConversationPopupView conversationPopup;
        [SerializeField] private MemoryUpgradePopupView memoryUpgradePopup;
        private readonly Dictionary<string, TaskCardView> cards = new Dictionary<string, TaskCardView>();
        private readonly Dictionary<string, TaskModuleView> modules = new Dictionary<string, TaskModuleView>();
        private CoreGameplayController controller;
        private ConversationWorkspaceView conversationWorkspace;
        public UIWindowLayers WindowLayers { get; private set; }
        private string draggedId;
        private int dragRotation;
        private Cell grabOffset;
        private Vector2 lastPointer;
        private Camera pointerCamera;
        private TaskModuleView ghost;
        public bool IsInitialized => controller != null;
        public bool IsOperatingRequest(string id) => controller != null && controller.IsOperatingTask(id);
        public bool IsDragging => draggedId != null;
        public RectTransform Board => board;

        public void Initialize(CoreGameplayController owner)
        {
            CancelDrag();
            if (controller == null)
            {
                if (memoryUpgradeLabel != null) fundsIdle = memoryUpgradeLabel.color;
                messageIdle = messageLabel.color;
                if (modelLabel != null) modelIdle = modelLabel.color;
            }
            foreach(var text in new[]{modelLabel,memoryUpgradeLabel,clockLabel,infoStatus,abilitiesLabel,qualityLabel})
                if(text!=null){text.fontSize=28;text.resizeTextForBestFit=false;}
            if(memoryUpgradeLabel!=null)memoryUpgradeLabel.rectTransform.sizeDelta=new Vector2(500,44);
            if(infoStatus!=null){infoStatus.fontSize=26;infoStatus.rectTransform.sizeDelta=new Vector2(640,72);}
            if(abilitiesLabel!=null){abilitiesLabel.rectTransform.anchoredPosition=new Vector2(0,-108);abilitiesLabel.rectTransform.sizeDelta=new Vector2(740,102);}
            if(qualityLabel!=null){qualityLabel.rectTransform.anchoredPosition=new Vector2(560,-176);qualityLabel.rectTransform.sizeDelta=new Vector2(180,36);}
            controller = owner; lastFunds = owner.Funds; fundsAt = rejectedAt = settlementAt = -100;
            if (settlementFeedback != null) settlementFeedback.Clear();
            if (speedIdleColors == null && speedButtons != null)
                speedIdleColors = speedButtons.Select(b => b.targetGraphic.color).ToArray();
            if (pauseButton == null)
            {
                pauseButton = pauseLabel.GetComponentInParent<Button>();
                pauseIdleColor = pauseButton.targetGraphic.color;
            }
            foreach (var module in modules.Values) if (module != null) { module.gameObject.SetActive(false); Destroy(module.gameObject); }
            modules.Clear(); cards.Clear();
            foreach (var card in candidateContent.GetComponentsInChildren<TaskCardView>(true))
            {
                if (controller.Simulation.Tasks.TryGetValue(card.RequestId, out var task) && !cards.ContainsKey(task.Id))
                { cards.Add(task.Id, card); card.Bind(this, task); }
                else
                {
                    card.gameObject.SetActive(false);
                    // Destroy在帧末执行，先移出容器，避免同一帧重开再次绑定即将销毁的卡片。
                    card.transform.SetParent(null, false); Destroy(card.gameObject);
                }
            }
            if(conversationWorkspace==null)conversationWorkspace=gameObject.AddComponent<ConversationWorkspaceView>();
            conversationWorkspace.Bind(owner,this,(RectTransform)modelLabel.transform.parent,candidateContent,modelLabel.font);
            logLabel.transform.parent.gameObject.SetActive(false);
            foreach(var text in new[]{infoTitle,infoStatus,abilitiesLabel,qualityLabel})text.gameObject.SetActive(false);
            var candidatePanel=candidateContent.GetComponentsInParent<RectTransform>().FirstOrDefault(r=>r.name=="CandidatePanel");
            var candidateTitle=candidatePanel==null?null:candidatePanel.Find("Title");if(candidateTitle!=null)candidateTitle.GetComponent<Text>().text="候选";
            ArrangeWorkspace(candidatePanel);
            ApplyWorkspaceHierarchy(candidatePanel);
            if(WindowLayers==null)WindowLayers=GetComponent<UIWindowLayers>()??gameObject.AddComponent<UIWindowLayers>();
            WindowLayers.Configure(board.GetComponentsInParent<RectTransform>().First(r=>r.name=="BoardPanel"),planningPresentation,owner);
            Canvas.ForceUpdateCanvases(); Refresh();
        }

        private void ApplyWorkspaceHierarchy(RectTransform candidatePanel)
        {
            var boardPanel=board.GetComponentsInParent<RectTransform>().First(r=>r.name=="BoardPanel");
            var boardPaper=boardPanel.GetComponent<Image>();boardPaper.sprite=null;boardPaper.color=new Color32(244,244,244,255);
            if(candidatePanel!=null)
            {
                var paper=candidatePanel.GetComponent<Image>();paper.sprite=null;paper.color=new Color32(226,226,226,255);
                SetColumnTitle(candidatePanel,"候选","CANDIDATES");
                var scroll=(RectTransform)candidateContent.GetComponentInParent<ScrollRect>().transform;scroll.anchorMin=Vector2.zero;scroll.anchorMax=Vector2.one;scroll.offsetMin=new Vector2(8,8);scroll.offsetMax=new Vector2(-8,-104);
            }
            SetColumnTitle(boardPanel,"运算","COMPUTE");
            var container=(RectTransform)board.parent;container.offsetMax=new Vector2(-24,-144);
            occupancyLabel.font=workspaceNumberFont??occupancyLabel.font;occupancyLabel.fontSize=22;occupancyLabel.supportRichText=true;occupancyLabel.alignment=TextAnchor.UpperRight;
            SetTopRect(occupancyLabel.rectTransform,new Vector2(1,1),new Vector2(-24,-16),new Vector2(300,64));
            memoryUpgradeLabel.font=workspaceNumberFont??memoryUpgradeLabel.font;memoryUpgradeLabel.fontSize=22;memoryUpgradeLabel.supportRichText=true;memoryUpgradeLabel.alignment=TextAnchor.MiddleRight;
            SetTopRect(memoryUpgradeLabel.rectTransform,new Vector2(1,1),new Vector2(-220,-76),new Vector2(580,60));
            SetTopRect((RectTransform)memoryUpgradeButton.transform,new Vector2(1,1),new Vector2(-24,-88),new Vector2(180,40));
            var controls=new List<Button>{pauseButton,rotateButton,withdrawButton,submitButton,nextRoundButton,memoryUpgradeButton};if(speedButtons!=null)controls.AddRange(speedButtons);
            foreach(var button in transform.GetComponentsInChildren<Button>(true))if(new[]{"AddRequestButton","ResetButton","ReturnToMenu","OpenTraining","SaveRun","LoadRun"}.Contains(button.name))controls.Add(button);
            foreach(var button in controls.Distinct())
            {
                if(button==null)continue;
                var artwork=button.GetComponent<ButtonArtwork>();
                int speedIndex=speedButtons==null?-1:System.Array.IndexOf(speedButtons,button);
                if(artwork!=null && (button==pauseButton || (speedIndex>=0 && speedIndex<speedValues.Length)))
                { artwork.enabled=true;button.transition=Selectable.Transition.None;continue; }
                if(artwork!=null)artwork.enabled=false;
                var image=button.GetComponent<Image>();if(image==null)continue;image.sprite=null;image.color=Color.white;button.targetGraphic=image;button.transition=Selectable.Transition.ColorTint;
                var colors=button.colors;colors.normalColor=colors.selectedColor=Color.white;colors.highlightedColor=new Color32(232,232,232,255);colors.pressedColor=new Color32(200,200,200,255);colors.disabledColor=new Color32(210,210,210,255);colors.fadeDuration=.08f;button.colors=colors;
                var text=button.GetComponentInChildren<Text>(true);if(text!=null){text.enabled=true;text.font=workspaceNumberFont??text.font;text.fontStyle=FontStyle.Normal;text.color=new Color32(22,22,22,255);}
            }
        }
        private void SetColumnTitle(RectTransform panel,string title,string english)
        {
            var label=panel.Find("Title")?.GetComponent<Text>();if(label==null)return;
            label.text=title;label.font=workspaceHeadingFont??label.font;label.fontStyle=FontStyle.Normal;label.fontSize=44;label.alignment=TextAnchor.UpperLeft;
            SetTopRect(label.rectTransform,new Vector2(0,1),new Vector2(20,-12),new Vector2(300,56));
            var existing=panel.Find("ColumnLabel");var tag=existing==null?null:existing.GetComponent<Text>();
            if(tag==null){var go=new GameObject("ColumnLabel",typeof(RectTransform),typeof(Text));go.transform.SetParent(panel,false);tag=go.GetComponent<Text>();}
            tag.text=english;tag.font=modelLabel.font;tag.fontSize=22;tag.color=new Color32(82,82,82,255);tag.raycastTarget=false;
            SetTopRect(tag.rectTransform,new Vector2(0,1),new Vector2(22,-68),new Vector2(240,30));
        }
        private static void SetTopRect(RectTransform r,Vector2 anchor,Vector2 position,Vector2 size){r.anchorMin=r.anchorMax=r.pivot=anchor;r.anchoredPosition=position;r.sizeDelta=size;}

        private void ArrangeWorkspace(RectTransform candidatePanel)
        {
            if(candidatePanel!=null)
            {
                candidatePanel.anchorMin=new Vector2(0,0);candidatePanel.anchorMax=new Vector2(0,1);candidatePanel.pivot=new Vector2(0,1);
                candidatePanel.offsetMin=new Vector2(432,80);candidatePanel.offsetMax=new Vector2(704,-120);
                candidatePanel.GetComponent<CallmeCatgirl.Presentation.PlanningPaper>()?.CaptureHome();
                var grid=candidateContent.GetComponent<GridLayoutGroup>();if(grid!=null)grid.cellSize=new Vector2(112,140);
            }
            var boardPanel=board.GetComponentsInParent<RectTransform>().First(r=>r.name=="BoardPanel");
            boardPanel.anchorMin=Vector2.zero;boardPanel.anchorMax=Vector2.one;boardPanel.offsetMin=new Vector2(728,80);boardPanel.offsetMax=new Vector2(-32,-120);
            var container=(RectTransform)board.parent;container.anchorMin=Vector2.zero;container.anchorMax=Vector2.one;container.offsetMin=new Vector2(24,96);container.offsetMax=new Vector2(-24,-112);
            var actions=(RectTransform)infoPanel.transform;actions.anchorMin=new Vector2(0,0);actions.anchorMax=new Vector2(1,0);actions.anchoredPosition=new Vector2(0,48);actions.sizeDelta=new Vector2(-36,72);
            var buttons=new[]{rotateButton,withdrawButton,submitButton,nextRoundButton};var names=new[]{"旋转","撤回","提交","下一轮"};float x=0;
            for(int i=0;i<buttons.Length;i++)
            {
                var r=(RectTransform)buttons[i].transform;r.anchorMin=r.anchorMax=new Vector2(0,.5f);r.pivot=new Vector2(0,.5f);r.anchoredPosition=new Vector2(x,0);r.sizeDelta=new Vector2(r.sizeDelta.x,44);x+=r.sizeDelta.x+16;
                var label=buttons[i].GetComponentInChildren<Text>(true);if(label!=null)label.text=names[i];
            }
            foreach(var text in transform.GetComponentsInChildren<Text>(true))
                if(text.name=="Hint"||text.name=="BoardHint"||text.name=="CommunicationHint"||text.name=="CommunicationFooter"||(text.name=="Title"&&text.transform.parent.name=="Header"))text.gameObject.SetActive(false);
            messageLabel.gameObject.SetActive(false);
            var oldConversation=transform.Find("ConversationPanel");if(oldConversation!=null)oldConversation.gameObject.SetActive(false);
        }

        public void Refresh()
        {
            if (controller == null || controller.Simulation == null) return;
            // Apply modal visibility before deriving control states, including the close frame.
            memoryUpgradePopup?.Refresh(controller, feedback);
            planningPresentation?.SetPresentationBlocked(controller.IsModalOpen);
            var sim = controller.Simulation;
            if (draggedId != null && (!sim.Tasks.TryGetValue(draggedId, out var dragging)
                || (dragging.State != RequestState.Running && dragging.State != RequestState.Waiting && dragging.State != RequestState.Withdrawn))) CancelDrag();
            if ((controller.GameEnded || controller.IsModalOpen) && IsDragging) CancelDrag();
            if (endingPanel != null) endingPanel.SetActive(controller.GameEnded);
            if (endingLabel != null) endingLabel.text = controller.EndingText;
            if (economyStatusLabel != null) economyStatusLabel.text = $"第{controller.CurrentMonth}月 · 月底剩 {controller.MonthTimeRemaining:0.0}s · "
                + (controller.GameEnded ? "经营结束" : controller.IsInGracePeriod ? $"宽限期，欠款 {controller.DebtAmount}" : "正常经营");
            pauseButton.interactable = !controller.GameEnded && !controller.IsModalOpen;
            SetControlSelected(pauseButton, sim.Paused || controller.IsModalOpen, feedback != null ? feedback.PauseSelected : pauseActiveColor, pauseIdleColor);
            if (conversationPopup != null) conversationPopup.Refresh(controller, GetAvatar(controller.Story.UserId), feedback);
            clockLabel.text = $"时间 {sim.GameTime:0.0}s · 周期 {sim.Model.Interval:0.#}s";
            messageLabel.text = controller.Message.StartsWith("拖动请求") ? "R 旋转 · 空格 暂停 · Esc 取消" : controller.Message; logLabel.text = string.Join("\n", controller.Logs);
            pauseLabel.text = controller.IsMemoryUpgradeOpen ? "内存升级中" : controller.GameEnded ? "经营结束" : sim.Paused ? (sim.GameTime == 0 ? "开始" : "继续") : "暂停";

            if (speedButtons != null && speedIdleColors != null)
                for (int i = 0; i < speedButtons.Length && i < speedValues.Length; i++)
                    SetControlSelected(speedButtons[i], Mathf.Approximately(controller.PlaybackSpeed, speedValues[i]),
                        feedback != null ? feedback.SpeedSelected : speedActiveColor, speedIdleColors[i]);
            if (memoryUpgradeButton != null)
            {
                memoryUpgradeButton.interactable = !controller.GameEnded && !controller.IsModalOpen;
                memoryUpgradeButtonLabel.text = "内存 / 加点";
                memoryUpgradeLabel.text = $"内存 Lv{controller.MemoryLevel}   资金 <size=42>{DisplayFunds}</size>   点数 {controller.AvailableAbilityPoints}";
            }
            float width = board.rect.width / sim.Width, height = board.rect.height / sim.Height;
            int open = 0;
            for (int i = 0; i < gridCells.Length; i++)
            {
                bool available = sim.IsOpen(new Cell(i % sim.Width, i / sim.Width));
                gridCells[i].Show(available, feedback); if (available) open++;
            }
            if (partitions != null) partitions.Show(sim);
            if (occupancyLabel != null) occupancyLabel.text = $"已用 <size=42>{sim.Occupancy.Count} / {open}</size> 格";
            if (modelLabel != null) modelLabel.text = $"{controller.ModelName}\n"
                + (controller.TrainingEnabled ? $"{controller.Training.Session.TokensPerSecond:0.##} token/s\n" : string.Empty)
                + $"能力周期 {sim.Model.Interval:0.#}s\n\n"
                + string.Join("\n", sim.Model.Points.Select(p => $"{CoreGameplayController.AbilityText(p.Key)}  {p.Value:0.##}"));
            foreach (var task in sim.Tasks.Values)
            {
                bool candidate = task.State == RequestState.Waiting || task.State == RequestState.Withdrawn || (task.State == RequestState.Ready && task.ReadyInCandidate);
                if (candidate && !cards.ContainsKey(task.Id))
                {
                    var card = Instantiate(candidatePrefab, candidateContent); card.name = "RequestCard_" + task.Id;
                    card.Bind(this, task); cards.Add(task.Id, card);
                }
                if (cards.TryGetValue(task.Id, out var existingCard))
                {
                    existingCard.gameObject.SetActive(candidate);
                    if (candidate) existingCard.Refresh(task, sim.GameTime, task.Id == controller.SelectedId);
                }
                bool onBoard = task.State == RequestState.Running || (task.State == RequestState.Ready && !task.ReadyInCandidate);
                if (onBoard && !modules.ContainsKey(task.Id))
                {
                    var module = Instantiate(modulePrefab, boardModules); module.name = "RunningModule_" + task.Id;
                    module.Bind(this, task.Id); modules.Add(task.Id, module);
                }
                if (modules.TryGetValue(task.Id, out var existingModule))
                {
                    existingModule.gameObject.SetActive(onBoard);
                    if (onBoard)
                    {
                        existingModule.Rect.anchoredPosition = new Vector2(task.Origin.X * width, -task.Origin.Y * height);
                        existingModule.Render(task, task.Rotation, width, height, task.Id == controller.SelectedId || IsAssociationHighlighted(task.ConversationId));
                    }
                }
            }
            conversationWorkspace?.Refresh();
            RefreshSignals();
            RefreshInfo();
            if (IsDragging) RenderGhost();
            WindowLayers?.Synchronize();
        }

        private static void SetControlSelected(Button button, bool selected, Color active, Color idle)
        {
            var artwork = button.GetComponent<ButtonArtwork>();
            if (artwork != null && artwork.enabled) artwork.SetSelected(selected);
            else
            {
                button.targetGraphic.color=selected?active:idle;
                var label=button.GetComponentInChildren<Text>(true);if(label!=null)label.color=selected?Color.white:new Color32(22,22,22,255);
            }
        }

        private void RefreshSignals()
        {
            if (lastFunds != controller.Funds) { lastFunds = controller.Funds; fundsAt = Time.unscaledTime; }
            float duration = feedback == null ? .55f : Mathf.Max(.05f,feedback.PulseSeconds);
            bool animate = feedback != null && feedback.Animate;
            if (!animate && settlementFeedback != null) settlementFeedback.Clear();
            float pulse = animate ? Mathf.Clamp01(1 - (Time.unscaledTime - fundsAt) / duration) : 0;
            if (memoryUpgradeLabel != null) memoryUpgradeLabel.color = Color.Lerp(fundsIdle, feedback == null ? fundsIdle : feedback.Signal,pulse);
            pulse = animate ? Mathf.Clamp01(1 - (Time.unscaledTime - rejectedAt) / duration) : 0;
            messageLabel.color = Color.Lerp(messageIdle,feedback == null ? messageIdle : feedback.Error,pulse);
            // 芯片呼吸使用游戏时间，暂停及弹窗不会继续呼吸。
            if (modelLabel != null)
            {
                bool working = controller.Simulation.Tasks.Values.Any(t => t.State == RequestState.Running);
                float breath = animate && working ? (.5f + .5f * Mathf.Sin((float)VisualGameTime * 2)) * feedback.PulseStrength : 0;
                modelLabel.color = Color.Lerp(modelIdle,feedback == null ? modelIdle : feedback.Signal,breath);
            }
        }

        private void RefreshInfo()
        {
            var task = controller.SelectedTask; infoPanel.SetActive(task != null);
            if(taskDebug==null && controller.DebugCommands!=null && CallmeCatgirl.Debugging.DebugBuild.Enabled)
            {
                var slot=new GameObject("InlineTaskDebugSlot",typeof(RectTransform)).GetComponent<RectTransform>();slot.SetParent(infoPanel.transform,false);
                slot.anchorMin=slot.anchorMax=new Vector2(1,1);slot.pivot=new Vector2(1,1);slot.anchoredPosition=new Vector2(-4,-42);slot.sizeDelta=new Vector2(350,270);slot.localScale=Vector3.one*.63f;
                taskDebug=LocalDebugFoldout.Attach(slot,controller.DebugCommands,"task",task?.Id,infoTitle.font);
            }
            taskDebug?.SetTarget(task?.Id);
            bool trainingTask = task != null && controller.TrainingEnabled && controller.Training.IsTrainingRequest(task.Id);
            if (conversationAvatar != null)
            { conversationAvatar.sprite = IsOperatingRequest(task?.Id) ? null : GetAvatar(task?.ConversationId); conversationAvatar.enabled = conversationAvatar.sprite != null; }
            if (conversationLabel != null) conversationLabel.text = task == null ? "选择用户查看通讯。"
                : trainingTask ? "训练模块 / 模型产物\n不产生用户订单收入或剧情。"
                : IsOperatingRequest(task.Id) ? "经营委托 · 不含角色剧情，不计亲密度" : controller.Story.Name(task.ConversationId) + $" · 亲密度 {controller.Story.Affinity(task.ConversationId):0.##}\n" + CoreGameplayController.StateText(task.State)
                + "\n\n" + controller.Story.Latest(task.ConversationId);
            submitButton.gameObject.SetActive(trainingTask || IsOperatingRequest(task?.Id));
            if (task == null) return;
            infoTitle.text = "当前请求 / " + task.Id + " · " + task.Definition.Title
                + (trainingTask ? " · 训练模块" : IsOperatingRequest(task.Id) ? " · 经营委托" : $" · 亲密度 {controller.Story.Affinity(task.ConversationId):0.##}");
            infoStatus.text = $"{CoreGameplayController.StateText(task.State)}  ·  计算 {task.Progress:0.#}/{task.Definition.Calculation:0.#}  ·  忍耐 {task.Remaining(controller.Simulation.GameTime):0.0}s · 达标奖励 {task.Definition.Tokens:0.#} Token";
            abilitiesLabel.text = string.Join("\n", task.Definition.Requirements.Select(req =>
                $"{CoreGameplayController.AbilityText(req.Kind)}  {task.GetAbility(req.Kind):0.#} / 完成 {req.Minimum:0.#} / 优秀 {req.Excellent:0.#}"
                + (task.Result == null ? "" : "  " + CoreGameplayController.QualityText(task.Result.ByAbility[req.Kind]))));
            qualityLabel.text = task.Result == null ? string.Empty : "质量：" + CoreGameplayController.QualityText(task.Result.Overall);
            rotateButton.interactable = !controller.GameEnded && !controller.IsModalOpen && (task.State == RequestState.Waiting || task.State == RequestState.Withdrawn || task.State == RequestState.Running);
            withdrawButton.interactable = !controller.GameEnded && !controller.IsModalOpen && (task.State == RequestState.Running);
            submitButton.interactable = !controller.GameEnded && !controller.IsModalOpen && (task.State == RequestState.Ready);
            nextRoundButton.interactable = !trainingTask && !IsOperatingRequest(task.Id) && !controller.GameEnded && !controller.IsModalOpen && !controller.Story.Finished(task.ConversationId) && (task.State == RequestState.Submitted || task.State == RequestState.TimedOut);
        }

        public Sprite GetAvatar(string conversationId)
        {
            if (string.IsNullOrEmpty(conversationId) || userAvatars == null || userAvatars.Length == 0) return null;
            int index = conversationId == "B" ? 1 : conversationId == "C" ? 2 : 0;
            if (conversationId.StartsWith("new-user-") && int.TryParse(conversationId.Substring(9), out int serial))
                index = (serial + 2) % userAvatars.Length;
            return userAvatars[index % userAvatars.Length];
        }
        public bool IsAssociationHighlighted(string conversation)=>conversationWorkspace!=null&&conversationWorkspace.IsHighlighted(conversation);
        public void HoverConversation(string id,UnityEngine.Object source)=>conversationWorkspace?.Hover(id,source);
        public void HoverRequest(string id,UnityEngine.Object source){if(controller.Simulation.Tasks.TryGetValue(id,out var task))HoverConversation(task.ConversationId,source);}
        public void ClearAssociationHover(UnityEngine.Object source)=>conversationWorkspace?.Clear(source);
        public IEnumerable<RectTransform> AssociationTargets(string conversation)
        {
            foreach(var pair in cards)if(pair.Value.gameObject.activeInHierarchy&&controller.Simulation.Tasks[pair.Key].ConversationId==conversation)yield return pair.Value.ShapeRect;
            foreach(var pair in modules)if(pair.Value.gameObject.activeInHierarchy&&controller.Simulation.Tasks[pair.Key].ConversationId==conversation)yield return pair.Value.Rect;
        }
        public void OpenRequestConversation(string id)
        {
            controller.SelectRequest(id);
            if (controller.IsOperatingTask(id)) { controller.SubmitSelected(); RefreshInfo(); return; }
            controller.OpenConversationHistory();
        }
        public void SelectRequest(string id) { controller.SelectRequest(id); RefreshInfo(); }
        public void BeginDrag(string id, PointerEventData data, bool fromBoard)
        {
            CancelDrag();
            if (controller.GameEnded || controller.IsModalOpen) return;
            if (!controller.Simulation.Tasks.TryGetValue(id, out var task)
                || (task.State != RequestState.Waiting && task.State != RequestState.Withdrawn && task.State != RequestState.Running)) return;
            controller.SelectRequest(id); draggedId = id; dragRotation = task.Rotation;
            lastPointer = data.position; pointerCamera = data.pressEventCamera;
            var cell = ScreenToCell(data.position, pointerCamera);
            grabOffset = fromBoard ? new Cell(cell.X - task.Origin.X, cell.Y - task.Origin.Y) : new Cell(0, 0);
            ghost = Instantiate(modulePrefab, dragLayer); ghost.name = "DragPreview"; ghost.Bind(this, id);
            var group = ghost.GetComponent<CanvasGroup>(); if (group == null) group = ghost.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;
            RenderGhost();
        }
        public void UpdateDrag(PointerEventData data)
        { if (!IsDragging) return; lastPointer = data.position; pointerCamera = data.pressEventCamera; RenderGhost(); }
        public void EndDrag(PointerEventData data)
        {
            if (!IsDragging) return;
            if (WindowLayers!=null&&WindowLayers.CoversPointer(data.position,data.pressEventCamera)) { CancelDrag(); Refresh(); return; }
            lastPointer = data.position; pointerCamera = data.pressEventCamera;
            var cell = ScreenToCell(lastPointer, pointerCamera);
            var origin = new Cell(cell.X - grabOffset.X, cell.Y - grabOffset.Y);
            bool success = controller.TryPlaceRequest(draggedId, origin, dragRotation, out var failure);
            controller.SetMessage(success ? "已放置" : "落位失败：" + CoreGameplayController.FailureText(failure) + "，原位置与进度保留。");
            string dropped = draggedId;
            if (!success) rejectedAt = Time.unscaledTime;
            CancelDrag(); Refresh();
            if (modules.TryGetValue(dropped,out var module) && module.gameObject.activeSelf)
            { if (success) module.PlayPlacement(); else module.PlayRejected(); }
            else if (!success)
            {
                if (cards.TryGetValue(dropped,out var card)) card.PlayRejected();
                if (settlementFeedback != null && feedback != null && feedback.Animate)
                    settlementFeedback.Play(CoreGameplayController.FailureText(failure),feedback.Error,feedback.RejectSeconds*3,0,0);
            }
        }
        public void RotateDrag()
        { if (!IsDragging) return; dragRotation = (dragRotation + 1) % 4; grabOffset = new Cell(0, 0); RenderGhost(); }
        public void CancelDrag()
        {
            draggedId = null;
            if (ghost != null) { ghost.gameObject.SetActive(false); Destroy(ghost.gameObject); }
            ghost = null;
        }
        private Cell ScreenToCell(Vector2 screen, Camera camera)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(board, screen, camera, out var local);
            var sim = controller.Simulation;
            return new Cell(Mathf.FloorToInt((local.x - board.rect.xMin) / (board.rect.width / sim.Width)),
                Mathf.FloorToInt((board.rect.yMax - local.y) / (board.rect.height / sim.Height)));
        }
        private void RenderGhost()
        {
            if (!IsDragging || ghost == null) return;
            var sim = controller.Simulation; var task = sim.Tasks[draggedId];
            var cell = ScreenToCell(lastPointer, pointerCamera); var origin = new Cell(cell.X - grabOffset.X, cell.Y - grabOffset.Y);
            bool legal = sim.Preview(draggedId, origin, dragRotation) == PlacementFailure.None;
            float width = board.rect.width / sim.Width, height = board.rect.height / sim.Height;
            if (RectTransformUtility.RectangleContainsScreenPoint(board, lastPointer, pointerCamera))
                ghost.Rect.position = board.TransformPoint(new Vector3(board.rect.xMin + origin.X * width, board.rect.yMax - origin.Y * height, 0));
            else
            {
                RectTransformUtility.ScreenPointToWorldPointInRectangle(dragLayer, lastPointer, pointerCamera, out var world);
                ghost.Rect.position = world;
            }
            ghost.Render(task, dragRotation, width, height, false, true, legal);
        }
    }
}
