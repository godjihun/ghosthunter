using UnityEngine;

namespace GhostHunter.Core
{
    /// <summary>
    /// 기술 문서 9번 요구사항: 밸런스 수치를 코드에 흩뿌리지 않고 여기 한 곳에 모은다.
    /// 값은 전부 플레이테스트 전 임시값이다 — 문서 9번의 "조정 신호" 표를 보고 조정할 것.
    ///
    /// 위쪽 = 로비에서 방장이 조절하는 밸런스 파라미터 (시나리오 6번)
    /// 아래쪽 = 조절 불가한 고정 상수 (시나리오 6번 "고정 수치")
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "GhostHunter/Game Config")]
    public class GameConfig : ScriptableObject
    {
        [Header("── 밸런스 파라미터 (로비에서 조절 가능) ──")]

        [Tooltip("게임 시작 시 귀신이 숨을 시간. 귀신이 자리를 못 잡고 쫓기듯 숨으면 늘린다.")]
        public float HidingDuration = 30f;

        [Tooltip("조사·퇴마 단계 전체 제한시간. 매 판 시간 초과로만 끝나면 늘린다.")]
        public float InvestigationDuration = 480f; // 8분

        [Tooltip("공포스킬 재사용 대기시간. 흡수가 너무 빨리 끝나면 늘린다.")]
        public float FearSkillCooldown = 30f;

        [Tooltip("탐지된 후 도구 무효화가 유지되는 시간 = 재은신 여유. 귀신이 도망칠 틈이 없으면 늘린다.")]
        public float ToolNullifyDuration = 15f;

        [Tooltip("도구 사용 시 탐지 사거리(m). 이 거리 안에서 화면에 들어와야 잡힌다. " +
                 "반경이 아니라 '보이는 거리'이므로 예전 반경보다 크게 잡아야 비슷한 체감이 된다.")]
        public float DetectionRange = 50f;

        [Tooltip("공포스킬 1회 성공 시 현실화 게이지 상승량(%). " +
                 "쿨타임과 곱해져 최소 현실화 시간을 만든다 — 기본값이면 30초 × 5회 = 2분. " +
                 "둘 중 하나만 바꿔도 판의 성격이 달라지므로 항상 함께 볼 것.")]
        public float AbsorbGaugePerHit = 20f;

        [Tooltip("사냥 단계 지속 시간. 짧으면 퇴마사가 숨어서 버티는 것만으로 이기고, " +
                 "길면 귀신이 전원을 잡을 시간이 충분해진다.")]
        public float HuntDuration = 60f;

        [Header("── 고정 상수 (조절 불가) ──")]

        [Tooltip("탐지가 벽을 통과하는가. 끄면 사이가 막혀 있을 때 실패한다. " +
                 "켜면 벽 너머 먼 귀신까지 잡혀 사거리만으로는 밸런스를 잡기 어려워진다.")]
        public bool DetectionThroughWalls = false;

        [Tooltip("탐지가 실패한 이유를 서버 콘솔에 찍는다. 밸런스 조정용. " +
                 "⚠️ 귀신 위치가 로그에 남으므로 실제 플레이에서는 반드시 꺼둘 것 — " +
                 "호스트가 퇴마사면 콘솔만 봐도 답을 알게 된다.")]
        public bool DebugDetection = false;

        [Tooltip("퇴마사 이동속도(m/s).")]
        public float ExorcistMoveSpeed = 4f;

        [Tooltip("영혼 이동속도(m/s). 벽에 막히는 대신 속도 이점은 주지 않는다.")]
        public float SoulMoveSpeed = 4f;

        [Tooltip("사냥 단계 귀신 이동속도(m/s). 시나리오상 퇴마사의 2배.")]
        public float GhostHuntMoveSpeed = 8f;

        [Tooltip("퇴마사 Shift 달리기 배수.")]
        public float SprintMultiplier = 1.6f;

        [Tooltip("영혼 Shift 달리기 배수. 퇴마사보다 크게 잡아 귀신이 추격에서 우위를 갖는다. " +
                 "사냥 단계 귀신에게는 적용하지 않는다 — 이미 2배라 곱하면 손쓸 수 없이 빨라진다.")]
        public float GhostSprintMultiplier = 2f;

        [Tooltip("공포스킬 사거리(m). 어몽어스의 칼처럼 밀착해야 발동한다. 사냥 단계의 처형도 같은 거리를 쓴다.")]
        public float FearSkillRange = 1.5f;

        [Tooltip("사냥 단계 처형 쿨타임(초). 한 번 누를 때 한 명만 죽도록 짧게 둔다. " +
                 "흡수 쿨타임(30초)을 쓰면 1분짜리 사냥에서 두 명도 못 잡는다.")]
        public float HuntKillCooldown = 3f;

        [Tooltip("귀신의 약점 도구 개수.")]
        public int WeaknessCount = 3;

        [Tooltip("제단에 서로 다른 도구가 이만큼 모이면 판정한다.")]
        public int AltarCapacity = 3;

        [Tooltip("맵에 배치할 도구 총 개수. 종류당 균등하게 나눠 배치된다.")]
        public int TotalToolCount = 30;

        [Tooltip("한 방의 최대 인원 (귀신 1 + 퇴마사 4).")]
        public int MaxPlayers = 5;

        [Header("── 연출 (기술 문서 6-2) ──")]

        [Tooltip("탐지 결과 연출 길이(초). 성공과 실패가 반드시 같아야 한다 — " +
                 "실패가 빨리 끝나면 연출 속도 자체가 단서가 되어 시나리오 3-2가 깨진다.")]
        public float DetectionFeedbackDuration = 1.5f;

        // PickupHoldDuration은 제거했다. 도구 줍기는 좌클릭 사용과 마찬가지로
        // F를 누르는 즉시 실행된다 — 홀드 게이지는 수집을 지루하게 만들 뿐이었다.

        [Header("── 관전 카메라 (기술 문서 5-6) ──")]

        [Tooltip("관전 대상 뒤쪽 거리(m).")]
        public float SpectatorCameraDistance = 2.5f;

        [Tooltip("관전 대상 위쪽 높이(m).")]
        public float SpectatorCameraHeight = 1.5f;

        [Tooltip("벽에 파묻히지 않도록 좁힐 수 있는 최소 거리(m).")]
        public float SpectatorCameraMinDistance = 0.5f;

        /// <summary>종류당 배치 개수. 종류마다 다르면 "이게 유난히 많네 = 약점?" 같은 오추론이 생긴다.</summary>
        public int ToolsPerType => Mathf.Max(1, TotalToolCount / ToolTypeExtensions.Count);
    }
}
