using GhostHunter.Core;
using GhostHunter.Environment;
using GhostHunter.Game;
using GhostHunter.Player;
using GhostHunter.Tools;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Exorcist
{
    /// <summary>
    /// F 키 상호작용 (기술 문서 4-2, 6-2).
    ///
    /// 도구 줍기와 문 여닫기를 <see cref="IInteractable"/> 하나로 묶어 처리한다.
    /// 귀신도 문을 열 수 있어야 하므로 <b>양쪽 진영 모두</b> 이 컴포넌트를 쓴다.
    /// </summary>
    public class PlayerInteractor : NetworkBehaviour
    {
        [SerializeField] private float interactRange = 2.5f;

        [Tooltip("조준 판정의 관대함. 크면 대충 봐도 잡힌다.")]
        [SerializeField] private float aimRadius = 0.4f;

        [Tooltip("도구(Prop)와 문(Door)을 포함해야 한다.")]
        [SerializeField] private LayerMask interactMask;

        [Tooltip("비워두면 몸체 정면으로 판정한다.")]
        [SerializeField] private Transform rayOrigin;

        private NetworkPlayer player;
        private ExorcistInventory inventory;

        /// <summary>이번 프레임에 F가 눌렸는가. 한 번 누르면 딱 한 번만 실행된다.</summary>
        private bool interactQueued;

        /// <summary>지금 조준 중인 대상. 프롬프트 표시에 쓴다.</summary>
        public IInteractable Target { get; private set; }

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            inventory = GetComponent<ExorcistInventory>();
        }

        public override void OnNetworkSpawn()
        {
            enabled = IsOwner;
        }

        public void OnInteract(InputValue value)
        {
            // 누르는 순간만 받는다. 떼는 통지는 무시한다.
            if (value.isPressed)
            {
                interactQueued = true;
            }
        }

        private void Update()
        {
            // 은신 단계의 퇴마사는 도구를 만질 수 없다 (시나리오 4번 [3]).
            bool blocked = player != null && player.IsExorcist && !GameManager.ExorcistsCanAct;

            if (!IsOwner || !GameManager.IsGameplayActive || blocked)
            {
                Target = null;
                interactQueued = false;
                return;
            }

            Target = FindTarget();

            if (!interactQueued)
            {
                return;
            }

            // 눌린 입력은 성공하든 실패하든 여기서 소비한다.
            // 남겨두면 나중에 엉뚱한 대상 앞에서 저절로 발동한다.
            interactQueued = false;

            if (Target != null && Target.CanInteract(player))
            {
                Execute(Target);
            }
        }

        private void Execute(IInteractable target)
        {
            switch (target)
            {
                case WorldTool tool when inventory != null:
                    inventory.RequestPickup(tool.NetworkObject);
                    break;

                case DoorController door:
                    door.Interact(player);
                    break;

                case Altar altar:
                    altar.RequestOffer(player);
                    break;
            }
        }

        /// <summary>
        /// 조준한 대상을 찾는다.
        ///
        /// 얇은 레이캐스트만 쓰면 <b>바닥에 놓인 도구를 거의 못 집는다</b> — 눈높이(1.6m)에서
        /// 수평으로 쏜 광선이 높이 0.35m짜리 물건 위를 그냥 지나가기 때문이다.
        /// 그래서 정면 구체 캐스트로 먼저 보고, 실패하면 주변을 훑는다.
        /// </summary>
        private IInteractable FindTarget()
        {
            Transform origin = rayOrigin != null ? rayOrigin : transform;

            if (Physics.SphereCast(origin.position, aimRadius, origin.forward,
                    out var hit, interactRange, interactMask, QueryTriggerInteraction.Collide))
            {
                var aimed = hit.collider.GetComponentInParent<IInteractable>();
                if (aimed != null && ShouldShow(aimed))
                {
                    return aimed;
                }
            }

            var hits = Physics.OverlapSphere(transform.position + Vector3.up * 0.9f,
                interactRange, interactMask, QueryTriggerInteraction.Collide);

            IInteractable best = null;
            float bestScore = float.MaxValue;

            foreach (var col in hits)
            {
                var candidate = col.GetComponentInParent<IInteractable>();
                if (candidate == null || !ShouldShow(candidate))
                {
                    continue;
                }

                Vector3 to = candidate.PromptAnchor.position - origin.position;
                float distance = to.magnitude;
                if (distance > interactRange)
                {
                    continue;
                }

                // 정면일수록 점수가 낮다. 등 뒤의 물건이 선택되지 않게 한다.
                float facing = Vector3.Dot(origin.forward, to.normalized);
                if (facing < 0.1f)
                {
                    continue;
                }

                float score = distance * (2f - facing);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// 프롬프트를 띄울 가치가 있는가.
        ///
        /// 판단은 대상 자신에게 맡긴다 — <c>GetPrompt</c>가 null이면 제외다.
        /// 여기서 타입을 나열하면 대상이 늘 때마다 이 함수를 고쳐야 한다.
        /// "이미 도구를 들고 있다"처럼 <b>못 쓰는 이유는 띄운다</b> — 안 뜨면 고장인 줄 안다.
        /// </summary>
        private bool ShouldShow(IInteractable target) => target.GetPrompt(player) != null;
    }
}
