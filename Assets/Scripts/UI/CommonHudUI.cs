using GhostHunter.Core;
using GhostHunter.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 양 진영 공통 HUD — 남은 시간, 현재 단계(원 안 텍스트), 현실화 게이지.
    /// </summary>
    public class CommonHudUI : MonoBehaviour
    {
        [Header("남은 시간 / 단계")]
        [SerializeField] private TextMeshProUGUI timeText;

        [Tooltip("원 안에 단계 이름을 표시하는 텍스트 (은신/조사/사냥).")]
        [SerializeField] private TextMeshProUGUI phaseText;

        [Tooltip("단계 이름을 둘러싼 원 이미지. 사냥 단계가 되면 빨간 채움 스프라이트로 바뀐다.")]
        [SerializeField] private Image phaseRingImage;
        [SerializeField] private Sprite phaseRingNormalSprite;
        [SerializeField] private Sprite phaseRingHuntSprite;

        [Header("현실화 게이지")]
        [Tooltip("빨간 채움 이미지. Image Type = Filled 로 설정할 것.")]
        [SerializeField] private Image gaugeFillImage;

        private void Update()
        {
            var manager = GameManager.Instance;
            if (manager == null)
            {
                return;
            }

            if (timeText != null)
            {
                float remaining = Mathf.Max(0f, manager.PhaseTimeRemaining.Value);
                int minutes = Mathf.FloorToInt(remaining / 60f);
                int seconds = Mathf.FloorToInt(remaining % 60f);
                timeText.text = $"{minutes:00}:{seconds:00}";
            }

            if (phaseText != null)
            {
                phaseText.text = PhaseLabel(manager.Phase.Value);
            }

            if (phaseRingImage != null)
            {
                var sprite = manager.Phase.Value == GamePhase.Hunt ? phaseRingHuntSprite : phaseRingNormalSprite;
                if (sprite != null)
                {
                    phaseRingImage.sprite = sprite;
                }
            }

            if (gaugeFillImage != null)
            {
                gaugeFillImage.fillAmount = Mathf.Clamp01(manager.MaterializeGauge.Value / 100f);
            }
        }

        private static string PhaseLabel(GamePhase phase) => phase switch
        {
            GamePhase.Hiding => "은신",
            GamePhase.Investigation => "조사",
            GamePhase.Hunt => "사냥",
            GamePhase.Result => "종료",
            _ => "대기",
        };
    }
}
