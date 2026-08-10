using GhostHunter.Game;
using GhostHunter.Player;
using TMPro;
using UnityEngine;

namespace GhostHunter.UI
{
    /// <summary>
    /// 대기방 상태 표시의 <b>내용</b>만 갱신한다 — 접속 인원, 참가 코드.
    ///
    /// 보이고 안 보이고는 이제 <see cref="GameHudController"/>가 관리한다(단일 책임).
    /// 그 컨트롤러가 이 오브젝트를 켜준 동안에만 이 스크립트의 Update가 자연히 돈다 —
    /// 그래서 이 오브젝트 자신을 에디터에서 꺼둔 채로 저장해도 아무 문제가 없다.
    /// </summary>
    public class WaitingRoomHudUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI playerCountText;
        [SerializeField] private TextMeshProUGUI joinCodeText;

        private void Update()
        {
            if (playerCountText != null)
            {
                playerCountText.text = $"{NetworkPlayer.All.Count}명";
            }

            if (joinCodeText != null)
            {
                joinCodeText.text = string.IsNullOrEmpty(RelayConnection.JoinCode)
                    ? "-"
                    : RelayConnection.JoinCode;
            }
        }
    }
}
