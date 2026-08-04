using GhostHunter.Game;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Player
{
    /// <summary>
    /// 어둠 속에서 각 진영이 무엇을 보는지 담당한다 (기술 문서 8-1).
    ///
    /// 퇴마사는 <b>랜턴</b>으로 앞을 비추고, 귀신은 랜턴이 없는 대신
    /// <b>자기 화면의 환경광만 올려</b> 저택 전체를 흐릿하게나마 본다.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class Lantern : NetworkBehaviour
    {
        [Tooltip("카메라에 붙은 스포트라이트. 시선을 따라간다. 퇴마사 전용.")]
        [SerializeField] private Light lanternLight;

        [Header("── 귀신 암시(暗視) ──")]

        // <b>배수가 아니라 절대값이다.</b> 예전에는 씬 값에 배수를 곱했는데,
        // 그러면 퇴마사 밝기를 올릴 때마다 귀신이 같이 밝아져 따로 맞출 수가 없었다.
        // 이제 두 진영의 밝기가 완전히 독립적이다.

        [Tooltip("귀신 화면의 하늘 방향 환경광. 위에서 오는 빛 — 바닥이 밝아진다.")]
        [SerializeField] private Color ghostAmbientSky = new(0.20f, 0.24f, 0.36f);

        [Tooltip("귀신 화면의 수평 방향 환경광. 벽이 밝아진다 — 실내에서 체감이 가장 크다.")]
        [SerializeField] private Color ghostAmbientEquator = new(0.13f, 0.15f, 0.22f);

        [Tooltip("귀신 화면의 아래 방향 환경광. 천장이 밝아진다.")]
        [SerializeField] private Color ghostAmbientGround = new(0.05f, 0.05f, 0.07f);

        [Tooltip("귀신 화면의 안개 농도. 낮출수록 멀리까지 보인다. 퇴마사 값과 무관하다.")]
        [SerializeField] private float ghostFogDensity = 0.010f;

        private NetworkPlayer player;

        /// <summary>마지막으로 적용한 상태. 매 프레임 만지지 않기 위한 캐시.</summary>
        private bool? lanternApplied;
        private bool? visionApplied;

        // 되돌리기 위한 원래 값. 진영이 정해지기 전에 잡아둔다.
        private Color baseSky, baseEquator, baseGround;
        private float baseFogDensity;
        private bool baseCaptured;

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
        }

        public override void OnNetworkSpawn()
        {
            ApplyLantern(false);
            ApplyVision(false);
        }

        public override void OnNetworkDespawn()
        {
            // 나가면서 밝기를 되돌려놓지 않으면 다음 판까지 밝은 채로 남는다.
            ApplyVision(false);
        }

        /// <summary>
        /// <see cref="RenderSettings"/>는 <b>씬 전체에 하나뿐인 전역 설정</b>이라
        /// 내 캐릭터의 컴포넌트만 건드려야 한다.
        ///
        /// 내 화면에는 남의 캐릭터 오브젝트도 존재하고 거기서도 이 컴포넌트가 돈다.
        /// 그것들까지 손대게 두면 <b>"나는 귀신이 아니니 원래 밝기로"라며 방금 올린 값을
        /// 도로 내려버리고</b>, 실행 순서에 따라서는 밝아진 값을 원래 값으로 잘못 기억한다.
        /// </summary>
        private bool OwnsRenderSettings => IsOwner;

        /// <summary>
        /// 랜턴을 켤 것인가.
        ///
        /// 귀신에게는 주지 않는다 — 은신이 성립하지 않는다.
        /// 죽으면 끈다. 시체 자리가 계속 밝으면 사망 위치가 그대로 표시된다.
        /// </summary>
        private bool ShouldLanternBeOn()
        {
            if (player == null || !player.IsExorcist)
            {
                return false;
            }

            return GameManager.IsGameplayActive && player.IsAlive.Value;
        }

        /// <summary>
        /// 귀신 암시를 켤 것인가.
        ///
        /// <b>반드시 소유자에게만.</b> <see cref="RenderSettings"/>는 그 클라이언트의
        /// 렌더링 설정이라 소유자 화면만 밝아지고 남에게는 영향이 없다.
        /// 서버가 이걸 대신 만지면 <b>호스트 화면이 같이 밝아져</b> 의미가 없어진다.
        /// </summary>
        private bool ShouldVisionBeOn()
        {
            if (!IsOwner || player == null || !player.IsGhost)
            {
                return false;
            }

            return GameManager.IsGameplayActive;
        }

        private void LateUpdate()
        {
            ApplyLantern(ShouldLanternBeOn());
            ApplyVision(ShouldVisionBeOn());
        }

        private void ApplyLantern(bool on)
        {
            if (lanternLight == null || lanternApplied == on)
            {
                return;
            }

            lanternApplied = on;
            lanternLight.enabled = on;
        }

        /// <summary>
        /// 환경광과 안개를 조정해 화면 전체를 밝힌다.
        ///
        /// 조명을 하나 더 붙이는 방식은 <b>귀신 주변만</b> 밝아져 저택을 파악하는 데
        /// 도움이 안 되고, URP의 추가 조명 한도(오브젝트당 4개)도 잡아먹는다.
        /// 환경광은 추가 패스도 조명 슬롯도 쓰지 않아 WebGL에서 사실상 공짜다.
        /// </summary>
        private void ApplyVision(bool on)
        {
            if (!OwnsRenderSettings || visionApplied == on)
            {
                return;
            }

            // 원래 값은 처음 손대기 직전에 잡는다. Awake에서 잡으면
            // 씬 조명 설정이 아직 적용되기 전일 수 있다.
            if (!baseCaptured)
            {
                baseSky = RenderSettings.ambientSkyColor;
                baseEquator = RenderSettings.ambientEquatorColor;
                baseGround = RenderSettings.ambientGroundColor;
                baseFogDensity = RenderSettings.fogDensity;
                baseCaptured = true;
            }

            visionApplied = on;

            // 켤 때는 귀신 전용 절대값, 끌 때는 씬에서 잡아둔 원래 값으로 되돌린다.
            RenderSettings.ambientSkyColor = on ? ghostAmbientSky : baseSky;
            RenderSettings.ambientEquatorColor = on ? ghostAmbientEquator : baseEquator;
            RenderSettings.ambientGroundColor = on ? ghostAmbientGround : baseGround;
            RenderSettings.fogDensity = on ? Mathf.Max(0f, ghostFogDensity) : baseFogDensity;
        }
    }
}
