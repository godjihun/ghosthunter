using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Game
{
    /// <summary>
    /// MainMenuScene → GameScene 전환 동안 로딩 화면을 보여준다.
    ///
    /// <b>NGO의 <c>NetworkSceneManager.OnSceneEvent</c>에 의존하지 않는다</b> — 호스트 자신에게는
    /// 그 이벤트가 기대한 타이밍에 오지 않을 수 있어(직접 검증되지 않음), 대신 우리가 직접 아는
    /// 시점(방을 막 만들었을 때 / 참가에 막 성공했을 때)에 <see cref="Show"/>를 부르고,
    /// <b>Unity 자체의 <c>SceneManager.activeSceneChanged</c></b>(항상 신뢰할 수 있는 표준 이벤트)로
    /// 실제 씬이 바뀌는 순간 자동으로 내린다.
    ///
    /// <c>NetworkManager</c>의 자식으로 둬서 <see cref="NetworkPersistence"/>의 DontDestroyOnLoad로
    /// MainMenuScene → GameScene 양쪽에 걸쳐 살아남아야 한다.
    /// </summary>
    public class SceneLoadingScreenUI : MonoBehaviour
    {
        [SerializeField] private GameObject loadingRoot;

        private static SceneLoadingScreenUI instance;

        private void Awake()
        {
            instance = this;
            if (loadingRoot != null)
            {
                loadingRoot.SetActive(false);
            }
        }

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>활성 씬이 실제로 바뀐 순간(=로드가 끝난 순간) 로딩 화면을 내린다.</summary>
        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            Hide();
        }

        /// <summary>방을 막 만들었거나 참가에 막 성공한 시점에 부른다.</summary>
        public static void Show()
        {
            if (instance != null && instance.loadingRoot != null)
            {
                instance.loadingRoot.SetActive(true);
            }
        }

        public static void Hide()
        {
            if (instance != null && instance.loadingRoot != null)
            {
                instance.loadingRoot.SetActive(false);
            }
        }
    }
}
