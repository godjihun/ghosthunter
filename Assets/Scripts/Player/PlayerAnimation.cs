using GhostHunter.Game;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Player
{
    /// <summary>
    /// 캐릭터 애니메이션 구동 (기술 문서 6번).
    ///
    /// <b>Animator 파라미터만 채운다.</b> 상태 전환 규칙은 Animator Controller가 갖는다 —
    /// 여기서 재생할 클립을 직접 고르면 전환 조건이 코드와 에셋 양쪽에 흩어진다.
    ///
    /// 모든 클라이언트에서 각자 돌아간다. 위치·속도·이모트가 이미 동기화돼 있으므로
    /// 애니메이션 자체를 따로 보낼 필요가 없다 — 같은 입력이면 같은 결과가 나온다.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerAnimation : NetworkBehaviour
    {
        [Tooltip("캐릭터 모델의 Animator. 진영에 따라 다른 모델을 쓰므로 런타임에 찾는다.")]
        [SerializeField] private Animator animator;

        [Tooltip("달리기 클립이 만들어진 기준 속도(m/s). 실제 속도를 이 값으로 나눠 재생 배속을 정한다. " +
                 "Mixamo 달리기는 대략 4.5m/s 보폭으로 만들어져 있다.")]
        [SerializeField] private float referenceSpeed = 4.5f;

        [Tooltip("재생 배속의 하한·상한. 너무 넓게 두면 발이 헛돌거나 슬로우모션이 된다.")]
        [SerializeField] private float minRate = 0.6f;
        [SerializeField] private float maxRate = 1.8f;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int EmoteHash = Animator.StringToHash("Emote");
        private static readonly int DeadHash = Animator.StringToHash("Dead");
        private static readonly int PickupHash = Animator.StringToHash("Pickup");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int JumpRateHash = Animator.StringToHash("JumpRate");

        /// <summary>
        /// 점프 클립의 원본 길이(초). Animator에서 직접 읽는다.
        ///
        /// 손으로 적어두면 <b>클립을 다시 자를 때마다 이 값이 어긋난 채 남는다</b> —
        /// 실제로 1.9초 → 0.83초 → 1.4초 → 0.6초로 여러 번 바뀌었다.
        /// </summary>
        private float jumpClipLength;

        private NetworkPlayer player;
        private PlayerEmote emote;
        private PlayerMovement movement;
        private CharacterController controller;
        private Vector3 lastPosition;

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            emote = GetComponent<PlayerEmote>();
            movement = GetComponent<PlayerMovement>();
            controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            lastPosition = transform.position;
            ResolveAnimator();
        }

        /// <summary>진영이 정해지면 켜진 모델이 달라지므로 그때마다 다시 찾는다.</summary>
        public void ResolveAnimator()
        {
            animator = GetComponentInChildren<Animator>(true);
            CacheJumpClipLength();
        }

        /// <summary>컨트롤러에 물린 점프 클립의 실제 길이를 읽어둔다.</summary>
        private void CacheJumpClipLength()
        {
            jumpClipLength = 0f;
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            foreach (var c in animator.runtimeAnimatorController.animationClips)
            {
                if (c != null && c.name.IndexOf("Jump", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    jumpClipLength = c.length;
                    return;
                }
            }
        }

        private void Update()
        {
            if (animator == null)
            {
                ResolveAnimator();
                if (animator == null)
                {
                    return;
                }
            }

            // 속도는 실제 이동량으로 잰다. 입력값을 쓰면 벽에 막혀 못 가는 동안에도
            // 제자리걸음 대신 걷는 시늉을 해 발이 미끄러진다.
            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;

            float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            animator.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);

            // 클립을 실제 속도에 맞춰 배속한다. 이게 없으면 발이 땅에서 미끄러진다.
            //
            // 기준은 <b>클립이 만들어진 보폭 속도</b>여야 한다. 게임의 이동 속도를 기준으로 삼으면
            // 배속이 항상 1이 되어, 클립보다 빠르게 움직일 때 발이 뒤처진다.
            float rate = referenceSpeed > 0.01f
                ? Mathf.Clamp(speed / referenceSpeed, minRate, maxRate)
                : 1f;
            animator.SetFloat("SpeedMultiplier", speed > 0.1f ? rate : 1f);

            animator.SetBool(DeadHash, !player.IsAlive.Value);

            int emoteIndex = emote != null ? emote.CurrentEmote.Value : -1;
            animator.SetInteger(EmoteHash, emoteIndex);
        }

        /// <summary>도구를 주웠을 때 한 번 재생. ExorcistInventory가 호출한다.</summary>
        public void PlayPickup()
        {
            if (animator != null)
            {
                animator.SetTrigger(PickupHash);
            }
        }

        /// <summary>
        /// 점프한 순간 한 번 재생.
        ///
        /// <b>모든 화면에서 같이 보여야 한다.</b> 접지 여부는 소유자만 알 수 있으므로
        /// (원격 플레이어는 위치만 받는다) 소유자가 알려주는 수밖에 없다.
        ///
        /// 클립은 'Jumping Up'(0.83초)을 쓴다. 앞서 시도한 'Jumping'(1.9초)은
        /// 도약과 착지 흡수로 <b>다리를 두 번 접어</b> 짧은 체공 시간과 맞지 않았다.
        /// </summary>
        public void PlayJump()
        {
            if (!IsOwner)
            {
                return;
            }

            JumpRpc();
        }

        [Rpc(SendTo.Everyone)]
        private void JumpRpc()
        {
            if (animator == null)
            {
                return;
            }

            // 클립 전체(도약+공중+착지)를 체공 시간 안에 끝낸다.
            // 이게 안 맞으면 이미 착지했는데 화면에서는 아직 공중 동작을 하고 있어
            // 뒤이어 나오는 착지 동작이 <b>두 번째 점프처럼</b> 보인다.
            animator.SetFloat(JumpRateHash, ComputeJumpRate());
            animator.SetTrigger(JumpHash);
        }

        private float ComputeJumpRate()
        {
            if (movement == null || jumpClipLength <= 0.01f)
            {
                return 1f;
            }

            float airTime = movement.AirTime;
            if (airTime <= 0.01f)
            {
                return 1f;
            }

            // 너무 극단적인 배속은 동작이 뭉개지므로 상·하한을 둔다.
            return Mathf.Clamp(jumpClipLength / airTime, 0.5f, 4f);
        }
    }
}
