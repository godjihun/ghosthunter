using System.Collections.Generic;
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
    /// <b>도구는 소모품이 아니다.</b> 저택에 종류당 하나씩(총 6개)만 있고, 쓰면 사라지는 대신
    /// 쿨타임이 돈다.
    ///
    /// <b>칸은 고정 4개다.</b> 목록을 앞에서부터 채우는 방식이 아니라, 1~4번 키로 고른
    /// <b>그 칸에</b> 넣고 빼고 쓴다 — 그래야 "3번 칸의 십자가"처럼 위치가 손에 익는다.
    /// 압축 목록이면 하나 버릴 때마다 뒤 칸이 앞으로 당겨져 번호가 계속 바뀐다.
    ///
    /// <b>쿨타임은 들고 있던 도구 전부에 함께 걸린다.</b> 한 사람이 4개를 쥐고 번갈아 쓰면
    /// 혼자서 저택의 탐지 수단을 독점하게 되므로, 하나를 쓰면 손에 있는 나머지도 같이 묶인다.
    /// 나눠 들수록 팀 전체의 탐지 빈도가 올라가는 구조다.
    ///
    /// 모든 판정은 서버가 하고, 클라이언트는 요청만 보낸다.
    /// 탐지 결과는 RpcTarget.Single로 <b>사용한 본인에게만</b> 돌아간다.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class ExorcistInventory : NetworkBehaviour
    {
        /// <summary>빈 칸 표시. <see cref="ToolType"/>에 없는 값이어야 한다.</summary>
        private const int Empty = -1;

        private const int DefaultCapacity = 4;

        /// <summary>
        /// 칸별 도구 종류. 길이는 항상 칸 수와 같고, <see cref="Empty"/>면 빈 칸이다.
        ///
        /// <b><see cref="cooldowns"/>와 자리가 맞물려 있다</b> — 같은 인덱스가 그 도구의
        /// 남은 쿨타임이다. 한쪽만 고치면 조용히 어긋난다.
        /// </summary>
        public readonly NetworkList<int> Slots = new(
            new List<int> { Empty, Empty, Empty, Empty },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkList<float> cooldowns = new(
            new List<float> { 0f, 0f, 0f, 0f },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>지금 고른 칸. 비어 있어도 고를 수 있다 — 그 상태가 맨손이다.</summary>
        public readonly NetworkVariable<int> SelectedSlot = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>탐지 결과가 본인에게 도착했을 때. (성공 여부, 사용한 도구)</summary>
        public event System.Action<bool, ToolType> OnDetectionResult;

        /// <summary>소지 상태가 바뀌었을 때. HUD 갱신용.</summary>
        public event System.Action OnHeldToolChanged;

        private NetworkPlayer player;

        private GameConfig Config => GameManager.Config;

        public int Capacity
        {
            get
            {
                var cfg = Config;
                return cfg != null ? Mathf.Clamp(cfg.MaxCarriedTools, 1, 4) : DefaultCapacity;
            }
        }

        // ── 바깥에서 읽는 상태 ─────────────────────────────────────

        public bool HasToolAt(int slot) => slot >= 0 && slot < Slots.Count && Slots[slot] != Empty;

        public ToolType TypeAt(int slot) => (ToolType)Slots[slot];

        public float CooldownAt(int slot) => slot < cooldowns.Count ? cooldowns[slot] : 0f;

        /// <summary>들고 있는 도구 개수. 빈 칸은 세지 않는다.</summary>
        public int Count
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Slots.Count; i++)
                {
                    if (Slots[i] != Empty) n++;
                }
                return n;
            }
        }

        public bool HasTool => Count > 0;

        /// <summary>어느 칸이든 비어 있는가. 줍기 가능 여부가 아니라 "자리가 있는가"다.</summary>
        public bool HasSpace
        {
            get
            {
                for (int i = 0; i < Slots.Count; i++)
                {
                    if (Slots[i] == Empty) return true;
                }
                return false;
            }
        }

        /// <summary>지금 고른 칸이 비어 있는가. 줍기·회수는 이 상태에서만 된다.</summary>
        public bool SelectedIsEmpty => !HasToolAt(SelectedSlot.Value);

        /// <summary>지금 고른 칸의 도구. 빈 칸이면 false.</summary>
        public bool TryGetSelected(out ToolType type, out float cooldown)
        {
            int i = SelectedSlot.Value;
            if (!HasToolAt(i))
            {
                type = default;
                cooldown = 0f;
                return false;
            }

            type = (ToolType)Slots[i];
            cooldown = CooldownAt(i);
            return true;
        }

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
        }

        public override void OnNetworkSpawn()
        {
            Slots.OnListChanged += _ => OnHeldToolChanged?.Invoke();
            SelectedSlot.OnValueChanged += (_, _) => OnHeldToolChanged?.Invoke();

            if (IsServer)
            {
                ServerResizeToCapacity();
            }
        }

        /// <summary>설정의 칸 수에 맞춰 목록 길이를 고정한다. 서버 전용.</summary>
        private void ServerResizeToCapacity()
        {
            int target = Capacity;

            while (Slots.Count < target) { Slots.Add(Empty); cooldowns.Add(0f); }
            while (Slots.Count > target)
            {
                Slots.RemoveAt(Slots.Count - 1);
                cooldowns.RemoveAt(cooldowns.Count - 1);
            }
        }

        private void Update()
        {
            if (IsServer)
            {
                TickCooldowns();
            }

            if (IsOwner)
            {
                ReadSlotKeys();
            }
        }

        /// <summary>서버만 깎는다. 클라이언트가 각자 깎으면 서로 어긋난다.</summary>
        private void TickCooldowns()
        {
            for (int i = 0; i < cooldowns.Count; i++)
            {
                if (Slots[i] != Empty && cooldowns[i] > 0f)
                {
                    cooldowns[i] = Mathf.Max(0f, cooldowns[i] - Time.deltaTime);
                }
            }
        }

        /// <summary>
        /// 1~4번 키로 칸을 고른다. <b>빈 칸도 고를 수 있다.</b>
        ///
        /// <b>액션맵이 아니라 키보드를 직접 읽는다.</b> <c>.inputactions</c>를 고치면
        /// 가상 플레이어(MPPM)를 재시작해야 반영되고, JSON을 손으로 편집하는 것도 위험하다.
        /// 진영 검사는 여기서 따로 하므로 귀신에게 새어 들어갈 일은 없다.
        /// </summary>
        private void ReadSlotKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || player == null || !player.IsExorcist)
            {
                return;
            }

            var keys = new[] { keyboard.digit1Key, keyboard.digit2Key, keyboard.digit3Key, keyboard.digit4Key };
            int limit = Mathf.Min(keys.Length, Slots.Count);

            for (int i = 0; i < limit; i++)
            {
                if (keys[i] != null && keys[i].wasPressedThisFrame)
                {
                    SelectSlotRpc(i);
                    return;
                }
            }
        }

        // ── 입력 (소유자 클라이언트) ────────────────────────────────

        public void OnUseTool(InputValue value)
        {
            // 빈 칸을 고르고 있으면 손에 든 게 없는 것이다.
            if (!IsOwner || !value.isPressed || !TryGetSelected(out var selected, out _))
            {
                return;
            }

            // <b>탐지는 조사 단계 전용이다.</b> 서버도 같은 검사를 하지만(UseToolRpc),
            // 여기서 걸러야 은신 중에 헛되이 RPC를 쏘지 않는다.
            if (GameManager.CurrentPhase != GamePhase.Investigation)
            {
                return;
            }

            // 이미 밝혀낸 약점은 다시 쓸 수 없다. 여기서 막지 않으면 눌리기는 해서
            // <b>손에 든 도구 전부가 쿨타임에 들어간다</b> — 아무 소득 없이 손해만 본다.
            if (GameManager.Instance != null && GameManager.Instance.IsWeaknessFound(selected))
            {
                return;
            }

            // 이모트 휠이 열려 있으면 좌클릭은 춤 선택이다.
            // 안 막으면 춤을 고르는 순간 들고 있던 도구까지 써버린다.
            //
            // <b>춤이 재생되는 동안도 막는다.</b> 그때는 카메라가 3인칭으로 뒤로 빠져
            // 시야가 넓어지는데, 탐지가 화면 기준이 되면서 그게 그대로 이득이 됐다.
            var emote = GetComponent<PlayerEmote>();
            if (emote != null && (emote.WheelOpen || emote.IsEmoting))
            {
                return;
            }

            // <b>시점은 클라이언트가 보내야 한다.</b> 상하 각도는 카메라 피벗에만 있고
            // 네트워크로 동기화되지 않아서 서버는 알 방법이 없다.
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
            if (!IsOwner || !value.isPressed || !TryGetSelected(out _, out _))
            {
                return;
            }

            DropToolRpc();
        }

        /// <summary>PlayerInteractor가 F 입력을 넘겨준다.</summary>
        public void RequestPickup(NetworkObject toolObject)
        {
            if (!IsOwner || !SelectedIsEmpty || toolObject == null)
            {
                return;
            }

            // <b>모션은 서버를 기다리지 않고 즉시 재생한다.</b>
            // Relay 왕복이 끝나야 손을 뻗으면 "F를 눌렀는데 반응이 없다"로 느껴진다.
            GetComponent<PlayerAnimation>()?.PlayPickup();

            PickupRpc(new NetworkObjectReference(toolObject));
        }

        // ── 서버 판정 ──────────────────────────────────────────────

        [Rpc(SendTo.Server)]
        private void SelectSlotRpc(int slot)
        {
            if (slot >= 0 && slot < Slots.Count)
            {
                SelectedSlot.Value = slot;
            }
        }

        [Rpc(SendTo.Server)]
        private void PickupRpc(NetworkObjectReference toolRef)
        {
            // 서버가 다시 검증한다. 클라이언트 말을 믿지 않는다.
            if (!SelectedIsEmpty || !player.IsAlive.Value)
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

            // 쿨타임을 그대로 물려받는다. 이게 "버렸다 주워도 유지"를 만든다.
            ServerPutInSelected(tool.Type.Value, tool.Cooldown.Value);
            tool.ServerConsume();

            // 줍는 동작은 전원에게 보인다 — 남이 뭘 줍는지는 숨길 정보가 아니다.
            PickupAnimationRpc();
        }

        [Rpc(SendTo.Server)]
        private void DropToolRpc()
        {
            if (!ServerTakeSelected(out var type, out float cooldown))
            {
                return;
            }

            Vector3 dropPosition = transform.position + transform.forward * 1f + Vector3.up * 0.2f;
            var dropped = ToolSpawner.Instance?.ServerDropTool(type, dropPosition, Quaternion.identity);
            if (dropped != null)
            {
                dropped.Cooldown.Value = cooldown;
            }
        }

        [Rpc(SendTo.Server)]
        private void UseToolRpc(Vector3 eyePosition, Quaternion eyeRotation,
            float verticalFov, float aspect, RpcParams rpcParams = default)
        {
            if (!player.IsAlive.Value || !TryGetSelected(out var toolType, out float cooldown))
            {
                return;
            }

            if (GameManager.CurrentPhase != GamePhase.Investigation || cooldown > 0f)
            {
                return;
            }

            // 이미 밝혀낸 약점은 아예 쓰이지 않는다 — 쿨타임도 걸지 않는다.
            // 걸어버리면 "쓸모없는 도구로 팀의 쿨타임을 태우는" 짓이 가능해진다.
            var manager = GameManager.Instance;
            if (manager != null && manager.IsWeaknessFound(toolType))
            {
                return;
            }

            // <b>도구는 사라지지 않는다.</b> 대신 손에 있는 것 전부가 쿨타임에 들어간다.
            ServerStartCooldownOnAll();

            bool detected = DetectionJudge.Judge(
                eyePosition, eyeRotation, verticalFov, aspect, toolType, Config);

            if (detected)
            {
                // 탐지음은 귀신에게도 들린다. 어차피 곧 은신 단계로 끌려가 들킨 걸 알게 된다.
                var ghost = NetworkPlayer.GetGhost();
                if (ghost != null)
                {
                    GhostHeardDetectionRpc(RpcTarget.Single(ghost.OwnerClientId, RpcTargetUse.Temp));
                }

                // <b>결과 통지를 먼저 보내고 단계를 바꾼다.</b> 순서를 뒤집으면 단계 전환
                // 안내와 탐지 결과가 같은 프레임에 겹쳐, 어느 도구로 찾았는지 묻히기 쉽다.
                DetectionResultRpc(true, toolType,
                    RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));

                // 약점 기록 → 3개째면 승리, 아니면 귀신이 다시 숨는다.
                GameManager.Instance?.ServerOnWeaknessFound(toolType);
                return;
            }

            // ⚠️ 실패해도 반드시 응답을 보낸다.
            // 응답을 생략하면 "통신이 없었다" 자체가 단서가 되어
            // 시나리오 3-2의 모호성이 깨진다 (기술 문서 2-4).
            DetectionResultRpc(false, toolType,
                RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        /// <summary>탐지 결과. 사용한 본인에게만 전송된다.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void DetectionResultRpc(bool detected, ToolType tool, RpcParams rpcParams = default)
        {
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
            if (IsOwner)
            {
                return;
            }

            GetComponent<PlayerAnimation>()?.PlayPickup();
        }

        // ── 서버 전용 조작 ─────────────────────────────────────────

        /// <summary>고른 칸에 도구를 넣는다. 제단에서 회수할 때도 이 경로를 쓴다.</summary>
        public bool ServerPutInSelected(ToolType type, float cooldown)
        {
            if (!IsServer || !SelectedIsEmpty)
            {
                return false;
            }

            int slot = SelectedSlot.Value;
            Slots[slot] = (int)type;
            cooldowns[slot] = Mathf.Max(0f, cooldown);

            OnHeldToolChanged?.Invoke();
            return true;
        }

        /// <summary>고른 칸을 비운다. 제단에 넣거나 바닥에 버릴 때 쓴다.</summary>
        public bool ServerTakeSelected(out ToolType type, out float cooldown)
        {
            type = default;
            cooldown = 0f;

            if (!IsServer || !TryGetSelected(out type, out cooldown))
            {
                return false;
            }

            int slot = SelectedSlot.Value;
            Slots[slot] = Empty;
            cooldowns[slot] = 0f;

            OnHeldToolChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 들고 있는 <b>모든</b> 도구에 쿨타임을 건다.
        ///
        /// 하나만 걸면 4개를 쥔 사람이 번갈아 쓰며 탐지를 독점한다.
        /// 나눠 들도록 유도하는 것이 이 규칙의 목적이다.
        /// </summary>
        private void ServerStartCooldownOnAll()
        {
            var cfg = Config;
            float duration = cfg != null ? cfg.ToolCooldown : 20f;

            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i] != Empty)
                {
                    cooldowns[i] = duration;
                }
            }
        }

        /// <summary>판이 끝날 때 서버가 손을 비운다.</summary>
        public void ServerClearAll()
        {
            if (!IsServer)
            {
                return;
            }

            for (int i = 0; i < Slots.Count; i++)
            {
                Slots[i] = Empty;
                cooldowns[i] = 0f;
            }

            SelectedSlot.Value = 0;
            OnHeldToolChanged?.Invoke();
        }
    }
}
