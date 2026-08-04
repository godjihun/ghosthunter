using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Player
{
    /// <summary>
    /// 사망한 퇴마사의 관전 (시나리오 3-6 / 기능 명세서 SP-01~04).
    ///
    /// <b>사망했을 때만</b> 켜진다. 흡수는 죽음이 아니므로 관전으로 넘어가지 않는다.
    ///
    /// <b>관전 대상은 생존한 퇴마사뿐이다.</b> 귀신은 절대 대상이 될 수 없다 —
    /// 관전으로 귀신 위치가 새면 이 게임의 비밀이 통째로 무너진다.
    /// <see cref="NetworkPlayer.GetLivingExorcists"/>가 퇴마사만 돌려주므로
    /// 목록을 만드는 지점에서 이미 막혀 있다.
    ///
    /// 전부 <b>소유자 화면에서만</b> 도는 로컬 처리다. 카메라를 옮길 뿐 네트워크 상태는 건드리지 않는다.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerSpectator : NetworkBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private PlayerInput playerInput;

        /// <summary>지금 관전 중인가. 카메라를 누가 잡을지 판단하는 데 쓴다.</summary>
        public bool IsSpectating { get; private set; }

        /// <summary>관전 중인 대상. 없으면 null.</summary>
        public NetworkPlayer Target { get; private set; }

        private NetworkPlayer player;

        /// <summary>마지막으로 적용한 액션맵 전환 상태.</summary>
        private bool mapSwitched;

        private static GameConfig Config => GameManager.Config;

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>(true);
            }
            if (playerInput == null)
            {
                playerInput = GetComponent<PlayerInput>();
            }
        }

        public override void OnNetworkSpawn()
        {
            enabled = IsOwner;
        }

        /// <summary>
        /// 관전 상태여야 하는가.
        ///
        /// 귀신은 죽지 않으므로(승패로만 종료) 자동으로 제외된다.
        /// </summary>
        private bool ShouldSpectate()
        {
            return IsOwner
                && player != null
                && player.IsExorcist
                && !player.IsAlive.Value
                && GameManager.IsGameplayActive;
        }

        private void Update()
        {
            if (!IsOwner)
            {
                return;
            }

            bool want = ShouldSpectate();
            if (want != IsSpectating)
            {
                IsSpectating = want;
                if (want)
                {
                    PickFirstTarget();
                }
                else
                {
                    Target = null;
                    RestoreCamera();
                }
                ApplyActionMap(want);
            }

            if (!IsSpectating)
            {
                return;
            }

            // 보던 사람이 죽으면 자동으로 다음 사람에게 넘어간다.
            // 시체를 계속 보고 있게 두면 관전이 멈춘 것처럼 느껴진다.
            if (Target == null || !Target.IsSpectatable)
            {
                PickFirstTarget();
            }
        }

        /// <summary>
        /// 관전을 끝낼 때 카메라를 1인칭 자리로 되돌린다.
        ///
        /// <b>이걸 빼먹으면 다음 판이 통째로 망가진다.</b> 관전 중에는 카메라의 월드 위치·회전을
        /// 직접 잡는데, 되돌리지 않으면 그 값이 <c>CameraPivot</c> 기준 로컬 변환으로 남는다.
        /// 그러면 카메라가 몸통에 대해 비뚤어진 채로 고정되어, <b>D를 눌렀는데 앞으로 가는 것처럼
        /// 보이고 마우스를 돌리면 화면이 꼬인다.</b> 실제로 그 버그를 냈다.
        /// </summary>
        private void RestoreCamera()
        {
            if (playerCamera == null)
            {
                return;
            }

            playerCamera.transform.localPosition = Vector3.zero;
            playerCamera.transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// 사망하면 <c>Spectator</c> 맵으로 갈아탄다.
        ///
        /// 퇴마사 맵을 그대로 두면 죽은 사람이 도구를 줍거나 쓰려는 입력이 계속 서버로 간다.
        /// 서버가 어차피 막지만, 입력 단계에서 끊는 편이 원인 추적이 쉽다.
        ///
        /// <b>돌아올 때 "Exorcist"로 고정하면 안 된다.</b> 대기방을 거쳐 다음 판에
        /// 귀신이 되면 귀신이 퇴마사 조작을 갖게 된다 — 진영을 보고 정한다.
        /// </summary>
        private void ApplyActionMap(bool spectating)
        {
            if (playerInput == null || mapSwitched == spectating)
            {
                return;
            }

            mapSwitched = spectating;
            playerInput.SwitchCurrentActionMap(
                spectating ? "Spectator" : (player != null && player.IsGhost ? "Ghost" : "Exorcist"));
        }

        // ── 입력 (Send Messages) ───────────────────────────────────

        public void OnSpectatePrev(InputValue value)
        {
            if (value.isPressed) Cycle(-1);
        }

        public void OnSpectateNext(InputValue value)
        {
            if (value.isPressed) Cycle(1);
        }

        /// <summary>관전 대상 목록. 항상 생존한 퇴마사만 들어간다.</summary>
        private List<NetworkPlayer> Candidates()
        {
            var list = NetworkPlayer.GetLivingExorcists();
            list.Remove(player); // 내 시체는 대상이 아니다
            return list;
        }

        private void PickFirstTarget()
        {
            var list = Candidates();
            Target = list.Count > 0 ? list[0] : null;
        }

        private void Cycle(int direction)
        {
            if (!IsSpectating)
            {
                return;
            }

            var list = Candidates();
            if (list.Count == 0)
            {
                Target = null;
                return;
            }

            int index = Target != null ? list.IndexOf(Target) : -1;
            index = index < 0 ? 0 : (index + direction + list.Count) % list.Count;
            Target = list[index];
        }

        /// <summary>
        /// 대상의 뒤쪽 3인칭으로 카메라를 옮긴다.
        ///
        /// <b>1인칭을 그대로 물려받지 않는다.</b> 남의 시점 흔들림을 계속 보면 멀미가 난다
        /// (시나리오 3-6). 벽에 파묻히지 않도록 뒤쪽을 훑어 거리를 줄인다.
        /// </summary>
        private void LateUpdate()
        {
            if (!IsSpectating || Target == null || playerCamera == null)
            {
                return;
            }

            var config = Config;
            float distance = config != null ? config.SpectatorCameraDistance : 2.5f;
            float height = config != null ? config.SpectatorCameraHeight : 1.5f;
            float minDistance = config != null ? config.SpectatorCameraMinDistance : 0.5f;

            Vector3 pivot = Target.transform.position + Vector3.up * height;
            Vector3 back = -Target.transform.forward;

            int mask = LayerMask.GetMask("Wall", "Door", "Floor");
            if (Physics.SphereCast(pivot, 0.25f, back, out var hit, distance, mask,
                    QueryTriggerInteraction.Ignore))
            {
                distance = Mathf.Max(minDistance, hit.distance - 0.15f);
            }

            var camT = playerCamera.transform;
            camT.position = pivot + back * distance;
            camT.rotation = Quaternion.LookRotation(pivot - camT.position, Vector3.up);
        }
    }
}
