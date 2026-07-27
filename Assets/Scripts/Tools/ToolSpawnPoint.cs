using UnityEngine;

namespace GhostHunter.Tools
{
    /// <summary>
    /// 도구가 숨겨질 수 있는 지점. 서랍·수납장·탁자 아래 등에 씬에 미리 뿌려둔다.
    ///
    /// 기술 문서 5-1: 스폰 지점은 도구 개수(30)보다 넉넉히 깔아둬야 매 판 배치가 달라진다.
    /// </summary>
    public class ToolSpawnPoint : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawSphere(transform.position, 0.15f);
        }
    }
}
