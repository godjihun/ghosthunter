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
            HasTool.OnValueChanged += OnHasToolChanged;
            HeldTool.OnValueChanged += (_, _) => OnHeldToolChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            HasTool.OnValueChanged -= OnHasToolChanged;
        }

        private void OnHasToolChanged(bool had, bool has)
        {
            OnHeldToolChanged?.Invoke();

            // 획득음은 <b>주운 본인에게만</b>. 남이 도구를 줍는 소리까지 들리면
            // 그 위치가 그대로 드러난다.
            if (IsOwner && !had && has)
            {
                Audio.GameAudio.PlayCollectItem();
            }
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
            //
            // <b>춤이 재생되는 동안도 막는다.</b> 그때는 카메라가 3인칭으로 뒤로 빠져
            // 시야가 넓어지는데, 탐지가 화면 기준이 되면서 그게 그대로 이득이 됐다 —
            // 춤을 켜고 도구를 쓰는 게 최적 전략이 되어버린다.
            var emote = GetComponent<PlayerEmote>();
            if (emote != null && (emote.WheelOpen || emote.IsEmoting))
            {
                return;
            }

            // 쓰는 동작은 즉시 보여준다. 결과(탐지 성공 여부)는 여전히 서버가 정한다 —
            // 그건 이 게임의 비밀이라 절대 미리 판단하면 안 된다 (시나리오 3-2).
            // 여기서 앞당기는 건 "썼다"는 동작뿐이다.
            GetComponent<PlayerAnimation>()?.PlayPickup();

            // <b>시점은 클라이언트가 보내야 한다.</b> 상하 각도는 카메라 피벗에만 있고
            // 네트워크로 동기화되지 않아서 서버는 알 방법이 없다. 좌표를 보내던 것과
            // 같은 신뢰 수준이고, 어차피 귀신 위치를 모르니 각도를 속여도 이득이 없다.
            // <b>판정 자체는 여전히 서버가 한다.</b>
            var cam = GetComponentInChildren<Camera>(true);
            if (cam == null)
            {
                return;
            }

            UseToolRpc(cam.transform.position, cam.transform.rotation, cam.fieldOfView, cam.aspect);
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

            // <b>모션은 서버를 기다리지 않고 즉시 재생한다.</b>
            // Relay를 거치는 왕복(요청 → 판정 → 응답)이 끝나야 손을 뻗으면,
            // 웹에서는 그 지연이 그대로 "F를 눌렀는데 반응이 없다"로 느껴진다.
            //
            // 모션은 연출일 뿐이라 서버가 거절해도 손해가 없다. 실제 획득 여부는
            // 여전히 서버가 정하며(PickupRpc), 인벤토리 표시는 그 결과를 따른다.
            GetComponent<PlayerAnimation>()?.PlayPickup();

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
        private void UseToolRpc(Vector3 eyePosition, Quaternion eyeRotation,
            float verticalFov, float aspect, RpcParams rpcParams = default)
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

            bool detected = DetectionJudge.Judge(
                eyePosition, eyeRotation, verticalFov, aspect, toolType, Config);

            if (detected)
            {
                // 페널티: 영혼 강제 복귀 + 도구 무효화 + 본체 이동 허용 (시나리오 3-5).
                // <b>현실화 게이지는 건드리지 않는다</b> — 탐지의 대가는 게이지가 아니라
                // 강제 복귀와 위치 노출이다 (시나리오 3-4).
                var ghost = NetworkPlayer.GetGhost();
                ghost?.GetComponent<GhostController>()?.ServerApplyDetectionPenalty();

                // 탐지음은 귀신에게도 들린다. 이미 강제 복귀 페널티로 들킨 걸 아는
                // 상황이라 <b>새로 새는 정보가 없다</b> — 실패했을 때 보내면 그때는
                // "누군가 도구를 썼다"가 새므로, 성공한 경우에만 보낸다.
                if (ghost != null)
                {
                    GhostHeardDetectionRpc(RpcTarget.Single(ghost.OwnerClientId, RpcTargetUse.Temp));
                }
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
            // 성공·실패 소리를 여기서 낸다. 이 RPC 자체가 이미 본인에게만 가므로
            // 추가로 대상을 가릴 필요가 없다.
            Audio.GameAudio.PlayDetection(detected);
            OnDetectionResult?.Invoke(detected, tool);
        }

        /// <summary>탐지 성공 시 귀신에게만 가는 소리.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void GhostHeardDetectionRpc(RpcParams rpcParams = default)
        {
            Audio.GameAudio.PlayDetection(true);
        }

        /// <summary>줍는 동작 재생. 모든 화면에서 같이 보여야 자연스럽다.</summary>
        [Rpc(SendTo.Everyone)]
        private void PickupAnimationRpc()
        {
            // 소유자는 이미 로컬에서 재생했다 (RequestPickup 참고).
            // 여기서 또 틀면 손을 두 번 뻗는다.
            if (IsOwner)
            {
                return;
            }

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
