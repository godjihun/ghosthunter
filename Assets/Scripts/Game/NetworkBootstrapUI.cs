using GhostHunter.Core;
using GhostHunter.Player;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Game
{
    /// <summary>
    /// 개발용 임시 UI. IMGUI라 씬 세팅이 필요 없다.
    ///
    /// 로비·결과 화면에서는 <b>가운데 패널</b>로 크게 띄우고,
    /// 게임 중에는 방해되지 않게 좌측 상단 작은 상태 표시로 물러난다.
    /// 정식 로비 UI(구현 순서 9번)로 교체될 자리다.
    /// </summary>
    public class NetworkBootstrapUI : MonoBehaviour
    {
        private const float PanelWidth = 320f;
        private const float PanelHeight = 420f;

        /// <summary>이 시간 안에 못 붙으면 방이 없는 것으로 본다.</summary>
        private const float ConnectTimeoutSeconds = 5f;

        /// <summary>포트가 풀리기를 기다리며 호스트 시작을 재시도하는 시간.</summary>
        private const float HostRetrySeconds = 3f;

        private GUIStyle titleStyle;

        private float connectTimeout;
        private string statusMessage;
        private bool subscribed;

        /// <summary>호스트 시작을 재시도할 남은 시간. 0이면 재시도 안 함.</summary>
        private float hostRetryTimer;

        /// <summary>참가 코드 입력란. Relay가 발급하는 코드는 6자 안팎이다.</summary>
        private string joinCodeInput = string.Empty;

        /// <summary>
        /// 방 만들기. <b>결과를 기다리는 동안 UI를 막아야 한다</b> —
        /// Relay 할당은 네트워크 왕복이라 즉시 끝나지 않는다.
        /// </summary>
        private async System.Threading.Tasks.Task HostAsync()
        {
            bool ok = await RelayConnection.StartHostAsync();
            statusMessage = ok
                ? $"방을 만들었습니다. 참가 코드: {RelayConnection.JoinCode}"
                : RelayConnection.LastError;
        }

        private async System.Threading.Tasks.Task JoinAsync(string code)
        {
            bool ok = await RelayConnection.StartClientAsync(code);
            if (!ok)
            {
                statusMessage = RelayConnection.LastError;
                connectTimeout = 0f;
            }
            else
            {
                statusMessage = null;
            }
        }

        /// <summary>다음 프레임에 Shutdown을 실행해야 하는가. 아래 RequestShutdown 주석 참고.</summary>
        private bool shutdownRequested;
        private string pendingMessage;

        /// <summary>
        /// 접속 실패를 <b>직접 감시한다.</b>
        ///
        /// <c>StartClient()</c>는 방이 없어도 즉시 true를 돌려준다 — 실제 연결은
        /// 그 뒤에 비동기로 이뤄지기 때문이다. 그래서 이걸 성공으로 받아들이면
        /// "모드: 클라이언트"인데 아무것도 못 하는 상태에 갇힌다.
        /// 끊김 통지와 제한시간, 두 가지로 실패를 잡는다.
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

            // 접속 시도 중일 때만 제한시간을 센다.
            if (!shutdownRequested && nm.IsClient && !nm.IsConnectedClient && !nm.IsServer)
            {
                connectTimeout -= Time.deltaTime;
                if (connectTimeout <= 0f)
                {
                    RequestShutdown("방을 찾을 수 없습니다. 먼저 호스트로 방을 만드세요.");
                }
            }

            TickHostRetry(nm);
        }

        /// <summary>
        /// 호스트 시작 재시도.
        ///
        /// <c>Shutdown()</c>은 즉시 끝나지 않는다 — 실패한 접속을 정리하는 동안
        /// 포트가 아직 물려 있어서, 그 순간 방을 만들면 "포트 사용 중"으로 거부된다.
        /// 사용자에게 "잠시 뒤 다시 누르세요"라고 떠넘기는 대신 정리가 끝나면 알아서 잡는다.
        /// </summary>
        private void TickHostRetry(NetworkManager nm)
        {
            if (hostRetryTimer <= 0f)
            {
                return;
            }

            hostRetryTimer -= Time.deltaTime;

            // 아직 정리 중이면 건드리지 않는다.
            if (nm.ShutdownInProgress || nm.IsListening)
            {
                if (nm.IsListening)
                {
                    hostRetryTimer = 0f;
                    statusMessage = null;
                }
                return;
            }

            if (nm.StartHost())
            {
                hostRetryTimer = 0f;
                statusMessage = null;
                return;
            }

            if (hostRetryTimer <= 0f)
            {
                statusMessage = "방을 만들지 못했습니다. 다른 유니티 창이 7777 포트를 쓰고 있는지 확인하세요.";
            }
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

            // 붙어보지도 못하고 끊겼다 = 방이 없거나 거부당했다.
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
            connectTimeout = 0f;
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
            // 접속하면 대기방에 캐릭터로 서게 되므로, 그때부터 이 큰 패널은
            // 화면 한가운데를 가리는 방해물일 뿐이다. 방을 만들거나 코드를 넣는
            // 일은 몸이 생기기 전에 끝나 있다.
            //
            // 결과 화면은 예외다. 몸은 있지만 "대기방으로" 버튼을 눌러야 한다.
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

        /// <summary>로비·결과 화면. 마우스 커서가 살아 있는 상태다.</summary>
        private void DrawCenterPanel(NetworkManager nm)
        {
            var rect = new Rect(
                (Screen.width - PanelWidth) * 0.5f,
                (Screen.height - PanelHeight) * 0.5f,
                PanelWidth, PanelHeight);

            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 16, rect.width - 32, rect.height - 32));

            var manager = GameManager.Instance;
            bool isResult = manager != null && manager.Phase.Value == GamePhase.Result;

            // 여기가 <b>로비</b>다 — 방을 만들거나 코드로 들어가는 화면.
            // 접속한 뒤 캐릭터로 서 있는 방은 <b>대기방</b>이라 부른다. 둘은 다른 곳이다.
            GUILayout.Label(isResult ? "게임 종료" : "로비", titleStyle);
            GUILayout.Space(12);

            // IsClient는 "접속을 시도 중"만으로도 true가 된다.
            // 실제로 붙었는지는 IsConnectedClient로 봐야 한다.
            bool connected = nm.IsServer || nm.IsConnectedClient;

            if (!connected)
            {
                if (nm.IsClient)
                {
                    // 접속 시도 중. 방이 없으면 여기서 시간만 흐르다 실패한다.
                    GUILayout.Label($"접속 중… ({Mathf.CeilToInt(connectTimeout)}초)");
                    GUILayout.Space(8);
                    if (GUILayout.Button("취소", GUILayout.Height(32)))
                    {
                        RequestShutdown(null);
                    }
                    GUILayout.EndArea();
                    return;
                }

                // 이전 연결을 정리하는 중에는 버튼을 막는다. 여기서 시작하면 반드시 실패한다.
                bool busy = nm.ShutdownInProgress || hostRetryTimer > 0f;

                GUILayout.Label(busy ? "이전 연결을 정리하는 중…" : "접속되지 않았습니다.");

                if (!string.IsNullOrEmpty(statusMessage))
                {
                    GUILayout.Space(4);
                    GUILayout.Label(statusMessage);
                }

                GUILayout.Space(8);

                // 닉네임은 접속 전에 정해야 한다. 스폰 순간 서버로 올라가므로
                // 접속한 뒤에 바꾸면 이번 판에는 반영되지 않는다.
                GUILayout.Label("닉네임");
                NetworkPlayer.LocalNickname = GUILayout.TextField(
                    NetworkPlayer.LocalNickname ?? string.Empty, 10, GUILayout.Height(24));

                GUILayout.Space(8);

                // 참가 코드
                GUILayout.Label("참가 코드 (참가할 때만)");
                joinCodeInput = GUILayout.TextField(joinCodeInput ?? string.Empty, 8, GUILayout.Height(24));

                GUILayout.Space(8);

                GUI.enabled = !busy && !RelayConnection.IsBusy;

                if (GUILayout.Button("방 만들기", GUILayout.Height(36)))
                {
                    statusMessage = "방을 만드는 중…";
                    _ = HostAsync();
                }

                if (GUILayout.Button("코드로 참가", GUILayout.Height(36)))
                {
                    statusMessage = "참가하는 중…";
                    connectTimeout = ConnectTimeoutSeconds;
                    _ = JoinAsync(joinCodeInput);
                }

                GUI.enabled = true;

                GUILayout.EndArea();
                return;
            }

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

            if (nm.IsServer && manager != null && manager.Phase.Value == GamePhase.Lobby)
            {
                // 시작 버튼은 <b>대기방의 게임 설정 단말에만</b> 둔다. 두 곳에 두면
                // 대기방에 들어서기도 전에 시작해버릴 수 있고, 방장이 어느 쪽을
                // 눌러야 하는지도 헷갈린다.
                GUILayout.Label("대기방의 게임 설정 앞에서 F를 눌러 시작하세요.");
            }
            else if (nm.IsServer && isResult)
            {
                // 시나리오 4번 [7]: 결과 확인 후 대기방으로 돌아가 다시 시작한다.
                if (GUILayout.Button("대기방으로 돌아가기", GUILayout.Height(40)))
                {
                    manager.ReturnToLobby();
                }
            }
            else if (!nm.IsServer)
            {
                GUILayout.Label(isResult
                    ? "방장이 대기방으로 돌아가기를 기다리는 중…"
                    : "방장이 시작하기를 기다리는 중…");
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
