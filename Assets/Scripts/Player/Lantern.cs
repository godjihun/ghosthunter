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

        [Header("── 게임 중: 퇴마사 ──")]

        // 씬(Lighting → Environment) 값은 <b>로비·대기방</b>의 밝기다. 대기방이 저택과
        // 같은 씬이라 씬을 어둡게 맞추면 사람이 모여 이야기하는 공간까지 캄캄해진다.
        // 그래서 저택의 어둠은 씬이 아니라 여기서 정한다 — 게임이 시작되는 순간 덮어씌운다.

        [Tooltip("퇴마사 화면의 하늘 방향 환경광. 위에서 오는 빛 — 바닥이 밝아진다.")]
        [SerializeField] private Color exorcistAmbientSky = new(0.012f, 0.012f, 0.020f);

        [Tooltip("퇴마사 화면의 수평 방향 환경광. 벽이 밝아진다 — 실내에서 체감이 가장 크다.")]
        [SerializeField] private Color exorcistAmbientEquator = new(0.008f, 0.008f, 0.012f);

        [Tooltip("퇴마사 화면의 아래 방향 환경광. 천장이 밝아진다.")]
        [SerializeField] private Color exorcistAmbientGround = new(0.012f, 0.012f, 0.020f);

        [Tooltip("퇴마사 화면의 안개 농도. 높일수록 랜턴 밖이 빨리 묻힌다.")]
        [SerializeField] private float exorcistFogDensity = 0.100f;

        [Tooltip("퇴마사 화면의 안개 색. 먼 곳이 결국 이 색으로 수렴한다 — 어두울수록 캄캄해진다.")]
        [SerializeField] private Color exorcistFogColor = new(0.020f, 0.025f, 0.040f);

        [Header("── 게임 중: 귀신 암시(暗視) ──")]

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

        [Tooltip("귀신 화면의 안개 색. 밝게 두면 먼 곳이 뿌옇게 뜨고, 어둡게 두면 어둠에 묻힌다.")]
        [SerializeField] private Color ghostFogColor = new(0.060f, 0.075f, 0.110f);

        /// <summary>화면 밝기를 결정하는 상태. 셋 중 하나만 적용된다.</summary>
        private enum AmbientMode
        {
            /// <summary>대기방·결과 화면. 씬(Lighting → Environment)에 설정한 값 그대로.</summary>
            Lobby,

            /// <summary>게임 중 퇴마사. 위 인스펙터 값 + 랜턴.</summary>
            Exorcist,

            /// <summary>게임 중 귀신. 랜턴 없이 환경광만 올린다.</summary>
            Ghost,
        }

        private NetworkPlayer player;

        /// <summary>마지막으로 적용한 상태. 매 프레임 만지지 않기 위한 캐시.</summary>
        private bool? lanternApplied;
        private AmbientMode? ambientApplied;

        // 씬에 설정된 원래 값 = 로비·대기방의 밝기. 처음 만지기 직전에 잡는다.
        private Color baseSky, baseEquator, baseGround, baseFogColor;
        private float baseFogDensity;
        private bool baseCaptured;

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
        }

        public override void OnNetworkSpawn()
        {
            ApplyLantern(false);
        }

        public override void OnNetworkDespawn()
        {
            // 나가면서 씬 값으로 되돌려놓지 않으면 진영 밝기가 다음 판까지 남는다.
            ApplyAmbient(AmbientMode.Lobby);
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
        /// 지금 화면이 어떤 밝기여야 하는가.
        ///
        /// <b>대기방은 저택과 같은 씬</b>이라 씬 환경광을 그대로 쓴다 — 사람이 모여
        /// 이야기하는 공간이므로 씬은 밝게 맞춰둔다. 저택의 어둠은 씬이 아니라
        /// 인스펙터 값이고, 게임이 시작되는 순간 진영에 따라 덮어씌운다.
        /// </summary>
        private AmbientMode CurrentMode()
        {
            if (!GameManager.IsGameplayActive)
            {
                return AmbientMode.Lobby;
            }

            return player != null && player.IsGhost ? AmbientMode.Ghost : AmbientMode.Exorcist;
        }

        private void LateUpdate()
        {
            ApplyLantern(ShouldLanternBeOn());

            // <b>반드시 소유자만.</b> RenderSettings는 그 클라이언트의 전역 설정이라,
            // 내 화면에 있는 남의 캐릭터까지 손대게 두면 서로 값을 덮어쓴다.
            if (IsOwner)
            {
                ApplyAmbient(CurrentMode());
            }
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
        private void ApplyAmbient(AmbientMode mode)
        {
            if (!OwnsRenderSettings || ambientApplied == mode)
            {
                return;
            }

            // 씬 값은 처음 손대기 직전에 잡는다. Awake에서 잡으면
            // 씬 조명 설정이 아직 적용되기 전일 수 있다.
            //
            // 이 값이 곧 <b>로비·대기방의 밝기</b>다. 대기방을 조절할 때는
            // Lighting → Environment를 만지면 되고, 저택 안의 두 진영은 인스펙터 값이다.
            if (!baseCaptured)
            {
                baseSky = RenderSettings.ambientSkyColor;
                baseEquator = RenderSettings.ambientEquatorColor;
                baseGround = RenderSettings.ambientGroundColor;
                baseFogDensity = RenderSettings.fogDensity;
                baseFogColor = RenderSettings.fogColor;
                baseCaptured = true;
            }

            ambientApplied = mode;

            switch (mode)
            {
                case AmbientMode.Exorcist:
                    RenderSettings.ambientSkyColor = exorcistAmbientSky;
                    RenderSettings.ambientEquatorColor = exorcistAmbientEquator;
                    RenderSettings.ambientGroundColor = exorcistAmbientGround;
                    RenderSettings.fogDensity = Mathf.Max(0f, exorcistFogDensity);
                    RenderSettings.fogColor = exorcistFogColor;
                    break;

                case AmbientMode.Ghost:
                    RenderSettings.ambientSkyColor = ghostAmbientSky;
                    RenderSettings.ambientEquatorColor = ghostAmbientEquator;
                    RenderSettings.ambientGroundColor = ghostAmbientGround;
                    RenderSettings.fogDensity = Mathf.Max(0f, ghostFogDensity);
                    RenderSettings.fogColor = ghostFogColor;
                    break;

                default: // Lobby — 씬에 설정한 그대로
                    RenderSettings.ambientSkyColor = baseSky;
                    RenderSettings.ambientEquatorColor = baseEquator;
                    RenderSettings.ambientGroundColor = baseGround;
                    RenderSettings.fogDensity = baseFogDensity;
                    RenderSettings.fogColor = baseFogColor;
                    break;
            }
        }
    }
}
