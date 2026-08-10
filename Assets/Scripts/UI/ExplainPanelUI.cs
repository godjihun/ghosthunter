using System;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 메인 메뉴의 게임 설명 패널. 오른쪽 화살표로 페이지를 넘기고, 첫 페이지에는 왼쪽
    /// 화살표가 없다. 마지막 페이지에서는 오른쪽 화살표가 닫기(X)로 바뀐다.
    ///
    /// <b>오브젝트 자체는 껐다 켜지 않는다</b> — <c>CanvasGroup</c>의 알파·interactable로만
    /// 여닫는다. <c>FadeTransition</c>과 같은 이유다: 자기 오브젝트를 <c>SetActive(false)</c>로
    /// 끄면 그 순간 Update도 멈춰서, 다음에 여는 코드가 자신을 다시 켜줄 때까지 아무 것도 안 돈다.
    /// </summary>
    public class ExplainPanelUI : MonoBehaviour
    {
        [Serializable]
        public class Page
        {
            [Tooltip("이 페이지에서 같이 보여줄 오브젝트들 (텍스트 여러 개를 한 페이지로 묶을 수 있다).")]
            public GameObject[] elements;
        }

        [Header("페이지 (순서대로)")]
        [SerializeField] private Page[] pages;

        [Header("버튼")]
        [SerializeField] private Button prevButton;
        [SerializeField] private Button nextButton;

        [Tooltip("다음 화살표의 아이콘. 마지막 페이지에서 Close Sprite로 바뀐다.")]
        [SerializeField] private Image nextButtonIcon;
        [SerializeField] private Sprite nextArrowSprite;
        [SerializeField] private Sprite closeSprite;

        [Header("페이드")]
        [Tooltip("ExplainPanel 자신의 CanvasGroup.")]
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private float fadeDuration = 0.25f;

        private enum FadeState { Hidden, FadingIn, Shown, FadingOut }

        private int pageIndex;
        private FadeState fadeState = FadeState.Hidden;
        private float fadeTimer;

        private void Awake()
        {
            if (prevButton != null) prevButton.onClick.AddListener(GoPrev);
            if (nextButton != null) nextButton.onClick.AddListener(OnNextOrClose);

            if (panelGroup != null)
            {
                panelGroup.alpha = 0f;
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
            }
        }

        /// <summary>지금 열려 있거나 여닫는 중인가.</summary>
        public bool IsOpen => fadeState != FadeState.Hidden;

        public void Open()
        {
            pageIndex = 0;
            RefreshPage();

            fadeState = FadeState.FadingIn;
            // 닫히는 중에 다시 열면, 지금 알파에서부터 자연스럽게 이어서 밝아지게 한다.
            fadeTimer = panelGroup != null ? panelGroup.alpha * fadeDuration : 0f;

            if (panelGroup != null)
            {
                panelGroup.interactable = true;
                panelGroup.blocksRaycasts = true;
            }
        }

        public void Close()
        {
            fadeState = FadeState.FadingOut;
            fadeTimer = panelGroup != null ? (1f - panelGroup.alpha) * fadeDuration : 0f;

            if (panelGroup != null)
            {
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
            }
        }

        private void Update()
        {
            if (panelGroup == null || fadeState == FadeState.Hidden || fadeState == FadeState.Shown)
            {
                return;
            }

            fadeTimer += Time.deltaTime;
            float t = Mathf.Clamp01(fadeTimer / Mathf.Max(0.01f, fadeDuration));

            if (fadeState == FadeState.FadingIn)
            {
                panelGroup.alpha = t;
                if (t >= 1f) fadeState = FadeState.Shown;
            }
            else // FadingOut
            {
                panelGroup.alpha = 1f - t;
                if (t >= 1f) fadeState = FadeState.Hidden;
            }
        }

        private void OnNextOrClose()
        {
            bool isLast = pageIndex >= pages.Length - 1;
            if (isLast)
            {
                Close();
                return;
            }

            pageIndex++;
            RefreshPage();
        }

        private void GoPrev()
        {
            if (pageIndex <= 0)
            {
                return;
            }

            pageIndex--;
            RefreshPage();
        }

        private void RefreshPage()
        {
            if (pages == null)
            {
                return;
            }

            for (int i = 0; i < pages.Length; i++)
            {
                bool active = i == pageIndex;
                var elements = pages[i]?.elements;
                if (elements == null)
                {
                    continue;
                }

                foreach (var element in elements)
                {
                    if (element != null)
                    {
                        element.SetActive(active);
                    }
                }
            }

            // <b>첫 페이지에는 왼쪽 화살표가 아예 없다</b> — 흐리게 하는 게 아니라 숨긴다.
            if (prevButton != null)
            {
                prevButton.gameObject.SetActive(pageIndex > 0);
            }

            bool isLast = pageIndex >= pages.Length - 1;
            if (nextButtonIcon != null)
            {
                nextButtonIcon.sprite = isLast ? closeSprite : nextArrowSprite;
            }
        }
    }
}
