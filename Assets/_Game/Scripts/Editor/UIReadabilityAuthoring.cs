using System;
using System.Linq;
using CallmeCatgirl.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.EditorTools
{
    // An explicit source-scene migration. No import hook, Awake, or Play-time rebuilding.
    public static class UIReadabilityAuthoring
    {
        [MenuItem("CallmeCatgirl/Apply Readability Pass To Active Scene")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Leave Play before editing UI.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            foreach (var scaler in roots.SelectMany(r => r.GetComponentsInChildren<CanvasScaler>(true)))
            { scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1; }
            var texts = roots.SelectMany(r => r.GetComponentsInChildren<Text>(true)).ToArray();
            foreach (var text in texts)
            {
                if (text.name == "LockedMarker" || text.GetComponentInParent<TaskModuleView>() != null || text.transform.GetComponentsInParent<Transform>().Any(t => t.name.Contains("Debug"))) continue;
                text.fontSize = Mathf.Max(text.fontSize, 30);
                text.resizeTextForBestFit = false;
                if (Mathf.Approximately(text.rectTransform.anchorMin.y, text.rectTransform.anchorMax.y))
                    text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(text.rectTransform.rect.height, text.fontSize * 1.4f));
                text.raycastTarget = text.GetComponent<FloatingWindowGesture>() != null
                    || text.GetComponent<CallmeCatgirl.Gameplay.Narrative.NarrativeWindowGesture>() != null;
            }
            foreach (var tmp in roots.SelectMany(r => r.GetComponentsInChildren<TMPro.TMP_Text>(true)))
            { tmp.fontSize = Mathf.Max(tmp.fontSize, 30); tmp.enableAutoSizing = false; }
            var view = roots.SelectMany(r => r.GetComponentsInChildren<CoreGameplayCanvasView>(true)).FirstOrDefault();
            if (view != null) Gameplay(view);
            else
            {
                var settings = roots.SelectMany(r => r.GetComponentsInChildren<Button>(true)).FirstOrDefault(b => b.name == "OpenSystemSettings");
                if (settings != null) { var r = (RectTransform)settings.transform; r.sizeDelta = new Vector2(210, 64); r.anchoredPosition = new Vector2(-128, 46); }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Readable UI saved in " + scene.path + "; Player uses these authored controls.");
        }

        public static void BuildQa()
        {
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.defaultIsNativeResolution = false; PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            EditorSceneManager.OpenScene(SceneNavigation.GameplayPath); Apply();
            EditorSceneManager.OpenScene(SceneNavigation.MenuPath); Apply();
            EditorSceneManager.OpenScene(SceneNavigation.GameplayPath);
            CallmeCatgirl.Verification.CompletionQaBuild.BuildWindows64();
        }

        private static void Gameplay(CoreGameplayCanvasView view)
        {
            var root = view.transform;
            var so = new SerializedObject(view);
            Func<string, Text> label = name => (Text)so.FindProperty(name).objectReferenceValue;
            var font = label("clockLabel").font;
            var toolbar = root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "Toolbar");
            var row = toolbar.GetComponent<HorizontalLayoutGroup>();
            if (row != null) { row.spacing = 8; row.childControlWidth = row.childControlHeight = false; row.childForceExpandWidth = row.childForceExpandHeight = false; }
            toolbar.sizeDelta = new Vector2(860, 64);
            toolbar.anchoredPosition = new Vector2(-24, -16);
            foreach (var button in toolbar.GetComponentsInChildren<Button>(true))
            {
                Skin(button, font);
                var r = (RectTransform)button.transform;
                r.sizeDelta = new Vector2(button.name == "PauseButton" ? 112 : button.name == "AddRequestButton" ? 174 : button.name == "ResetButton" ? 100 : 104, 64);
                var caption = button.GetComponentInChildren<Text>(true);
                if (caption != null) caption.text = button.name == "0.5SpeedButton" ? "0.5×" : button.name == "SpeedButton" ? "1×" : button.name == "Speed2Button" ? "2×" : button.name == "Speed4Button" ? "4×" : caption.text;
            }
            so.FindProperty("speedActiveColor").colorValue = new Color32(45, 55, 56, 255);
            so.FindProperty("pauseActiveColor").colorValue = new Color32(45, 55, 56, 255);
            if (view.Feedback != null)
            {
                view.Feedback.SpeedSelected = view.Feedback.PauseSelected = new Color32(45, 55, 56, 255);
                EditorUtility.SetDirty(view.Feedback); AssetDatabase.SaveAssetIfDirty(view.Feedback);
            }
            var clock = label("clockLabel"); clock.rectTransform.anchorMin = clock.rectTransform.anchorMax = clock.rectTransform.pivot = new Vector2(0, 1);
            clock.rectTransform.sizeDelta = new Vector2(920, 44); clock.rectTransform.anchoredPosition = new Vector2(24, -48); clock.alignment = TextAnchor.MiddleLeft;
            var economy = label("economyStatusLabel"); economy.rectTransform.anchorMin = new Vector2(0, 1); economy.rectTransform.anchorMax = Vector2.one; economy.rectTransform.pivot = new Vector2(0, 1);
            economy.rectTransform.offsetMin = new Vector2(24, -62); economy.rectTransform.offsetMax = new Vector2(-916, -14); economy.horizontalOverflow = HorizontalWrapMode.Wrap;
            var memory = (Button)so.FindProperty("memoryUpgradeButton").objectReferenceValue; Skin(memory, font);
            ((RectTransform)memory.transform).sizeDelta = new Vector2(190, 64);
            var boardPanel = root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "BoardPanel");
            var board = (RectTransform)so.FindProperty("board").objectReferenceValue;
            var container = (RectTransform)board.parent; container.offsetMin = new Vector2(24, 184);
            var title = label("infoTitle"); title.transform.SetParent(boardPanel, false); title.gameObject.SetActive(true);
            title.fontSize = 30; title.text = "选中请求后显示完整名称"; title.alignment = TextAnchor.MiddleLeft;
            title.horizontalOverflow = HorizontalWrapMode.Wrap; title.verticalOverflow = VerticalWrapMode.Truncate;
            title.rectTransform.anchorMin = new Vector2(0, 0); title.rectTransform.anchorMax = new Vector2(1, 0);
            title.rectTransform.pivot = new Vector2(.5f, 0); title.rectTransform.anchoredPosition = new Vector2(0, 88); title.rectTransform.sizeDelta = new Vector2(-40, 76);
            string[] actions = { "rotateButton", "withdrawButton", "submitButton", "nextRoundButton" };
            float x = 0;
            for (int n = 0; n < actions.Length; n++)
            {
                var button = (Button)so.FindProperty(actions[n]).objectReferenceValue; Skin(button, font);
                var r = (RectTransform)button.transform; r.anchorMin = r.anchorMax = new Vector2(0, .5f); r.pivot = new Vector2(0, .5f);
                r.anchoredPosition = new Vector2(x, 0); r.sizeDelta = new Vector2(n == 3 ? 180 : 144, 64); x += r.sizeDelta.x + 14;
            }
            var info = (RectTransform)((GameObject)so.FindProperty("infoPanel").objectReferenceValue).transform;
            info.anchoredPosition = new Vector2(0, 40); info.sizeDelta = new Vector2(-36, 76);
            string[] bottom = { "ReturnToMenu", "OpenTraining", "SaveRun", "LoadRun" }; x = 16;
            foreach (string name in bottom)
            {
                var b = root.GetComponentsInChildren<Button>(true).First(q => q.name == name); Skin(b, font);
                var r = (RectTransform)b.transform; r.anchorMin = r.anchorMax = new Vector2(0, 0); r.pivot = new Vector2(0, 0);
                r.anchoredPosition = new Vector2(x, 10); r.sizeDelta = new Vector2(name == "OpenTraining" ? 220 : 160, 64); x += r.sizeDelta.x + 14;
            }
            var policy = root.GetComponentsInChildren<Button>(true).First(b => b.name == "OpenProgression"); Skin(policy, font);
            ((RectTransform)policy.transform).sizeDelta = new Vector2(150, 64);
            ((RectTransform)policy.transform).anchoredPosition = new Vector2(-92, 42);
            foreach (var workspace in root.GetComponentsInChildren<ConversationWorkspaceView>(true))
            {
                var contact = (RectTransform)new SerializedObject(workspace).FindProperty("contactTemplate").objectReferenceValue;
                if (contact == null) continue;
                var layout = contact.GetComponent<LayoutElement>(); if (layout != null) layout.preferredHeight = 176;
                var summary = contact.Find("Summary") as RectTransform; if (summary != null) summary.sizeDelta = new Vector2(summary.sizeDelta.x, 88);
            }
            foreach (var module in root.GetComponentsInChildren<TaskModuleView>(true))
            {
                var moduleSo = new SerializedObject(module); var text = (Text)moduleSo.FindProperty("titleLabel").objectReferenceValue;
                text.fontSize = 30; text.resizeTextForBestFit = false; text.alignment = TextAnchor.MiddleCenter;
            }
            var page = root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "TrainingFullPage");
            foreach (var hint in page.GetComponentsInChildren<Text>(true).Where(t => t.name == "BoardHint")) hint.text = "点击库存添加，拖动调整";
            foreach (var slot in page.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Slot_")))
            { var t = slot.GetComponentInChildren<Text>(true); t.fontSize = 26; t.text = "＋"; t.alignment = TextAnchor.MiddleCenter; }
            foreach (var input in page.GetComponentsInChildren<InputField>(true))
            { input.textComponent.fontSize = 30; if (input.placeholder is Text t) t.fontSize = 30; }
            foreach (var dropdown in page.GetComponentsInChildren<Dropdown>(true))
            {
                dropdown.captionText.fontSize = 30;
                dropdown.itemText.fontSize = 30;
                ((RectTransform)dropdown.itemText.transform.parent).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 64);
            }
            foreach (var button in page.GetComponentsInChildren<Button>(true).Where(b => !b.name.StartsWith("Slot_"))) Skin(button, font);
            var model = root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "ModelCardTemplate");
            ModelCard(model);
            var cards = root.GetComponentsInChildren<GridLayoutGroup>(true).FirstOrDefault(g => g.transform.name == "Cards");
            if (cards != null)
            {
                cards.cellSize = new Vector2(model.rect.width, 714); cards.childAlignment = TextAnchor.UpperCenter;
                var catalogue = (RectTransform)cards.transform.parent;
                catalogue.anchorMin = Vector2.zero; catalogue.anchorMax = Vector2.one; catalogue.offsetMin = new Vector2(35, 24); catalogue.offsetMax = new Vector2(-35, -230);
            }
            var gallery = view.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ModelGalleryView>(true)).First(); var gallerySo = new SerializedObject(gallery);
            var galleryBody = (RectTransform)gallerySo.FindProperty("body").objectReferenceValue;
            var frame = (RectTransform)galleryBody.parent; var galleryLayout = (RectTransform)frame.parent;
            galleryLayout.sizeDelta = new Vector2(1600, 1032);
            frame.anchorMin = Vector2.zero; frame.anchorMax = Vector2.one; frame.offsetMin = new Vector2(24, 24); frame.offsetMax = new Vector2(-24, -24);
            galleryBody.anchorMin = Vector2.zero; galleryBody.anchorMax = Vector2.one; galleryBody.offsetMin = galleryBody.offsetMax = Vector2.zero;
            foreach (string property in new[] { "pageButton", "closeButton" })
            {
                var b = (Button)gallerySo.FindProperty(property).objectReferenceValue; Skin(b, font); var r = (RectTransform)b.transform;
                r.anchorMin = r.anchorMax = r.pivot = Vector2.one; r.anchoredPosition = new Vector2(property == "pageButton" ? -240 : -40, -38); r.sizeDelta = new Vector2(property == "pageButton" ? 280 : 160, 64);
            }
            foreach (var b in model.GetComponentsInChildren<Button>(true)) Skin(b, font);
            foreach (var chat in new[] { (ConversationPopupView)so.FindProperty("conversationPopup").objectReferenceValue }.Where(c => c != null))
            {
                var close = (Button)new SerializedObject(chat).FindProperty("closeButton").objectReferenceValue;
                var caption = close.transform.Find("CloseCaption")?.GetComponent<Text>();
                if (caption == null) { caption = new GameObject("CloseCaption", typeof(RectTransform), typeof(Text)).GetComponent<Text>(); caption.transform.SetParent(close.transform, false); }
                caption.font = font; caption.text = "×"; caption.fontSize = 36; caption.alignment = TextAnchor.MiddleCenter; caption.color = new Color32(22, 22, 22, 255); caption.raycastTarget = false;
                caption.rectTransform.anchorMin = Vector2.zero; caption.rectTransform.anchorMax = Vector2.one; caption.rectTransform.offsetMin = caption.rectTransform.offsetMax = Vector2.zero;
                ((RectTransform)close.transform).sizeDelta = new Vector2(64, 64);
            }
            var progression = root.GetComponentsInChildren<CallmeCatgirl.Gameplay.Progression.ProgressionPanel>(true).First();
            var progressionSo = new SerializedObject(progression);
            for (int index = 0; index < 2; index++)
            {
                var b = (Button)progressionSo.FindProperty(index == 0 ? "firstPage" : "secondPage").objectReferenceValue; Skin(b, font);
                var r = (RectTransform)b.transform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(20 + index * 416, -56); r.sizeDelta = new Vector2(400, 64);
            }
            var policyClose = (Button)progressionSo.FindProperty("closeButton").objectReferenceValue; Skin(policyClose, font);
            var policyCloseRect = (RectTransform)policyClose.transform; policyCloseRect.anchorMin = policyCloseRect.anchorMax = policyCloseRect.pivot = Vector2.one; policyCloseRect.anchoredPosition = new Vector2(-16, -8); policyCloseRect.sizeDelta = new Vector2(134, 64);
            var graph = (RectTransform)((ScrollRect)progressionSo.FindProperty("graphScroll").objectReferenceValue).transform; graph.offsetMax = new Vector2(-16, -132);
            var node = (RectTransform)progressionSo.FindProperty("nodeTemplate").objectReferenceValue;
            var nodeTitle = node.Find("Title").GetComponent<Text>(); nodeTitle.fontSize = 24; nodeTitle.rectTransform.offsetMin = new Vector2(12, 128); nodeTitle.rectTransform.offsetMax = new Vector2(-12, -8);
            var mechanics = node.Find("Mechanics").GetComponent<Text>(); mechanics.fontSize = 22; mechanics.rectTransform.offsetMin = new Vector2(12, 46); mechanics.rectTransform.offsetMax = new Vector2(-12, -70);
            node.Find("Status").GetComponent<Text>().fontSize = 24;
            Notification(view, so, font);
            ModulePrefab((TaskModuleView)so.FindProperty("modulePrefab").objectReferenceValue);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ModulePrefab(TaskModuleView module)
        {
            string path = AssetDatabase.GetAssetPath(module);
            if (string.IsNullOrEmpty(path)) return;
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var serialized = new SerializedObject(prefab.GetComponent<TaskModuleView>());
                var text = (Text)serialized.FindProperty("titleLabel").objectReferenceValue;
                text.fontSize = 26; text.resizeTextForBestFit = false; text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                PrefabUtility.SaveAsPrefabAsset(prefab, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

        private static void ModelCard(RectTransform model)
        {
            model.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 714);
            Action<string, float, float, float, float> place = (name, x, y, w, h) =>
            {
                var r = model.Find(name) as RectTransform; if (r == null) return;
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            };
            place("ArchitectureIcon", 24, 20, 52, 52); place("ModelName", 90, 16, 600, 84);
            place("ArchitectureTitle", 24, 112, 672, 76); place("Throughput", 24, 196, 672, 44);
            string[] abilities = { "Writing", "Code", "Reasoning", "Retrieval" };
            for (int i = 0; i < abilities.Length; i++) place("Ability_" + abilities[i], 24 + (i % 2) * 336, 246 + (i / 2) * 46, 320, 44);
            place("Outline", 24, 356, 160, 90); place("OutlineSummary", 210, 356, 480, 44); place("Interval", 210, 402, 480, 44);
            place("ModelState", 24, 462, 672, 44); place("Rename", 24, 516, 510, 64); place("改名", 550, 516, 146, 64); place("SelectModel", 24, 600, 672, 64);
            var uuid = model.Find("ModelUuid"); if (uuid != null) uuid.gameObject.SetActive(false);
        }

        private static void Notification(CoreGameplayCanvasView view, SerializedObject so, Font font)
        {
            var parent = view.transform; var found = parent.Find("SaveNotification");
            var r = found as RectTransform;
            if (r == null)
            {
                r = new GameObject("SaveNotification", typeof(RectTransform), typeof(Image), typeof(CanvasGroup)).GetComponent<RectTransform>(); r.SetParent(parent, false);
                var text = new GameObject("Message", typeof(RectTransform), typeof(Text)).GetComponent<Text>(); text.transform.SetParent(r, false);
                text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = new Vector2(20, 8); text.rectTransform.offsetMax = new Vector2(-20, -8);
                text.font = font; text.fontSize = 32; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; text.text = "游戏已保存。";
            }
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, 1); r.anchoredPosition = new Vector2(0, -144); r.sizeDelta = new Vector2(760, 84); r.SetAsLastSibling();
            var image = r.GetComponent<Image>(); image.color = new Color32(244, 248, 242, 255); image.raycastTarget = false;
            InnerBorderGraphic.Attach(r, 1, BorderEdges.All, new Color32(84, 108, 98, 255));
            var group = r.GetComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = group.interactable = false;
            so.FindProperty("persistenceGroup").objectReferenceValue = group; so.FindProperty("persistenceLabel").objectReferenceValue = r.GetComponentInChildren<Text>(true);
        }
        private static void Skin(Button button, Font font)
        {
            const string art = "Assets/_Game/Art/UI/独立切图_v1/";
            var normal = AssetDatabase.LoadAssetAtPath<Sprite>(art + "button_normal.png");
            var selected = AssetDatabase.LoadAssetAtPath<Sprite>(art + "button_selected.png");
            var image = button.GetComponent<Image>(); if (image == null) return;
            image.sprite = normal; image.type = Image.Type.Sliced; image.material = null; image.color = Color.white;
            button.targetGraphic = image; button.transition = Selectable.Transition.None;
            foreach (var text in button.GetComponentsInChildren<Text>(true))
            { text.enabled = true; text.font = font; text.fontSize = 30; text.fontStyle = FontStyle.Normal; text.alignment = TextAnchor.MiddleCenter; text.color = new Color32(22, 22, 22, 255); text.resizeTextForBestFit = false; text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Overflow; text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero; }
            var artwork = button.GetComponent<ButtonArtwork>() ?? button.gameObject.AddComponent<ButtonArtwork>();
            var serialized = new SerializedObject(artwork);
            serialized.FindProperty("normalSprite").objectReferenceValue = normal; serialized.FindProperty("selectedSprite").objectReferenceValue = selected;
            serialized.FindProperty("hoverSprite").objectReferenceValue = normal; serialized.FindProperty("pressedSprite").objectReferenceValue = selected;
            serialized.FindProperty("disabledSprite").objectReferenceValue = normal;
            serialized.FindProperty("label").objectReferenceValue = button.GetComponentInChildren<Text>(true);
            serialized.FindProperty("normalIncludesText").boolValue = false; serialized.FindProperty("selectedIncludesText").boolValue = false; serialized.FindProperty("hideLabel").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo(); artwork.enabled = true;
        }
    }
}
