using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Exorcist;
using GhostHunter.Player;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Game
{
    /// <summary>
    /// 지하 제단 (시나리오 3-3 / 기술 문서 5-3).
    ///
    /// 헌납 목록은 전원 공개다 — 우측 UI에 표시되어야 하므로 숨길 이유가 없다.
    /// 다만 <b>정답 여부 판정은 서버만</b> 한다. 판정에 약점 3종이 필요하기 때문이다.
    /// </summary>
    public class Altar : NetworkBehaviour, IInteractable
    {
        public static Altar Instance { get; private set; }

        [SerializeField] private float offerRange = 3f;

        [Tooltip("헌납 프롬프트를 띄울 위치. 비우면 제단의 원점(= 바닥)에 뜬다.")]
        [SerializeField] private Transform promptAnchor;

        /// <summary>제단에 헌납된 도구들. 서로 다른 3종이 모이면 판정한다.</summary>
        public readonly NetworkList<int> OfferedTools = new(
            new List<int>(),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>판정 결과가 나왔을 때. (정답 여부) — UI 연출용.</summary>
        public event System.Action<bool> OnJudged;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            OfferedTools.OnListChanged += OnOfferedChanged;
        }

        public override void OnNetworkDespawn()
        {
            OfferedTools.OnListChanged -= OnOfferedChanged;
        }

        /// <summary>
        /// 헌납 소리. <b>RPC를 따로 보내지 않는다</b> — 목록이 이미 전원에게 동기화되므로
        /// 각자 자기 쪽 변화를 보고 재생하면 그것으로 전원이 듣는다.
        ///
        /// 판정 실패로 목록을 비울 때(<see cref="ServerClear"/>)도 이 콜백이 오므로
        /// <b>추가된 경우만</b> 걸러낸다.
        /// </summary>
        private void OnOfferedChanged(NetworkListEvent<int> change)
        {
            if (change.Type == NetworkListEvent<int>.EventType.Add)
            {
                Audio.GameAudio.PlayAltarOffer();
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            base.OnDestroy();
        }

        /// <summary>현재 헌납된 도구 종류 목록. UI가 읽는다.</summary>
        public List<ToolType> GetOffered()
        {
            var result = new List<ToolType>();
            foreach (int raw in OfferedTools)
            {
                result.Add((ToolType)raw);
            }
            return result;
        }

        public bool ContainsType(ToolType type)
        {
            foreach (int raw in OfferedTools)
            {
                if ((ToolType)raw == type)
                {
                    return true;
                }
            }
            return false;
        }

        // ── IInteractable ──────────────────────────────────────────

        /// <summary>
        /// 제단은 원점이 <b>바닥</b>이라 그대로 쓰면 프롬프트가 발밑에 뜬다.
        /// 게다가 사거리(2.5m)를 눈높이에서 재므로, 바닥을 기준으로 잡으면
        /// 수직 1.6m가 사거리를 통째로 갉아먹어 <b>제단에 붙어야 프롬프트가 뜬다.</b>
        /// </summary>
        public Transform PromptAnchor => promptAnchor != null ? promptAnchor : transform;

        public bool CanInteract(NetworkPlayer viewer)
        {
            if (viewer == null || !viewer.IsExorcist || !viewer.IsAlive.Value)
            {
                return false;
            }

            var inventory = viewer.GetComponent<ExorcistInventory>();
            if (inventory == null || !inventory.HasTool.Value)
            {
                return false;
            }

            return !ContainsType(inventory.HeldTool.Value);
        }

        public string GetPrompt(NetworkPlayer viewer)
        {
            // 제단은 퇴마사의 것이다. 귀신에게는 띄우지 않는다.
            if (viewer == null || !viewer.IsExorcist)
            {
                return null;
            }

            var inventory = viewer.GetComponent<ExorcistInventory>();
            if (inventory == null || !inventory.HasTool.Value)
            {
                return "헌납할 도구가 없다";
            }

            var type = inventory.HeldTool.Value;
            if (ContainsType(type))
            {
                // 시나리오 3-3은 "서로 다른 3종"을 요구한다. 같은 걸 또 넣으면 판정이 성립하지 않는다.
                return $"{type.ToKorean()}은(는) 이미 헌납했다";
            }

            return $"{type.ToKorean()} 헌납 (F)";
        }

        // ── 헌납 ───────────────────────────────────────────────────

        /// <summary>퇴마사가 제단 앞에서 헌납을 시도한다. 소유자 클라이언트에서 호출.</summary>
        public void RequestOffer(NetworkPlayer player)
        {
            if (player == null || !player.IsOwner)
            {
                return;
            }

            OfferRpc(player.OwnerClientId);
        }

        [Rpc(SendTo.Server)]
        private void OfferRpc(ulong clientId)
        {
            if (GameManager.CurrentPhase != GamePhase.Investigation)
            {
                return;
            }

            var player = FindPlayer(clientId);
            if (player == null || !player.IsAlive.Value || !player.IsExorcist)
            {
                return;
            }

            var inventory = player.GetComponent<ExorcistInventory>();
            if (inventory == null || !inventory.HasTool.Value)
            {
                return;
            }

            // 제단 근처에 있어야 한다. 클라이언트 말을 믿지 않고 서버가 거리를 잰다.
            if (Vector3.Distance(player.transform.position, transform.position) > offerRange)
            {
                return;
            }

            var type = inventory.HeldTool.Value;

            // 시나리오 3-3: "서로 다른 도구 3종류"가 모여야 판정한다.
            // 이미 있는 종류를 또 넣는 건 의미가 없으므로 막는다.
            if (ContainsType(type))
            {
                return;
            }

            inventory.ServerConsumeHeldTool();
            OfferedTools.Add((int)type);

            var config = GameManager.Config;
            int capacity = config != null ? config.AltarCapacity : 3;
            if (OfferedTools.Count >= capacity)
            {
                JudgeOnServer();
            }
        }

        private void JudgeOnServer()
        {
            var manager = GameManager.Instance;
            if (manager == null)
            {
                return;
            }

            bool correct = manager.ServerWeakness.Matches(GetOffered());
            JudgedRpc(correct);

            if (correct)
            {
                // 시나리오 5번: 올바른 3종 헌납 → 퇴마사 승리
                manager.EndGame(GameResult.ExorcistWin);
                return;
            }

            // 틀린 조합 → 무작위 1명 사망, 제단 초기화, 게임은 계속된다.
            KillRandomExorcist();
            ServerClear();
        }

        private void KillRandomExorcist()
        {
            var living = NetworkPlayer.GetLivingExorcists();
            if (living.Count == 0)
            {
                return;
            }

            var victim = living[Random.Range(0, living.Count)];
            victim.IsAlive.Value = false;

            GameManager.Instance?.OnExorcistDied();
        }

        public void ServerClear()
        {
            if (!IsServer)
            {
                return;
            }

            OfferedTools.Clear();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void JudgedRpc(bool correct)
        {
            OnJudged?.Invoke(correct);
        }

        private static NetworkPlayer FindPlayer(ulong clientId)
        {
            foreach (var p in NetworkPlayer.All)
            {
                if (p != null && p.OwnerClientId == clientId)
                {
                    return p;
                }
            }
            return null;
        }
    }
}
