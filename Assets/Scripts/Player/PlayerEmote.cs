using GhostHunter.Core;
using GhostHunter.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Player
{
    /// <summary>
    /// Tab을 누른 채 마우스로 방향을 잡고 <b>좌클릭</b>하면 이모트가 재생된다 (3인칭 전환).
    /// Tab을 그냥 놓으면 아무것도 고르지 않고 닫힌다.
    ///
    /// <b>선택된 이모트는 서버를 거쳐 모두에게 퍼진다.</b> 춤은 남이 봐야 의미가 있는데,
    /// 로컬에서만 재생하면 정작 본인 눈에만 보인다.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerEmote : NetworkBehaviour
    {
        /// <summary>
        /// 이모트 목록. <b>인덱스가 곧 네트워크로 오가는 값이라 순서를 바꾸면 안 된다.</b>
        /// 새 이모트는 뒤에 붙일 것 — 중간에 끼우면 이전 인덱스가 다른 동작을 가리킨다.
        /// 휠 UI는 이 개수에 맞춰 각도를 알아서 나눈다.
        /// </summary>
        public static readonly string[] EmoteNames = { "막춤", "트월크", "앉기" };

        /// <summary>재생 중인 이모트. -1이면 없음. 남에게도 보여야 하므로 전원 공개.</summary>
        public readonly NetworkVariable<int> CurrentEmote = new(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>휠이 열려 있는가. 소유자 화면에서만 쓰는 로컬 상태다.</summary>
        public bool WheelOpen { get; private set; }

        /// <summary>휠에서 지금 가리키는 항목. 선택 안 했으면 -1.</summary>
        public int HighlightedIndex { get; private set; } = -1;

        /// <summary>이모트 중인가. 3인칭 전환과 이동 잠금 판단에 쓴다.</summary>
        public bool IsEmoting => CurrentEmote.Value >= 0;

        private NetworkPlayer player;
        private PlayerMovement movement;
        private PlayerInput playerInput;
        private InputActionMap cachedMap;
        private InputAction wheelAction;
        private Vector2 wheelDelta;

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            movement = GetComponent<PlayerMovement>();
            playerInput = GetComponent<PlayerInput>();
        }

        public override void OnNetworkSpawn()
        {
            enabled = IsOwner;
        }

        /// <summary>
        /// Tab 상태를 <b>매 프레임 직접 읽는다.</b>
        ///
        /// Send Messages 방식은 <b>누를 때만 통지가 오고 뗄 때는 오지 않는다.</b>
        /// 그래서 이벤트로 여닫으면 한 번 열린 휠이 영영 닫히지 않는다 — 실제로 그 버그를 냈다.
        /// 홀드형 입력은 이벤트가 아니라 현재 상태를 물어야 한다.
        /// </summary>
        private bool WheelKeyHeld()
        {
            if (playerInput == null)
            {
                return false;
            }

            var map = playerInput.currentActionMap;
            if (map != cachedMap)
            {
                cachedMap = map;
                wheelAction = map?.FindAction("EmoteWheel");
            }

            return wheelAction != null && wheelAction.IsPressed();
        }

        /// <summary>
        /// 지금 이모트를 쓸 수 있는 상황인가.
        ///
        /// <b>귀신은 현실화(사냥 단계) 이후에만</b> 쓸 수 있다. 은신·조사 단계의 귀신은
        /// 남의 화면에 아예 그려지지 않으므로 춤춰봐야 아무도 못 본다.
        /// </summary>
        private bool CanUseEmotes()
        {
            if (player == null)
            {
                return false;
            }

            if (player.IsGhost)
            {
                return GameManager.CurrentPhase == GamePhase.Hunt;
            }

            // 대기방에서도 쓸 수 있다. 진영이 아직 없어 전원이 퇴마사 모습이고,
            // 서로 보이는 상태라 춤이 실제로 남에게 보인다.
            return GameManager.IsFirstPersonActive;
        }

        private void Update()
        {
            if (!IsOwner)
            {
                return;
            }

            bool held = WheelKeyHeld() && CanUseEmotes();

            if (held && !WheelOpen)
            {
                WheelOpen = true;
                HighlightedIndex = -1;
                wheelDelta = Vector2.zero;
            }
            else if (!held && WheelOpen)
            {
                // Tab을 그냥 놓으면 취소. 재생은 좌클릭으로만 한다.
                WheelOpen = false;
                HighlightedIndex = -1;
            }

            if (WheelOpen)
            {
                UpdateWheelSelection();

                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame && HighlightedIndex >= 0)
                {
                    PlayEmoteRpc(HighlightedIndex);
                    WheelOpen = false;
                    HighlightedIndex = -1;
                }
                return;
            }

            // 움직이거나 쓸 수 없는 단계가 되면 이모트를 끊는다.
            // 이모트 중에는 3인칭이라 그대로 두면 시점이 계속 뒤에 머문다.
            if (IsEmoting && (!CanUseEmotes() || (movement != null && movement.HasMoveInput)))
            {
                StopEmoteRpc();
            }
        }

        /// <summary>
        /// 마우스를 움직인 방향으로 항목을 고른다.
        ///
        /// 커서 위치가 아니라 <b>누적 이동량</b>으로 판단한다. 1인칭이라 커서가 잠겨 있어
        /// 화면상 위치라는 게 없기 때문이다. 커서 잠금은 PlayerRoleSetup이 관리하므로
        /// <b>여기서 만지지 않는다.</b>
        /// </summary>
        private void UpdateWheelSelection()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                wheelDelta += mouse.delta.ReadValue();
            }

            const float deadZone = 35f;
            if (wheelDelta.magnitude < deadZone)
            {
                HighlightedIndex = -1;
                return;
            }

            // 너무 멀리 밀어도 방향만 유지되도록 길이를 제한한다.
            wheelDelta = Vector2.ClampMagnitude(wheelDelta, 300f);

            // 위쪽을 0번으로 두고 시계 방향으로 센다.
            float angle = Mathf.Atan2(wheelDelta.x, wheelDelta.y) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;

            int count = EmoteNames.Length;
            float step = 360f / count;
            HighlightedIndex = Mathf.FloorToInt((angle + step * 0.5f) % 360f / step);
        }

        [Rpc(SendTo.Server)]
        private void PlayEmoteRpc(int index)
        {
            if (index < 0 || index >= EmoteNames.Length)
            {
                return;
            }

            CurrentEmote.Value = index;
        }

        [Rpc(SendTo.Server)]
        private void StopEmoteRpc()
        {
            CurrentEmote.Value = -1;
        }

        /// <summary>휠 각도(도). UI가 항목을 배치할 때 쓴다.</summary>
        public static float AngleOf(int index) => 360f / EmoteNames.Length * index;
    }
}
