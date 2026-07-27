using GhostHunter.Player;
using UnityEngine;

namespace GhostHunter.Core
{
    /// <summary>
    /// F 키로 상호작용할 수 있는 대상 (기술 문서 4-2).
    ///
    /// 도구 줍기와 문 여닫기가 같은 키를 쓰므로, 조준·프롬프트·실행을 한 경로로 묶는다.
    /// <b>모든 상호작용은 누르는 즉시 실행된다</b> — 좌클릭으로 도구를 쓰는 것과 감각을 맞춘다.
    /// 진영마다 할 수 있는 일이 다르므로 <see cref="CanInteract"/>가 보는 사람을 인자로 받는다 —
    /// 예를 들어 도구는 퇴마사만 주울 수 있지만 문은 양쪽 다 연다.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>프롬프트를 띄울 월드 위치.</summary>
        Transform PromptAnchor { get; }

        /// <summary>
        /// "줍기 (F)"처럼 화면에 띄울 문구.
        ///
        /// <b>null을 반환하면 이 사람에게는 대상으로 잡히지 않는다.</b>
        /// 귀신에게 도구·제단 프롬프트를 띄워봐야 화면만 어지럽기 때문이다.
        /// 진영 분기를 여기 한 곳에 모아두면 조준 로직이 타입을 몰라도 된다.
        /// </summary>
        string GetPrompt(NetworkPlayer viewer);

        /// <summary>이 사람이 지금 이걸 쓸 수 있는가. false여도 프롬프트는 띄운다(이유 안내용).</summary>
        bool CanInteract(NetworkPlayer viewer);
    }
}
