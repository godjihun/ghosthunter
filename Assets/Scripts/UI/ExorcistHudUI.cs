using System;
using GhostHunter.Core;
using GhostHunter.Exorcist;
using GhostHunter.Game;
using GhostHunter.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 퇴마사 전용 HUD — 밝혀낸 약점 현황(전원 공개)과 소지 도구 슬롯(4칸, 칸 위치 고정).
    /// </summary>
    public class ExorcistHudUI : MonoBehaviour
    {
        [Serializable]
        public class ItemSlot
        {
            [Tooltip("칸 배경(검은 박스). 선택 여부에 따라 Slot Normal/Selected Sprite로 바뀐다.")]
            public Image background;
            [Tooltip("도구 아이콘. 빈 칸이면 꺼진다.")]
            public Image icon;

            [Tooltip("쿨타임 오버레이. icon과 겹쳐서 그 위에 놓고, Image Type=Filled / " +
                "Fill Method=Vertical / Fill Origin=Top으로 설정할 것 — 쓴 직후엔 아이콘 전체를 덮고, " +
                "쿨타임이 돌수록 위에서부터 걷혀 아래에서 위로 차오르는 것처럼 보인다.")]
            public Image cooldownOverlay;
        }

        [Header("공용 아이콘")]
        [SerializeField] private ToolIconSet toolIcons;

        [Header("칸 배경 (선택 여부 표시)")]
        [Tooltip("선택 안 된 칸의 배경 스프라이트 (테두리 없는 박스).")]
        [SerializeField] private Sprite slotNormalSprite;
        [Tooltip("선택된 칸의 배경 스프라이트 (테두리 있는 박스).")]
        [SerializeField] private Sprite slotSelectedSprite;

        [Header("밝혀낸 약점 (GameManager.FoundWeaknesses, 전원 공개)")]
        [Tooltip("기본은 '?' 모양. 밝혀진 순서대로 왼쪽부터 아이콘으로 바뀐다.")]
        [SerializeField] private Image[] foundWeaknessIcons;
        [SerializeField] private Sprite unknownSprite;
        [Tooltip("'제단 현황 0/3' 같은 카운트 텍스트. 비워도 된다.")]
        [SerializeField] private TextMeshProUGUI foundWeaknessCountText;

        [Header("소지 도구 슬롯 (칸 위치 고정 — ExorcistInventory.Slots와 인덱스가 그대로 대응)")]
        [SerializeField] private ItemSlot[] itemSlots;

        private NetworkPlayer local;
        private ExorcistInventory inventory;

        private void Update()
        {
            ApplyFoundWeaknesses();

            if (local == null)
            {
                local = NetworkPlayer.GetLocal();
                if (local == null || local.IsGhost)
                {
                    return;
                }

                inventory = local.GetComponent<ExorcistInventory>();
            }

            ApplyItemSlots();
        }

        private void ApplyFoundWeaknesses()
        {
            var manager = GameManager.Instance;
            if (manager == null || foundWeaknessIcons == null)
            {
                return;
            }

            int found = manager.FoundWeaknesses.Count;

            for (int i = 0; i < foundWeaknessIcons.Length; i++)
            {
                if (foundWeaknessIcons[i] == null)
                {
                    continue;
                }

                if (i < found)
                {
                    var type = (ToolType)manager.FoundWeaknesses[i];
                    var sprite = toolIcons != null ? toolIcons.IconFor(type) : null;
                    foundWeaknessIcons[i].sprite = sprite != null ? sprite : unknownSprite;
                }
                else
                {
                    foundWeaknessIcons[i].sprite = unknownSprite;
                }
            }

            if (foundWeaknessCountText != null)
            {
                int total = GameManager.Config != null ? GameManager.Config.WeaknessCount : 3;
                foundWeaknessCountText.text = $"제단 현황 {found}/{total}";
            }
        }

        private void ApplyItemSlots()
        {
            if (inventory == null || itemSlots == null)
            {
                return;
            }

            int selected = inventory.SelectedSlot.Value;

            for (int i = 0; i < itemSlots.Length; i++)
            {
                var slot = itemSlots[i];
                if (slot == null)
                {
                    continue;
                }

                bool filled = inventory.HasToolAt(i);

                if (slot.icon != null)
                {
                    slot.icon.enabled = filled;
                    if (filled && toolIcons != null)
                    {
                        var sprite = toolIcons.IconFor(inventory.TypeAt(i));
                        if (sprite != null)
                        {
                            slot.icon.sprite = sprite;
                        }
                    }
                }

                ApplyCooldownOverlay(slot.cooldownOverlay, filled ? inventory.CooldownAt(i) : 0f);

                if (slot.background != null)
                {
                    bool isSelected = i == selected;
                    var wanted = isSelected ? slotSelectedSprite : slotNormalSprite;
                    if (wanted != null)
                    {
                        slot.background.sprite = wanted;
                    }
                }
            }
        }

        /// <summary>
        /// 오버레이는 Fill Origin=Top이라 fillAmount가 "위에서부터 덮은 비율"이다.
        /// 남은 쿨타임 비율을 그대로 넣으면 쓴 직후(비율 1)엔 아이콘 전체를 덮고,
        /// 다 돌수록(비율 0) 위에서부터 걷혀 아래에서 위로 차오르는 것처럼 보인다.
        /// </summary>
        private void ApplyCooldownOverlay(Image overlay, float remaining)
        {
            if (overlay == null)
            {
                return;
            }

            float total = GameManager.Config != null ? GameManager.Config.ToolCooldown : 20f;
            float fraction = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;

            overlay.enabled = fraction > 0f;
            overlay.fillAmount = fraction;
        }
    }
}
