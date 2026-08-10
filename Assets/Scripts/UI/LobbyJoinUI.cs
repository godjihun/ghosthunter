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

        [Header("입력 오류 안내 (평소엔 비어있다가 검증 실패 시에만 표시)")]
        [SerializeField] private TextMeshProUGUI nicknameErrorText;
        [SerializeField] private TextMeshProUGUI joinCodeErrorText;

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

                // 입력칸을 다시 누르는(포커스 얻는) 순간 그 칸의 오류 문구를 지운다.
                nicknameInput.onSelect.AddListener(_ => SetNicknameError(null));
            }

            if (joinCodeInput != null)
            {
                joinCodeInput.onSelect.AddListener(_ => SetJoinCodeError(null));
            }
        }

        private void OnEnable()
        {
            SetStatus(null);
            SetNicknameError(null);
            SetJoinCodeError(null);
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
                    SetJoinCodeError("참가코드를 확인해주세요");
                }
            }

            bool busy = localBusy || nm.ShutdownInProgress || RelayConnection.IsBusy;
            if (hostButton != null) hostButton.interactable = !busy;

            // 코드가 비어 있어도 눌리게 둔다 — 눌러야 "코드를 입력해주세요"가 뜬다.
            // 어떤 게 비었는지는 OnJoinClicked가 판단해서 각자 오류 문구로 알려준다.
            if (joinButton != null) joinButton.interactable = !busy;

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
            if (!ValidateNickname())
            {
                return;
            }

            SetStatus("방을 만드는 중…");
            localBusy = true;
            _ = HostAsync();
        }

        /// <summary>
        /// 닉네임·참가 코드를 <b>독립적으로</b> 검증한다 — 서로 눈치 안 보고 각자 비었으면
        /// 각자의 오류 문구를 띄운다. 둘 다 비었으면 둘 다 뜬다.
        ///
        /// 실제 접속 시도는 <b>코드가 있어야만</b> 가능하다(뭘로 시도할지가 없으므로).
        /// 닉네임이 비어 있어도 코드만 있으면 일단 접속은 시도한다 — 이름 없이 들어가도
        /// 치명적이지 않고("플레이어 N"으로 표시될 뿐), 그래야 코드가 진짜 틀렸는지도
        /// 같이 확인할 수 있다.
        /// </summary>
        private void OnJoinClicked()
        {
            // 닉네임 오류 표시는 이 호출 하나로 끝난다(값을 따로 안 써도 된다) — 접속 자체를
            // 막지는 않는다.
            ValidateNickname();

            bool hasCode = HasJoinCode();
            SetJoinCodeError(hasCode ? null : "참가 코드를 입력해주세요");

            if (!hasCode)
            {
                return;
            }

            SetStatus("참가하는 중…");
            localBusy = true;
            connectTimeout = ConnectTimeoutSeconds;
            _ = JoinAsync(joinCodeInput.text);
        }

        private void OnCancelClicked()
        {
            RequestShutdown(null);
        }

        private bool HasJoinCode()
        {
            return joinCodeInput != null && !string.IsNullOrWhiteSpace(joinCodeInput.text);
        }

        /// <summary>닉네임 칸이 비어 있으면 오류를 띄우고 false. 그 외엔 오류를 지우고 true.</summary>
        private bool ValidateNickname()
        {
            bool valid = !string.IsNullOrWhiteSpace(NetworkPlayer.LocalNickname);
            SetNicknameError(valid ? null : "이름을 입력해주세요");
            return valid;
        }

        private void SetNicknameError(string message)
        {
            if (nicknameErrorText == null)
            {
                return;
            }

            nicknameErrorText.text = message ?? string.Empty;
            nicknameErrorText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        private void SetJoinCodeError(string message)
        {
            if (joinCodeErrorText == null)
            {
                return;
            }

            joinCodeErrorText.text = message ?? string.Empty;
            joinCodeErrorText.gameObject.SetActive(!string.IsNullOrEmpty(message));
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
                // 씬 전환으로 화면이 잠깐 지저분해지는 구간을 가린다. SceneLoadingScreenUI가
                // 실제로 GameScene이 활성화되는 순간 스스로 내린다.
                SceneLoadingScreenUI.Show();
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
                SetJoinCodeError("참가코드를 확인해주세요");
                connectTimeout = 0f;
            }
            else
            {
                // 접속에 성공하면 곧 방장이 있는 씬(대개 이미 GameScene)으로 동기화된다.
                // 그 전환도 같은 로딩 화면으로 가린다.
                SceneLoadingScreenUI.Show();
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
                SetJoinCodeError("참가코드를 확인해주세요");
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
