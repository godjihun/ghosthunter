using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 시작 화면(MainMenuScene)의 버튼 동작.
    ///
    /// <b>게임 시작 → 대기방</b>은 그냥 씬 전환이다. 대기방 화면 자체는
    /// 게임플레이 씬의 <see cref="Game.NetworkBootstrapUI"/>가 그리므로
    /// 여기서는 씬만 넘겨주면 된다.
    ///
    /// 아직 아무도 접속하지 않은 시점이라 <c>SceneManager.LoadScene</c>으로 충분하다.
    /// NGO의 <c>NetworkManager.SceneManager</c>는 <b>접속한 뒤</b> 전원을 같이
    /// 옮길 때 쓰는 것이고, 여기서 쓰면 아직 없는 서버를 향해 부르는 꼴이 된다.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Tooltip("게임 시작을 누르면 넘어갈 씬. Build Profiles의 씬 목록에도 들어 있어야 한다.")]
        [SerializeField] private string gameplayScene = "GameScene";

        [SerializeField] private Button startButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;

        /// <summary>설정 창이 떠 있는가. 대기방 ESC 메뉴와 같은 내용을 그린다.</summary>
        private bool settingsOpen;

        private void Awake()
        {
            // <b>리스너는 코드에서 붙인다.</b> 인스펙터의 OnClick 목록에 넣으면
            // 어떤 함수가 걸려 있는지 코드만 봐서는 알 수 없고, 스크립트 이름이
            // 바뀌면 조용히 끊어진 채로 남는다.
            if (startButton != null)
            {
                startButton.onClick.AddListener(StartGame);
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.AddListener(() => SetSettingsOpen(true));
            }

            if (quitButton != null)
            {
                // 브라우저는 앱이 스스로 탭을 닫을 수 없다. WebGL에서 나가기 버튼은
                // 눌러도 아무 일이 없으므로, 죽은 버튼을 두느니 감춘다.
#if UNITY_WEBGL && !UNITY_EDITOR
                quitButton.gameObject.SetActive(false);
#else
                quitButton.onClick.AddListener(QuitGame);
#endif
            }
        }

        /// <summary>대기방(방 만들기 / 코드로 참가)으로 넘어간다.</summary>
        public void StartGame()
        {
            if (!Application.CanStreamedLevelBeLoaded(gameplayScene))
            {
                // 빌드 씬 목록에 없으면 에디터에서는 되고 빌드에서만 검은 화면이 된다.
                // 그때 원인을 찾기 어려우므로 여기서 바로 짚어준다.
                Debug.LogError($"[MainMenu] '{gameplayScene}' 씬이 빌드 목록에 없습니다. " +
                               "File → Build Profiles → Scene List에 추가하세요.");
                return;
            }

            SceneManager.LoadScene(gameplayScene);
        }

        /// <summary>
        /// 설정 창 여닫기.
        ///
        /// 여는 동안 <b>뒤쪽 메뉴 버튼을 잠근다.</b> 설정 창은 IMGUI라 UGUI 버튼 위에
        /// 그려지지만, 클릭 판정은 뒤쪽 버튼에도 그대로 간다 — 감도를 조절하려다
        /// 게임이 시작돼버린다.
        /// </summary>
        private void SetSettingsOpen(bool open)
        {
            settingsOpen = open;

            if (startButton != null) startButton.interactable = !open;
            if (settingsButton != null) settingsButton.interactable = !open;
            if (quitButton != null) quitButton.interactable = !open;
        }

        private void OnGUI()
        {
            if (!settingsOpen)
            {
                return;
            }

            // 기본 폰트에는 한글 글리프가 없어 빌드에서 글자가 사라진다.
            HudFont.ApplyToSkin();

            const float extra = 60f;   // 닫기 버튼 자리
            var rect = new Rect(
                (Screen.width - SettingsPanel.Width) * 0.5f,
                (Screen.height - (SettingsPanel.Height + extra)) * 0.5f,
                SettingsPanel.Width, SettingsPanel.Height + extra);

            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 18, rect.y + 16, rect.width - 36, rect.height - 32));

            // 내용은 대기방 ESC 메뉴와 같은 코드가 그린다.
            SettingsPanel.Draw();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("닫기", GUILayout.Height(32)))
            {
                SetSettingsOpen(false);
            }

            GUILayout.EndArea();
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
