using System;
using UnityEngine;

namespace GhostHunter.UI
{
    /// <summary>
    /// 화면을 덮는 오버레이가 서서히 나타났다 사라지며 패널을 전환한다.
    ///
    /// <c>CanvasGroup.alpha</c>만 보간하는 게 전부다 — 셰이더도, 여러 장의 이미지도,
    /// 위치 애니메이션도 없어서 지금까지 시도한 방식 중 가장 가볍고 단순하다.
    ///
    /// <b>오브젝트 자체는 껐다 켜지 않는다</b> — 알파 0/차단 해제 상태로 항상 켜둔다.
    /// 이 컴포넌트가 <c>overlay</c>(CanvasGroup)와 <b>같은 오브젝트</b>에 있는 경우가 많은데,
    /// 그 상태에서 자기 오브젝트를 <c>SetActive(false)</c>로 끄면 Update 자체가 멈춰버려서
    /// 다음 Play()가 자신을 다시 켤 때까지 아무 것도 안 도는 어색한 상태가 된다. 알파 0은
    /// 씬 뷰에서도 그대로 투명하게 렌더링되므로 에디터에서 편집을 가릴 일도 없다.
    /// </summary>
    public class FadeTransition : MonoBehaviour
    {
        [Tooltip("화면을 덮을 오버레이. 보통 검은 Image 하나에 CanvasGroup을 붙여 쓴다. " +
                 "패널들보다 아래 형제(= 위에 그려짐)에 있어야 한다.")]
        [SerializeField] private CanvasGroup overlay;

        [Header("타이밍")]
        [Tooltip("투명 → 완전히 덮임까지 걸리는 시간(초).")]
        [SerializeField] private float fadeInDuration = 0.3f;

        [Tooltip("완전히 덮인 채로 유지하는 시간(초). 패널 교체는 이 구간이 시작되는 순간 일어난다.")]
        [SerializeField] private float holdDuration = 0.1f;

        [Tooltip("완전히 덮임 → 투명까지 걸리는 시간(초).")]
        [SerializeField] private float fadeOutDuration = 0.3f;

        private enum Stage { Idle, FadingIn, Holding, FadingOut }

        private Stage stage = Stage.Idle;
        private float timer;
        private Action onFullyCovered;

        private void Awake()
        {
            if (overlay != null)
            {
                overlay.alpha = 0f;
                overlay.blocksRaycasts = false;
            }
        }

        /// <summary>지금 재생 중인가. 버튼 연타로 겹쳐 재생되는 걸 막는 데 쓴다.</summary>
        public bool IsPlaying => stage != Stage.Idle;

        /// <summary>
        /// 전환을 재생한다. 화면이 완전히 덮인 순간 <paramref name="onFullyCovered"/>를 호출한다 —
        /// 그 콜백 안에서 패널을 SetActive로 바꿔치기하면 오버레이에 가려 자연스럽다.
        /// </summary>
        public void Play(Action onFullyCovered)
        {
            if (IsPlaying || overlay == null)
            {
                // 아직 오버레이를 안 만들었으면 그냥 즉시 콜백 — 전환 없이도 기능은 동작해야 한다.
                onFullyCovered?.Invoke();
                return;
            }

            this.onFullyCovered = onFullyCovered;
            timer = 0f;
            stage = Stage.FadingIn;

            overlay.alpha = 0f;
            // 전환 중엔 밑에 있는 버튼이 안 눌리게 막는다.
            overlay.blocksRaycasts = true;
        }

        private void Update()
        {
            if (stage == Stage.Idle)
            {
                return;
            }

            timer += Time.deltaTime;

            switch (stage)
            {
                case Stage.FadingIn:
                    overlay.alpha = Mathf.Clamp01(timer / Mathf.Max(0.01f, fadeInDuration));
                    if (timer >= fadeInDuration)
                    {
                        overlay.alpha = 1f;

                        var callback = onFullyCovered;
                        onFullyCovered = null;
                        callback?.Invoke();

                        timer = 0f;
                        stage = Stage.Holding;
                    }
                    break;

                case Stage.Holding:
                    if (timer >= holdDuration)
                    {
                        timer = 0f;
                        stage = Stage.FadingOut;
                    }
                    break;

                case Stage.FadingOut:
                    overlay.alpha = 1f - Mathf.Clamp01(timer / Mathf.Max(0.01f, fadeOutDuration));
                    if (timer >= fadeOutDuration)
                    {
                        overlay.alpha = 0f;
                        overlay.blocksRaycasts = false;
                        stage = Stage.Idle;
                    }
                    break;
            }
        }
    }
}
