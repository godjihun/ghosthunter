using GhostHunter.Core;
using GhostHunter.Game;
using GhostHunter.Player;
using TMPro;
using UnityEngine;

namespace GhostHunter.UI
{
    /// <summary>
    /// 화면 중앙 살짝 하단에 스르륵 떴다가 스르륵 사라지는 안내 배너.
    /// 단계 전환("조사 시간입니다…")과 약점 발견 공지가 같은 배너를 나눠 쓴다 —
    /// 예전 GameHudUI의 IMGUI TickAnnouncement/TickWeaknessAnnouncement/DrawAnnouncement를
    /// UGUI로 옮긴 것으로, 문구·타이밍(10초 유지, 1.2초 페이드)은 그대로다.
    /// </summary>
    public class AnnouncementUI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TextMeshProUGUI text;

        private const float Duration = 10f;
        private const float Fade = 1.2f;

        private string announceText;
        private float announceTimer;

        /// <summary>마지막으로 본 약점 발견 개수. 늘어난 순간에만 공지한다.</summary>
        private int lastFoundCount;

        /// <summary>마지막으로 본 단계. 바뀌는 순간을 잡기 위한 것.</summary>
        private GamePhase lastPhase;

        /// <summary>
        /// 첫 프레임에는 안내를 띄우지 않는다.
        ///
        /// 판이 도는 중에 들어온 사람에게 "조사 시간입니다"가 뜨면 방금 시작한 줄 안다.
        /// 처음 본 단계는 기록만 하고 넘어간다.
        /// </summary>
        private bool phaseKnown;

        private void Awake()
        {
            if (group != null)
            {
                group.alpha = 0f;
            }
        }

        private void Update()
        {
            TickPhase();

            // 단계 안내보다 <b>뒤에</b> 부른다. 탐지 성공은 곧 은신 전환을 부르는데,
            // 같은 프레임에 둘 다 뜨면 "어떤 도구로 찾았는가"가 묻힌다.
            TickWeaknessFound();

            Apply();
        }

        /// <summary>단계가 바뀌는 순간을 잡아 안내를 띄운다.</summary>
        private void TickPhase()
        {
            var phase = GameManager.CurrentPhase;

            if (!phaseKnown)
            {
                phaseKnown = true;
                lastPhase = phase;
                return;
            }

            if (phase == lastPhase)
            {
                return;
            }

            lastPhase = phase;

            // 진영마다 해야 할 일이 반대다. 퇴마사용 문구를 귀신에게 그대로 띄우면
            // "귀신을 퇴마하세요"를 귀신이 읽게 된다.
            bool isGhost = NetworkPlayer.GetLocal()?.IsGhost ?? false;

            // 은신은 <b>판이 시작될 때만이 아니라</b> 탐지에 성공할 때마다 돌아온다.
            // 그래서 첫 은신인지 재은신인지에 따라 문구가 달라야 한다.
            bool rehiding = (GameManager.Instance?.FoundWeaknesses.Count ?? 0) > 0;

            string t = phase switch
            {
                GamePhase.Hiding => isGhost
                    ? (rehiding ? "들켰습니다…\n다른 곳으로 자리를 옮기세요." : "몸을 숨기세요…")
                    : (rehiding ? "귀신이 자리를 옮기고 있습니다…" : "귀신이 숨고 있습니다…"),
                GamePhase.Investigation => isGhost
                    ? "조사 시간입니다…\n공포스킬로 퇴마를 저지하세요."
                    : "조사 시간입니다…\n올바른 퇴마 도구 3종을 찾아 제단에 바쳐 귀신을 퇴마하세요.",
                GamePhase.Hunt => isGhost
                    ? "사냥의 시간입니다…\n모든 퇴마사를 처치하세요."
                    : "이제 사냥이 시작됩니다…\n귀신으로부터 살아남으세요.",
                _ => null,
            };

            if (t != null)
            {
                Show(t);
            }
        }

        /// <summary>
        /// 약점이 밝혀지는 순간을 잡아 <b>전원에게</b> 공지한다.
        ///
        /// RPC를 따로 파지 않는다 — 목록이 이미 동기화되므로 각자 자기 쪽 증가를 보고
        /// 띄우면 그것으로 전원이 본다. 도중에 접속한 사람에게 옛 공지가 뜨지도 않는다.
        /// </summary>
        private void TickWeaknessFound()
        {
            var manager = GameManager.Instance;
            if (manager == null)
            {
                return;
            }

            int count = manager.FoundWeaknesses.Count;

            // 판이 바뀌면 목록이 비워진다. 그때는 새 공지가 아니라 초기화다.
            if (count == lastFoundCount)
            {
                return;
            }

            int previous = lastFoundCount;
            lastFoundCount = count;

            if (count <= previous || count == 0)
            {
                return;
            }

            var tool = (ToolType)manager.FoundWeaknesses[count - 1];
            int total = GameManager.Config != null ? GameManager.Config.WeaknessCount : 3;

            Show(count >= total
                ? $"{tool.ToKorean()}! 약점을 모두 밝혀냈습니다."
                : $"{tool.ToKorean()}(으)로 귀신을 찾아냈습니다!\n약점 {count}/{total} — 귀신이 자리를 옮깁니다.");
        }

        private void Show(string value)
        {
            announceText = value;
            announceTimer = Duration;
        }

        /// <summary>
        /// 페이드는 알파로만 준다. 들어올 때와 나갈 때 중 더 작은 값을 쓰면
        /// 양쪽이 자연스럽게 이어진다(예전 IMGUI 버전과 같은 계산).
        /// </summary>
        private void Apply()
        {
            if (announceTimer > 0f)
            {
                announceTimer -= Time.deltaTime;
            }

            bool visible = announceTimer > 0f && !string.IsNullOrEmpty(announceText);

            if (visible && text != null)
            {
                text.text = announceText;
            }

            if (group == null)
            {
                return;
            }

            if (!visible)
            {
                group.alpha = 0f;
                return;
            }

            float elapsed = Duration - announceTimer;
            group.alpha = Mathf.Clamp01(Mathf.Min(elapsed / Fade, announceTimer / Fade));
        }
    }
}
