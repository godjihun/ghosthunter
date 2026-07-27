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
        /// 도구 사용 지점에서 탐지가 성공하는가?
        ///
        /// 성공 조건: 귀신 <b>본체</b>가 반경 안에 있고, 그 도구가 약점이며, 도구 무효화 중이 아닐 것.
        /// 영혼 위치는 판정에 쓰지 않는다 — 도구가 잡는 것은 본체다 (시나리오 3-5).
        /// </summary>
        public static bool Judge(Vector3 usePosition, ToolType tool, GameConfig config)
        {
            if (config == null)
            {
                return false;
            }

            var ghost = NetworkPlayer.GetGhost();
            if (ghost == null)
            {
                return false;
            }

            // 도구 무효화 중에는 무조건 실패 (시나리오 3-5의 페널티 시퀀스).
            var ghostController = ghost.GetComponent<GhostController>();
            if (ghostController != null && ghostController.IsToolNullified)
            {
                return false;
            }

            var manager = GameManager.Instance;
            if (manager == null || !manager.ServerWeakness.Contains(tool))
            {
                return false;
            }

            Vector3 bodyPosition = ghostController != null
                ? ghostController.BodyPosition
                : ghost.transform.position;

            // 3D 거리로 판정한다. 수평 거리로 하면 다층 저택에서
            // 위층에서 쓴 도구가 아래층 귀신을 잡아버린다 (기술 문서 5-2).
            return Vector3.Distance(usePosition, bodyPosition) <= config.DetectionRadius;
        }
    }
}
