using GhostHunter.Core;
using GhostHunter.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Player
{
    /// <summary>
    /// CharacterController 기반 1인칭 이동 (기술 문서 4번).
    ///
    /// 소유자 클라이언트에서만 입력을 받아 움직이고, 위치 동기화는 NetworkTransform이 담당한다.
    /// 이동속도는 진영과 게임 단계에 따라 달라진다 — 사냥 단계의 귀신만 2배다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerMovement : NetworkBehaviour
    {
        [Tooltip("비워두면 GameManager의 설정을 쓴다.")]
        [SerializeField] private GameConfig config;

        [SerializeField] private float gravity = -9.81f;

        [Tooltip("점프 도달 높이(m). 중력과 함께 초기 속도를 계산한다.")]
        [SerializeField] private float jumpHeight = 1.1f;

        private CharacterController controller;
        private NetworkPlayer player;
        private PlayerEmote emote;
        private PlayerAnimation animation;
        private PlayerInput playerInput;
        private InputActionMap cachedMap;
        private InputAction sprintAction;
        private Vector2 moveInput;
        private float verticalVelocity;
        private bool jumpQueued;

        /// <summary>은신 단계 종료 후 귀신 본체는 이동할 수 없다 (시나리오 4번 [3]).</summary>
        public bool MovementLocked { get; set; }

        /// <summary>지금 이동 키가 눌려 있는가. 이모트를 끊을지 판단하는 데 쓴다.</summary>
        public bool HasMoveInput => moveInput.sqrMagnitude > 0.01f;

        /// <summary>
        /// 뛰어올라 다시 땅에 닿기까지 걸리는 시간(초).
        ///
        /// 점프 애니메이션의 재생 배속을 여기에 맞춘다. 값을 따로 적어두면
        /// <b>점프 높이를 바꿀 때 애니메이션만 어긋난 채 남는다</b> — 높이에서 계산한다.
        /// 등속 중력에서 왕복 시간은 2v/g, 초기 속도 v = sqrt(2gh)이므로 2*sqrt(2h/g)다.
        /// </summary>
        public float AirTime
        {
            get
            {
                float g = Mathf.Abs(gravity);
                if (g < 0.01f || jumpHeight <= 0f)
                {
                    return 0.6f;
                }
                return 2f * Mathf.Sqrt(2f * jumpHeight / g);
            }
        }

        private GameConfig Config => config != null ? config : GameManager.Config;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            player = GetComponent<NetworkPlayer>();
            emote = GetComponent<PlayerEmote>();
            animation = GetComponent<PlayerAnimation>();
            playerInput = GetComponent<PlayerInput>();
        }

        public override void OnNetworkSpawn()
        {
            // 소유자가 아니면 입력을 받을 이유가 없다. 위치는 NetworkTransform이 받아온다.
            enabled = IsOwner;
        }

        // PlayerInput의 Send Messages 방식. 메서드 이름은 "On" + 액션 이름 규칙 고정.
        public void OnMove(InputValue value)
        {
            moveInput = value.Get<Vector2>();
        }

        public void OnJump(InputValue value)
        {
            if (value.isPressed)
            {
                jumpQueued = true;
            }
        }

        /// <summary>
        /// Shift 상태를 <b>매 프레임 직접 읽는다.</b>
        ///
        /// PlayerInput의 Send Messages(<c>OnSprint</c>) 방식은 누름/뗌 통지가
        /// 상황에 따라 누락돼 계속 달리거나 아예 안 달리는 일이 생긴다.
        /// 홀드형 입력은 이벤트보다 현재 상태를 묻는 쪽이 확실하다.
        /// </summary>
        private bool SprintHeld()
        {
            if (playerInput == null)
            {
                return false;
            }

            var map = playerInput.currentActionMap;
            if (map != cachedMap)
            {
                // 진영이 바뀌면 액션맵이 통째로 갈리므로 캐시를 다시 잡는다.
                cachedMap = map;
                sprintAction = map?.FindAction("Sprint");
            }

            return sprintAction != null && sprintAction.IsPressed();
        }

        private void Update()
        {
            if (!IsOwner || controller == null || !controller.enabled)
            {
                return;
            }

            // 결과 화면에서는 중력만 적용하고 조작은 받지 않는다.
            // 대기방은 걸어다니는 공간이므로 여기 포함된다.
            bool canControl = GameManager.IsFirstPersonActive && !MovementLocked;

            // 죽은 사람은 움직이지 않는다. 관전 시스템(시나리오 3-6)이 붙기 전까지는
            // 시신이 걸어다니지 않게 하는 것만으로 충분하다.
            if (player != null && !player.IsAlive.Value)
            {
                canControl = false;
            }

            // 은신 단계의 퇴마사는 대기지점에 묶인다 (시나리오 4번 [3]).
            if (player != null && player.IsExorcist && !GameManager.ExorcistsCanAct)
            {
                canControl = false;
            }

            // 이모트 휠을 연 동안에는 마우스가 항목 선택에 쓰이므로 이동도 멈춘다.
            if (emote != null && emote.WheelOpen)
            {
                canControl = false;
            }

            // 게임 설정 단말 같은 창이 떠 있으면 마우스가 UI로 넘어간다. 뒤에서 계속
            // 걸어다니면 창을 조작하는 동안 벽에 처박힌다.
            if (PlayerRoleSetup.UiPanelOpen)
            {
                canControl = false;
            }

            if (controller.isGrounded && verticalVelocity < 0f)
            {
                // 살짝 눌러줘야 경사에서 붕 뜨지 않는다.
                verticalVelocity = -2f;
            }

            if (jumpQueued)
            {
                // 접지 상태에서만 점프한다. 공중에서 누른 입력은 그냥 버린다 —
                // 남겨두면 착지하는 순간 저절로 튀어오른다.
                if (canControl && controller.isGrounded)
                {
                    verticalVelocity = Mathf.Sqrt(-2f * gravity * jumpHeight);
                    animation?.PlayJump();
                }
                jumpQueued = false;
            }

            verticalVelocity += gravity * Time.deltaTime;

            Vector2 input = canControl ? moveInput : Vector2.zero;
            Vector3 horizontal = (transform.right * input.x + transform.forward * input.y) * CurrentSpeed();
            Vector3 velocity = horizontal + Vector3.up * verticalVelocity;

            controller.Move(velocity * Time.deltaTime);
        }

        private float CurrentSpeed()
        {
            var cfg = Config;
            if (cfg == null)
            {
                return 4f;
            }

            bool sprinting = SprintHeld();

            if (player != null && player.IsGhost)
            {
                // <b>단계와 무관하게 같은 규칙</b>이다 — 평소엔 걷고 Shift로 달린다.
                //
                // 예전에는 사냥 단계만 8m/s로 고정했는데, 그러면 Shift가 아무 반응이
                // 없어서 조작이 고장난 것처럼 느껴졌다. 최고 속도(8)는 그대로 두고
                // 그걸 <b>누를 때만</b> 내도록 바꾼 것이라, 추격 상한은 달라지지 않는다.
                //
                // 귀신 달리기 배수가 퇴마사(1.6)보다 큰 이유는 추격이 성립해야 하기 때문이다.
                float ghostFactor = sprinting ? Mathf.Max(1f, cfg.GhostSprintMultiplier) : 1f;
                return cfg.SoulMoveSpeed * ghostFactor;
            }

            float factor = sprinting ? Mathf.Max(1f, cfg.SprintMultiplier) : 1f;
            return cfg.ExorcistMoveSpeed * factor;
        }
    }
}
