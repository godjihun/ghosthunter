using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 시작 화면(MainMenuScene)의 버튼 동작.
    ///
    /// <b>게임 시작 → 로비 패널</b>. 접속(Relay 호스트/참가)은 이 씬 안에서 처리한다 —
    /// <c>NetworkManager</c>가 <see cref="Game.NetworkPersistence"/>로 이 씬에 붙박여 있고,
    /// <see cref="LobbyJoinUI"/>가 접속을 그린다. <b>방을 만들면 곧바로 GameScene으로 넘어간다</b>
    /// (<see cref="Game.PreGameLobby.ServerStartGame"/>, NGO 동기화 씬 전환) — 어몽어스·챠메레온류처럼
    /// 사람들이 모이는 것 자체를 걸어다니는 대기방에서 보고, 방장이 그 안의 키오스크(LobbyConsole)에서
    /// 밸런스를 정하고 실제 게임을 시작한다. 여기서는 <b>패널만 바꾼다</b>.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Tooltip("시작하기를 누르면 꺼질 메인 메뉴 패널.")]
        [SerializeField] private GameObject mainMenuPanel;

        [Tooltip("시작하기를 누르면 켜질 로비(방 만들기/참가) 패널.")]
        [SerializeField] private GameObject lobbyPanel;

        [SerializeField] private Button startButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;

        [Tooltip("로비 패널의 '메인으로 돌아가기'. 접속 전이라 씬 전환 없이 패널만 되돌린다.")]
        [SerializeField] private Button backToMainButton;

        [Tooltip("패널 전환 시 재생할 페이드 효과. 비워두면 즉시 전환한다.")]
        [SerializeField] private FadeTransition transition;

        [Tooltip("'게임 설명' 버튼(원래 설정 버튼)이 열 설명 패널.")]
        [SerializeField] private ExplainPanelUI explainPanel;

        /// <summary>설정 창이 떠 있는가. 대기방 메뉴와 같은 내용을 그린다.</summary>
        private bool settingsOpen;

        private void Awake()
        {
            // 편집 중 LobbyPanel/ExplainPanel/Cover를 꺼둔 채로 저장해도 상관없게,
            // 시작할 때 한 번 강제로 켜서 그 안의 스크립트들(Awake)이 확실히 초기화되게 한다.
            // ExplainPanel·Cover(전환 효과)는 알파로만 여닫으므로 계속 켜둔 채로 둔다 —
            // LobbyPanel만 초기 상태(꺼짐)로 되돌린다.
            ForceActivateOnce(lobbyPanel);
            ForceActivateOnce(explainPanel != null ? explainPanel.gameObject : null);
            ForceActivateOnce(transition != null ? transition.gameObject : null);
            if (lobbyPanel != null) lobbyPanel.SetActive(false);

            // <b>리스너는 코드에서 붙인다.</b> 인스펙터의 OnClick 목록에 넣으면
            // 어떤 함수가 걸려 있는지 코드만 봐서는 알 수 없고, 스크립트 이름이
            // 바뀌면 조용히 끊어진 채로 남는다.
            if (startButton != null)
            {
                startButton.onClick.AddListener(StartGame);
            }

            // 원래 "설정" 버튼을 "게임 설명"으로 바꿔 쓰는 중이다 — 마우스 감도 설정(SettingsPanel)은
            // 아직 코드에 남아있지만 이 버튼에서는 더 이상 열리지 않는다. 나중에 감도 설정을 다시
            // 넣고 싶으면 별도 버튼을 만들어 SetSettingsOpen(true)에 연결하면 된다.
            if (settingsButton != null)
            {
                settingsButton.onClick.AddListener(() => explainPanel?.Open());
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

            if (backToMainButton != null)
            {
                backToMainButton.onClick.AddListener(BackToMainMenu);
            }
        }

        private static void ForceActivateOnce(GameObject go)
        {
            if (go != null && !go.activeSelf)
            {
                go.SetActive(true);
            }
        }

        /// <summary>로비(방 만들기 / 코드로 참가) 패널로 넘어간다. 씬 전환은 아직 없다.</summary>
        public void StartGame()
        {
            SwitchPanel(mainMenuPanel, lobbyPanel);
        }

        /// <summary>
        /// 로비 패널에서 메인 메뉴로 되돌아간다.
        ///
        /// 아직 접속 전이므로(이 버튼은 LobbyPanel에 있고, 접속되면 LobbyJoinUI가 스스로
        /// 패널을 꺼서 RoomPanel로 넘어간다) 되돌릴 네트워크 상태가 없다 — 패널만 바꾼다.
        /// </summary>
        public void BackToMainMenu()
        {
            SwitchPanel(lobbyPanel, mainMenuPanel);
        }

        /// <summary>
        /// <paramref name="from"/>을 끄고 <paramref name="to"/>를 켠다. 전환 애니메이션이 연결돼
        /// 있으면 화면이 다 덮인 프레임에 바꿔치기해 자연스럽다. 없으면 즉시 바뀐다.
        /// </summary>
        private void SwitchPanel(GameObject from, GameObject to)
        {
            void Swap()
            {
                if (from != null) from.SetActive(false);
                if (to != null) to.SetActive(true);
            }

            if (transition != null)
            {
                transition.Play(Swap);
            }
            else
            {
                Swap();
            }
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

            // 내용은 대기방 메뉴와 같은 코드가 그린다.
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
