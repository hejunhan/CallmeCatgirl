using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    /// <summary>
    /// Input/chance controller for the authored memory-install layout.
    /// Visual state is owned by the Animator state machine; this class only sets its State parameter.
    /// </summary>
    public sealed class MemoryInstallManualController : MonoBehaviour
    {
        private enum Stage { Empty, LeftIn, RightIn, Seated }

        private RectTransform slot;
        private RectTransform ram;
        private RectTransform leftClip;
        private RectTransform rightClip;
        private RectTransform frontMask;
        private Text stateText;
        private Text chanceText;
        private Button leftButton;
        private Button middleButton;
        private Button rightButton;
        private Animator animator;

        private Stage stage;
        private int chance;
        private int? lastSide;
        private bool middleBlocked;

        private void Awake()
        {
            slot = FindRect("03_插槽底座");
            ram = FindRect("02_内存条");
            leftClip = FindRect("04_左卡扣");
            rightClip = FindRect("05_右卡扣");
            frontMask = FindRect("06_前沿遮挡");
            animator = GetComponent<Animator>();
            stateText = FindText("State");
            chanceText = FindText("Chance");
            leftButton = FindButton("左边用力");
            middleButton = FindButton("中间用力");
            rightButton = FindButton("右边用力");

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystem.transform.SetParent(transform, false);
            }

            ApplyOrder();

            if (leftButton != null) leftButton.onClick.AddListener(() => PressSide(0));
            if (middleButton != null) middleButton.onClick.AddListener(PressMiddle);
            if (rightButton != null) rightButton.onClick.AddListener(() => PressSide(1));

            ResetState();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ResetState();
        }

        private void PressSide(int side)
        {
            if (stage == Stage.Seated) return;

            if (lastSide.HasValue && lastSide.Value != side)
                chance = Mathf.Min(100, chance + 2);
            lastSide = side;
            stage = side == 0 ? Stage.LeftIn : Stage.RightIn;
            middleBlocked = false;
            ApplyState();
            SetStateText(side == 0 ? "左端进入" : "右端进入", false);
        }

        private void PressMiddle()
        {
            if (stage == Stage.Seated) return;

            if (chance <= 0)
            {
                FailMiddle();
                return;
            }

            if (Random.value * 100f < chance)
            {
                stage = Stage.Seated;
                middleBlocked = false;
                ApplyState();
                SetStateText("已插入", false);
                if (leftButton != null) leftButton.interactable = false;
                if (middleButton != null) middleButton.interactable = false;
                if (rightButton != null) rightButton.interactable = false;
            }
            else
            {
                FailMiddle();
            }
        }

        private void FailMiddle()
        {
            chance = Mathf.Max(0, chance - 10);
            middleBlocked = true;
            ApplyState();
            if (animator != null) animator.SetTrigger("Fail");
            SetStateText($"中间压不动 · {chance}%", true);
        }

        private void ResetState()
        {
            stage = Stage.Empty;
            chance = 0;
            lastSide = null;
            middleBlocked = false;
            if (leftButton != null) leftButton.interactable = true;
            if (middleButton != null) middleButton.interactable = true;
            if (rightButton != null) rightButton.interactable = true;
            ApplyState();
            SetStateText("未插入", false);
        }

        private void ApplyState()
        {
            int state = 0;
            if (middleBlocked) state = 3;
            else if (stage == Stage.Seated) state = 4;
            else if (stage == Stage.LeftIn) state = 1;
            else if (stage == Stage.RightIn) state = 2;

            if (animator != null) animator.SetInteger("State", state);
            if (chanceText != null) chanceText.text = $"中间成功率：{chance}%";
        }

        private void ApplyOrder()
        {
            if (slot != null) slot.SetAsFirstSibling();
            if (ram != null) ram.SetSiblingIndex(1);
            if (leftClip != null) leftClip.SetSiblingIndex(2);
            if (rightClip != null) rightClip.SetSiblingIndex(3);
            if (frontMask != null) frontMask.SetAsLastSibling();
        }

        private void SetStateText(string text, bool error)
        {
            if (stateText == null) return;
            stateText.text = text;
            stateText.color = error ? new Color32(155, 62, 48, 255) : new Color32(63, 58, 52, 255);
        }

        private static RectTransform FindRect(string name)
        {
            var go = GameObject.Find(name);
            return go == null ? null : go.GetComponent<RectTransform>();
        }

        private static Text FindText(string name)
        {
            var go = GameObject.Find(name);
            return go == null ? null : go.GetComponent<Text>();
        }

        private static Button FindButton(string name)
        {
            var go = GameObject.Find(name);
            return go == null ? null : go.GetComponent<Button>();
        }
    }
}
