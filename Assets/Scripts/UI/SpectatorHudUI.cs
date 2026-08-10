using GhostHunter.Core;
using GhostHunter.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 관전자 전용 HUD — 관전 대상 이름, 좌우 전환 버튼, 귀신 약점 3종.
    ///
    /// 약점은 <see cref="PlayerSpectator.RevealedWeakness"/>에서 읽는다 — 퇴마사일 땐 "?"였던
    /// 자리가 죽어서 관전자가 되는 순간 실제 아이콘으로 바뀐다(서버가 그 순간 한 번 보내준다).
    /// </summary>
    public class SpectatorHudUI : MonoBehaviour
    {
        [Header("관전 대상")]
        [SerializeField] private TextMeshProUGUI targetNameText;
        [SerializeField] private Button prevButton;
        [SerializeField] private Button nextButton;

        [Header("귀신 약점 3종 (관전자에게는 전부 공개)")]
        [SerializeField] private Image[] weaknessIcons;
        [SerializeField] private ToolIconSet toolIcons;

        private PlayerSpectator spectator;

        private void Update()
        {
            if (spectator == null)
            {
                var local = NetworkPlayer.GetLocal();
                spectator = local != null ? local.GetComponent<PlayerSpectator>() : null;
                if (spectator == null)
                {
                    return;
                }
            }

            ApplyTargetName();
            ApplyButtons();
            ApplyWeakness();
        }

        private void ApplyTargetName()
        {
            if (targetNameText != null)
            {
                targetNameText.text = spectator.Target != null ? spectator.Target.DisplayName : "-";
            }
        }

        /// <summary>
        /// 갈아탈 대상이 한 명뿐이면(=지금 보는 사람뿐) 버튼을 못 누르게 한다.
        ///
        /// <b>색은 여기서 만지지 않는다</b> — Button의 Color Tint에 이미 Disabled Color가
        /// 있으므로 <c>interactable</c>만 바꾸면 유니티가 알아서 흐리게 그린다. 직접 색을
        /// 덮어쓰면 유니티가 매 프레임 다시 칠하는 것과 충돌해서 깜빡인다.
        /// </summary>
        private void ApplyButtons()
        {
            bool canCycle = spectator.CandidateCount > 1;

            if (prevButton != null) prevButton.interactable = canCycle;
            if (nextButton != null) nextButton.interactable = canCycle;
        }

        private void ApplyWeakness()
        {
            if (weaknessIcons == null || toolIcons == null)
            {
                return;
            }

            var weakness = spectator.RevealedWeakness;
            if (!weakness.Assigned)
            {
                return;
            }

            SetIcon(0, weakness.A);
            SetIcon(1, weakness.B);
            SetIcon(2, weakness.C);
        }

        private void SetIcon(int index, ToolType type)
        {
            if (index >= weaknessIcons.Length || weaknessIcons[index] == null)
            {
                return;
            }

            var sprite = toolIcons.IconFor(type);
            if (sprite != null)
            {
                weaknessIcons[index].sprite = sprite;
            }
        }
    }
}
