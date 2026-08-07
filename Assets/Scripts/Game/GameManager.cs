using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Ghost;
using GhostHunter.Player;
using GhostHunter.Tools;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Game
{
    /// <summary>
    /// 서버 권한 게임 진행자 (기술 문서 2-1, 3번).
    ///
    /// 이 클래스가 이 게임의 비밀을 독점한다:
    ///  - 약점 3종을 생성해 귀신에게만 전달 (ReadPermission.Owner)
    ///  - 단계 전이·타이머·승패 판정
    ///
    /// 귀신의 <b>투명 처리는 여기서 하지 않는다</b> — 각 클라이언트의
    /// <see cref="Ghost.GhostVisibility"/>가 현재 단계를 보고 스스로 판단한다.
    /// NetworkHide를 쓰지 않게 된 경위는 그 클래스 주석에 적어뒀다.
    ///
    /// 상태를 바꾸는 코드는 전부 IsServer 가드 안에 있어야 한다.
    /// </summary>
    public class GameManager : NetworkBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>씬 어디서든 설정에 접근하기 위한 단일 창구.</summary>
        public static GameConfig Config => Instance != null ? Instance.config : null;

        public static GamePhase CurrentPhase =>
            Instance != null ? Instance.Phase.Value : GamePhase.Lobby;

        /// <summary>
        /// 지금이 "1인칭으로 몸을 조작하는 중"인가.
        ///
        /// <b>대기방도 걸어다니는 공간이다.</b> 그래서 로비는 여기 포함된다 —
        /// 이동·시점·카메라·커서·이모트·상호작용이 전부 이 값을 따른다.
        /// 각자 판단하게 두면 "커서는 잠겼는데 UI는 떠 있는" 상태가 생기므로 한 곳에 모은다.
        ///
        /// 결과 화면만 예외다. 거기서는 마우스로 버튼을 눌러야 한다.
        /// </summary>
        public static bool IsFirstPersonActive => CurrentPhase != GamePhase.Result;

        /// <summary>
        /// 지금이 "판이 진행 중"인가.
        ///
        /// <see cref="IsFirstPersonActive"/>와 갈라야 한다. 대기방에서는 몸은 움직이지만
        /// 게이지·타이머·손전등·관전은 아직 의미가 없다 — 그런 것들이 이 값을 본다.
        /// </summary>
        public static bool IsGameplayActive
        {
            get
            {
                var phase = CurrentPhase;
                return phase != GamePhase.Lobby && phase != GamePhase.Result;
            }
        }

        /// <summary>
        /// 퇴마사가 움직이고 도구를 다룰 수 있는가 (시나리오 4번 [3]).
        ///
        /// <b>은신 단계에는 퇴마사가 대기지점에 묶여 있어야 한다.</b> 미리 돌아다니며
        /// 도구를 주워두면 귀신이 숨을 시간을 준다는 의미 자체가 사라진다.
        /// 이동·상호작용·줍기가 각자 판단하면 한 군데씩 빠뜨리므로 여기로 모은다.
        /// </summary>
        public static bool ExorcistsCanAct
        {
            get
            {
                var phase = CurrentPhase;
                return phase == GamePhase.Investigation || phase == GamePhase.Hunt;
            }
        }

        [SerializeField] private GameConfig config;

        [Tooltip("귀신이 은신 단계에 시작하는 위치. 비워두면 원점.")]
        [SerializeField] private Transform ghostSpawnPoint;

        [Tooltip("퇴마사들이 대기하는 지점들.")]
        [SerializeField] private Transform[] exorcistSpawnPoints;

        [Tooltip("대기방에서 서 있는 지점. 접속하면 여기로, 판이 끝나도 여기로 돌아온다.")]
        [SerializeField] private Transform lobbySpawnPoint;

        [Tooltip("이 높이보다 아래로 떨어지면 맵 밖으로 본 것이다. 저택 최저점(-22m)보다 낮게 잡을 것.")]
        [SerializeField] private float fallResetHeight = -30f;

        // ── 공개 동기화 상태 ────────────────────────────────────────

        public readonly NetworkVariable<GamePhase> Phase = new(
            GamePhase.Lobby,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>현재 단계의 남은 시간(초).</summary>
        public readonly NetworkVariable<float> PhaseTimeRemaining = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<GameResult> Result = new(
            GameResult.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>결과 화면에서 공개되는 약점. 게임이 끝나기 전에는 비어 있다.</summary>
        public readonly NetworkVariable<WeaknessSet> RevealedWeakness = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// 현실화 게이지 0~100 (시나리오 3-4).
        ///
        /// 공포스킬이 성공할 때마다 오르고 100에 닿으면 사냥 단계로 넘어간다.
        /// <b>양 진영 모두에게 공개한다</b> — 위치 정보를 담지 않으므로 공개해도 은닉이 깨지지 않고,
        /// 퇴마사에게는 "현실화가 얼마나 임박했는가"라는 압박이 된다.
        ///
        /// 이 값이 인원수와 무관하다는 점이 중요하다. 예전의 "생존 퇴마사 전원 흡수" 조건은
        /// 제단 오답으로 누가 죽는 순간 달성 불가가 되는 예외를 안고 있었는데, 게이지에는 그 문제가 없다.
        /// </summary>
        public readonly NetworkVariable<float> MaterializeGauge = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// 방장이 대기방에서 정한 밸런스. 전원이 같은 값을 봐야 한다 —
        /// 탐지 반경 표시처럼 클라이언트도 읽는 값이 섞여 있다.
        /// </summary>
        public readonly NetworkVariable<LobbySettings> Settings = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>서버만 아는 진짜 약점. 절대 공개 NetworkVariable에 넣지 말 것.</summary>
        private WeaknessSet serverWeakness;

        public WeaknessSet ServerWeakness => serverWeakness;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // <b>에셋을 복제해서 쓴다.</b> ScriptableObject는 파일이라, 방장이 값을 바꾸면
            // 에디터에서는 그게 그대로 디스크에 남는다 — 플레이 모드를 껐다 켜도 안 돌아오고
            // git에도 올라간다. 사본에만 쓰면 원본은 손대지 않는다.
            if (config != null)
            {
                config = Instantiate(config);
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && config != null)
            {
                // 처음 값은 에셋에 적힌 그대로. 방장이 만지기 전까지 이게 기본값이다.
                Settings.Value = LobbySettings.From(config);
            }

            Settings.OnValueChanged += OnSettingsChanged;
            ApplySettings(Settings.Value);
        }

        public override void OnNetworkDespawn()
        {
            Settings.OnValueChanged -= OnSettingsChanged;
        }

        private void OnSettingsChanged(LobbySettings previous, LobbySettings current)
        {
            ApplySettings(current);
        }

        /// <summary>
        /// 동기화된 값을 런타임 사본에 옮긴다.
        ///
        /// 이렇게 해두면 <c>GameManager.Config</c>를 읽는 기존 코드는 하나도 안 고쳐도 된다 —
        /// 읽는 곳이 스무 군데가 넘는데 거기까지 손대면 빠뜨리는 곳이 반드시 생긴다.
        /// </summary>
        private void ApplySettings(LobbySettings settings)
        {
            // 스폰 직후 서버가 값을 채우기 전에는 전부 0이다. 그대로 적용하면
            // 조사 시간 0초짜리 판이 된다.
            if (config == null || settings.InvestigationDuration <= 0f)
            {
                return;
            }

            settings.ApplyTo(config);
        }

        /// <summary>방장이 대기방에서 밸런스를 바꾼다. 서버 전용.</summary>
        public void ServerSetSettings(LobbySettings settings)
        {
            if (!IsServer || Phase.Value != GamePhase.Lobby)
            {
                return;
            }

            Settings.Value = settings;
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            base.OnDestroy();
        }

        // ── 게임 시작 ──────────────────────────────────────────────

        /// <summary>방장이 시작을 누르면 호출. 서버에서만 유효하다.</summary>
        public void StartGame()
        {
            if (!IsServer || Phase.Value != GamePhase.Lobby)
            {
                return;
            }

            AssignRoles();
            GenerateWeakness();
            PlaceAtSpawnPoints();

            // 스폰 배치가 끝난 뒤에 초기화해야 본체 좌표가 시작 위치로 잡힌다.
            ResetGhostStates();
            MaterializeGauge.Value = 0f;

            ToolSpawner.Instance?.SpawnAllTools(config);
            Altar.Instance?.ServerClear();

            Result.Value = GameResult.None;
            RevealedWeakness.Value = default;
            EnterPhase(GamePhase.Hiding);
        }

        /// <summary>
        /// 무작위로 귀신 1명을 정한다. 시나리오 2번에 따라 정체는 숨기지 않으므로
        /// 공개 NetworkVariable에 그대로 쓴다 — 숨기는 것은 정체가 아니라 위치다.
        /// </summary>
        private void AssignRoles()
        {
            // <b>파괴된 항목을 반드시 걸러낸다.</b> NetworkPlayer.All은 static 리스트라
            // 비정상 종료로 despawn 콜백을 놓치면 죽은 참조가 남는다. 그대로 두면
            // Faction 대입에서 예외가 터지며 <b>루프가 중간에 끊겨</b>, 뒤쪽 플레이어들이
            // 지난 판 진영을 그대로 유지한다 — "매번 같은 사람만 귀신"이 되는 경로다.
            // 다른 순회(GetGhost, ResetGhostStates 등)는 전부 null을 거르는데 여기만 빠져 있었다.
            var players = new List<NetworkPlayer>();
            foreach (var p in NetworkPlayer.All)
            {
                if (p != null)
                {
                    players.Add(p);
                }
            }

            if (players.Count == 0)
            {
                return;
            }

            int ghostIndex = Random.Range(0, players.Count);
            for (int i = 0; i < players.Count; i++)
            {
                players[i].Faction.Value = i == ghostIndex ? Faction.Ghost : Faction.Exorcist;
                players[i].IsAlive.Value = true;
            }

            Debug.Log($"[GameManager] 진영 배정: {players.Count}명 중 {ghostIndex}번째"
                      + $"(클라 {players[ghostIndex].OwnerClientId})가 귀신.");
        }

        /// <summary>
        /// 약점 3종 생성. 서버가 만들고 <b>귀신에게만</b> 전달한다.
        /// NetworkPlayer.Weakness는 ReadPermission.Owner라 퇴마사에게는 전송조차 되지 않는다.
        /// </summary>
        private void GenerateWeakness()
        {
            var rng = new System.Random(System.Environment.TickCount);
            serverWeakness = WeaknessSet.CreateRandom(config.WeaknessCount, rng);

            var ghost = NetworkPlayer.GetGhost();
            if (ghost != null)
            {
                ghost.Weakness.Value = serverWeakness;
            }
        }

        private void PlaceAtSpawnPoints()
        {
            int exorcistIndex = 0;
            foreach (var p in NetworkPlayer.All)
            {
                if (p == null)
                {
                    continue;
                }

                Transform target = null;
                if (p.IsGhost)
                {
                    target = ghostSpawnPoint;
                }
                else if (exorcistSpawnPoints != null && exorcistSpawnPoints.Length > 0)
                {
                    target = exorcistSpawnPoints[exorcistIndex % exorcistSpawnPoints.Length];
                    exorcistIndex++;
                }

                if (target != null)
                {
                    TeleportPlayer(p, target.position, target.rotation);
                }
            }
        }

        /// <summary>
        /// 대기방으로 보낸다. 서버 전용.
        ///
        /// 접속 직후와 판이 끝난 뒤 모두 여기를 거친다. <b>한 점에 겹쳐 세우지 않는다</b> —
        /// 다섯 명이 같은 좌표에 생기면 <c>CharacterController</c>끼리 밀어내며 튀어나간다.
        /// 접속 순번에 따라 원형으로 벌려 세운다.
        /// </summary>
        public void ServerSendToLobby(NetworkPlayer player)
        {
            if (!IsServer || player == null)
            {
                return;
            }

            if (lobbySpawnPoint == null)
            {
                // 조용히 넘어가면 "왜 엉뚱한 데서 시작하지"로만 보인다. 원인을 남긴다.
                Debug.LogError("[GameManager] lobbySpawnPoint가 비어 있습니다. " +
                               "인스펙터에서 LobbySpawnPoint를 연결하세요.");
                return;
            }

            // 원 위에 순서대로 놓는다. 인원이 늘어도 규칙이 그대로라 자리가 안 겹친다.
            int index = 0;
            foreach (var p in NetworkPlayer.All)
            {
                if (p == player) break;
                if (p != null) index++;
            }

            const float Radius = 1.6f;
            float angle = index * Mathf.PI * 2f / Mathf.Max(1, GameConfigMaxPlayers);
            Vector3 offset = new(Mathf.Cos(angle) * Radius, 0f, Mathf.Sin(angle) * Radius);

            // 원 바깥을 보고 서면 서로 등지게 된다. 안쪽(스폰 지점)을 보게 돌린다.
            Quaternion look = offset.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(-offset)
                : lobbySpawnPoint.rotation;

            Vector3 target = lobbySpawnPoint.position + offset;
            TeleportPlayer(player, target, look);
            Debug.Log($"[GameManager] 플레이어 {player.OwnerClientId}를 대기방 {target:F2}에 세웠다.");
        }

        /// <summary>대기방 배치에 쓰는 인원 기준. 설정이 없으면 5명으로 본다.</summary>
        private int GameConfigMaxPlayers => config != null ? Mathf.Max(1, config.MaxPlayers) : 5;

        /// <summary>
        /// 아직 대기방에 못 세운 사람을 세운다. 서버가 매 프레임 확인한다.
        ///
        /// <b>스폰 순간에 세우면 안 된다.</b> <c>OnNetworkSpawn</c> 안에서 보낸
        /// 텔레포트 RPC는 스폰 절차가 끝나기 전이라 그대로 묻힌다 — 호스트가
        /// 엉뚱한 자리에서 시작하던 원인이 이것이었다. 한 프레임 뒤에 세우면
        /// 오브젝트가 완전히 자리를 잡은 뒤라 확실히 먹는다.
        /// </summary>
        private void PlaceNewcomersInLobby()
        {
            foreach (var p in NetworkPlayer.All)
            {
                if (p == null || !p.IsSpawned || p.ServerLobbyPlaced)
                {
                    continue;
                }

                p.ServerLobbyPlaced = true;
                ServerSendToLobby(p);
            }
        }

        /// <summary>전원을 다시 세우도록 표시한다. 다음 프레임에 옮겨진다.</summary>
        private void MarkEveryoneForLobby()
        {
            foreach (var p in NetworkPlayer.All)
            {
                if (p != null)
                {
                    p.ServerLobbyPlaced = false;
                }
            }
        }

        /// <summary>
        /// 플레이어를 특정 자리로 옮긴다.
        ///
        /// <b>서버가 transform을 직접 바꾸면 안 된다.</b> 위치가 소유자 권한이라
        /// 다음 프레임에 소유자 좌표로 덮어써진다 — 호스트만 옮겨지고 나머지는
        /// 제자리에 남는다. 실제 이동은 소유자에게 시킨다 (NetworkPlayer.TeleportRpc).
        /// </summary>
        private static void TeleportPlayer(NetworkPlayer player, Vector3 position, Quaternion rotation)
        {
            player.TeleportRpc(position, rotation);
        }

        // ── 단계 전이 ──────────────────────────────────────────────

        private void EnterPhase(GamePhase phase)
        {
            if (!IsServer)
            {
                return;
            }

            Phase.Value = phase;
            PhaseTimeRemaining.Value = DurationOf(phase);

            switch (phase)
            {
                case GamePhase.Investigation:
                    // 은신 종료 → 귀신 본체 고정 (시나리오 4번 [3])
                    LockGhostBody(true);
                    break;

                case GamePhase.Hunt:
                    // 현실화: 모습과 소리가 함께 드러난다 (기술 문서 6-1).
                    // 실제로 모습을 드러내는 건 각 클라이언트의 GhostVisibility가
                    // 단계를 보고 알아서 한다 — 여기서 따로 명령하지 않는다.
                    //
                    // 영혼이 나가 있는 채로 사냥에 들어갈 수 있다. 합쳐주지 않으면
                    // 옛 본체 자리에 표식이 계속 남는다 (ServerMergeSoulIntoBody 주석 참고).
                    MergeGhostSoul();
                    LockGhostBody(false);

                    // 흡수 쿨타임(30초)을 처형에 물려주면 1분짜리 사냥이 절반 날아간다.
                    ResetGhostCooldown();
                    break;

                case GamePhase.Result:
                    RevealedWeakness.Value = serverWeakness;
                    break;
            }
        }

        private float DurationOf(GamePhase phase) => phase switch
        {
            GamePhase.Hiding => config.HidingDuration,
            GamePhase.Investigation => config.InvestigationDuration,
            GamePhase.Hunt => config.HuntDuration,
            _ => 0f,
        };

        private void LockGhostBody(bool locked)
        {
            var ghost = NetworkPlayer.GetGhost();
            if (ghost == null)
            {
                return;
            }

            var ghostController = ghost.GetComponent<GhostController>();
            if (ghostController != null)
            {
                ghostController.SetBodyLocked(locked);
            }
        }

        /// <summary>현실화 시 영혼과 본체를 합친다.</summary>
        private void MergeGhostSoul()
        {
            NetworkPlayer.GetGhost()?.GetComponent<GhostController>()?.ServerMergeSoulIntoBody();
        }

        /// <summary>귀신의 공포스킬 쿨타임을 0으로 되돌린다.</summary>
        private void ResetGhostCooldown()
        {
            NetworkPlayer.GetGhost()?.GetComponent<Ghost.FearSkill>()?.ServerResetCooldown();
        }

        /// <summary>
        /// 모든 플레이어의 귀신 상태를 초기화한다.
        ///
        /// <b>귀신이었던 사람뿐 아니라 전원에게</b> 돌린다. 다음 판에 누가 귀신이 될지
        /// 모르는데 지난 판 귀신만 지우면, 그 전 판에 귀신이었던 사람의 찌꺼기가 남는다.
        /// </summary>
        private void ResetGhostStates()
        {
            foreach (var p in NetworkPlayer.All)
            {
                if (p == null)
                {
                    continue;
                }

                p.GetComponent<GhostController>()?.ServerResetForNewRound();

                // 쿨타임도 판을 넘어가면 안 된다. 지난 판 끝에 스킬을 썼다면
                // 새 판이 시작되자마자 쓸 수 없는 상태로 출발한다.
                p.GetComponent<Ghost.FearSkill>()?.ServerResetCooldown();
            }
        }

        /// <summary>
        /// 맵 밖으로 떨어진 플레이어를 되돌린다.
        ///
        /// 저택 에셋에는 <b>문 뒤에 바닥이 없는 곳</b>이 있어서 들어가면 끝없이 추락한다.
        /// 어느 문이 그런지 일일이 찾아 막는 것보다 이 안전망이 확실하다 —
        /// 나중에 맵을 손보다가 새 구멍이 생겨도 게임이 멈추지 않는다.
        /// </summary>
        private void RescueFallenPlayers()
        {
            foreach (var p in NetworkPlayer.All)
            {
                if (p == null || p.transform.position.y > fallResetHeight)
                {
                    continue;
                }

                // <b>어디로 되돌릴지는 단계가 정한다.</b> 대기방에서 떨어진 사람을
                // 저택으로 보내면 시작도 안 한 게임의 한복판에 떨어뜨리는 셈이다.
                if (Phase.Value == GamePhase.Lobby)
                {
                    ServerSendToLobby(p);
                    Debug.Log($"[GameManager] 대기방 밖으로 떨어진 플레이어 {p.OwnerClientId}를 복귀시켰다.");
                    continue;
                }

                Transform target = p.IsGhost
                    ? ghostSpawnPoint
                    : exorcistSpawnPoints is { Length: > 0 } ? exorcistSpawnPoints[0] : null;

                if (target == null)
                {
                    continue;
                }

                TeleportPlayer(p, target.position, target.rotation);
                Debug.Log($"[GameManager] 맵 밖으로 떨어진 플레이어 {p.OwnerClientId}를 복귀시켰다.");
            }
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            RescueFallenPlayers();

            var phase = Phase.Value;
            if (phase is GamePhase.Lobby or GamePhase.Result)
            {
                // 접속하자마자는 자리를 잡아줄 수 없어 여기서 뒤늦게 세운다.
                if (phase == GamePhase.Lobby)
                {
                    PlaceNewcomersInLobby();
                }
                return;
            }

            PhaseTimeRemaining.Value -= Time.deltaTime;
            if (PhaseTimeRemaining.Value > 0f)
            {
                return;
            }

            switch (phase)
            {
                case GamePhase.Hiding:
                    EnterPhase(GamePhase.Investigation);
                    break;

                case GamePhase.Investigation:
                    // 제한시간 종료 → 사냥 단계 (시나리오 4번 [4])
                    EnterPhase(GamePhase.Hunt);
                    break;

                case GamePhase.Hunt:
                    // 1분 종료 시 생존자가 있으면 퇴마사 승리 (시나리오 5번)
                    EndGame(NetworkPlayer.GetLivingExorcists().Count > 0
                        ? GameResult.ExorcistWin
                        : GameResult.GhostWin);
                    break;
            }
        }

        // ── 승패 ───────────────────────────────────────────────────

        public void EndGame(GameResult result)
        {
            if (!IsServer || Phase.Value == GamePhase.Result)
            {
                return;
            }

            Result.Value = result;
            EnterPhase(GamePhase.Result);
        }

        /// <summary>
        /// 결과 화면에서 대기방으로 돌아간다 (시나리오 4번 [7]). 방장만 호출한다.
        ///
        /// <b>다음 판에 남으면 안 되는 것을 전부 지운다</b> — 도구, 제단, 진영, 약점,
        /// 생존·흡수 상태, 본체 고정. 하나라도 남으면 새 판이 이전 판 상태를 물려받아
        /// "왜 시작하자마자 제단에 도구가 있지" 같은 유령 버그가 된다.
        /// </summary>
        public void ReturnToLobby()
        {
            if (!IsServer || Phase.Value != GamePhase.Result)
            {
                return;
            }

            ToolSpawner.Instance?.DespawnAll();
            Altar.Instance?.ServerClear();

            // 영혼 상태·본체 좌표·본체 고정·쿨타임을 한 번에 지운다.
            // 하나라도 남으면 새 판이 이전 판 상태를 물려받는다.
            ResetGhostStates();

            foreach (var p in NetworkPlayer.All)
            {
                if (p == null)
                {
                    continue;
                }

                p.Faction.Value = Faction.Unassigned;
                p.IsAlive.Value = true;
                p.Weakness.Value = default;
            }

            serverWeakness = default;
            RevealedWeakness.Value = default;
            Result.Value = GameResult.None;
            MaterializeGauge.Value = 0f;

            Phase.Value = GamePhase.Lobby;
            PhaseTimeRemaining.Value = 0f;

            // 단계만 되돌리면 저택 한복판에 그대로 서 있게 된다. 몸도 같이 옮긴다.
            MarkEveryoneForLobby();
        }

        /// <summary>
        /// 퇴마사가 사망했을 때 서버가 호출. 전멸하면 사냥 단계에서 귀신 승리다.
        /// </summary>
        public void OnExorcistDied()
        {
            if (!IsServer)
            {
                return;
            }

            if (NetworkPlayer.GetLivingExorcists().Count == 0)
            {
                EndGame(GameResult.GhostWin);
                return;
            }

            // 게이지는 인원수와 무관하므로 사망으로 현실화 조건이 바뀌지 않는다.
            // 예전에는 여기서 "흡수되지 않은 생존자가 사라졌는지"를 다시 확인해야 했다.
        }

        /// <summary>
        /// 공포스킬 성공 시 현실화 게이지를 올린다 (시나리오 3-4). 서버 전용.
        ///
        /// 100에 닿으면 곧바로 사냥 단계로 넘어간다. 조사 단계가 아니면 아무것도 하지 않는다 —
        /// 사냥 중의 처형까지 게이지를 올리면 의미가 없다.
        /// </summary>
        public void ServerAddMaterializeGauge(float amount)
        {
            if (!IsServer || Phase.Value != GamePhase.Investigation)
            {
                return;
            }

            MaterializeGauge.Value = Mathf.Clamp(MaterializeGauge.Value + amount, 0f, 100f);
            Debug.Log($"[GameManager] 현실화 게이지 {MaterializeGauge.Value:F0}%");

            if (MaterializeGauge.Value >= 100f)
            {
                EnterPhase(GamePhase.Hunt);
            }
        }
    }
}
