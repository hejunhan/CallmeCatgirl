using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.Narrative;
using CallmeCatgirl.Gameplay.Progression;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Gameplay.Training;
using CallmeCatgirl.UI;
using CallmeCatgirl.UI.SystemMenu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CallmeCatgirl.Verification
{
    /// <summary>Opt-in Player verification. No Editor, ordinary runtime, credentials, network or desktop input automation.</summary>
    public sealed class CompletionQaPlayer : MonoBehaviour
    {
        public const string QaCompany = "CodexValidation";
        public const string QaProduct = "CallmeCatgirl-QA-20261018";
        [Serializable] public sealed class Check
        { public string stage, detail; public bool passed, synthetic; public double elapsedSeconds; }
        [Serializable] public sealed class Capture
        { public string file, stage; public int width, height; public bool synthetic; }
        [Serializable] public sealed class SettingsObservation
        {
            public float masterBefore, masterDuring, masterAfter;
            public int capDuring, vSyncDuring, sampledFrames;
            public double sampledSeconds, observedFramesPerSecond;
            public int rollbackFromWidth, rollbackFromHeight, rollbackToWidth, rollbackToHeight;
            public double rollbackElapsedSeconds;
            public bool physicalFullscreenTested;
            public string physicalFullscreenStatus = "Not tested: windowed QA must not switch the physical display to fullscreen.";
            public string frameRateObservation = "Measured background Player cadence is observational; cap/VSync property checks are separate.";
        }
        [Serializable] public sealed class Report
        {
            public int version = 1;
            public string status = "Running", stage = "bootstrap", mode = "full", error, errorStack, startedUtc, finishedUtc, slot;
            public bool success, tutorialCompleted, saveContinueVerified, syntheticFixturesOnlyForEndings = true;
            public int completedTasks, fundsAfterInstall, physicalRamAfterInstall;
            public List<Check> checks = new List<Check>();
            public List<Capture> captures = new List<Capture>();
            public SettingsObservation settings = new SettingsObservation();
        }
        private readonly Report report = new Report();
        private string output;
        private CoreGameplayController owner;
        private double started;
        private bool finishing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if !UNITY_EDITOR
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("--completion-qa", StringComparer.Ordinal)) return;
            if (FindAnyObjectByType<CompletionQaPlayer>() != null) return;
            var go = new GameObject("CompletionQA-ExplicitPlayerOnly"); DontDestroyOnLoad(go);
            var driver = go.AddComponent<CompletionQaPlayer>();
            driver.StartCoroutine(driver.Guarded(driver.Run(args)));
#endif
        }
        private IEnumerator Guarded(IEnumerator work)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(work);
            while (stack.Count > 0)
            {
                object current = null; bool moved = false; Exception failure = null;
                try { moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current; }
                catch (Exception ex) { failure = ex; }
                if (failure != null) { Finish(false, failure); yield break; }
                if (!moved) { stack.Pop(); continue; }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }
            Finish(true, null);
        }
        private IEnumerator Run(string[] args)
        {
            started = Time.realtimeSinceStartupAsDouble; report.startedUtc = DateTime.UtcNow.ToString("o");
            output = Argument(args, "--qa-output");
            Require(!string.IsNullOrWhiteSpace(output) && Path.IsPathRooted(output), "--qa-output requires an absolute output directory.");
            output = Path.GetFullPath(output); Directory.CreateDirectory(output);
            Require(Application.companyName == QaCompany && Application.productName == QaProduct,
                "The QA driver only runs under the isolated QA company/product identity.");
            report.mode = Argument(args, "--qa-mode") ?? "full";
            Require(report.mode == "full" || report.mode == "fixtures" || report.mode == "settings", "Unknown QA mode; use full, fixtures or settings.");
            Application.runInBackground = true;
            report.slot = "completion-player-qa-" + Guid.NewGuid().ToString("N");
            Stage("menu");
            if (SceneManager.GetActiveScene().path != SceneNavigation.MenuPath)
                yield return SceneManager.LoadSceneAsync(SceneNavigation.MenuPath, LoadSceneMode.Single);
            yield return Frames(3);
            yield return CaptureBoth("menu", false);
            var navigation = FindAnyObjectByType<SceneNavigation>(); Require(navigation != null, "Main menu navigation was not found.");
            Record("menu", "Normal menu scene is loaded; new game uses SceneNavigation.StartNewGame().");
            if (report.mode == "fixtures")
            {
                Stage("synthetic-ending-fixtures"); yield return EndingFixtures();
                Record("synthetic-ending-fixtures", "Explicit fixtures-only mode: narrative panel snapshots and closures; no game progression or tutorial claimed.", true);
                yield break;
            }
            if (report.mode == "settings")
            { Stage("system-settings"); yield return VerifySettings(); yield break; }
            navigation.StartNewGame();
            yield return WaitFor(() => FindAnyObjectByType<CoreGameplayController>() != null, 30, "Game controller did not load.");
            owner = FindAnyObjectByType<CoreGameplayController>(); yield return Frames(3);
            owner.ConfigureSaveSlot(report.slot); Command(new GameCommand { kind = "clock", paused = true });

            Stage("opening-zero");
            Require(owner.Simulation.OpenCellCount == 0 && owner.Simulation.Tasks.Count == 0 && owner.MemoryLevel == 0,
                "Fresh game must have zero compute cells, zero tasks and zero physical RAM.");
            yield return CaptureBoth("opening-zero", false);
            Require(owner.Narrative.Choose("mew"), "Displayed writing opening choice was rejected.");
            yield return Frames(3);
            Require(owner.Narrative.CenterCellRevealed && owner.Narrative.MemoryCount == 0 && owner.Simulation.OpenCellCount == 1,
                "Opening did not reveal exactly one compute cell while keeping physical RAM at zero.");
            var training = owner.Training.Session;
            Require(training.Corpus.Count == 1 && Math.Abs(training.Corpus[0].Scores[AbilityKind.Writing] - 1) < 1e-9,
                "Opening must grant exactly one real Writing corpus point.");
            CloseNarrative(); yield return Frames(2);
            Record("opening-zero", "Actual narrative choice granted one Writing point and revealed one compute cell; physical RAM stayed zero.");

            Stage("training-arrangement");
            Command(new GameCommand { kind = "pause", paused = true });
            yield return OpenTrainingWorkspace();
            var workspace = FindAnyObjectByType<TrainingWorkspaceView>();
            Require(workspace.TryPlaceConversation(NarrativeSession.DoctorId, 0), "The real corpus draft rejected the opening corpus.");
            Require(workspace.Investments.Count == 1 && Math.Abs(workspace.Investments[0].Points - 1) < 1e-9, "Draft must contain one invested point.");
            workspace.SetInventoryFilter(AbilityKind.Code, "不存在的长中文筛选条件也必须保留右侧已经编排好的语料");
            Require(workspace.Investments.Count == 1, "Filtering cleared the real recipe draft.");
            yield return CaptureBoth("training-arranged-filter-preserved", false);
            workspace.SetInventoryFilter(null, "");
            FindAnyObjectByType<TrainingPanel>().Close(); yield return Frames(2);
            Command(new GameCommand { kind = "train", recipe = TrainingSession.StarterRecipeId, architecture = "baseline",
                displayName = "第一台由一份语料训练的模型", investments = new[] { new ConversationInvestment { ConversationId = NarrativeSession.DoctorId, Points = 1 } } });
            var job = training.Jobs.Single();
            Require(job.PaidCost == 0 && owner.Simulation.Tasks[job.RequestId].Definition.Shape.Count == 1,
                "First real training must cost zero money and occupy one compute cell.");
            yield return OpenTrainingWorkspace();
            Require(training.ConversationCorpora.All(c => c.AvailablePoints <= 1e-9), "Consumed opening point is still available in inventory.");
            yield return CaptureBoth("training-zero-inventory", false);
            FindAnyObjectByType<TrainingPanel>().Close(); yield return Frames(2);
            yield return FinishTask(job.RequestId);
            Require(job.Completed && training.ActiveModelId == job.Output.Id, "First training did not complete and select the actual resulting model.");
            CloseNarrative(); yield return Frames(2);
            Record("training-arrangement", "Real draft survived filtering; GameCommand created and completed the one-point, one-cell starter job without payment.");

            Stage("tutorial-orders");
            Require(owner.Simulation.Tasks.ContainsKey(NarrativeSession.FirstDoctorRequestId), "Doctor's real first order was not created.");
            yield return FinishTask(NarrativeSession.FirstDoctorRequestId); CloseNarrative(); yield return Frames(2);
            Require(owner.Simulation.Tasks.ContainsKey(NarrativeSession.FirstOrdinaryRequestId), "First ordinary order was not created after doctor settlement.");
            yield return FinishTask(NarrativeSession.FirstOrdinaryRequestId); CloseNarrative(); yield return Frames(2);
            if (owner.Story.IsReply) Command(new GameCommand { kind = "reply", choice = 0, storyRevision = owner.Story.Revision });
            if (owner.Story.IsOpen && !owner.Story.IsReply) Command(new GameCommand { kind = "closeHistory" });
            CloseNarrative(); yield return Frames(3);
            Require(owner.CompletedTasks == 2 && owner.Funds == 120, "Two tutorial orders must really settle: expected completed=2 and funds=120; actual=" + owner.CompletedTasks + "/" + owner.Funds);
            Record("tutorial-orders", "Both real task submissions settled through GameCommand; completed=2, funds=120.");

            Stage("memory-install-commit");
            owner.UpgradeMemory(); Require(owner.IsMemoryInstallPending, "First installation did not open a pending transaction.");
            var install = FindObjectsByType<MemoryInstallGame>(FindObjectsInactive.Include).Single(i => i.IsOpen);
            Require(owner.MemoryLevel == 0 && owner.Funds == 120, "Opening installation changed memory or paid before commit.");
            install.SkipInstall(); yield return Frames(3);
            Require(owner.MemoryLevel == 1 && owner.Narrative.MemoryCount == 1 && owner.Funds == 20, "Skip commit did not install exactly once and pay 100.");
            report.tutorialCompleted = true; report.completedTasks = owner.CompletedTasks;
            report.fundsAfterInstall = owner.Funds; report.physicalRamAfterInstall = owner.MemoryLevel;
            Record("memory-install-commit", "Actual SkipInstall committed physical RAM 0→1 and funds 120→20; no direct state assignment.");
            yield return CaptureBoth("main-after-first-install", false);

            Stage("save-menu-continue");
            CloseNarrative();
            int savedFunds = owner.Funds, savedCorpus = owner.Training.Session.Corpus.Count, savedJobs = owner.Training.Session.Jobs.Count;
            string savedNarrative = JsonUtility.ToJson(owner.Narrative.Capture());
            Command(new GameCommand { kind = "save" });
            navigation = FindAnyObjectByType<SceneNavigation>(); Require(navigation != null, "Gameplay menu navigation is missing.");
            navigation.ReturnToMenu();
            yield return WaitFor(() => SceneManager.GetActiveScene().path == SceneNavigation.MenuPath, 30, "Save-and-return did not reach the menu.");
            yield return Frames(3);
            navigation = FindAnyObjectByType<SceneNavigation>(); navigation.ContinueSlot(report.slot);
            yield return WaitFor(() => SceneManager.GetActiveScene().path == SceneNavigation.GameplayPath && FindAnyObjectByType<CoreGameplayController>() != null,
                30, "ContinueSlot did not reload gameplay.");
            owner = FindAnyObjectByType<CoreGameplayController>(); yield return Frames(4);
            Command(new GameCommand { kind = "clock", paused = true });
            Require(owner.Funds == savedFunds && owner.MemoryLevel == 1 && owner.CompletedTasks == 2
                && owner.Training.Session.Corpus.Count == savedCorpus && owner.Training.Session.Jobs.Count == savedJobs
                && JsonUtility.ToJson(owner.Narrative.Capture()) == savedNarrative, "Continue changed saved balances, tutorial ledger, jobs or narrative state.");
            report.saveContinueVerified = true; Record("save-menu-continue", "Named isolated slot survived normal save/menu/ContinueSlot without repeating rewards or orders.");
            yield return CaptureBoth("main-after-continue", false);

            Stage("policy-ui");
            Command(new GameCommand { kind = "pause", paused = true });
            ClickNamed("OpenProgression"); yield return Frames(4);
            var policy = FindAnyObjectByType<ProgressionPanel>(); Require(policy != null, "Policy entry did not open a real policy panel.");
            policy.ShowPage(0); yield return CaptureBoth("policy-page-1", false);
            policy.ShowPage(1); yield return CaptureBoth("policy-page-2", false);
            policy.Close(); yield return Frames(3);
            Record("policy-ui", "Real policy entry and public ShowPage navigation captured; no policy purchases or fabricated terminal progression.");

            Stage("system-settings");
            yield return VerifySettings();
            Stage("synthetic-ending-fixtures");
            yield return EndingFixtures();
            Record("synthetic-ending-fixtures", "Four narrative-only fixtures and response closures captured. These do not prove whole-run policy progression.", true);
        }

        private IEnumerator OpenTrainingWorkspace()
        {
            if (!owner.IsTrainingOpen) owner.OpenTraining();
            yield return Frames(3);
            Require(owner.IsTrainingOpen, "Public OpenTraining did not open the model/training page: " + owner.Message);
            var workspace = FindObjectsByType<TrainingWorkspaceView>(FindObjectsInactive.Include).FirstOrDefault();
            if (workspace == null || !workspace.gameObject.activeInHierarchy)
                ClickCaption("语料与训练");
            yield return Frames(4);
            Require(FindAnyObjectByType<TrainingWorkspaceView>() != null, "The real model catalogue navigation did not open the full-screen corpus workspace.");
        }
        private IEnumerator FinishTask(string id)
        {
            CloseNarrative();
            Command(new GameCommand { kind = "place", id = id, x = (owner.Simulation.Width - 1) / 2, y = (owner.Simulation.Height - 1) / 2 });
            Command(new GameCommand { kind = "pause", paused = false });
            for (int step = 0; step < 240 && owner.Simulation.Tasks[id].State == RequestState.Running; step++)
            { Command(new GameCommand { kind = "advance", seconds = .25 }); if (step % 20 == 0) yield return null; }
            Require(owner.Simulation.Tasks[id].State == RequestState.Ready, "Real request did not reach Ready in 60 game seconds: " + id);
            Command(new GameCommand { kind = "submit", id = id }); yield return Frames(3);
        }
        private void CloseNarrative()
        {
            for (int n = 0; n < 8 && owner.Narrative.CurrentFrame != null; n++)
            {
                var frame = owner.Narrative.CurrentFrame;
                if (frame.CanClose && owner.Narrative.CloseConversation()) return;
                Require(frame.Choices.Count > 0 && owner.Narrative.Choose(frame.Choices[0].Id), "Cannot close current tutorial conversation: " + frame.Id);
            }
            Require(owner.Narrative.CurrentFrame == null, "Tutorial narrative did not release its modal claim.");
        }
        private GameCommandResult Command(GameCommand command)
        {
            var result = owner.ExecuteCommand(command);
            Require(result.ok, "GameCommand " + command.kind + " rejected with " + result.code + ": " + result.message);
            return result;
        }
        private IEnumerator VerifySettings()
        {
            var menu = FindAnyObjectByType<SystemMenuHost>(); Require(menu != null, "Shared settings host was not found.");
            menu.OpenSettings(); yield return Frames(3);
            yield return CaptureBoth("settings", false);
            var settings = menu.Settings; var before = settings.Current;
            report.settings.masterBefore = AudioListener.volume;
            settings.SetMasterVolume(.35f); yield return Frames(2);
            report.settings.masterDuring = AudioListener.volume;
            Require(Math.Abs(AudioListener.volume - .35f) < .001f, "Master slider service did not reach AudioListener.volume.");
            settings.SetMasterVolume(before.MasterVolume); report.settings.masterAfter = AudioListener.volume;
            Require(Math.Abs(AudioListener.volume - report.settings.masterBefore) < .001f, "Master volume was not restored.");
            settings.SetVSync(false); settings.SetFrameRateLimit(30); yield return Frames(3);
            Require(QualitySettings.vSyncCount == 0 && Application.targetFrameRate == 30, "Actual Player frame cap/VSync API parameters did not apply.");
            double sampleStart = Time.realtimeSinceStartupAsDouble; int frames = 0;
            while (Time.realtimeSinceStartupAsDouble - sampleStart < 1.5) { frames++; yield return null; }
            report.settings.sampledFrames = frames; report.settings.sampledSeconds = Time.realtimeSinceStartupAsDouble - sampleStart;
            report.settings.observedFramesPerSecond = frames / report.settings.sampledSeconds;
            settings.SetFrameRateLimit(60); settings.SetVSync(true); yield return Frames(2);
            report.settings.capDuring = Application.targetFrameRate; report.settings.vSyncDuring = QualitySettings.vSyncCount;
            Require(Application.targetFrameRate == 60 && QualitySettings.vSyncCount == 1, "VSync enabled must preserve the selected cap property while overriding desktop pacing semantics.");
            settings.SetVSync(before.VSync); settings.SetFrameRateLimit(before.FrameRateLimit);

            yield return SetWindow(1280, 720);
            settings.RefreshDisplayState(); yield return Frames(3);
            var confirmed = settings.ConfirmedDisplay;
            Require(confirmed.Mode == GameWindowMode.Windowed, "QA preview must remain windowed.");
            var alternate = settings.AvailableResolutions.Where(r => !r.Equals(confirmed.Resolution))
                .OrderBy(r => Math.Abs(r.Width - 1920) + Math.Abs(r.Height - 1080)).FirstOrDefault();
            Require(alternate.Width > 0 && alternate.Height > 0, "The Player exposes no alternate supported resolution for an actual preview rollback test.");
            double previewStart = Time.realtimeSinceStartupAsDouble;
            Require(settings.PreviewDisplay(new DisplayConfiguration(alternate.Width, alternate.Height, GameWindowMode.Windowed)), "Display preview was not started.");
            yield return WaitFor(() => Screen.width == alternate.Width && Screen.height == alternate.Height, 6, "Windowed preview did not take effect across rendered frames.");
            report.settings.rollbackFromWidth = Screen.width; report.settings.rollbackFromHeight = Screen.height;
            yield return CaptureOne("settings-display-preview", false);
            yield return WaitFor(() => !settings.IsDisplayPreviewPending, 18, "Unconfirmed display preview did not time out.");
            yield return WaitFor(() => Screen.width == confirmed.Width && Screen.height == confirmed.Height, 6, "Timed-out preview did not restore the original window size.");
            report.settings.rollbackToWidth = Screen.width; report.settings.rollbackToHeight = Screen.height;
            report.settings.rollbackElapsedSeconds = Time.realtimeSinceStartupAsDouble - previewStart;
            Require(Screen.fullScreenMode == FullScreenMode.Windowed, "QA unexpectedly changed the physical fullscreen mode.");
            menu.CloseAll(); yield return Frames(3);
            Record("system-settings", "Actual Player backend: master volume applied/restored; VSync/cap properties applied/restored; unconfirmed windowed preview rolled back. Physical fullscreen deliberately untested.");
        }

        private IEnumerator EndingFixtures()
        {
            var font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name.Contains("Harmony"))
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("CompletionQA-SyntheticNarrativeOnly", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2000;
            var scale = root.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scale.referenceResolution = new Vector2(1600, 1000); scale.matchWidthOrHeight = .5f;
            var fixtureLabel = new GameObject("SyntheticFixtureLabel", typeof(RectTransform), typeof(Text)); fixtureLabel.transform.SetParent(root.transform, false);
            var text = fixtureLabel.GetComponent<Text>(); text.font = font; text.fontSize = 16; text.color = Color.yellow; text.text = "QA · SYNTHETIC NARRATIVE FIXTURE"; text.raycastTarget = false;
            var r = text.rectTransform; r.anchorMin = new Vector2(0, 1); r.anchorMax = Vector2.one; r.pivot = new Vector2(0, 1); r.sizeDelta = new Vector2(0, 28); r.anchoredPosition = new Vector2(12, -4);
            var panel = NarrativePanel.Create(root.transform, font, NarrativeSession.AdoptLegacy(5)); fixtureLabel.transform.SetAsLastSibling();
            var low = Fixture(-10); panel.Bind(low, font); yield return Frames(3);
            yield return CaptureBoth("synthetic-ending-low", true);
            var removed = panel.GetComponentsInChildren<Button>().Single(b => b.name == "Choice_remember").gameObject;
            Require(low.Choose("remember"), "Low fixture refused the memory response."); yield return Frames(2);
            Require(low.Notice == "你没有" && !low.CurrentFrame.Choices.Any(c => c.Id == "remember")
                && (EventSystem.current == null || EventSystem.current.currentSelectedGameObject != removed), "Low fixture did not erase the option and remove its focus.");
            yield return CaptureBoth("synthetic-ending-low-erased-notice", true);
            low.DismissNotice(); Require(low.Choose("destroy"), "Low fixture destroy response failed.");
            yield return CaptureBoth("synthetic-ending-low-closure", true); Require(low.Choose("curtain"), "Low fixture curtain failed.");
            foreach (string response in new[] { "comply", "decline", "analyze" })
            {
                var normal = Fixture(0); panel.Bind(normal, font); yield return Frames(2);
                if (response == "comply")
                {
                    yield return CaptureBoth("synthetic-ending-normal-long-chinese", true);
                    var scroll = panel.GetComponentInChildren<ScrollRect>(); scroll.verticalNormalizedPosition = 0;
                    yield return Frames(2); yield return CaptureBoth("synthetic-ending-normal-options-bottom", true);
                }
                Require(normal.Choose(response), "Normal fixture response failed: " + response);
                yield return CaptureBoth("synthetic-ending-normal-closure-" + response, true); normal.Choose("curtain");
            }
            foreach (string response in new[] { "play", "joke" })
            {
                var good = Fixture(10); panel.Bind(good, font); yield return Frames(2);
                if (response == "play") yield return CaptureBoth("synthetic-ending-good", true);
                Require(good.Choose(response), "Good fixture response failed: " + response);
                yield return CaptureBoth("synthetic-ending-good-closure-" + response, true); good.Choose("curtain");
            }
            var companion = NarrativeSession.AdoptLegacy(5); companion.UnlockCompanionOpportunity();
            for (int beat = 1; beat <= 7; beat++)
            {
                companion.OpenContact(NarrativeSession.CompanionId);
                Require(companion.Choose(beat == 7 ? "accept-invitation" : "first"), "Synthetic companion beat failed.");
                companion.Choose("continue"); companion.ObserveRequestSettled("synthetic-qa-narrative-only-order-" + beat);
            }
            companion = NarrativeSession.Restore(companion.Capture()); companion.RequestPolicyEnding(0, -5, 5); panel.Bind(companion, font);
            yield return CaptureBoth("synthetic-ending-explicit-route-choice", true);
            Require(companion.Choose("leave") && companion.EndingReason == NarrativeEndingReason.Companion, "Explicit companion route was not selected.");
            yield return CaptureBoth("synthetic-ending-companion", true);
            companion.Choose("together"); yield return CaptureBoth("synthetic-ending-companion-closure", true); companion.Choose("curtain");
            Require(companion.PendingEffects.Count(e => e.Kind == NarrativeEffectKind.EndRun) == 1, "Synthetic companion terminal effect was not exactly once.");
            Destroy(root); yield return Frames(2);
        }
        private static NarrativeSession Fixture(int reputation)
        {
            var session = NarrativeSession.AdoptLegacy(5); session.RequestPolicyEnding(reputation, -5, 5);
            return NarrativeSession.Restore(session.Capture());
        }
        private IEnumerator CaptureBoth(string name, bool synthetic)
        {
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) })
            { yield return SetWindow(size.x, size.y); yield return CaptureOne(name, synthetic); }
        }
        private IEnumerator CaptureOne(string name, bool synthetic)
        {
            Canvas.ForceUpdateCanvases(); yield return Frames(2); yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            Require(texture != null && texture.width == Screen.width && texture.height == Screen.height, "Screen capture returned no rendered Player pixels.");
            string file = name + "-" + texture.width + "x" + texture.height + ".png";
            try { File.WriteAllBytes(Path.Combine(output, file), texture.EncodeToPNG()); }
            finally { Destroy(texture); }
            report.captures.Add(new Capture { file = file, stage = report.stage, width = Screen.width, height = Screen.height, synthetic = synthetic });
            Flush();
        }
        private IEnumerator SetWindow(int width, int height)
        {
            if (Screen.width == width && Screen.height == height && Screen.fullScreenMode == FullScreenMode.Windowed) { yield return Frames(2); yield break; }
            new UnitySystemSettingsBackend().SetDisplay(new DisplayConfiguration(width, height, GameWindowMode.Windowed));
            yield return WaitFor(() => Screen.width == width && Screen.height == height && Screen.fullScreenMode == FullScreenMode.Windowed, 8,
                "Actual window did not reach " + width + "x" + height + "; observed=" + Screen.width + "x" + Screen.height + ".");
            yield return Frames(3);
        }
        private static IEnumerator Frames(int count) { for (int n = 0; n < count; n++) yield return null; }
        private static IEnumerator WaitFor(Func<bool> condition, double seconds, string error)
        {
            double start = Time.realtimeSinceStartupAsDouble;
            while (!condition()) { Require(Time.realtimeSinceStartupAsDouble - start < seconds, error); yield return null; }
        }
        private static string Argument(string[] args, string key)
        { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void ClickNamed(string name)
        {
            var button = FindObjectsByType<Button>().SingleOrDefault(b => b.name == name && b.isActiveAndEnabled);
            Require(button != null && button.interactable, "Active public UI entry is unavailable: " + name); button.onClick.Invoke();
        }
        private static void ClickCaption(string caption)
        {
            var button = FindObjectsByType<Button>().SingleOrDefault(b => b.isActiveAndEnabled && b.interactable
                && b.GetComponentsInChildren<Text>(true).Any(t => t.text == caption));
            Require(button != null, "Active real UI navigation caption is unavailable: " + caption); button.onClick.Invoke();
        }
        private void Stage(string stage) { report.stage = stage; Debug.Log("[CompletionQA] Stage: " + stage); Flush(); }
        private void Record(string stage, string detail, bool synthetic = false)
        { report.checks.Add(new Check { stage = stage, detail = detail, passed = true, synthetic = synthetic, elapsedSeconds = Time.realtimeSinceStartupAsDouble - started }); Flush(); }
        private void Flush()
        { if (!string.IsNullOrEmpty(output)) File.WriteAllText(Path.Combine(output, "result.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false)); }
        private void Finish(bool success, Exception failure)
        {
            if (finishing) return; finishing = true;
            report.success = success; report.status = success ? "Passed" : "Failed"; report.finishedUtc = DateTime.UtcNow.ToString("o");
            if (failure != null)
            {
                report.error = failure.GetType().Name + ": " + failure.Message;
                report.errorStack = failure.StackTrace;
                report.checks.Add(new Check { stage = report.stage, detail = report.error, passed = false, elapsedSeconds = Time.realtimeSinceStartupAsDouble - started });
                Debug.LogError("[CompletionQA] Failed at " + report.stage + ": " + report.error);
            }
            try { Flush(); } catch (Exception ex) { Debug.LogError("[CompletionQA] Could not write QA report: " + ex.GetType().Name); success = false; }
            Debug.Log("[CompletionQA] Finished: " + (success ? "Passed" : "Failed")); Application.Quit(success ? 0 : 1);
        }
    }
}
