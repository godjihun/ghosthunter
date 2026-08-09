using GhostHunter.Game;
using GhostHunter.Player;
using UnityEngine;

namespace GhostHunter.Audio
{
    /// <summary>
    /// 발소리. 걷기·달리기 클립을 <b>반복 재생</b>하다가 멈추면 끈다.
    ///
    /// <b>모든 클라이언트에서 각자 돈다.</b> 위치는 이미 NetworkTransform으로 동기화돼 있으니,
    /// 각자 상대의 이동량을 재서 소리를 내면 된다 — <b>RPC가 필요 없다.</b>
    /// PlayerAnimation이 애니메이션 속도를 구하는 방식과 같다.
    ///
    /// ⚠️ <b>귀신은 발소리를 내지 않는다.</b> 이 게임은 귀신의 위치를 숨기는 것이 축이라
    /// (기술 문서 2-2-1), 화면에서 렌더러를 껐는데 소리로 위치가 새면 그 설계가 통째로
    /// 무의미해진다. 렌더러를 끄는 것과 같은 이유로 소리도 내지 않는다.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerFootsteps : MonoBehaviour
    {
        /// <summary>이 속도 아래는 멈춘 것으로 본다. 미세한 떨림에 소리가 켜지지 않게 한다.</summary>
        private const float StopSpeed = 0.6f;

        /// <summary>걷기↔달리기 경계에서 클립이 딸꾹질하지 않도록 두는 여유 폭(m/s).</summary>
        private const float SwitchMargin = 0.4f;

        /// <summary>이 속도보다 빠르게 떨어지면 공중으로 본다(m/s). 계단을 내려갈 때는 안 걸린다.</summary>
        private const float FallSpeed = 3f;

        private NetworkPlayer player;
        private PlayerMovement movement;
        private AudioSource source;
        private Vector3 lastPosition;
        private bool wasRunning;

        /// <summary>이 시각까지는 공중에 있는 것으로 친다. 점프 통지가 갱신한다.</summary>
        private float airborneUntil;

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            movement = GetComponent<PlayerMovement>();
            lastPosition = transform.position;

            // AudioSource를 프리팹에 미리 붙이지 않고 여기서 만든다.
            // 프리팹에는 이 컴포넌트 하나만 있으면 되고, 설정이 코드에 남아 추적하기 쉽다.
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;                       // 3D — 멀면 안 들린다
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.dopplerLevel = 0f;
        }

        private void Update()
        {
            var library = GameAudio.Library;
            if (library == null || source == null)
            {
                return;
            }

            // 이동량으로 속도를 잰다. 입력값을 쓰면 원격 플레이어는 알 수가 없고,
            // 벽에 막혀 못 가는 동안에도 제자리에서 발소리가 난다.
            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;

            float verticalSpeed = Time.deltaTime > 0f ? delta.y / Time.deltaTime : 0f;
            delta.y = 0f;

            float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;

            if (!ShouldMakeSound(speed, verticalSpeed))
            {
                if (source.isPlaying)
                {
                    source.Stop();
                }
                return;
            }

            source.maxDistance = Mathf.Max(2f, library.FootstepHearingRange);
            source.volume = Mathf.Clamp01(library.SfxVolume * library.FootstepVolume);

            var clip = PickClip(library, speed);
            if (clip == null)
            {
                return;
            }

            // 같은 클립이 이미 돌고 있으면 건드리지 않는다. 매 프레임 Play()를 부르면
            // 소리가 계속 처음으로 되감겨 <b>딸깍거리기만 하고 재생이 안 된다.</b>
            if (source.clip != clip)
            {
                source.clip = clip;
                source.Play();
            }
            else if (!source.isPlaying)
            {
                source.Play();
            }
        }

        /// <summary>
        /// 점프했다는 통지. <see cref="PlayerAnimation"/>의 점프 RPC가 전원에게 알려준다.
        ///
        /// <b>접지 여부를 물어볼 수가 없다.</b> <c>CharacterController.isGrounded</c>는
        /// 실제로 <c>Move()</c>를 부르는 소유자에게만 유효하고, 원격 플레이어는 위치만
        /// 받아오므로 항상 엉뚱한 값이 나온다. 그래서 체공 시간만큼 시간을 재는 쪽을 택했다.
        /// </summary>
        public void NotifyJump()
        {
            float airTime = movement != null ? movement.AirTime : 0.6f;
            airborneUntil = Time.time + airTime;
        }

        /// <summary>발소리를 낼 상황인가.</summary>
        private bool ShouldMakeSound(float speed, float verticalSpeed)
        {
            if (player == null || !player.IsSpawned)
            {
                return false;
            }

            // 공중에서는 발이 땅에 없다. 점프 통지로 체공 시간을 재고,
            // 낙하 속도로 한 번 더 거른다 — 난간에서 뛰어내리면 통지가 없기 때문이다.
            if (Time.time < airborneUntil || verticalSpeed < -FallSpeed)
            {
                return false;
            }

            // 대기방·결과 화면에서는 내지 않는다. 대기방에서 다 같이 걸어다니면
            // 발소리가 겹쳐 시끄럽기만 하다.
            if (!GameManager.IsGameplayActive)
            {
                return false;
            }

            // 귀신은 소리를 내지 않는다 (클래스 주석 참고).
            if (!player.IsExorcist || !player.IsAlive.Value)
            {
                return false;
            }

            return speed > StopSpeed;
        }

        /// <summary>
        /// 걷기와 달리기 중 무엇을 틀 것인가.
        ///
        /// 경계를 한 값으로 두면 그 근처에서 두 클립이 번갈아 잡히며 소리가 끊긴다.
        /// 이미 달리는 중이면 조금 더 느려져도 달리기를 유지한다(이력 현상).
        /// </summary>
        private AudioClip PickClip(AudioLibrary library, float speed)
        {
            float threshold = RunThreshold();
            float effective = wasRunning ? threshold - SwitchMargin : threshold + SwitchMargin;

            wasRunning = speed > effective;

            if (wasRunning && library.Running != null)
            {
                return library.Running;
            }

            return library.Walking != null ? library.Walking : library.Running;
        }

        /// <summary>
        /// 걷기와 달리기를 가르는 속도. <b>설정값에서 계산한다</b> — 숫자를 적어두면
        /// 로비에서 이동속도를 바꿨을 때 기준만 옛날 값으로 남는다.
        /// </summary>
        private static float RunThreshold()
        {
            var config = GameManager.Config;
            if (config == null)
            {
                return 5f;
            }

            float walk = config.ExorcistMoveSpeed;
            float run = walk * Mathf.Max(1f, config.SprintMultiplier);
            return (walk + run) * 0.5f;
        }
    }
}
