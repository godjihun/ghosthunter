using UnityEngine;

namespace GhostHunter.Core
{
    /// <summary>
    /// 도구 6종의 아이콘. 귀신 약점판·퇴마사 발견 현황판·아이템 슬롯이 전부 이걸 같이 쓴다 —
    /// 아이콘을 하나 바꿀 때 여러 컴포넌트를 돌아다니며 다시 끼우지 않아도 되게 하기 위해서다.
    /// </summary>
    [CreateAssetMenu(fileName = "ToolIconSet", menuName = "GhostHunter/Tool Icon Set")]
    public class ToolIconSet : ScriptableObject
    {
        [Tooltip("ToolType 순서(Camera, Cross, Bible, HolyWater, Detector, Incense)와 정확히 일치해야 한다.")]
        [SerializeField] private Sprite[] icons = new Sprite[ToolTypeExtensions.Count];

        public Sprite IconFor(ToolType type)
        {
            int i = (int)type;
            return icons != null && i >= 0 && i < icons.Length ? icons[i] : null;
        }
    }
}
