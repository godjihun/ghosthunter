using Unity.Netcode.Components;
using UnityEngine;

namespace GhostHunter.Player
{
    /// <summary>
    /// 소유자 권한 NetworkTransform.
    ///
    /// NGO 기본 <see cref="NetworkTransform"/>은 <b>서버 권한</b>이라, 클라이언트가 직접 움직이면
    /// 서버가 원래 위치로 되돌려 캐릭터가 제자리에서 튕긴다.
    /// 이 게임은 각 플레이어가 자기 캐릭터를 로컬에서 움직이므로 소유자 권한이어야 한다.
    ///
    /// 대가로 위치는 클라이언트를 신뢰하게 된다(속도 핵 등). 사전과제 범위에서는 감수한다 —
    /// 귀신 은닉은 데이터 차단이 아니라 <see cref="GhostVisibility"/>의 렌더러 제어로
    /// 바뀌었으므로(기술 문서 2-2-1), 어차피 위치는 전원에게 간다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
