using GhostHunter.Exorcist;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Tools
{
    /// <summary>
    /// 예전에 바닥에 탐지 반경을 원으로 그리던 표시. <b>지금은 아무것도 그리지 않는다.</b>
    ///
    /// 탐지가 <b>반경에서 시야로</b> 바뀌면서 이 원은 거짓말이 됐다 — 이제는 발밑 원 안에
    /// 있느냐가 아니라 도구를 쓴 순간 <b>화면 안에 들어왔느냐</b>로 판정한다
    /// (<see cref="DetectionJudge"/>). 원을 계속 그리면 "원 안으로 들어가면 잡힌다"고
    /// 잘못 배우게 되므로 지웠다.
    ///
    /// <b>컴포넌트를 없애지 않은 이유</b>: 플레이어 프리팹과
    /// <c>PlayerRoleSetup</c>이 이 타입을 참조하고 있다. 시야를 알려주는 표시를
    /// 새로 만들 때 이 자리를 그대로 쓰면 된다.
    /// </summary>
    [RequireComponent(typeof(ExorcistInventory))]
    public class DetectionRadiusView : NetworkBehaviour
    {
        [Tooltip("예전 원판 오브젝트. 남아 있으면 꺼둔다.")]
        [SerializeField] private Transform radiusVisual;

        public override void OnNetworkSpawn()
        {
            if (radiusVisual != null)
            {
                radiusVisual.gameObject.SetActive(false);
            }

            enabled = false;
        }
    }
}
