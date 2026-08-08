using GhostHunter.Core;
using GhostHunter.Player;
using GhostHunter.UI;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Game
{
    /// <summary>
    /// 대기방의 게임 설정 단말. 방장이 F로 열어 밸런스를 조절하고 게임을 시작한다.
    ///
    /// <b>NetworkBehaviour가 아니다.</b> 자체 동기화 상태가 없고, 조작할 수 있는 사람이
    /// 방장 = 서버뿐이라 RPC를 거칠 이유가 없다. 방장이 누른 것은 그 자리에서
    /// 서버 코드(<see cref="GameManager.StartGame"/> 등)로 바로 들어간다.
    /// 씬에 놓인 오브젝트라 전원이 같은 것을 갖고 있고, 남들에게는 프롬프트만 다르게 보인다.
    /// </summary>
    public class LobbyConsole : MonoBehaviour, IInteractable
    {
        [Tooltip("프롬프트를 띄울 위치. 비우면 오브젝트 원점.")]
        [SerializeField] private Transform promptAnchor;

        private const float PanelWidth = 460f;
        private const float PanelHeight = 490f;

        private bool panelOpen;
        private GUIStyle titleStyle;
        private GUIStyle noticeStyle;

        /// <summary>방장인가. 방을 만든 사람이 곧 서버라 클라이언트 번호로 가른다.</summary>
        private static bool IsHostPlayer(NetworkPlayer viewer)
        {
            return viewer != null && viewer.OwnerClientId == NetworkManager.ServerClientId;
        }

        private static bool InLobby => GameManager.CurrentPhase == GamePhase.Lobby;

        // ── IInteractable ──────────────────────────────────────────

        public Transform PromptAnchor => promptAnchor != null ? promptAnchor : transform;

        public bool CanInteract(NetworkPlayer viewer) => InLobby && IsHostPlayer(viewer);

        public string GetPrompt(NetworkPlayer viewer)
        {
            if (!InLobby)
            {
                return null;
            }

            // <b>남에게도 문구는 띄운다.</b> 아무 반응이 없으면 고장인 줄 알고
            // 계속 누르게 된다. 왜 안 되는지 알려주는 편이 낫다.
            return IsHostPlayer(viewer) ? "게임 설정 (F)" : "방장만 사용할 수 있다";
        }

        /// <summary>PlayerInteractor가 F 입력을 넘겨준다.</summary>
        public void Interact(NetworkPlayer viewer)
        {
            if (!CanInteract(viewer))
            {
                return;
            }

            SetPanelOpen(!panelOpen);
        }

        // ── 창 ────────────────────────────────────────────────────

        private void SetPanelOpen(bool open)
        {
            panelOpen = open;

            // 커서는 PlayerRoleSetup이 단독으로 관리한다. 여기서 Cursor를 직접 만지면
            // 매 프레임 서로 덮어써서 깜빡인다 (PlayerRoleSetup.UiPanelOpen 주석 참고).
            PlayerRoleSetup.UiPanelOpen = open;
        }

        private void Update()
        {
            if (!panelOpen)
            {
                return;
            }

            // 게임이 시작되면 창을 붙들고 있을 이유가 없다. 열린 채로 저택에 떨어지면
            // 커서가 풀린 상태로 게임이 시작된다.
            if (!InLobby)
            {
                SetPanelOpen(false);
            }
        }

        private void OnDisable()
        {
            // 창을 연 채로 씬이 정리되면 커서가 풀린 채 남는다.
            if (panelOpen)
            {
                SetPanelOpen(false);
            }
        }

        private void OnGUI()
        {
            if (!panelOpen)
            {
                return;
            }

            // 기본 폰트에는 한글 글리프가 없다. 빌드에서 글자가 통째로 사라진다.
            HudFont.ApplyToSkin();
            EnsureStyles();

            var rect = new Rect(
                (Screen.width - PanelWidth) * 0.5f,
                (Screen.height - PanelHeight) * 0.5f,
                PanelWidth, PanelHeight);

            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 18, rect.y + 16, rect.width - 36, rect.height - 32));

            GUILayout.Label("게임 설정", titleStyle);
            GUILayout.Space(8);

            DrawRoster();
            GUILayout.Space(10);
            DrawSettings();
            GUILayout.Space(12);
            DrawActions();

            GUILayout.EndArea();
        }

        private void DrawRoster()
        {
            int count = 0;
            var names = new System.Text.StringBuilder();
            foreach (var p in NetworkPlayer.All)
            {
                if (p == null) continue;
                if (count > 0) names.Append(", ");
                names.Append(p.DisplayName);
                count++;
            }

            GUILayout.Label($"대기 인원 {count}명");
            GUILayout.Label(names.Length > 0 ? names.ToString() : "-", noticeStyle);
        }

        private void DrawSettings()
        {
            var manager = GameManager.Instance;
            if (manager == null)
            {
                return;
            }

            var settings = manager.Settings.Value;
            bool changed = false;

            for (int i = 0; i < LobbySettings.Fields.Length; i++)
            {
                var (label, min, max, step, unit) = LobbySettings.Fields[i];
                float value = settings[i];

                GUILayout.BeginHorizontal();
                GUILayout.Label(label, GUILayout.Width(130));
                float raw = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(190));

                // 슬라이더는 아주 미세한 값도 뱉는다. 눈금 단위로 끊어야
                // 매 프레임 "바뀌었다"고 판단해 네트워크로 계속 쏘지 않는다.
                //
                // 도구 개수는 눈금이 6이다 — 6의 배수가 아니면 나머지가 배치되지 않는다.
                float next = Mathf.Round(raw / step) * step;

                GUILayout.Label($"{next}{unit}", GUILayout.Width(60));
                GUILayout.EndHorizontal();

                if (!Mathf.Approximately(next, value))
                {
                    settings[i] = next;
                    changed = true;
                }
            }

            if (changed)
            {
                // 방장이 곧 서버라 그대로 서버 함수를 부른다.
                manager.ServerSetSettings(settings);
            }
        }

        private void DrawActions()
        {
            var manager = GameManager.Instance;

            int players = 0;
            foreach (var p in NetworkPlayer.All)
            {
                if (p != null) players++;
            }

            // 혼자서도 시작할 수 있게 둔다 — 개발 중 테스트가 훨씬 편하다.
            // 다만 인원이 적으면 진영이 한쪽으로 쏠린다는 것만 알려준다.
            if (players < 2)
            {
                GUILayout.Label("혼자 시작하면 귀신 역할로 배정된다.", noticeStyle);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("게임 시작", GUILayout.Height(34)))
            {
                manager?.StartGame();
                SetPanelOpen(false);
            }
            if (GUILayout.Button("닫기", GUILayout.Height(34), GUILayout.Width(90)))
            {
                SetPanelOpen(false);
            }
            GUILayout.EndHorizontal();
        }

        private void EnsureStyles()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            noticeStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
            };
        }
    }
}
