using GhostHunter.Core;
using GhostHunter.Game;
using GhostHunter.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Ghost
{
    /// <summary>
    /// 귀신의 Ctrl 능력. <b>단계에 따라 하는 일이 다르다.</b>
    ///
    ///  - <b>조사 단계</b>: 공포스킬 = 영혼 흡수 (시나리오 3-4). 영혼 상태에서만.
    ///    <b>흡수는 죽이는 게 아니다.</b> 현실화를 위한 진행도일 뿐이며,
    ///    흡수당한 퇴마사는 조작·도구 사용·제단 헌납이 전부 그대로 가능하다.
    ///  - <b>사냥 단계</b>: 처형 = 실제 사망 (시나리오 5번). 현실화한 본체로 직접 잡는다.
    ///
    /// 사거리는 어몽어스의 칼처럼 밀착 거리(1.5m)이고, 벽·문 너머로는 걸리지 않는다.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class FearSkill : NetworkBehaviour
    {
        [Tooltip("공포스킬을 막는 레이어. 벽·바닥·문을 포함하되 가구·소품은 제외한다. " +
                 "영혼의 이동 마스크와 다르다 — 이동은 문을 통과하지만 스킬은 문에 막힌다.")]
        [SerializeField] private LayerMask blockingMask;

        /// <summary>
        /// 남은 쿨타임. 0이면 사용 가능. <b>서버가 소유하고 귀신에게만 보인다.</b>
        ///
        /// 일반 필드로 두면 서버가 값을 넣어도 <b>귀신 클라이언트의 값은 0인 채로 남아</b>
        /// 무한히 연타할 수 있다. 호스트만 쿨타임이 걸리는 이유가 이것이다 —
        /// 호스트는 서버이면서 소유자라 같은 변수를 보기 때문이다.
        ///
        /// 퇴마사는 귀신의 쿨타임을 알 이유가 없으므로 ReadPermission은 Owner.
        /// </summary>
        private readonly NetworkVariable<float> cooldown = new(
            0f,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        public float CooldownRemaining => cooldown.Value;

        /// <summary>사거리 안에 대상이 있는가. UI 버튼 활성화에 쓴다 (기술 문서 5-4).</summary>
        public bool HasTargetInRange { get; private set; }

        /// <summary>지금 흡수할 수 있는 상대. 프롬프트를 그 사람 위에 띄우기 위해 노출한다.</summary>
        public NetworkPlayer CurrentTarget { get; private set; }

        public bool CanUse => CooldownRemaining <= 0f && HasTargetInRange;

        private NetworkPlayer player;
        private GhostController ghost;

        private GameConfig Config => GameManager.Config;

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            ghost = GetComponent<GhostController>();
        }

        public override void OnNetworkSpawn()
        {
            if (!player.IsGhost)
            {
                enabled = false;
            }
        }

        public void OnFearSkill(InputValue value)
        {
            if (!IsOwner || !value.isPressed || !CanUse)
            {
                return;
            }

            UseFearSkillRpc();
        }

        private void Update()
        {
            // 쿨타임은 <b>서버만</b> 깎는다. 클라이언트가 각자 깎으면 서로 어긋나고,
            // 애초에 서버가 세지 않으면 아무도 막을 수 없다.
            if (IsServer && cooldown.Value > 0f)
            {
                cooldown.Value = Mathf.Max(0f, cooldown.Value - Time.deltaTime);
            }

            if (IsOwner)
            {
                // 로컬 미리보기. 실제 판정은 서버가 다시 한다.
                CurrentTarget = FindTarget();
                HasTargetInRange = CurrentTarget != null;
            }
        }

        /// <summary>사냥 단계인가. 이 값에 따라 Ctrl이 흡수가 되기도 처형이 되기도 한다.</summary>
        public static bool IsHuntPhase => GameManager.CurrentPhase == GamePhase.Hunt;

        /// <summary>
        /// 쿨타임을 즉시 0으로 만든다. 서버 전용.
        ///
        /// <b>단계가 바뀌면 초기화해야 한다.</b> 흡수 쿨타임(30초)과 처형 쿨타임(3초)은
        /// 길이가 열 배 차이라, 흡수를 쓴 직후 사냥에 들어가면 <b>사냥 1분 중 절반을
        /// 아무것도 못 하고 흘려보낸다.</b> 같은 칸에 다른 능력이 들어앉는 이상
        /// 쿨타임도 그 능력의 것이어야 한다.
        /// </summary>
        public void ServerResetCooldown()
        {
            if (IsServer)
            {
                cooldown.Value = 0f;
            }
        }

        [Rpc(SendTo.Server)]
        private void UseFearSkillRpc()
        {
            // <b>서버가 최종 판정한다.</b> 클라이언트의 CanUse는 버튼을 흐리게 만드는 용도일 뿐,
            // 여기서 다시 막지 않으면 연타로 그대로 통과한다.
            if (cooldown.Value > 0f)
            {
                return;
            }

            switch (GameManager.CurrentPhase)
            {
                case GamePhase.Investigation:
                    ServerAbsorb();
                    break;

                case GamePhase.Hunt:
                    ServerKill();
                    break;
            }
        }

        /// <summary>조사 단계: 영혼 흡수. 죽이지 않는다.</summary>
        private void ServerAbsorb()
        {
            if (ghost != null && !ghost.IsSoulOut.Value)
            {
                return; // 영혼 상태에서만 쓸 수 있다
            }

            var target = FindTarget();
            if (target == null)
            {
                return;
            }

            // 흡수 연출은 당한 본인에게만. 남이 흡수당한 걸 실시간으로 알면
            // 그게 곧 귀신의 위치 제보가 된다 (기술 문서 6-2).
            AbsorbedRpc(RpcTarget.Single(target.OwnerClientId, RpcTargetUse.Temp));

            cooldown.Value = Config != null ? Config.FearSkillCooldown : 30f;

            // 흡수 성공 = 현실화 게이지 충전 (시나리오 3-4).
            float gain = Config != null ? Config.AbsorbGaugePerHit : 20f;
            GameManager.Instance?.ServerAddMaterializeGauge(gain);
        }

        /// <summary>
        /// 사냥 단계: 처형. 실제로 죽인다 (시나리오 5번).
        ///
        /// 현실화한 본체로 잡는 것이라 영혼 상태를 요구하지 않는다.
        /// </summary>
        private void ServerKill()
        {
            var target = FindTarget();
            if (target == null)
            {
                return;
            }

            target.IsAlive.Value = false;
            KilledRpc(RpcTarget.Single(target.OwnerClientId, RpcTargetUse.Temp));

            cooldown.Value = Config != null ? Config.HuntKillCooldown : 3f;

            // 전멸했으면 귀신 승리로 끝난다.
            GameManager.Instance?.OnExorcistDied();
        }

        /// <summary>흡수당한 본인에게만 가는 공포 연출.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void AbsorbedRpc(RpcParams rpcParams = default)
        {
            // TODO: 화면 효과 + 공포음 재생 (기술 문서 6-2)
            Debug.Log("[Exorcist] 영혼을 흡수당했다! (계속 플레이 가능)");
        }

        /// <summary>사냥 단계에서 죽은 본인에게만 가는 통지.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void KilledRpc(RpcParams rpcParams = default)
        {
            // TODO: 사망 연출 + 관전 전환 (시나리오 3-6)
            Debug.Log("[Exorcist] 귀신에게 잡혔다.");
        }

        /// <summary>
        /// 밀착 거리 안에 있고 <b>사이가 막혀 있지 않은</b> 퇴마사를 찾는다.
        ///
        /// 거리만 재면 얇은 벽이나 닫힌 문을 사이에 두고 붙어서 흡수할 수 있게 되어
        /// 영혼이 벽을 통과하지 못하게 만든 의미가 사라진다.
        /// </summary>
        private NetworkPlayer FindTarget()
        {
            var config = Config;
            float range = config != null ? config.FearSkillRange : 1.5f;

            NetworkPlayer best = null;
            float bestDistance = float.MaxValue;

            foreach (var candidate in NetworkPlayer.GetLivingExorcists())
            {
                Vector3 from = transform.position + Vector3.up * 1f;
                Vector3 to = candidate.transform.position + Vector3.up * 1f;
                float distance = Vector3.Distance(from, to);

                if (distance > range || distance >= bestDistance)
                {
                    continue;
                }

                // 벽·문에 막혀 있으면 실패.
                if (Physics.Linecast(from, to, blockingMask, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                best = candidate;
                bestDistance = distance;
            }

            return best;
        }
    }
}
