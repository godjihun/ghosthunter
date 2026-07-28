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
        /// 지금이 "1인칭으로 조작하는 중"인가.
        ///
        /// 로비와 결과 화면은 마우스로 UI를 눌러야 하므로 1인칭이 아니다.
        /// 이 값 하나로 카메라·커서·이동·시점을 한꺼번에 전환한다 —
        /// 각자 판단하게 두면 "커서는 잠겼는데 UI는 떠 있는" 상태가 생긴다.
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
            // 다른 순회(GetGhost, ResetAbsorption 등)는 전부 null을 거르는데 여기만 빠져 있었다.
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
                players[i].IsAbsorbed.Value = false;
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

        private static void TeleportPlayer(NetworkPlayer player, Vector3 position, Quaternion rotation)
        {
            // CharacterController가 켜져 있으면 위치 대입이 무시되므로 잠깐 끈다.
            var cc = player.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
            }

            player.transform.SetPositionAndRotation(position, rotation);

            if (cc != null)
            {
                cc.enabled = true;
            }
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
                if (p != null)
                {
                    p.GetComponent<GhostController>()?.ServerResetForNewRound();
                }
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

            foreach (var p in NetworkPlayer.All)
            {
                if (p == null)
                {
                    continue;
                }

                // 본체 고정뿐 아니라 영혼 분리 상태·본체 좌표까지 지운다.
                // 고정만 풀면 IsSoulOut이 남아 다음 판에 옛 본체 표식이 그대로 뜬다.
                p.GetComponent<GhostController>()?.ServerResetForNewRound();

                p.Faction.Value = Faction.Unassigned;
                p.IsAlive.Value = true;
                p.IsAbsorbed.Value = false;
                p.Weakness.Value = default;
            }

            serverWeakness = default;
            RevealedWeakness.Value = default;
            Result.Value = GameResult.None;

            Phase.Value = GamePhase.Lobby;
            PhaseTimeRemaining.Value = 0f;
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

            // 사망으로 "흡수되지 않은 생존자"가 사라졌을 수 있다 → 현실화 조건 재확인.
            CheckMaterializationCondition();
        }

        /// <summary>
        /// 현실화 조건은 "흡수되지 않은 생존 퇴마사가 0명"이다 (시나리오 3-4).
        ///
        /// 흡수 카운트와 총 인원을 비교하면 안 된다 — 제단 오답으로 누가 죽는 순간
        /// 조건이 영영 달성 불가능해져 게임이 멈춘다.
        /// </summary>
        public void CheckMaterializationCondition()
        {
            if (!IsServer || Phase.Value != GamePhase.Investigation)
            {
                return;
            }

            foreach (var p in NetworkPlayer.GetLivingExorcists())
            {
                if (!p.IsAbsorbed.Value)
                {
                    return; // 아직 남아 있다
                }
            }

            EnterPhase(GamePhase.Hunt);
        }

        /// <summary>탐지당하면 영혼 수집이 초기화된다 (시나리오 3-4).</summary>
        public void ResetAbsorption()
        {
            if (!IsServer)
            {
                return;
            }

            foreach (var p in NetworkPlayer.All)
            {
                if (p != null && p.IsExorcist)
                {
                    p.IsAbsorbed.Value = false;
                }
            }
        }
    }
}
