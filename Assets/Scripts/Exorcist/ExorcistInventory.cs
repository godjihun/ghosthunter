using GhostHunter.Core;
using GhostHunter.Game;
using GhostHunter.Ghost;
using GhostHunter.Player;
using GhostHunter.Tools;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Exorcist
{
    /// <summary>
    /// 퇴마사의 도구 소지·사용 (시나리오 3-1, 3-2 / 기술 문서 5-1, 5-2).
    ///
    /// <b>최대 1개만 소지</b>한다. 모든 판정은 서버가 하고, 클라이언트는 요청만 보낸다.
    /// 탐지 결과는 RpcTarget.Single로 <b>사용한 본인에게만</b> 돌아간다.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class ExorcistInventory : NetworkBehaviour
    {
        /// <summary>손에 든 도구 종류. 눈에 보이는 정보이므로 공개해도 된다.</summary>
        public readonly NetworkVariable<ToolType> HeldTool = new(
            ToolType.Camera,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> HasTool = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>탐지 결과가 본인에게 도착했을 때. (성공 여부, 사용한 도구)</summary>
        public event System.Action<bool, ToolType> OnDetectionResult;

        /// <summary>도구를 들거나 놓았을 때. 반경 표시 갱신용.</summary>
        public event System.Action OnHeldToolChanged;

        private NetworkPlayer player;

        private GameConfig Config => GameManager.Config;

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
        }

        public override void OnNetworkSpawn()
        {
            HasTool.OnValueChanged += (_, _) => OnHeldToolChanged?.Invoke();
            HeldTool.OnValueChanged += (_, _) => OnHeldToolChanged?.Invoke();
        }

        // ── 입력 (소유자 클라이언트) ────────────────────────────────

        public void OnUseTool(InputValue value)
        {
            if (!IsOwner || !value.isPressed || !HasTool.Value)
            {
                return;
            }

            // 이모트 휠이 열려 있으면 좌클릭은 춤 선택이다.
            // 안 막으면 춤을 고르는 순간 들고 있던 도구까지 써버린다.
            var emote = GetComponent<PlayerEmote>();
            if (emote != null && emote.WheelOpen)
            {
                return;
            }

            UseToolRpc(transform.position);
        }

        public void OnDropTool(InputValue value)
        {
            if (!IsOwner || !value.isPressed || !HasTool.Value)
            {
                return;
            }

            DropToolRpc();
        }

        /// <summary>PlayerInteractor가 홀드를 완료했을 때 호출한다.</summary>
        public void RequestPickup(NetworkObject toolObject)
        {
            if (!IsOwner || HasTool.Value || toolObject == null)
            {
                return;
            }

            PickupRpc(new NetworkObjectReference(toolObject));
        }

        // ── 서버 판정 ──────────────────────────────────────────────

        [Rpc(SendTo.Server)]
        private void PickupRpc(NetworkObjectReference toolRef)
        {
            // 서버가 다시 검증한다. 클라이언트 말을 믿지 않는다.
            if (HasTool.Value || !player.IsAlive.Value)
            {
                return;
            }

            // 은신 단계에는 도구를 주울 수 없다 (시나리오 4번 [3]).
            if (!GameManager.ExorcistsCanAct)
            {
                return;
            }

            if (!toolRef.TryGet(out var netObj))
            {
                return;
            }

            var tool = netObj.GetComponent<WorldTool>();
            if (tool == null)
            {
                return;
            }

            HeldTool.Value = tool.Type.Value;
            HasTool.Value = true;
            tool.ServerConsume();

            // 줍는 동작은 전원에게 보인다 — 남이 뭘 줍는지는 숨길 정보가 아니다.
            PickupAnimationRpc();
        }

        [Rpc(SendTo.Server)]
        private void DropToolRpc()
        {
            if (!HasTool.Value)
            {
                return;
            }

            Vector3 dropPosition = transform.position + transform.forward * 1f + Vector3.up * 0.2f;
            ToolSpawner.Instance?.ServerDropTool(HeldTool.Value, dropPosition, Quaternion.identity);

            HasTool.Value = false;
        }

        [Rpc(SendTo.Server)]
        private void UseToolRpc(Vector3 usePosition, RpcParams rpcParams = default)
        {
            if (!HasTool.Value || !player.IsAlive.Value)
            {
                return;
            }

            if (GameManager.CurrentPhase != GamePhase.Investigation)
            {
                return;
            }

            var toolType = HeldTool.Value;

            // 사용하면 사라진다 (시나리오 3-1).
            HasTool.Value = false;

            bool detected = DetectionJudge.Judge(usePosition, toolType, Config);

            if (detected)
            {
                // 페널티: 영혼 강제 복귀 + 도구 무효화 + 영혼 수집 초기화 (시나리오 3-4, 3-5)
                var ghost = NetworkPlayer.GetGhost();
                ghost?.GetComponent<GhostController>()?.ServerApplyDetectionPenalty();
                GameManager.Instance?.ResetAbsorption();
            }

            // ⚠️ 성공이든 실패든 반드시 응답을 보낸다.
            // 실패 시 응답을 생략하면 "통신이 없었다" 자체가 단서가 되어
            // 시나리오 3-2의 모호성이 깨진다 (기술 문서 2-4).
            DetectionResultRpc(detected, toolType,
                RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        /// <summary>탐지 결과. 사용한 본인에게만 전송된다.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void DetectionResultRpc(bool detected, ToolType tool, RpcParams rpcParams = default)
        {
            OnDetectionResult?.Invoke(detected, tool);
        }

        /// <summary>줍는 동작 재생. 모든 화면에서 같이 보여야 자연스럽다.</summary>
        [Rpc(SendTo.Everyone)]
        private void PickupAnimationRpc()
        {
            GetComponent<PlayerAnimation>()?.PlayPickup();
        }

        /// <summary>제단 헌납처럼 서버가 직접 도구를 소모시킬 때 쓴다.</summary>
        public void ServerConsumeHeldTool()
        {
            if (!IsServer)
            {
                return;
            }

            HasTool.Value = false;
        }
    }
}
