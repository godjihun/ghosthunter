using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// Button의 Interactable 상태에 맞춰 자식 텍스트도 함께 흐려지게 한다.
    ///
    /// Button의 Color Tint 전환은 <b>Target Graphic 하나만</b> 물들인다 — 배경 Image를
    /// Target Graphic으로 쓰면 글자는 그 대상이 아니라서 버튼이 비활성화돼도 글자만 선명하게
    /// 남는다. 이 컴포넌트가 그 텍스트를 버튼 상태에 맞춰 따로 맞춰준다.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ButtonTextDim : MonoBehaviour
    {
        [Tooltip("같이 흐려질 텍스트. 비워두면 자식에서 자동으로 찾는다.")]
        [SerializeField] private TextMeshProUGUI text;

        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color disabledColor = new(1f, 1f, 1f, 0.5f);

        private Button button;

        /// <summary>마지막으로 적용한 상태. 매 프레임 색을 다시 쓰지 않기 위한 캐시.</summary>
        private bool? appliedInteractable;

        private void Awake()
        {
            button = GetComponent<Button>();
            if (text == null)
            {
                text = GetComponentInChildren<TextMeshProUGUI>();
            }
        }

        // Button에는 Interactable이 바뀔 때 알려주는 이벤트가 없어서 매 프레임 값을 비교한다 —
        // 이 프로젝트의 다른 곳(LobbyCamera 등)도 같은 이유로 캐시 비교 방식을 쓴다.
        private void Update()
        {
            if (button == null || text == null || appliedInteractable == button.interactable)
            {
                return;
            }

            appliedInteractable = button.interactable;
            text.color = button.interactable ? normalColor : disabledColor;
        }
    }
}
