using System;
using GhostHunter.Core;
using GhostHunter.Game;
using GhostHunter.Ghost;
using GhostHunter.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 귀신 전용 HUD — 자신의 약점 3종 표시(들킨 건 X 표시)와 공격(Ctrl) 버튼 상태.
    /// </summary>
    public class GhostHudUI : MonoBehaviour
    {
        [Serializable]
        public class WeaknessSlot
        {
            public Image icon;
            [Tooltip("이 약점이 밝혀졌을 때 켤 X 표시 오브젝트.")]
            public GameObject foundMark;
        }

        [Header("약점 3종 (내 화면에만 값이 옴 — ReadPermission.Owner)")]
        [Tooltip("배열 순서 = WeaknessSet의 A, B, C 순서.")]
        [SerializeField] private WeaknessSlot[] weaknessSlots;

        [SerializeField] private ToolIconSet toolIcons;

        [Header("공격 버튼 (Ctrl)")]
        [SerializeField] private Image attackButtonImage;
        [SerializeField] private TextMeshProUGUI attackKeyText;
        [Tooltip("공격 가능할 때(빨갛게 채워진) 아이콘.")]
        [SerializeField] private Sprite attackReadySprite;
        [Tooltip("공격 불가능할 때 아이콘.")]
        [SerializeField] private Sprite attackIdleSprite;
        [SerializeField] private Color attackReadyTextColor = Color.white;
        [SerializeField] private Color attackIdleTextColor = new(1f, 1f, 1f, 0.4f);

        private NetworkPlayer local;
        private FearSkill fearSkill;
        private GhostController ghostController;

        private void Update()
        {
            if (local == null)
            {
                local = NetworkPlayer.GetLocal();
                if (local == null || !local.IsGhost)
                {
                    return;
                }

                fearSkill = local.GetComponent<FearSkill>();
                ghostController = local.GetComponent<GhostController>();
            }

            ApplyWeaknessIcons();
            ApplyAttackButton();
        }

        private void ApplyWeaknessIcons()
        {
            if (weaknessSlots == null)
            {
                return;
            }

            var weakness = local.Weakness.Value;
            if (!weakness.Assigned)
            {
                return;
            }

            SetSlot(0, weakness.A);
            SetSlot(1, weakness.B);
            SetSlot(2, weakness.C);
        }

        private void SetSlot(int index, ToolType type)
        {
            if (weaknessSlots == null || index >= weaknessSlots.Length)
            {
                return;
            }

            var slot = weaknessSlots[index];
            if (slot == null)
            {
                return;
            }

            if (slot.icon != null && toolIcons != null)
            {
                var sprite = toolIcons.IconFor(type);
                if (sprite != null)
                {
                    slot.icon.sprite = sprite;
                }
            }

            if (slot.foundMark != null)
            {
                var manager = GameManager.Instance;
                bool found = manager != null && manager.IsWeaknessFound(type);
                slot.foundMark.SetActive(found);
            }
        }

        /// <summary>
        /// 사냥 단계에서는 항상 가능, 조사 단계에서는 영혼 상태일 때만 — <c>GameHudUI</c>가
        /// 예전에 IMGUI로 그리던 것과 같은 조건이다(주석 참고).
        /// </summary>
        private void ApplyAttackButton()
        {
            if (fearSkill == null)
            {
                return;
            }

            bool hunt = GameManager.CurrentPhase == GamePhase.Hunt;
            bool phaseOk = hunt || (GameManager.CurrentPhase == GamePhase.Investigation
                                     && ghostController != null && ghostController.IsSoulOut.Value);

            bool ready = phaseOk && fearSkill.HasTargetInRange && fearSkill.CooldownRemaining <= 0f;

            if (attackButtonImage != null && attackReadySprite != null && attackIdleSprite != null)
            {
                attackButtonImage.sprite = ready ? attackReadySprite : attackIdleSprite;
            }

            if (attackKeyText != null)
            {
                attackKeyText.color = ready ? attackReadyTextColor : attackIdleTextColor;
            }
        }
    }
}
