using GhostHunter.Core;
using GhostHunter.Game;
using GhostHunter.Ghost;
using GhostHunter.Player;
using UnityEngine;

namespace GhostHunter.Tools
{
    /// <summary>
    /// 탐지 판정 (기술 문서 2-4). <b>서버에서만</b> 호출한다.
    ///
    /// 서버는 귀신 위치와 약점을 모두 알고 있으므로 판정이 한 곳에서 끝난다.
    /// 귀신 클라이언트는 판정에 관여하지 않으므로 결과를 조작할 수 없다.
    /// </summary>
    public static class DetectionJudge
    {
        /// <summary>
        /// 본체에서 검사할 지점들. <b>하나라도 화면에 걸리고 시선이 통하면 성공</b>이다.
        ///
        /// 예전에는 허리 한 점만 봤는데, 그러면 <b>기둥 뒤에 몸이 반쯤 보여도 실패</b>했다.
        /// 화면에는 분명히 귀신이 있는데 판정만 안 되는 상황이라 납득이 안 된다.
        ///
        /// <c>Up</c>은 발바닥 기준 높이, <c>Side</c>는 <b>관찰자 기준</b> 좌우 폭이다.
        /// 본체의 정면 방향을 쓰지 않는 이유는 두 가지다 — 영혼이 나가 있으면 본체의
        /// 회전을 알 수 없고, 애초에 판정에 필요한 건 <b>보는 쪽에서 본 실루엣의 폭</b>이다.
        /// </summary>
        private static readonly (float Up, float Side, string Name)[] BodyPoints =
        {
            (1.70f,  0.00f, "머리"),
            (1.45f, -0.22f, "왼어깨"),
            (1.45f,  0.22f, "오른어깨"),
            (1.05f, -0.35f, "왼팔"),
            (1.05f,  0.35f, "오른팔"),
            (0.50f, -0.15f, "왼다리"),
            (0.50f,  0.15f, "오른다리"),
        };

        /// <summary>
        /// 도구를 쓴 순간 <b>사용자 화면 안에</b> 귀신 본체가 있었는가?
        ///
        /// 성공 조건: 본체가 <b>화면 안 + 사거리 안 + 시야가 막히지 않음</b>,
        /// 그 도구가 약점, 도구 무효화 중이 아닐 것.
        /// 영혼 위치는 판정에 쓰지 않는다 — 도구가 잡는 것은 본체다 (시나리오 3-5).
        ///
        /// <b>반경이 아니라 시야로 바꾼 이유</b>: 반경은 "가까이 가서 아무 데나 쓴다"가
        /// 최적 전략이라 조절할 손잡이가 반경 하나뿐이었다. 시야로 바꾸면 어디를
        /// 겨눴는지가 결과를 가르므로, 사거리·화각·벽 차단 세 가지로 난이도를 나눠 잡을 수 있다.
        ///
        /// <b>본체가 안 보인다는 사실은 그대로다.</b> 렌더러가 꺼져 있어 화면에는
        /// 아무것도 안 보이지만, 판정은 서버가 실제 좌표로 한다.
        /// </summary>
        /// <param name="eyePosition">사용자 카메라 위치.</param>
        /// <param name="eyeRotation">사용자 카메라 회전.</param>
        /// <param name="verticalFov">카메라 세로 화각(도).</param>
        /// <param name="aspect">카메라 가로/세로 비. 화면 모양 그대로 판정하기 위해 받는다.</param>
        public static bool Judge(Vector3 eyePosition, Quaternion eyeRotation,
            float verticalFov, float aspect, ToolType tool, GameConfig config)
        {
            if (config == null)
            {
                return false;
            }

            var ghost = NetworkPlayer.GetGhost();
            if (ghost == null)
            {
                Fail(config, "귀신이 없다");
                return false;
            }

            // 도구 무효화 중에는 무조건 실패 (시나리오 3-5의 페널티 시퀀스).
            var ghostController = ghost.GetComponent<GhostController>();
            if (ghostController != null && ghostController.IsToolNullified)
            {
                Fail(config, "도구 무효화 중 (직전 탐지 페널티)");
                return false;
            }

            var manager = GameManager.Instance;
            if (manager == null || !manager.ServerWeakness.Contains(tool))
            {
                Fail(config, $"{tool}은(는) 약점이 아니다");
                return false;
            }

            Vector3 bodyPosition = ghostController != null
                ? ghostController.BodyPosition
                : ghost.transform.position;

            if (config.DebugDetection)
            {
                bool soulOut = ghostController != null && ghostController.IsSoulOut.Value;
                Debug.Log($"[탐지] 본체 {bodyPosition:F1} / 눈 {eyePosition:F1} / "
                    + $"거리 {Vector3.Distance(eyePosition, bodyPosition):F1}m / "
                    + $"영혼 분리 {(soulOut ? "예 — 본체는 영혼과 다른 곳에 있다" : "아니오")}");
            }

            // 관찰자 기준 좌우 축. 어깨·팔·다리를 이 방향으로 벌려 실루엣 폭을 만든다.
            Vector3 side = ViewerSideAxis(eyeRotation);

            int offScreen = 0;
            string blockedBy = null;
            var wallMask = LayerMask.GetMask("Wall", "Door");

            foreach (var (up, sideOffset, name) in BodyPoints)
            {
                Vector3 target = bodyPosition + Vector3.up * up + side * sideOffset;

                if (!IsOnScreen(eyePosition, eyeRotation, verticalFov, aspect, target, config.DetectionRange))
                {
                    offScreen++;
                    continue;
                }

                // 벽 너머까지 잡히면 사거리를 아무리 줄여도 "복도에서 한 바퀴 훑기"가
                // 최적 전략이 된다. 기본값은 막고, 필요하면 설정에서 열 수 있게 둔다.
                if (!config.DetectionThroughWalls
                    && Physics.Linecast(eyePosition, target, out var block, wallMask, QueryTriggerInteraction.Ignore))
                {
                    blockedBy ??= $"{name}→{block.collider.name}";
                    continue;
                }

                if (config.DebugDetection)
                {
                    Debug.Log($"[탐지] 성공 — {name}이(가) 보였다");
                }

                return true;
            }

            // 어느 지점도 통과하지 못했다. 화면 밖이었는지 가려졌는지를 나눠 남긴다.
            Fail(config, offScreen >= BodyPoints.Length
                ? "전 지점 화면 밖 또는 사거리 초과"
                : $"보이는 지점이 전부 막힘 — {blockedBy}");
            return false;
        }

