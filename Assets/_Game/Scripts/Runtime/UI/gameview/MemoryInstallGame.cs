using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    /// <summary>Shared installation interaction for the upgrade popup and standalone scene.</summary>
    public class MemoryInstallGame : MonoBehaviour
    {
        [SerializeField] private GameObject gamePanel;
        [SerializeField] private RectTransform memoryStick, leftClip, rightClip;
        [SerializeField] private Button pressLeftButton, pressRightButton, skipButton, finishButton;
        [SerializeField] private TMP_Text tipText;
        [SerializeField] private float openAngle = 20, initialTilt = 3, halfTilt = 1.5f;
        private bool leftPressed, installed, bound;
        public bool IsOpen { get; private set; }
        public event Action InstallCompleted;

        public void OpenGame()
        {
            if (IsOpen) return;
            if (!bound)
            {
                pressLeftButton.onClick.AddListener(OnPressLeft);
                pressRightButton.onClick.AddListener(OnPressRight);
                skipButton.onClick.AddListener(SkipInstall);
                finishButton.onClick.AddListener(CompleteInstall);
                bound = true;
            }
            ResetGame();
            IsOpen = true;
            if (gamePanel != null) gamePanel.SetActive(true);
        }

        public void ResetGame()
        {
            IsOpen = leftPressed = installed = false;
            if (finishButton != null) finishButton.gameObject.SetActive(false);
            Paint(false);
            if (gamePanel != null) gamePanel.SetActive(false);
        }

        private void OnPressLeft()
        {
            if (!IsOpen || installed || leftPressed) return;
            leftPressed = true;
            Paint(false);
        }

        private void OnPressRight()
        {
            if (!IsOpen || installed) return;
            if (!leftPressed) { Paint(false); return; }
            installed = true;
            Paint(true);
            finishButton.gameObject.SetActive(true);
        }

        public void SkipInstall()
        {
            if (!IsOpen) return;
            installed = true;
            Paint(true);
            CompleteInstall();
        }

        private void CompleteInstall()
        {
            if (!IsOpen || !installed) return;
            IsOpen = false; // Close before notifying observers, including repeated clicks.
            finishButton.gameObject.SetActive(false);
            if (gamePanel != null) gamePanel.SetActive(false);
            InstallCompleted?.Invoke();
        }

        private void Paint(bool complete)
        {
            if (leftClip != null) leftClip.localEulerAngles = new Vector3(0, 0, complete || leftPressed ? 0 : openAngle);
            if (rightClip != null) rightClip.localEulerAngles = new Vector3(0, 0, complete ? 0 : -openAngle);
            if (memoryStick != null) memoryStick.localEulerAngles = new Vector3(0, 0, complete ? 0 : leftPressed ? halfTilt : initialTilt);
            if (tipText != null) tipText.text = complete ? "内存安装完成" : leftPressed ? "按压右侧卡扣" : "先按压左侧卡扣，再按压右侧";
            if (pressLeftButton != null) pressLeftButton.interactable = !complete && !leftPressed;
            if (pressRightButton != null) pressRightButton.interactable = !complete;
        }
    }
}
