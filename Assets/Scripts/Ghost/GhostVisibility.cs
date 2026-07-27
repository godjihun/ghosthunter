using GhostHunter.Core;
using GhostHunter.Game;
using GhostHunter.Player;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Ghost
{
    /// <summary>
    /// 귀신의 투명/현실화 처리 (시나리오 2, 4번 [5]).
    ///
    /// <b>이 프로젝트는 렌더러를 끄는 방식을 택했다.</b> 원래 기술 문서 2-2는
    /// <c>NetworkObject.NetworkHide</c>로 데이터 자체를 안 보내는 설계였지만,
    /// <b>서버 자신에게는 NetworkHide를 쓸 수 없다</b> — 호스트가 곧 서버이므로
    /// 호스트가 퇴마사로 배정되면 귀신이 그대로 보였다.
    ///
    /// 사전과제라 개발자도구 수준의 치팅은 고려하지 않기로 했고, 그 대신
    /// <b>호스트와 클라이언트의 동작이 완전히 같아지는</b> 이점을 얻었다.
    /// 전용 서버를 도입하게 되면 그때 NetworkHide를 다시 얹으면 된다.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class GhostVisibility : NetworkBehaviour
    {
        private NetworkPlayer player;
        private Renderer[] renderers = System.Array.Empty<Renderer>();
        private bool? applied;

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
        }

        public override void OnNetworkSpawn()
        {
            player.Faction.OnValueChanged += OnFactionChanged;
            RefreshRenderers();
        }

        public override void OnNetworkDespawn()
        {
            if (player != null)
            {
                player.Faction.OnValueChanged -= OnFactionChanged;
            }
        }

        private void OnFactionChanged(Faction previous, Faction current)
        {
            // 진영이 바뀌면 켜져 있는 모델이 달라진다. 캐시를 다시 잡고 판단도 리셋한다.
            RefreshRenderers();
            applied = null;
        }

        private void RefreshRenderers()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>
        /// PlayerRoleSetup이 모델을 켜고 끈 뒤에 판단해야 하므로 LateUpdate에서 처리한다.
        /// </summary>
        private void LateUpdate()
        {
            // 내 화면 속 내 몸은 PlayerRoleSetup이 그림자만 남기도록 이미 처리했다.
            // 여기서 또 만지면 서로 덮어써서 원인을 못 찾게 된다.
            if (IsOwner || !player.IsGhost)
            {
                return;
            }

            // 현실화 = 사냥 단계. 그 전까지 귀신은 남의 화면에 존재하지 않는다.
            bool visible = GameManager.CurrentPhase == GamePhase.Hunt;

            if (applied == visible)
            {
                return;
            }

            applied = visible;
            foreach (var r in renderers)
            {
                if (r != null)
                {
                    r.enabled = visible;
                }
            }
        }
    }
}
