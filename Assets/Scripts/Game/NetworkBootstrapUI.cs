using GhostHunter.Core;
using GhostHunter.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Game
{
    /// <summary>
    /// 개발용 임시 UI. IMGUI라 씬 세팅이 필요 없다.
    ///
    /// <b>방 만들기·참가·닉네임은 더 이상 여기서 다루지 않는다.</b> MainMenuScene의
    /// <see cref="UI.LobbyJoinUI"/>가 접속을 전담하고, 방을 만들면 곧바로 GameScene으로
    /// 넘어간다(<see cref="PreGameLobby.ServerStartGame"/>) — GameScene은 이제 <b>항상
    /// 접속 후에만</b> 로드된다. 이 클래스는 게임 중 좌측 상단 상태 표시와,
    /// 결과 화면(승패·"대기방으로 돌아가기")만 그린다.
    /// </summary>
    public class NetworkBootstrapUI : MonoBehaviour
    {
        private const float PanelWidth = 320f;
        private const float PanelHeight = 260f;

        private GUIStyle titleStyle;

        private string statusMessage;
        private bool subscribed;

        /// <summary>다음 프레임에 Shutdown을 실행해야 하는가. 아래 RequestShutdown 주석 참고.</summary>
        private bool shutdownRequested;
        private string pendingMessage;

        /// <summary>
        /// 접속이 끊기는 것을 <b>직접 감시한다.</b>
        ///
        /// 로비(MainMenuScene)를 거쳐야만 GameScene에 도착하므로 여기서는 항상 접속된
        /// 상태로 시작한다. 그런데도 도중에 끊기면(호스트 종료 등) 캐릭터가 사라지고
        /// 아무 화면도 못 그리는 상태가 되므로, 끊김 콜백으로 붙잡아 메인 메뉴로
        /// 돌아갈 길을 만들어준다.
        /// </summary>
        private void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                return;
            }

            if (!subscribed)
            {
                nm.OnClientDisconnectCallback += OnClientDisconnect;
                subscribed = true;
            }

            TickShutdown(nm);
        }

        private void OnDestroy()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                return;
            }

            if (subscribed)
            {
                nm.OnClientDisconnectCallback -= OnClientDisconnect;
                subscribed = false;
            }

            // 여기서 Shutdown을 부르지 말 것.
            //
            // Play 모드를 나갈 때 NGO는 스스로 정리 절차를 밟는다. 그런데 컴포넌트
            // 파괴 순서는 보장되지 않아서, 그 정리 도중에 끼어들어 Shutdown을 또 부르면
            // 전송 계층이 어중간한 상태에서 끊겨 <b>UDP 소켓이 반환되지 않는다.</b>
            // 실제로 그렇게 만들었다가 Play를 다시 켜도 포트가 안 풀리는 회귀를 냈다.
        }

        private void OnClientDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer)
            {
                return;
            }

            if (!nm.IsConnectedClient)
            {
                RequestShutdown("접속이 끊겼습니다. 방이 없거나 호스트가 종료했습니다.");
            }
        }

        /// <summary>
        /// Shutdown을 <b>다음 프레임으로 미룬다.</b>
        ///
        /// 끊김 콜백 안에서 곧바로 <c>Shutdown()</c>을 부르면 전송 계층이 자기 정리를
        /// 하는 도중에 발밑이 무너진다. 그러면 <b>UDP 소켓이 반환되지 않고 프로세스에 남아</b>,
        /// 화면상 미접속인데도 포트가 계속 물려 다음 호스트 시작이 실패한다.
        /// 에디터를 껐다 켜야만 풀리는 상태가 되므로 반드시 프레임을 넘겨서 정리한다.
        /// </summary>
        private void RequestShutdown(string message)
        {
            shutdownRequested = true;
            pendingMessage = message;
        }

        /// <summary>예약된 Shutdown을 안전한 시점에 실행한다.</summary>
        private void TickShutdown(NetworkManager nm)
        {
            if (!shutdownRequested || nm.ShutdownInProgress)
            {
                return;
            }

            shutdownRequested = false;

            if (nm.IsClient || nm.IsServer)
            {
                nm.Shutdown();
            }

            statusMessage = pendingMessage;
            pendingMessage = null;
        }

        private void OnGUI()
        {
            // 기본 폰트에는 한글 글리프가 없어 빌드에서 글자가 사라진다 (UI.HudFont 참고).
            UI.HudFont.ApplyToSkin();

            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                GUI.Label(new Rect(10, 10, 400, 24), "NetworkManager가 씬에 없습니다.");
                return;
            }

            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            // <b>기준은 단계가 아니라 "내 몸이 있는가"다.</b>
            //
            // GameScene은 항상 접속 후에만 로드되므로 여기 있는 동안 몸이 없다는 것은
            // "이미 접속이 끊겼다"는 뜻이다(예전엔 "아직 접속 전"도 포함했지만 그 경로는
            // 이제 MainMenuScene 쪽에만 있다). 결과 화면은 몸은 있지만 "대기방으로" 버튼을
            // 눌러야 하는 예외다.
            bool hasBody = Player.NetworkPlayer.GetLocal() != null;
            bool isResult = GameManager.CurrentPhase == GamePhase.Result;

            if (hasBody && !isResult)
            {
                DrawInGameHud();
            }
            else
            {
                DrawCenterPanel(nm);
            }
        }

        /// <summary>접속 끊김 안내 또는 결과 화면. 마우스 커서가 살아 있는 상태다.</summary>
        private void DrawCenterPanel(NetworkManager nm)
        {
            var rect = new Rect(
                (Screen.width - PanelWidth) * 0.5f,
                (Screen.height - PanelHeight) * 0.5f,
                PanelWidth, PanelHeight);

            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 16, rect.width - 32, rect.height - 32));

            bool connected = nm.IsServer || nm.IsConnectedClient;

            if (!connected)
            {
                // GameScene은 로비를 거쳐야만 들어오므로, 여기 도달했는데 미접속이면
                // 도중에 끊긴 것이다 — 돌아갈 곳이 없으면 화면이 막힌 채로 남는다.
                GUILayout.Label("연결이 끊겼습니다", titleStyle);
                GUILayout.Space(10);

                if (!string.IsNullOrEmpty(statusMessage))
                {
                    GUILayout.Label(statusMessage);
                    GUILayout.Space(10);
                }

                if (GUILayout.Button("메인 메뉴로", GUILayout.Height(36)))
                {
                    SceneManager.LoadScene("MainMenuScene");
                }

                GUILayout.EndArea();
                return;
            }

            var manager = GameManager.Instance;
            bool isResult = manager != null && manager.Phase.Value == GamePhase.Result;

            GUILayout.Label(isResult ? "게임 종료" : "대기방", titleStyle);
            GUILayout.Space(12);

            GUILayout.Label(nm.IsHost ? "모드: 호스트(방장)" : nm.IsServer ? "모드: 서버" : "모드: 클라이언트");

            // 참가 코드는 방장이 상대에게 알려줘야 하므로 접속 후에도 계속 보여준다.
            if (!string.IsNullOrEmpty(RelayConnection.JoinCode))
            {
                GUILayout.Label($"참가 코드: {RelayConnection.JoinCode}");
            }

            GUILayout.Label($"접속 인원: {NetworkPlayer.All.Count}명");
            GUILayout.Space(8);

            if (isResult && manager != null)
            {
                GUILayout.Label($"결과: {ResultText(manager.Result.Value)}");
                GUILayout.Label($"약점: {manager.RevealedWeakness.Value}");
                GUILayout.Space(8);
            }

            // 참가자 목록. 닉네임이 비어 있으면 DisplayName이 클라이언트 번호로 대신한다.
            foreach (var p in NetworkPlayer.All)
            {
                if (p == null) continue;
                string me = p.IsOwner ? " (나)" : "";
                GUILayout.Label($"· {p.DisplayName}{me}");
            }

            GUILayout.FlexibleSpace();

            if (nm.IsServer && isResult)
            {
                // 시나리오 4번 [7]: 결과 확인 후 대기방으로 돌아가 다시 시작한다.
                // 이 "대기방"은 MainMenuScene의 로비가 아니라 GameScene 안의 걸어다니는
                // 대기방이다 — 재시작은 씬을 넘나들지 않는다(LobbyConsole 참고).
                if (GUILayout.Button("대기방으로 돌아가기", GUILayout.Height(40)))
                {
                    manager.ReturnToLobby();
                }
            }
            else if (!nm.IsServer && isResult)
            {
                GUILayout.Label("방장이 대기방으로 돌아가기를 기다리는 중…");
            }

            if (GUILayout.Button("접속 종료"))
            {
                nm.Shutdown();
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// 대기방에 서 있을 때의 좌측 상단 표시.
        ///
        /// <b>참가 코드가 여기 있어야 한다.</b> 방장은 대기방을 돌아다니면서 코드를
        /// 남에게 알려줘야 하는데, 큰 패널이 접힌 뒤라 볼 방법이 이것뿐이다.
        /// </summary>
        private void DrawLobbyHud()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 160));

            GUILayout.Label("대기방");

            if (!string.IsNullOrEmpty(RelayConnection.JoinCode))
            {
                GUILayout.Label($"참가 코드: {RelayConnection.JoinCode}");
            }

            GUILayout.Label($"접속 인원: {NetworkPlayer.All.Count}명");

            var nm = NetworkManager.Singleton;
            GUILayout.Label(nm != null && nm.IsServer
                ? "게임 설정(F)에서 설정과 시작을 조작합니다."
                : "방장이 시작하기를 기다리는 중…");

            // 백틱은 눈에 띄는 키가 아니다. 안내가 없으면 메뉴가 있는 줄도 모른다.
            GUILayout.Label("` 키: 메뉴 (설정 / 나가기)");

            GUILayout.EndArea();
        }

        /// <summary>게임 중 좌측 상단 상태 표시. 1인칭 화면을 가리지 않게 최소한만.</summary>
        private void DrawInGameHud()
        {
            var manager = GameManager.Instance;
            if (manager == null)
            {
                return;
            }

            if (manager.Phase.Value == GamePhase.Lobby)
            {
                DrawLobbyHud();
                return;
            }

            GUILayout.BeginArea(new Rect(10, 10, 260, 300));

            GUILayout.Label($"단계: {PhaseText(manager.Phase.Value)}");
            GUILayout.Label($"남은 시간: {Mathf.CeilToInt(manager.PhaseTimeRemaining.Value)}초");

            // 현실화 게이지는 조사 단계에만 의미가 있다. 사냥에 들어가면
            // 이미 현실화된 뒤이므로 남은 사냥 시간이 그 자리를 대신한다.
            if (manager.Phase.Value == GamePhase.Investigation)
            {
                GUILayout.Label($"현실화: {manager.MaterializeGauge.Value:F0}%");
            }

            var local = NetworkPlayer.GetLocal();
            if (local != null)
            {
                GUILayout.Label($"진영: {(local.IsGhost ? "귀신" : "퇴마사")}");

                // 약점은 ReadPermission.Owner라 귀신 본인 화면에만 값이 들어온다.
                // 퇴마사 클라이언트에서는 영원히 비어 있다 — 이게 정상이다.
                if (local.IsGhost)
                {
                    GUILayout.Label($"내 약점: {local.Weakness.Value}");
                }
                else
                {
                    GUILayout.Label(local.IsAlive.Value ? "생존" : "사망");

                    var inventory = local.GetComponent<Exorcist.ExorcistInventory>();
                    if (inventory != null)
                    {
                        GUILayout.Label(inventory.HasTool.Value
                            ? $"소지: {inventory.HeldTool.Value.ToKorean()}"
                            : "소지: 없음");
                    }
                }
            }

            var altar = Altar.Instance;
            if (altar != null)
            {
                var offered = altar.GetOffered();
                GUILayout.Label($"제단: {offered.Count}/3");
            }

            GUILayout.EndArea();
        }

        private static string PhaseText(GamePhase phase) => phase switch
        {
            GamePhase.Lobby => "대기",
            GamePhase.Hiding => "은신",
            GamePhase.Investigation => "조사",
            GamePhase.Hunt => "사냥",
            GamePhase.Result => "종료",
            _ => phase.ToString(),
        };

        private static string ResultText(GameResult result) => result switch
        {
            GameResult.ExorcistWin => "퇴마사 승리",
            GameResult.GhostWin => "귀신 승리",
            _ => "-",
        };
    }
}
