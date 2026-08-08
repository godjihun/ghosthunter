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
        /// <summary>본체 중심 높이. 원점이 발바닥이라 그대로 쓰면 바닥을 겨눠야 잡힌다.</summary>
        private const float BodyCenterHeight = 1.0f;

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

            Vector3 target = bodyPosition + Vector3.up * BodyCenterHeight;

            if (config.DebugDetection)
            {
                bool soulOut = ghostController != null && ghostController.IsSoulOut.Value;
                Debug.Log($"[탐지] 본체 {bodyPosition:F1} / 눈 {eyePosition:F1} / "
                    + $"거리 {Vector3.Distance(eyePosition, target):F1}m / "
                    + $"영혼 분리 {(soulOut ? "예 — 본체는 영혼과 다른 곳에 있다" : "아니오")}");
            }

            if (!IsOnScreen(eyePosition, eyeRotation, verticalFov, aspect, target, config.DetectionRange))
            {
                Fail(config, "화면 밖 또는 사거리 초과");
                return false;
            }

            // 벽 너머까지 잡히면 사거리를 아무리 줄여도 "복도에서 한 바퀴 훑기"가
            // 최적 전략이 된다. 기본값은 막고, 필요하면 설정에서 열 수 있게 둔다.
            if (!config.DetectionThroughWalls
                && Physics.Linecast(eyePosition, target, out var block,
                    LayerMask.GetMask("Wall", "Door"), QueryTriggerInteraction.Ignore))
            {
                Fail(config, $"시선이 막힘 — {block.collider.name} @ {block.point:F1}");
                return false;
            }

            if (config.DebugDetection)
            {
                Debug.Log("[탐지] 성공");
            }

            return true;
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
