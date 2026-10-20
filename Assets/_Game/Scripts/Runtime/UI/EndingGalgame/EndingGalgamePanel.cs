using System;
using System.Collections.Generic;
using CallmeCatgirl.Gameplay.Narrative;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CallmeCatgirl.UI.EndingGalgame
{
    /// <summary>Ending-only galgame view. Text and flow come from ending-scripts.json; the authored prefab owns every fixed widget.</summary>
    [DisallowMultipleComponent]
    public sealed class EndingGalgamePanel : MonoBehaviour
    {
        public const string OpenState = "Open";
        public const string SceneChangeState = "SceneChange";

        [SerializeField] private Image backdrop;
        [SerializeField] private Image characterLeft;
        [SerializeField] private Image characterMid;
        [SerializeField] private Image characterRight;
        [SerializeField] private Text titleText;
        [SerializeField] private Text speakerName;
        [SerializeField] private Text bodyText;
        [SerializeField] private GameObject continueIndicator;
        [SerializeField] private RectTransform choicesRoot;
        [SerializeField] private Button choiceTemplate;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Animator animator;
        [SerializeField] private AudioSource audioSource;

        private readonly EndingCompletionGate completion = new EndingCompletionGate();
        private readonly List<Button> choiceButtons = new List<Button>();
        private readonly HashSet<string> missingTextIds = new HashSet<string>(StringComparer.Ordinal);
        private EndingStoryRunner runner;
        private int choicesReadyFrame;

        /// <summary>Fires once after a terminal choice; the host applies its EndRun semantics from RunEndReason.</summary>
        public event Action<EndingRunRequest> Completed;

        public bool IsShowing => runner != null && !runner.IsComplete;

        private void Awake() => EnsureReady();

        private void Update()
        {
            if (!IsShowing || runner.IsAtChoices) return;
            if (!AdvancePressed()) return;
            Advance();
        }

        /// <summary>Opens the ending for an ending ID or trigger node ID, with or without the optional "special." prefix.</summary>
        public void Show(string endingId)
        {
            EnsureReady();
            var document = EndingScriptLibrary.LoadFromResources();
            runner = new EndingStoryRunner(document, endingId);
            completion.Reset();
            missingTextIds.Clear();
            ClearChoices();
            gameObject.SetActive(true);
            group.blocksRaycasts = true;
            titleText.text = ResolveText(runner.Ending.titleTextId);
            ApplyScene();
            if (animator != null) { group.alpha = 0f; Play(OpenState); }
        }

        public void Hide()
        {
            ClearChoices();
            runner = null;
            StopBgm();
            if (group != null) group.blocksRaycasts = false;
            gameObject.SetActive(false);
        }

        /// <summary>Completes the current line or moves to the next one; ignored while choices are shown.</summary>
        public void Advance()
        {
            if (!IsShowing || runner.IsAtChoices) return;
            runner.Advance();
            if (runner.IsAtChoices) ShowChoices(); else ShowLine();
        }

        private void ShowChoices()
        {
            ClearChoices();
            var choices = runner.Scene.choices;
            for (int index = 0; index < choices.Length; index++)
            {
                int captured = index;
                Button button = Instantiate(choiceTemplate, choicesRoot);
                button.gameObject.name = "Choice." + choices[index].id;
                button.gameObject.SetActive(true);
                Text label = button.GetComponentInChildren<Text>(true);
                if (label != null) label.text = ResolveText(choices[index].labelTextId);
                button.onClick.AddListener(() => OnChoiceClicked(captured));
                choiceButtons.Add(button);
            }
            continueIndicator.SetActive(false);
            choicesReadyFrame = Time.frameCount + 1;
        }

        private void OnChoiceClicked(int choiceIndex)
        {
            if (!IsShowing || Time.frameCount < choicesReadyFrame) return;
            if (!runner.Choose(choiceIndex)) return;
            ClearChoices();
            if (runner.IsComplete) { CompleteRun(); return; }
            ApplyScene();
            Play(SceneChangeState);
        }

        private void ApplyScene()
        {
            EndingScene scene = runner.Scene;
            SetSprite(backdrop, scene.background);
            SetSprite(characterLeft, scene.characters?.left?.sprite);
            SetSprite(characterMid, scene.characters?.mid?.sprite);
            SetSprite(characterRight, scene.characters?.right?.sprite);
            PlayBgm(scene.bgm);
            ShowLine();
        }

        private void ShowLine()
        {
            EndingLine line = runner.CurrentLine;
            if (line == null) return;
            speakerName.text = ResolveText(line.speakerTextId);
            bodyText.text = ResolveText(line.textTextId);
            continueIndicator.SetActive(true);
        }

        private void CompleteRun()
        {
            var request = new EndingRunRequest
            {
                EndingId = runner.Ending.id,
                TriggerNodeId = runner.Ending.triggerNodeId,
                RunEndReason = EndingRunReasons.Resolve(runner.Ending.triggerNodeId)
            };
            ClearChoices();
            continueIndicator.SetActive(false);
            StopBgm();
            gameObject.SetActive(false);
            completion.TryRaise(request, RaiseCompleted);
        }

        private void RaiseCompleted(EndingRunRequest request) => Completed?.Invoke(request);

        private void ClearChoices()
        {
            foreach (Button button in choiceButtons)
                if (button != null) Destroy(button.gameObject);
            choiceButtons.Clear();
        }

        private void SetSprite(Image image, string resourcePath)
        {
            if (image == null) return;
            Sprite sprite = string.IsNullOrEmpty(resourcePath) ? null : Resources.Load<Sprite>(resourcePath);
            if (sprite == null && !string.IsNullOrEmpty(resourcePath))
                Debug.LogWarning("Missing ending sprite at Resources/" + resourcePath + ".png", this);
            image.sprite = sprite;
            image.enabled = sprite != null;
        }

        private void PlayBgm(string resourcePath)
        {
            AudioClip clip = string.IsNullOrEmpty(resourcePath) ? null : Resources.Load<AudioClip>(resourcePath);
            if (clip == null) { StopBgm(); return; }
            AudioSource source = BgmSource();
            if (source.clip == clip && source.isPlaying) return;
            source.clip = clip;
            source.loop = true;
            source.Play();
        }

        private AudioSource BgmSource()
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.loop = true;
            }
            return audioSource;
        }

        private void StopBgm()
        {
            if (audioSource != null && audioSource.isPlaying) audioSource.Stop();
        }

        private void Play(string stateName)
        {
            if (animator != null) animator.Play(stateName, 0, 0f);
        }

        private string ResolveText(string textId)
            => EndingScriptLibrary.ResolveText(NarrativeTextCatalog.Get, textId, ReportMissingText);

        private void ReportMissingText(string textId)
        {
            if (missingTextIds.Add(textId))
                Debug.LogWarning("Missing narrative text ID: " + textId + "; showed the ID instead.", this);
        }

        private static bool AdvancePressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame
                || keyboard.numpadEnterKey.wasPressedThisFrame)) return true;
            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        private void EnsureReady()
        {
            if (backdrop == null || characterLeft == null || characterMid == null || characterRight == null
                || titleText == null || speakerName == null || bodyText == null || continueIndicator == null
                || choicesRoot == null || choiceTemplate == null || group == null)
                throw new InvalidOperationException(name + " is missing its authored UGUI references.");
            if (choiceTemplate.gameObject.activeSelf) choiceTemplate.gameObject.SetActive(false);
        }
    }
}
