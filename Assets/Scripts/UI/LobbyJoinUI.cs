using System.Threading.Tasks;
using GhostHunter.Game;
using GhostHunter.Player;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// MainMenuScene의 로비 화면 — 닉네임 입력, 방 만들기, 참가 코드로 참가 (UGUI).
    ///
    /// 접속 시도·실패·타임아웃 처리는 예전 IMGUI <c>NetworkBootstrapUI</c>에서 이미 검증된
    /// 로직을 그대로 옮긴 것이다 — <c>StartClient()</c>가 방이 없어도 즉시 true를 돌려주는 문제,
    /// <c>Shutdown()</c>을 콜백 안에서 바로 부르면 소켓이 안 풀리는 문제를 다시 겪지 않기 위함.
    ///
    /// <b>방 만들기에 성공하면 곧바로 GameScene으로 넘어간다</b>(<see cref="PreGameLobby.ServerStartGame"/>) —
    /// 어몽어스·챠메레온류처럼 사람들이 모이는 것 자체를 게임 공간(걸어다니는 대기방)에서 본다.
    /// 참가자는 아무것도 안 해도 된다 — NGO가 접속하는 순간 방장이 있는 씬으로 자동으로 데려간다.
    /// </summary>
    public class LobbyJoinUI : MonoBehaviour
    {
        [Header("패널")]
        [SerializeField] private GameObject panelRoot;

        [Header("입력")]
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private TMP_InputField joinCodeInput;

        [Header("버튼")]
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button cancelButton;

        [Header("상태 표시")]
        [SerializeField] private TextMeshProUGUI statusText;

        private const float ConnectTimeoutSeconds = 5f;

        private float connectTimeout;
        private bool subscribed;

        /// <summary>지금 막 누른 버튼의 비동기 처리를 기다리는 중인가 (UI 전용, RelayConnection.IsBusy와 별개).</summary>
        private bool localBusy;

        private bool shutdownRequested;
        private string pendingMessage;

        private void Awake()
        {
            if (hostButton != null) hostButton.onClick.AddListener(OnHostClicked);
            if (joinButton != null) joinButton.onClick.AddListener(OnJoinClicked);
            if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelClicked);

            if (nicknameInput != null)
            {
                nicknameInput.text = NetworkPlayer.LocalNickname ?? string.Empty;
                nicknameInput.onValueChanged.AddListener(v => NetworkPlayer.LocalNickname = v);
            }
        }

        private void OnEnable()
        {
            SetStatus(null);
        }

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

            // 접속되면 이 화면은 볼일이 끝났다 — 곧바로 GameScene으로 넘어간다(HostAsync 참고).
            bool connected = nm.IsServer || nm.IsConnectedClient;
            if (panelRoot != null)
            {
                panelRoot.SetActive(!connected);
            }
            if (connected)
            {
                return;
            }

            // IsClient는 "접속 시도 중"만으로도 true가 된다. 실제로 붙었는지는 IsConnectedClient로 본다.
            if (!shutdownRequested && nm.IsClient && !nm.IsConnectedClient && !nm.IsServer)
            {
                connectTimeout -= Time.deltaTime;
                if (connectTimeout <= 0f)
                {
                    RequestShutdown("방을 찾을 수 없습니다. 참가 코드를 확인해주세요.");
                }
            }

            bool busy = localBusy || nm.ShutdownInProgress || RelayConnection.IsBusy;
            if (hostButton != null) hostButton.interactable = !busy;

            // 참가 코드를 입력하기 전까지는 참가 버튼을 눌러도 뭘 시도할지가 없다 —
            // 빈 코드로 시도하면 어차피 실패하니 아예 못 누르게 막아 실패 메시지를 안 보게 한다.
            if (joinButton != null) joinButton.interactable = !busy && HasJoinCode();

            if (cancelButton != null) cancelButton.gameObject.SetActive(nm.IsClient && !nm.IsConnectedClient && !nm.IsServer);
        }

        private void OnDestroy()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && subscribed)
            {
                nm.OnClientDisconnectCallback -= OnClientDisconnect;
                subscribed = false;
            }

            // Shutdown()은 여기서 부르지 않는다 — Play 종료 시 NGO가 스스로 정리하는데
            // 끼어들면 UDP 소켓이 안 풀린다 (NetworkBootstrapUI와 같은 이유).
        }

        private void OnHostClicked()
        {
            SetStatus("방을 만드는 중…");
            localBusy = true;
            _ = HostAsync();
        }

        private void OnJoinClicked()
        {
            SetStatus("참가하는 중…");
            localBusy = true;
            connectTimeout = ConnectTimeoutSeconds;
            _ = JoinAsync(joinCodeInput != null ? joinCodeInput.text : string.Empty);
        }

        private void OnCancelClicked()
        {
            RequestShutdown(null);
        }

        private bool HasJoinCode()
        {
            return joinCodeInput != null && !string.IsNullOrWhiteSpace(joinCodeInput.text);
        }

        private async Task HostAsync()
        {
            bool ok = await RelayConnection.StartHostAsync();
            localBusy = false;
            SetStatus(ok ? null : RelayConnection.LastError);

            // 방을 만들자마자 바로 저택(걸어다니는 대기방)으로 들어간다. 방장이 나중에
            // LobbyConsole에서 밸런스를 정하고 실제 "게임 시작"을 누른다 — 여기서는
            // 씬만 넘어간다.
            if (ok)
            {
                PreGameLobby.Instance?.ServerStartGame();
            }
        }

        private async Task JoinAsync(string code)
        {
            bool ok = await RelayConnection.StartClientAsync(code);
            localBusy = false;
            if (!ok)
            {
                SetStatus(RelayConnection.LastError);
                connectTimeout = 0f;
            }
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

        /// <summary>Shutdown을 다음 프레임으로 미룬다 (NetworkBootstrapUI와 같은 소켓 누수 회피).</summary>
        private void RequestShutdown(string message)
        {
            shutdownRequested = true;
            pendingMessage = message;
            connectTimeout = 0f;
        }

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

            localBusy = false;
            SetStatus(pendingMessage);
            pendingMessage = null;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }
    }
}