        /// <summary>
        /// 관찰자가 본 "좌우" 방향. 수평면에 눕혀서 쓴다 —
        /// 카메라를 위아래로 젖혔을 때 어깨 폭이 같이 기울면 실루엣이 좁아진다.
        /// </summary>
        private static Vector3 ViewerSideAxis(Quaternion eyeRotation)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(eyeRotation * Vector3.forward, Vector3.up);

            return flatForward.sqrMagnitude > 0.0001f
                ? Vector3.Cross(Vector3.up, flatForward).normalized
                : eyeRotation * Vector3.right;   // 바로 위/아래를 볼 때의 예외
        }

        /// <summary>
        /// 실패 사유를 남긴다. <b>기본적으로 꺼져 있다</b> —
        /// 귀신 위치가 로그에 남아서, 호스트가 퇴마사면 콘솔만 봐도 답을 알게 된다.
        /// </summary>
        private static void Fail(GameConfig config, string reason)
        {
            if (config != null && config.DebugDetection)
            {
                Debug.Log("[탐지] 실패 — " + reason);
            }
        }

        /// <summary>
        /// 대상이 카메라 화면 안에 들어오는가.
        ///
        /// 원뿔로 근사하지 않고 <b>화면 모양 그대로</b> 잰다 — 원뿔을 쓰면 세로 화각에
        /// 맞출 때 화면 좌우 끝이 판정에서 빠지고, 가로에 맞추면 위아래 밖까지 잡힌다.
        /// 카메라 좌표계로 옮겨 x/z, y/z를 화각의 탄젠트와 비교하면 정확히 화면 사각형이 된다.
        /// </summary>
        private static bool IsOnScreen(Vector3 eyePosition, Quaternion eyeRotation,
            float verticalFov, float aspect, Vector3 target, float range)
        {
            Vector3 local = Quaternion.Inverse(eyeRotation) * (target - eyePosition);

            // 등 뒤는 볼 수 없다. z가 0 이하면 카메라 뒤쪽이다.
            if (local.z <= 0.01f || local.z > range)
            {
                return false;
            }

            float tanV = Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * Mathf.Max(0.1f, aspect);

            return Mathf.Abs(local.y) <= local.z * tanV
                && Mathf.Abs(local.x) <= local.z * tanH;
        }

    }
}
