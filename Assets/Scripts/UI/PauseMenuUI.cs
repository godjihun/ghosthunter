using GhostHunter.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.UI
{
    /// <summary>
    /// ESC로 여는 일시정지 창. 설정과 나가기.
    ///
    /// <b>ESC는 액션맵을 거치지 않고 키보드를 직접 읽는다.</b> 액션맵은 진영·상태에 따라
    /// Exorcist / Ghost / Spectator로 갈리는데, 이 창은 <b>어느 상태에서든</b> 열려야 한다.
    /// 네 맵 모두에 같은 액션을 넣으면 하나 빠뜨렸을 때 "특정 상황에서만 ESC가 안 되는"
    /// 찾기 어려운 버그가 된다. 레거시 Input Manager가 아니라 새 Input System의 API다.
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        private const float PanelWidth = 300f;
        private const float PanelHeight = 210f;
        private const float SettingsHeight = 250f;

        private bool open;
        private bool settingsOpen;

        private GUIStyle titleStyle;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            // 접속 전(로비 화면)에는 열 이유가 없다. 거기엔 이미 나가는 버튼이 있고,
            // 커서도 원래 풀려 있다.
            if (NetworkPlayer.GetLocal() == null)
            {
                return;
            }

            // 설정을 열어둔 채 ESC를 누르면 설정만 닫는다. 한 번에 다 닫히면
            // 되돌아갈 방법이 없어 답답하다.
            if (open && settingsOpen)
            {
                settingsOpen = false;
                return;
            }

            SetOpen(!open);
        }

        private void SetOpen(bool value)
        {
            open = value;
            if (!value)
            {
                settingsOpen = false;
            }

            // 커서는 PlayerRoleSetup이 단독으로 관리한다. 여기서 Cursor를 직접 만지면
            // 매 프레임 서로 덮어써서 깜빡인다.
            PlayerRoleSetup.UiPanelOpen = value;
        }

        private void OnDisable()
        {
            // 창을 연 채로 정리되면 커서가 풀린 채 남는다.
            if (open)
            {
                SetOpen(false);
            }
        }

        private void OnGUI()
        {
            if (!open)
            {
                return;
            }

            // 기본 폰트에는 한글 글리프가 없어 빌드에서 글자가 사라진다.
            HudFont.ApplyToSkin();
            EnsureStyles();

            float h = settingsOpen ? SettingsHeight : PanelHeight;
            var rect = new Rect(
                (Screen.width - PanelWidth) * 0.5f,
                (Screen.height - h) * 0.5f,
                PanelWidth, h);

            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 18, rect.y + 16, rect.width - 36, rect.height - 32));

            if (settingsOpen)
            {
                DrawSettings();
            }
            else
            {
                DrawMenu();
            }

            GUILayout.EndArea();
        }

        private void DrawMenu()
        {
            GUILayout.Label("메뉴", titleStyle);
            GUILayout.Space(14);

            if (GUILayout.Button("설정", GUILayout.Height(36)))
            {
                settingsOpen = true;
            }

            GUILayout.Space(6);

            if (GUILayout.Button("나가기", GUILayout.Height(36)))
            {
                LeaveToLobby();
            }

            GUILayout.Space(6);

            if (GUILayout.Button("계속하기", GUILayout.Height(30)))
            {
                SetOpen(false);
            }
        }

        private void DrawSettings()
        {
            // 내용은 메인 메뉴와 공유한다 (SettingsPanel 주석 참고).
            SettingsPanel.Draw();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("뒤로", GUILayout.Height(32)))
            {
                settingsOpen = false;
            }
        }

        /// <summary>
        /// 접속을 끊고 로비(방 만들기·코드 참가 화면)로 돌아간다.
        ///
        /// <b>씬은 바꾸지 않는다.</b> 로비는 이 씬의 화면이고, 접속이 끊기면
        /// 내 캐릭터가 사라져 <see cref="Game.NetworkBootstrapUI"/>가 알아서
        /// 로비 패널로 되돌아간다.
        ///
        /// <b>방장이 나가면 방이 사라진다</b> — 방장이 곧 서버이기 때문이다.
        /// 남은 사람들은 접속이 끊기고 각자 로비로 돌아간다.
        /// </summary>
        private void LeaveToLobby()
        {
            SetOpen(false);

            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer))
            {
                nm.Shutdown();
            }
        }

        private void EnsureStyles()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }
    }
}
