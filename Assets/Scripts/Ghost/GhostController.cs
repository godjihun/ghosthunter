using GhostHunter.Core;
using GhostHunter.Game;
using GhostHunter.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Ghost
{
    /// <summary>
    /// 귀신의 본체/영혼 분리 (시나리오 2, 3-5 / 기술 문서 5-5).
    ///
    /// 본체는 은신 종료 후 고정되고, 영혼만 돌아다닌다.
    /// 영혼은 <b>벽·바닥을 통과하지 못하지만 문은 통과한다</b> — 문을 못 뚫으면
    /// 저절로 열리는 문이 곧 위치 노출이 되기 때문이다.
    ///
    /// 투명 처리는 렌더러를 끄는 게 아니라 GameManager의 NetworkHide가 담당한다.
    /// 그래서 이 클래스는 가시성을 전혀 건드리지 않는다.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class GhostController : NetworkBehaviour
    {
        /// <summary>
        /// 본체가 남아 있는 월드 좌표.
        ///
        /// <b>자식 Transform으로 두면 안 된다</b> — 자식은 부모를 따라다니므로
        /// 영혼이 나가도 본체가 같이 끌려가 "분리"가 성립하지 않는다. 실제로 그 버그를 냈다.
        /// 탐지 판정(시나리오 3-5)이 이 좌표를 쓰므로 서버가 소유한다.
        /// </summary>
        public readonly NetworkVariable<Vector3> BodyWorldPosition = new(
            Vector3.zero,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        /// <summary>영혼이 본체 밖으로 나와 있는가.</summary>
        public readonly NetworkVariable<bool> IsSoulOut = new(
            false,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// 본체가 고정돼 있는가 (시나리오 4번 [3]).
        ///
        /// <b>반드시 NetworkVariable이어야 한다.</b> 이동은 소유자 클라이언트가 직접 하는데,
        /// 이 값을 서버 전용 필드로 두면 잠금 신호가 소유자에게 영영 도달하지 않아
        /// 조사 단계에도 본체가 걸어다닌다 — 실제로 그 버그를 냈다.
        /// 귀신 본인만 알면 되므로 ReadPermission은 Owner.
        /// </summary>
        public readonly NetworkVariable<bool> BodyLocked = new(
            false,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        /// <summary>도구 무효화 중인가. 이 동안 모든 탐지가 실패한다 (시나리오 3-5).</summary>
        public bool IsToolNullified => nullifyTimer > 0f;

        /// <summary>탐지 판정에 쓰이는 본체 위치. 영혼 위치가 아니다.</summary>
        public Vector3 BodyPosition =>
            IsSoulOut.Value ? BodyWorldPosition.Value : transform.position;

        private NetworkPlayer player;
        private PlayerMovement movement;
        private float nullifyTimer;

        /// <summary>복귀 순간이동이 소유자에게서 반영되기를 기다리는 중인가 (서버 전용).</summary>
        private bool awaitingReturn;
        private float returnTimeout;

        private GameConfig Config => GameManager.Config;

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            movement = GetComponent<PlayerMovement>();
        }

        public override void OnNetworkSpawn()
        {
            // 귀신이 아닌 플레이어에게는 이 컴포넌트가 할 일이 없다.
            if (!player.IsGhost)
            {
                enabled = false;
            }
        }

        public void OnToggleSoul(InputValue value)
        {
            if (!IsOwner || !value.isPressed)
            {
                return;
            }

            ToggleSoulRpc();
        }

        [Rpc(SendTo.Server)]
        private void ToggleSoulRpc()
        {
            if (GameManager.CurrentPhase != GamePhase.Investigation)
            {
                return;
            }

            // 페널티 중에는 영혼이 본체에 묶여 있다.
            if (IsToolNullified)
            {
                return;
            }

            if (!IsSoulOut.Value)
            {
                // 나가기: 지금 서 있는 자리에 본체를 남긴다.
                BodyWorldPosition.Value = transform.position;
                IsSoulOut.Value = true;
            }
            else
            {
                // 돌아가기: 본체 자리로 순간이동한다.
                IsSoulOut.Value = false;
                awaitingReturn = true;
                returnTimeout = 2f;
                ReturnToBodyRpc(BodyWorldPosition.Value);
            }

            // 레이어는 PlayerRoleSetup이 진영에 따라 한 번만 정한다 (귀신은 항상 Soul).
            // 여기서 또 만지면 주인이 둘이 되어 추적이 어려워진다.
        }

        /// <summary>
        /// 탐지당했을 때의 페널티 시퀀스 (시나리오 3-5). 서버가 호출한다.
        /// 영혼 강제 복귀 → 도구 무효화 타이머 → 그 동안 본체 이동 가능.
        /// </summary>
        public void ServerApplyDetectionPenalty()
        {
            if (!IsServer)
            {
                return;
            }

            // 강제 복귀도 소유자에게 시켜야 한다 (ReturnToBodyRpc 주석 참고).
            IsSoulOut.Value = false;
            awaitingReturn = true;
            returnTimeout = 2f;
            ReturnToBodyRpc(BodyWorldPosition.Value);

            nullifyTimer = Config != null ? Config.ToolNullifyDuration : 15f;

            // 무효화 동안에는 본체가 다른 은신처로 옮겨갈 수 있다.
            SetBodyLocked(false);
            DetectedRpc(RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        /// <summary>탐지당한 사실은 귀신 본인만 안다 (기술 문서 6-2).</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void DetectedRpc(RpcParams rpcParams = default)
        {
            // TODO: 영혼이 본체로 끌려가는 연출 재생 (기술 문서 6-2)
            Debug.Log("[Ghost] 탐지당했다. 영혼 강제 복귀 + 도구 무효화 시작.");
        }

        /// <summary>
        /// 본체 자리로 순간이동. <b>반드시 소유자 클라이언트에서 실행해야 한다.</b>
        ///
        /// 위치 동기화가 소유자 권한(ClientNetworkTransform)이라, 서버가 위치를 바꿔봐야
        /// 다음 프레임에 소유자가 보낸 좌표로 덮어써진다. 그러면 영혼은 그대로 있고
        /// <b>본체 좌표만 영혼 자리로 끌려와</b> "본체가 영혼에게 이동"하는 것처럼 보인다 —
        /// 실제로 그 버그를 냈다.
        /// </summary>
        [Rpc(SendTo.Owner)]
        private void ReturnToBodyRpc(Vector3 bodyPosition)
        {
            // CharacterController가 켜져 있으면 위치 대입이 무시되므로 잠깐 끈다.
            var cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
            }

            transform.position = bodyPosition;

            if (cc != null)
            {
                cc.enabled = true;
            }
        }

        /// <summary>은신 종료 후 본체를 고정한다. 페널티 중에만 풀린다. 서버 전용.</summary>
        public void SetBodyLocked(bool locked)
        {
            if (IsServer)
            {
                BodyLocked.Value = locked;
            }
        }

        private void ApplyMovementLock()
        {
            if (movement == null)
            {
                return;
            }

            // ReadPermission.Owner인 변수라 소유자·서버 외에는 읽을 수 없다.
            // 어차피 이동은 소유자만 계산하므로 그 외에는 할 일이 없다.
            if (!IsOwner && !IsServer)
            {
                return;
            }

            // 영혼이 나가 있으면 이동 가능. 본체 상태에서는 잠금 여부를 따른다.
            movement.MovementLocked = BodyLocked.Value && !IsSoulOut.Value;
        }

        private void Update()
        {
            if (IsServer && nullifyTimer > 0f)
            {
                nullifyTimer -= Time.deltaTime;
                if (nullifyTimer <= 0f)
                {
                    // 재은신 시간 종료 → 본체 다시 고정
                    SetBodyLocked(GameManager.CurrentPhase == GamePhase.Investigation);
                }
            }

            ApplyMovementLock();

            // 본체를 놓고 나온 게 아니라면 본체 좌표가 몸을 따라다닌다.
            if (!IsServer || IsSoulOut.Value)
            {
                return;
            }

            // 복귀 순간이동이 아직 서버에 반영되기 전이다. 여기서 좌표를 갱신하면
            // 본체가 영혼 자리로 끌려간다 — 소유자가 실제로 도착할 때까지 기다린다.
            if (awaitingReturn)
            {
                returnTimeout -= Time.deltaTime;
                bool arrived = Vector3.Distance(transform.position, BodyWorldPosition.Value) < 1f;
                if (arrived || returnTimeout <= 0f)
                {
                    awaitingReturn = false;
                }
                return;
            }

            BodyWorldPosition.Value = transform.position;
        }
    }
}
