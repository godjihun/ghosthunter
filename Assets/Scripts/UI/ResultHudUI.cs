using GhostHunter.Core;
using GhostHunter.Game;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 결과 화면 — 승패, 귀신의 실제 약점 3종 공개, 대기방으로 돌아가기 / 나가기.
    /// </summary>
    public class ResultHudUI : MonoBehaviour
    {
        [Header("승패 표시")]
        [SerializeField] private TextMeshProUGUI resultTitleText;
        [SerializeField] private Color ghostWinColor = Color.red;
        [SerializeField] private Color exorcistWinColor = Color.white;

        [Tooltip("패널 배경/테두리 Image. 승패에 따라 다른 스프라이트로 바뀐다.")]
        [SerializeField] private Image panelBorderImage;
        [SerializeField] private Sprite ghostWinBorderSprite;
        [SerializeField] private Sprite exorcistWinBorderSprite;

        [Header("공개된 약점 3종")]
        [SerializeField] private Image[] weaknessIcons;
        [SerializeField] private TextMeshProUGUI[] weaknessLabels;
        [SerializeField] private ToolIconSet toolIcons;

        [Header("버튼")]
        [Tooltip("같은 방의 대기방으로. 방장만 누를 수 있다.")]
        [SerializeField] private Button returnToLobbyButton;
        [SerializeField] private TextMeshProUGUI returnToLobbyText;
        [SerializeField] private string returnToLobbyHostLabel = "대기방으로 돌아가기";
        [SerializeField] private string returnToLobbyWaitingLabel = "방장이 대기방으로 돌아가기를 기다리는 중…";

        [Tooltip("접속을 끊고 메인 메뉴(방 생성/참가 화면)로.")]
        [SerializeField] private Button leaveButton;

        /// <summary>이번에 켜진 뒤 결과를 이미 반영했는가. 다시 켜질 때마다 새로 반영한다.</summary>
        private bool applied;

        private void Awake()
        {
            if (returnToLobbyButton != null)
            {
                returnToLobbyButton.onClick.AddListener(OnReturnToLobbyClicked);
            }
            if (leaveButton != null)
            {
                leaveButton.onClick.AddListener(OnLeaveClicked);
            }
        }

        private void OnEnable()
        {
            applied = false;
        }

        private void Update()
        {
            // 방장 여부는 매 프레임 갱신한다 — 결과 화면이 떠 있는 동안 바뀔 일은 없지만
            // 값이 늦게 도착하는 경우(막 스폰된 직후)를 대비한다.
            ApplyButtons();

            if (applied)
            {
                return;
            }

            var manager = GameManager.Instance;
            if (manager == null)
            {
                return;
            }

            applied = true;
            ApplyResult(manager.Result.Value);
            ApplyWeakness(manager.RevealedWeakness.Value);
        }

        private void ApplyResult(GameResult result)
        {
            bool ghostWin = result == GameResult.GhostWin;

            if (resultTitleText != null)
            {
                resultTitleText.text = ghostWin ? "귀신 승리" : "퇴마사 승리";
                resultTitleText.color = ghostWin ? ghostWinColor : exorcistWinColor;
            }

            if (panelBorderImage != null)
            {
                var sprite = ghostWin ? ghostWinBorderSprite : exorcistWinBorderSprite;
                if (sprite != null)
                {
                    panelBorderImage.sprite = sprite;
                }
            }
        }

        private void ApplyWeakness(WeaknessSet weakness)
        {
            if (!weakness.Assigned)
            {
                return;
            }

            SetWeaknessSlot(0, weakness.A);
            SetWeaknessSlot(1, weakness.B);
            SetWeaknessSlot(2, weakness.C);
        }

        private void SetWeaknessSlot(int index, ToolType type)
        {
            if (weaknessIcons != null && index < weaknessIcons.Length
                && weaknessIcons[index] != null && toolIcons != null)
            {
                var sprite = toolIcons.IconFor(type);
                if (sprite != null)
                {
                    weaknessIcons[index].sprite = sprite;
                }
            }

            if (weaknessLabels != null && index < weaknessLabels.Length && weaknessLabels[index] != null)
            {
                weaknessLabels[index].text = type.ToKorean();
            }
        }

        private void ApplyButtons()
        {
            var nm = NetworkManager.Singleton;
            bool isHost = nm != null && nm.IsServer;

            if (returnToLobbyButton != null)
            {
                returnToLobbyButton.interactable = isHost;
            }
            if (returnToLobbyText != null)
            {
                returnToLobbyText.text = isHost ? returnToLobbyHostLabel : returnToLobbyWaitingLabel;
            }
        }

        private void OnReturnToLobbyClicked()
        {
            GameManager.Instance?.ReturnToLobby();
        }

        /// <summary>접속을 끊고 메인 메뉴로 돌아간다 — 여기서는 한 번에 씬까지 넘겨준다.</summary>
        private void OnLeaveClicked()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer))
            {
                nm.Shutdown();
            }

            SceneManager.LoadScene("MainMenuScene");
        }
    }
}
