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

        /// <summary>
        /// 걷기·달리기 클립이 <b>실제로</b> 만들어진 보폭 속도(m/s). 클립에서 직접 읽는다.
        ///
        /// 손으로 적어두면 클립을 갈아끼울 때 조용히 어긋난다 — 실제로 4.5로 적어뒀는데
        /// 진짜 값은 4.635였고, 블렌드 임계값은 아예 게임 속도(4/8)로 잡혀 있어
        /// <b>4m/s로 달리면서 1.6m/s 보폭으로 걷는</b> 상태였다.
        /// </summary>
        private float walkClipSpeed;
        private float runClipSpeed;

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
            CacheClipMetrics();
        }

        /// <summary>
        /// 컨트롤러에 물린 클립에서 점프 길이와 이동 보폭 속도를 읽어둔다.
        /// 에셋에 있는 값을 코드에 옮겨 적지 않기 위한 것이다.
        /// </summary>
        private void CacheClipMetrics()
        {
            jumpClipLength = 0f;
            walkClipSpeed = 0f;
            runClipSpeed = 0f;

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            foreach (var c in animator.runtimeAnimatorController.animationClips)
            {
                if (c == null)
                {
                    continue;
                }

                if (jumpClipLength <= 0f && c.name.IndexOf("Jump", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    jumpClipLength = c.length;
                }
                else if (walkClipSpeed <= 0f && c.name.IndexOf("Walk", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    walkClipSpeed = HorizontalSpeedOf(c);
                }
                else if (runClipSpeed <= 0f && c.name.IndexOf("Run", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    runClipSpeed = HorizontalSpeedOf(c);
                }
            }
        }

        /// <summary>클립의 루트 모션이 초당 얼마나 나아가는가. 수직 성분은 보폭과 무관하므로 뺀다.</summary>
        private static float HorizontalSpeedOf(AnimationClip clip)
        {
            var v = clip.averageSpeed;
            return new Vector2(v.x, v.z).magnitude;
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

            animator.SetFloat("SpeedMultiplier", speed > 0.1f ? ComputeMoveRate(speed) : 1f);

            animator.SetBool(DeadHash, !player.IsAlive.Value);

            int emoteIndex = emote != null ? emote.CurrentEmote.Value : -1;
            animator.SetInteger(EmoteHash, emoteIndex);
        }

        /// <summary>
        /// 이동 클립의 재생 배속. 발이 땅에서 미끄러지지 않게 하는 것이 목적이다.
        ///
        /// <b>블렌드 트리 임계값이 각 클립의 보폭 속도로 맞춰져 있다는 전제</b>다. 그러면
        /// 걷기~달리기 구간 안에서는 블렌드 자체가 이미 올바른 보폭을 만들어내므로
        /// 배속은 1이어야 한다. 여기에 또 <c>speed / 기준속도</c>를 곱하면 이중 보정이라
        /// 오히려 느려진다 — 이전 구현이 그래서 틀렸다.
        ///
        /// 보정이 필요한 건 <b>구간 바깥</b>뿐이다. 달리기보다 빠르면(질주) 클립이 못 따라가고,
        /// 걷기보다 느리면 대기 자세와 섞이며 보폭만 줄어 발이 헛돈다.
        /// </summary>
        private float ComputeMoveRate(float speed)
        {
            if (walkClipSpeed <= 0.01f || runClipSpeed <= 0.01f)
            {
                return 1f;
            }

            // 구간 안이면 speed 그대로 → 배속 1. 바깥이면 가장 가까운 클립 속도로 잘린다.
            float natural = Mathf.Clamp(speed, walkClipSpeed, runClipSpeed);
            return Mathf.Clamp(speed / natural, minRate, maxRate);
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
            // 점프 소리는 <b>애니메이터보다 먼저</b> 낸다. 아래에서 animator가 null이면
            // 그냥 돌아가버리므로, 뒤에 두면 모델이 아직 안 잡힌 순간에 소리가 통째로 빠진다.
            //
            // 귀신은 소리를 내지 않는다 — 발소리를 막은 것과 같은 이유다.
            // 렌더러를 꺼서 숨겨놓고 점프 소리로 위치가 새면 의미가 없다.
            // 발소리는 진영과 무관하게 멈춘다. 공중에 떠 있는데 발소리가 나면
            // 안 되고, 귀신은 애초에 발소리를 안 내므로 호출해도 손해가 없다.
            GetComponent<Audio.PlayerFootsteps>()?.NotifyJump();

            if (player != null && player.IsExorcist)
            {
                Audio.GameAudio.PlayJump(transform.position);
            }

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
