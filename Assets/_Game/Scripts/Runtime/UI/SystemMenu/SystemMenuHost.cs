using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI.SystemMenu
{
    /// <summary>Reusable scene-owned UGUI menu. The caller owns the single Escape input entry point.</summary>
    [DisallowMultipleComponent]
    public sealed class SystemMenuHost : MonoBehaviour
    {
        private static readonly Color Ink = new Color32(30, 33, 34, 255);
        private static readonly Color Paper = new Color32(242, 240, 232, 255);
        private static readonly Color Muted = new Color32(83, 88, 89, 255);
        [SerializeField] private RectTransform root, drawer, settingsFrame;
        [SerializeField] private CanvasGroup drawerGroup, settingsGroup;
        [SerializeField] private GameObject blocker, settingsRoot, previewRoot;
        [SerializeField] private Text saveError, status, volumeValue, modeValue, resolutionValue, capValue, syncValue, syncExplanation, previewLabel;
        [SerializeField] private Slider volume;
        [SerializeField] private Button resumeButton, settingsButton, saveButton, settingsBackButton, previewConfirm, previewCancel;
        [SerializeField] private Font font;
        [SerializeField] private Vector2 preferredSettingsSize = new Vector2(820,900), preferredPreviewSize = new Vector2(660,350);
        [SerializeField] private float preferredDrawerWidth = 390;
        private float drawerProgress;
        private bool refreshing, configured, wasBlocking;
        private GameObject previousSelection;
        private SystemMenuSession session;
        public SystemSettingsService Settings { get; private set; }
        public bool IsPauseClaimed => session != null && session.IsPauseClaimed;
        public bool IsBlockingInput => session != null && session.IsBlockingInput;
        public bool IsPauseOpen => session != null && session.IsPauseOpen;
        public bool IsSettingsOpen => session != null && session.IsSettingsOpen;

        public static SystemMenuHost Create(RectTransform parent, Font font, bool gameplay,
            Action<bool> pauseClaimChanged = null, Func<bool> consumeEscapeBeforeMenu = null,
            Func<MenuSaveResult> saveAndReturnToMenu = null, SystemSettingsService service = null, bool applySavedSettings = true)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            var host = parent.GetComponentsInChildren<SystemMenuHost>(true).FirstOrDefault();
            if(host==null && Application.isPlaying)throw new InvalidOperationException("System menu UI is missing. Save its authored scene layout before Play.");
            if (host == null) { var rect = Rect("SystemMenuHost", parent); Stretch(rect); host = rect.gameObject.AddComponent<SystemMenuHost>(); }
            host.Configure(font, gameplay, pauseClaimChanged, consumeEscapeBeforeMenu, saveAndReturnToMenu, service, applySavedSettings);
            return host;
        }

        public void Configure(Font sharedFont, bool gameplay, Action<bool> pauseClaimChanged = null,
            Func<bool> consumeEscapeBeforeMenu = null, Func<MenuSaveResult> saveAndReturnToMenu = null,
            SystemSettingsService service = null, bool applySavedSettings = true)
        {
            if (configured) throw new InvalidOperationException("This system menu is already configured.");
            if (sharedFont == null) throw new ArgumentNullException(nameof(sharedFont), "Pass the existing game's Chinese UI font.");
            if(Application.isPlaying && drawer==null)throw new InvalidOperationException("System menu references must be authored before Play.");
            configured = true; PrepareSceneLayout(sharedFont);
            Settings = service ?? new SystemSettingsService(new UnitySystemSettingsBackend(), new PlayerPrefsSystemSettingsStore());
            session = new SystemMenuSession(gameplay, pauseClaimChanged, consumeEscapeBeforeMenu, saveAndReturnToMenu);
            BindActions(); session.Changed += RefreshVisibility; Settings.Changed += RefreshSettings;
            if (applySavedSettings) Run(Settings.LoadAndApply);
            RefreshSettings(); RefreshVisibility();
        }

        public void PrepareSceneLayout(Font sharedFont)
        {
            if (font == null) font = sharedFont;
            root = (RectTransform)transform;
            if (drawer != null) return;
            var canvas = root.GetComponent<Canvas>();
            if (canvas == null) canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true; canvas.sortingOrder = 1000;
            if (root.GetComponent<GraphicRaycaster>() == null) root.gameObject.AddComponent<GraphicRaycaster>();
            var group=root.GetComponent<CanvasGroup>();
            if(group==null)group=root.gameObject.AddComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            Build();
            blocker.SetActive(false); drawer.gameObject.SetActive(false); settingsRoot.SetActive(false); previewRoot.SetActive(false);
        }
        private static void Wire(Button button, Action action)
        { button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => action()); }
        private static void WireChoice(Text value, Action previous, Action next)
        {
            var buttons = value.transform.parent.GetComponentsInChildren<Button>(true);
            Wire(buttons[0], previous); Wire(buttons[1], next);
        }
        private void BindActions()
        {
            Wire(resumeButton, CloseAll); Wire(settingsButton, OpenSettings); Wire(saveButton, () => session.SaveAndReturn());
            Wire(settingsBackButton, CloseSettings);
            Wire(previewConfirm, () => Run(() => Settings.ConfirmDisplayPreview()));
            Wire(previewCancel, () => Run(Settings.RevertDisplayPreview));
            WireChoice(modeValue, PreviewMode, PreviewMode);
            WireChoice(resolutionValue, () => StepResolution(-1), () => StepResolution(1));
            WireChoice(capValue, () => StepFrameRate(-1), () => StepFrameRate(1));
            WireChoice(syncValue, () => Run(() => Settings.SetVSync(!Settings.Current.VSync)), () => Run(() => Settings.SetVSync(!Settings.Current.VSync)));
            volume.onValueChanged.RemoveAllListeners();
            volume.onValueChanged.AddListener(value => { if (!refreshing) Run(() => Settings.SetMasterVolume(value)); });
        }
        public bool HandleEscape()
        {
            if (session == null) return false;
            if (IsSettingsOpen && Settings.IsDisplayPreviewPending) { Run(Settings.RevertDisplayPreview); return true; }
            bool closesSettings = IsSettingsOpen;
            if (closesSettings) Run(Settings.RevertDisplayPreview);
            return session.HandleEscape();
        }
        public void OpenPause() { CaptureSelection(); session?.OpenPause(); }
        public void OpenSettings() { CaptureSelection(); if (Settings != null) Run(Settings.RefreshDisplayState); session?.OpenSettings(); }
        public void CloseAll() { if (Settings != null) Run(Settings.RevertDisplayPreview); session?.CloseAll(); }
        public void CloseSettings() { if (Settings != null) Run(Settings.RevertDisplayPreview); session?.CloseSettings(); }

        private void Update()
        {
            if (!configured) return;
            Settings.Tick(Time.unscaledDeltaTime);
            float target = IsPauseOpen ? 1 : 0;
            drawerProgress = Mathf.MoveTowards(drawerProgress, target, Time.unscaledDeltaTime / .15f);
            drawer.anchoredPosition = new Vector2(-drawer.rect.width * (1 - drawerProgress), 0);
            drawerGroup.alpha = drawerProgress;
            drawer.gameObject.SetActive(IsPauseOpen || drawerProgress > 0);
            if (Settings.IsDisplayPreviewPending) { UpdatePreviewText(); previewConfirm.interactable = Settings.CanConfirmDisplayPreview; }
            Layout();
        }
        private void OnApplicationFocus(bool focused) { if (configured) Run(() => Settings.OnFocusChanged(focused)); }
        private void OnApplicationQuit() { if (configured) Run(Settings.OnLeavingApplication); }
        private void OnDisable()
        {
            if (!configured) return;
            Run(Settings.RevertDisplayPreview); session.CloseAll();
        }
        private void OnDestroy()
        {
            if (!configured) return;
            Settings.Changed -= RefreshSettings; session.Changed -= RefreshVisibility;
        }

        private void Build()
        {
            var veil = Rect("SystemMenuInputBlocker", root); Stretch(veil);
            var veilImage = veil.gameObject.AddComponent<Image>(); veilImage.color = new Color(0, 0, 0, .2f); blocker = veil.gameObject;
            drawer = Rect("PauseDrawer", root); drawer.anchorMin = Vector2.zero; drawer.anchorMax = new Vector2(0, 1); drawer.pivot = new Vector2(0, .5f); drawer.sizeDelta = new Vector2(390, 0);
            Surface(drawer); drawerGroup = drawer.gameObject.AddComponent<CanvasGroup>();
            var drawerViewport = Rect("DrawerViewport", drawer); Stretch(drawerViewport, 26, 28, 26, 36);
            var drawerScroll = drawerViewport.gameObject.AddComponent<ScrollRect>(); drawerScroll.horizontal = false; drawerScroll.scrollSensitivity = 36;
            drawerViewport.gameObject.AddComponent<RectMask2D>();
            var drawerHit = drawerViewport.gameObject.AddComponent<Image>(); drawerHit.color = Color.clear; drawerHit.canvasRenderer.cullTransparentMesh = false;
            var drawerContent = Rect("DrawerContent", drawerViewport); drawerContent.anchorMin = new Vector2(0,1); drawerContent.anchorMax = Vector2.one; drawerContent.pivot = new Vector2(.5f,1); drawerContent.sizeDelta = Vector2.zero;
            var drawerFit = drawerContent.gameObject.AddComponent<ContentSizeFitter>(); drawerFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            drawerScroll.viewport = drawerViewport; drawerScroll.content = drawerContent;
            var stack = Vertical(drawerContent, 18); stack.childForceExpandHeight = false;
            RowLabel(drawerContent, "已暂停", 36, 58, Ink);
            RowLabel(drawerContent, "继续时保留原来的暂停与倍速设置。", 22, 78, Muted);
            resumeButton = RowButton(drawerContent, "继续游戏", () => CloseAll(), 64);
            settingsButton = RowButton(drawerContent, "系统设置", OpenSettings, 64);
            saveButton = RowButton(drawerContent, "保存并返回主菜单", () => session.SaveAndReturn(), 82);
            saveError = RowLabel(drawerContent, "", 22, 154, new Color32(143, 47, 42, 255));
            RowLabel(drawerContent, "Esc 继续", 20, 38, Muted);

            var settings = Rect("SharedSystemSettings", root); Stretch(settings); settingsRoot = settings.gameObject;
            var bg = settings.gameObject.AddComponent<Image>(); bg.color = new Color(0, 0, 0, .24f);
            settingsFrame = Rect("SettingsPaper", settings); settingsFrame.anchorMin = settingsFrame.anchorMax = settingsFrame.pivot = new Vector2(.5f, .5f); Surface(settingsFrame);
            settingsGroup = settingsFrame.gameObject.AddComponent<CanvasGroup>();
            var heading = Label("Heading", settingsFrame, "系统设置", 34, TextAnchor.MiddleLeft); Top(heading.rectTransform, 28, 16, -56, 58);
            var scrollRect = Rect("SettingsScroll", settingsFrame); Stretch(scrollRect, 26, 100, 26, 86);
            var scroll = scrollRect.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 36;
            var viewport = Rect("Viewport", scrollRect); Stretch(viewport); viewport.gameObject.AddComponent<RectMask2D>();
            var viewportImage = viewport.gameObject.AddComponent<Image>(); viewportImage.color = Color.clear; viewportImage.canvasRenderer.cullTransparentMesh = false;
            var content = Rect("SettingsContent", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
            Vertical(content, 12); var fit = content.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            BuildVolume(content);
            modeValue = Choice(content, "显示模式", () => PreviewMode(), () => PreviewMode());
            resolutionValue = Choice(content, "分辨率", () => StepResolution(-1), () => StepResolution(1));
            RowLabel(content, "分辨率来自当前显示器支持的模式，包含当前尺寸。修改显示模式后需确认；15 秒未确认自动恢复。", 21, 100, Muted);
            capValue = Choice(content, "帧率上限", () => StepFrameRate(-1), () => StepFrameRate(1));
            syncValue = Choice(content, "垂直同步", () => Run(() => Settings.SetVSync(!Settings.Current.VSync)), () => Run(() => Settings.SetVSync(!Settings.Current.VSync)));
            syncExplanation = RowLabel(content, "", 21, 94, Muted);
            RowLabel(content, "音量和帧率设置立即保存；显示设置仅在确认后保存。", 21, 66, Muted);
            status = RowLabel(content, "", 21, 82, new Color32(143, 47, 42, 255));
            status.gameObject.SetActive(false);
            var footer = Rect("Footer", settingsFrame); footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0); footer.pivot = new Vector2(.5f, 0); footer.anchoredPosition = new Vector2(0, 20); footer.sizeDelta = new Vector2(-52, 58);
            settingsBackButton = Button(footer, "返回", CloseSettings); Stretch((RectTransform)settingsBackButton.transform);
            BuildPreview(settings);
            Layout();
        }

        private void BuildVolume(RectTransform content)
        {
            var row = Rect("MasterVolume", content); Height(row, 112);
            var title = Label("Title", row, "总音量", 25, TextAnchor.MiddleLeft); Top(title.rectTransform, 0, 0, -130, 42);
            volumeValue = Label("Value", row, "100%", 25, TextAnchor.MiddleRight); Top(volumeValue.rectTransform, 0, 0, 0, 42);
            var sliderRect = Rect("VolumeSlider", row); sliderRect.anchorMin = new Vector2(0, 0); sliderRect.anchorMax = new Vector2(1, 0); sliderRect.pivot = new Vector2(.5f, 0); sliderRect.sizeDelta = new Vector2(-12, 48); sliderRect.anchoredPosition = new Vector2(0, 6);
            var track = Rect("Track", sliderRect); Stretch(track, 0, 20, 0, 20); var trackImage = track.gameObject.AddComponent<Image>(); trackImage.color = new Color32(181, 181, 173, 255);
            var handleArea = Rect("HandleArea", sliderRect); Stretch(handleArea, 10, 0, 10, 0);
            var handle = Rect("Handle", handleArea); handle.sizeDelta = new Vector2(22, 38); var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = Ink;
            volume = sliderRect.gameObject.AddComponent<Slider>(); volume.handleRect = handle; volume.targetGraphic = handleImage; volume.direction = Slider.Direction.LeftToRight;
            sliderRect.gameObject.AddComponent<SystemMenuScrollFocus>();
            volume.minValue = 0; volume.maxValue = 1; volume.onValueChanged.AddListener(value => { if (!refreshing) Run(() => Settings.SetMasterVolume(value)); });
        }

        private void BuildPreview(RectTransform parent)
        {
            var overlay = Rect("DisplayPreviewConfirmation", parent); Stretch(overlay); previewRoot = overlay.gameObject;
            var dim = overlay.gameObject.AddComponent<Image>(); dim.color = new Color(0, 0, 0, .48f);
            var paper = Rect("PreviewPaper", overlay); paper.anchorMin = paper.anchorMax = paper.pivot = new Vector2(.5f,.5f); paper.sizeDelta = new Vector2(660, 310); Surface(paper);
            var body = Rect("Content", paper); Stretch(body, 24, 24, 24, 24); Vertical(body, 12);
            previewLabel = RowLabel(body, "保留这次显示设置？", 26, 124, Ink);
            previewConfirm = RowButton(body, "保留显示设置", () => Run(() => Settings.ConfirmDisplayPreview()), 58);
            previewCancel = RowButton(body, "恢复原设置", () => Run(Settings.RevertDisplayPreview), 58);
            // A pointer veil cannot contain keyboard/gamepad navigation. Keep this two-button modal closed.
            LinkPreviewNavigation(previewConfirm, previewCancel);
            LinkPreviewNavigation(previewCancel, previewConfirm);
        }

        private static void LinkPreviewNavigation(Button source, Button other)
        {
            source.navigation = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnUp = other, selectOnDown = other, selectOnLeft = other, selectOnRight = other };
        }

        private Text Choice(RectTransform parent, string title, Action previous, Action next)
        {
            var row = Rect(title, parent); Height(row, 96);
            var label = Label("Title", row, title, 25, TextAnchor.UpperLeft); Top(label.rectTransform, 0, 0, 0, 38);
            var prev = Button(row, "‹", previous); var nextButton = Button(row, "›", next);
            var a = (RectTransform)prev.transform; a.anchorMin = a.anchorMax = new Vector2(0,0); a.pivot = Vector2.zero; a.anchoredPosition = Vector2.zero; a.sizeDelta = new Vector2(54,52);
            var b = (RectTransform)nextButton.transform; b.anchorMin = b.anchorMax = new Vector2(1,0); b.pivot = new Vector2(1,0); b.anchoredPosition = Vector2.zero; b.sizeDelta = new Vector2(54,52);
            var value = Label("Value", row, "", 25, TextAnchor.MiddleCenter); value.rectTransform.anchorMin = Vector2.zero; value.rectTransform.anchorMax = new Vector2(1,0); value.rectTransform.pivot = new Vector2(.5f,0); value.rectTransform.sizeDelta = new Vector2(-124,52);
            return value;
        }

        private void PreviewMode()
        {
            var display = Settings.Current.Display;
            display.Mode = display.Mode == GameWindowMode.Windowed ? GameWindowMode.BorderlessFullscreen : GameWindowMode.Windowed;
            Run(() => Settings.PreviewDisplay(display));
        }
        private void StepResolution(int step)
        {
            var list = Settings.AvailableResolutions; if (list.Count == 0) return;
            var display = Settings.Current.Display; int index = Enumerable.Range(0, list.Count).FirstOrDefault(i => list[i].Equals(display.Resolution));
            var chosen = list[(index + step + list.Count) % list.Count]; display.Width = chosen.Width; display.Height = chosen.Height;
            Run(() => Settings.PreviewDisplay(display));
        }
        private void StepFrameRate(int step)
        {
            var list = SystemSettingsService.FrameRateOptions; int index = Enumerable.Range(0, list.Count).First(i => list[i] == Settings.Current.FrameRateLimit);
            Run(() => Settings.SetFrameRateLimit(list[(index + step + list.Count) % list.Count]));
        }

        private void RefreshSettings()
        {
            if (volume == null) return;
            refreshing = true; var value = Settings.Current; volume.SetValueWithoutNotify(value.MasterVolume); volumeValue.text = Mathf.RoundToInt(value.MasterVolume * 100) + "%";
            modeValue.text = value.Display.Mode == GameWindowMode.Windowed ? "窗口" : value.Display.Mode == GameWindowMode.ExclusiveFullscreen ? "全屏（当前独占模式）" : value.Display.Mode == GameWindowMode.MaximizedWindow ? "最大化窗口（当前）" : "无边框全屏";
            resolutionValue.text = value.Display.Resolution.ToString(); capValue.text = value.FrameRateLimit < 0 ? "默认 / 不主动限帧" : value.FrameRateLimit + " FPS";
            syncValue.text = value.VSync ? "开启" : "关闭";
            syncExplanation.text = value.VSync ? "桌面 Player 开启垂直同步时按显示器刷新率同步，Unity 会忽略帧率上限。关闭后恢复所选上限。" : "桌面 Player 关闭垂直同步后，帧率上限才生效。实际帧率仍受设备性能限制。";
#if UNITY_EDITOR
            syncExplanation.text += " Editor 预览不能证明独立 Player 的显示与限帧效果。";
#endif
            bool wasPreview = previewRoot.activeSelf; previewRoot.SetActive(Settings.IsDisplayPreviewPending);
            settingsGroup.interactable = settingsGroup.blocksRaycasts = !Settings.IsDisplayPreviewPending;
            if (Settings.IsDisplayPreviewPending) { UpdatePreviewText(); previewConfirm.interactable = Settings.CanConfirmDisplayPreview; if (!wasPreview) Select(previewConfirm.interactable ? previewConfirm : previewCancel); }
            else if (wasPreview && IsSettingsOpen) Select(settingsBackButton);
            refreshing = false;
        }
        private void UpdatePreviewText() => previewLabel.text = "保留这次显示设置？\n" + Mathf.CeilToInt(Settings.PreviewSecondsRemaining) + " 秒后恢复。Esc、失焦或离开设置也会恢复。";
        private void RefreshVisibility()
        {
            if (session == null) return;
            bool blocking = IsBlockingInput;
            if (blocking && !wasBlocking) CaptureSelection();
            blocker.SetActive(blocking); settingsRoot.SetActive(IsSettingsOpen);
            drawer.gameObject.SetActive(IsPauseOpen || drawerProgress > 0);
            drawer.anchoredPosition = new Vector2(-drawer.rect.width * (1 - drawerProgress), 0);
            drawerGroup.interactable = drawerGroup.blocksRaycasts = IsPauseOpen && !IsSettingsOpen;
            saveError.text = session.SaveError ?? "";
            saveError.gameObject.SetActive(!string.IsNullOrEmpty(session.SaveError));
            if (blocking)
            {
                root.SetAsLastSibling();
                if (IsSettingsOpen) Select(settingsBackButton); else Select(resumeButton);
            }
            else if (wasBlocking && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(CanReceiveSelection(previousSelection) ? previousSelection : null);
                previousSelection = null;
            }
            wasBlocking = blocking;
        }
        private void CaptureSelection()
        {
            if (wasBlocking || EventSystem.current == null) return;
            previousSelection = EventSystem.current.currentSelectedGameObject;
        }
        private static bool CanReceiveSelection(GameObject target)
        {
            if (target == null || !target.activeInHierarchy) return false;
            var selectable = target.GetComponent<Selectable>();
            return selectable == null || (selectable.isActiveAndEnabled && selectable.IsInteractable());
        }
        private static void Select(Selectable target) { if (EventSystem.current != null && target != null && CanReceiveSelection(target.gameObject)) EventSystem.current.SetSelectedGameObject(target.gameObject); }
        private void Layout()
        {
            if (root == null || drawer == null) return;
            var size = root.rect.size; if (size.x <= 0 || size.y <= 0) return;
            drawer.sizeDelta = new Vector2(Mathf.Min(preferredDrawerWidth, size.x - 24), 0);
            settingsFrame.sizeDelta = Vector2.Min(preferredSettingsSize, size - new Vector2(40,40));
            if (previewRoot != null) ((RectTransform)previewRoot.transform.GetChild(0)).sizeDelta = Vector2.Min(preferredPreviewSize, size - new Vector2(64,64));
        }
        private void Run(Action action)
        {
            try { action(); if (status != null) { status.text = ""; status.gameObject.SetActive(false); } }
            catch (Exception exception) { if (status != null) { status.text = "设置未能保存或应用：" + exception.Message; status.gameObject.SetActive(true); } }
        }

        private static RectTransform Rect(string name, Transform parent) { var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent,false); return r; }
        private static void Stretch(RectTransform r, float left=0, float bottom=0, float right=0, float top=0) { r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.offsetMin=new Vector2(left,bottom); r.offsetMax=new Vector2(-right,-top); }
        private static void Top(RectTransform r,float left,float top,float widthDelta,float height) { r.anchorMin=new Vector2(0,1); r.anchorMax=Vector2.one; r.pivot=new Vector2(.5f,1); r.sizeDelta=new Vector2(widthDelta-left,height); r.anchoredPosition=new Vector2(left*.5f,-top); }
        private static void Height(RectTransform r,float height) { var e=r.gameObject.AddComponent<LayoutElement>(); e.minHeight=e.preferredHeight=height; }
        private static VerticalLayoutGroup Vertical(RectTransform r,float spacing) { var g=r.gameObject.AddComponent<VerticalLayoutGroup>(); g.spacing=spacing; g.childControlWidth=g.childControlHeight=true; g.childForceExpandWidth=true; g.childForceExpandHeight=false; return g; }
        private static void Surface(RectTransform r) { r.gameObject.AddComponent<Image>().color=Paper; var border=r.gameObject.AddComponent<Outline>(); border.effectColor=new Color32(66,70,69,255); border.effectDistance=new Vector2(1,-1); }
        private Text Label(string name,Transform parent,string text,int size,TextAnchor alignment) { var label=Rect(name,parent).gameObject.AddComponent<Text>(); label.font=font; label.text=text; label.fontSize=size; label.alignment=alignment; label.color=Ink; label.raycastTarget=false; label.horizontalOverflow=HorizontalWrapMode.Wrap; label.verticalOverflow=VerticalWrapMode.Overflow; return label; }
        private Text RowLabel(RectTransform parent,string text,int size,float height,Color color) { var label=Label("Label",parent,text,size,TextAnchor.UpperLeft); label.color=color; Height(label.rectTransform,height); label.GetComponent<LayoutElement>().preferredHeight=-1; return label; }
        private Button RowButton(RectTransform parent,string text,Action click,float height) { var button=Button(parent,text,click); Height((RectTransform)button.transform,height); return button; }
        private Button Button(Transform parent,string text,Action click)
        {
            var r=Rect(text,parent); var image=r.gameObject.AddComponent<Image>(); image.color=new Color32(225,225,215,255);
            var button=r.gameObject.AddComponent<Button>(); button.targetGraphic=image; var colors=button.colors; colors.highlightedColor=new Color32(202,216,214,255); colors.selectedColor=colors.highlightedColor; colors.pressedColor=new Color32(165,190,188,255); button.colors=colors;
            r.gameObject.AddComponent<SystemMenuScrollFocus>();
            button.onClick.AddListener(()=>click()); var label=Label("Caption",r,text,25,TextAnchor.MiddleCenter); Stretch(label.rectTransform,10,3,10,3); return button;
        }
    }
}
