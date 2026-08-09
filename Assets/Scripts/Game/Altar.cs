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

        /// <summary>
        /// 제단 바구니에 들어 있는 도구들. 서로 다른 3종이 모이면 판정한다.
        ///
        /// <b>도구는 소모되지 않는다.</b> 넣었다가 F로 다시 꺼낼 수 있고, 틀린 조합으로
        /// 판정이 끝난 뒤에도 그대로 남아 회수할 수 있다.
        /// </summary>
        public readonly NetworkList<int> OfferedTools = new(
            new List<int>(),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>바구니 속 도구들의 남은 쿨타임. <see cref="OfferedTools"/>와 자리가 맞물린다.</summary>
        private readonly NetworkList<float> offeredCooldowns = new(
            new List<float>(),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// 지금 담긴 조합으로 이미 판정을 마쳤는가.
        ///
        /// <b>틀려도 도구가 남기 때문에</b> 이 표시가 없으면 3개가 담긴 상태 그대로
        /// 매 프레임 다시 판정해 퇴마사가 순식간에 전멸한다. 내용이 바뀌면 풀린다.
        /// </summary>
        private bool judgedCurrent;

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
            return inventory != null && (CanPlace(inventory) || CanRetrieve(inventory));
        }

        /// <summary>넣을 수 있는가 — 손에 도구가 있고, 같은 종류가 아직 없고, 자리가 남았을 때.</summary>
        private bool CanPlace(ExorcistInventory inventory)
        {
            int capacity = GameManager.Config != null ? GameManager.Config.AltarCapacity : 3;
            return OfferedTools.Count < capacity
                && inventory.TryGetSelected(out var type, out _)
                && !ContainsType(type);
        }

        /// <summary>
        /// 회수할 수 있는가 — 바구니에 뭔가 있고 <b>고른 칸이 비어 있을 때</b>.
        ///
        /// 칸이 고정이라 회수한 도구는 지금 고른 칸에 들어간다. 그래서 넣기와 회수가
        /// 한 키로 갈려도 헷갈리지 않는다 — 도구를 든 칸이면 넣기, 빈 칸이면 회수다.
        /// </summary>
        private bool CanRetrieve(ExorcistInventory inventory)
            => OfferedTools.Count > 0 && inventory.SelectedIsEmpty;

        public string GetPrompt(NetworkPlayer viewer)
        {
            // 제단은 퇴마사의 것이다. 귀신에게는 띄우지 않는다.
            if (viewer == null || !viewer.IsExorcist)
            {
                return null;
            }

            var inventory = viewer.GetComponent<ExorcistInventory>();
            if (inventory == null)
            {
                return null;
            }

            // 넣기가 우선이다. 손에 든 것을 올려놓는 게 기본 동작이고,
            // 회수는 "더 넣을 게 없을 때" 자연스럽게 이어진다.
            if (CanPlace(inventory))
            {
                inventory.TryGetSelected(out var type, out _);
                return $"{type.ToKorean()} 올려놓기 (F)";
            }

            if (CanRetrieve(inventory))
            {
                var last = (ToolType)OfferedTools[OfferedTools.Count - 1];
                return $"{last.ToKorean()} 회수 (F)";
            }

            if (inventory.TryGetSelected(out var held, out _) && ContainsType(held))
            {
                // 시나리오 3-3은 "서로 다른 3종"을 요구한다.
                return $"{held.ToKorean()}은(는) 이미 올려놨다";
            }

            if (OfferedTools.Count > 0 && !inventory.SelectedIsEmpty)
            {
                return "회수하려면 빈 칸을 고르세요 (1~4)";
            }

            if (OfferedTools.Count == 0)
            {
                return "올려놓을 도구가 없다";
            }

            return "제단이 가득 찼다";
        }

        // ── 헌납 ───────────────────────────────────────────────────

        /// <summary>퇴마사가 제단을 F로 건드렸다. 소유자 클라이언트에서 호출.</summary>
        public void RequestOffer(NetworkPlayer player)
        {
            if (player == null || !player.IsOwner)
            {
                return;
            }

            OfferRpc(player.OwnerClientId);
        }

        /// <summary>
        /// F 한 번에 <b>넣기 또는 회수</b>가 일어난다.
        ///
        /// 키를 나누지 않은 이유는, 제단 앞에서 하고 싶은 일이 상황에 따라 하나로 정해지기
        /// 때문이다 — 넣을 게 있으면 넣고, 없으면 꺼낸다. 두 키로 나누면 안내 문구만 늘고
        /// 실제로 헷갈릴 일은 없다.
        /// </summary>
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
            if (inventory == null)
            {
                return;
            }

            // 제단 근처에 있어야 한다. 클라이언트 말을 믿지 않고 서버가 거리를 잰다.
            if (Vector3.Distance(player.transform.position, transform.position) > offerRange)
            {
                return;
            }

            if (CanPlace(inventory))
            {
                ServerPlace(inventory);
                return;
            }

            if (CanRetrieve(inventory))
            {
                ServerRetrieveLast(inventory);
            }
        }

        private void ServerPlace(ExorcistInventory inventory)
        {
            if (!inventory.ServerTakeSelected(out var type, out float cooldown))
            {
                return;
            }

            OfferedTools.Add((int)type);
            offeredCooldowns.Add(cooldown);

            // 내용이 바뀌었으니 다시 판정할 수 있다.
            judgedCurrent = false;

            var config = GameManager.Config;
            int capacity = config != null ? config.AltarCapacity : 3;
            if (OfferedTools.Count >= capacity && !judgedCurrent)
            {
                judgedCurrent = true;
                JudgeOnServer();
            }
        }

        private void ServerRetrieveLast(ExorcistInventory inventory)
        {
            int last = OfferedTools.Count - 1;
            var type = (ToolType)OfferedTools[last];
            float cooldown = last < offeredCooldowns.Count ? offeredCooldowns[last] : 0f;

            if (!inventory.ServerPutInSelected(type, cooldown))
            {
                return;
            }

            OfferedTools.RemoveAt(last);
            if (last < offeredCooldowns.Count)
            {
                offeredCooldowns.RemoveAt(last);
            }

            // 조합이 달라졌으므로 다음에 3개가 다시 모이면 새로 판정한다.
            judgedCurrent = false;
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

            // 틀린 조합 → 무작위 1명 사망. <b>도구는 그대로 둔다</b> —
            // 종류당 하나뿐이라 여기서 없애면 그 도구가 판에서 영영 사라진다.
            // 남은 사람이 F로 회수해 다른 조합을 시도하면 된다.
            KillRandomExorcist();
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

        /// <summary>판을 새로 시작할 때만 비운다. 판정 실패로는 비우지 않는다.</summary>
        public void ServerClear()
        {
            if (!IsServer)
            {
                return;
            }

            OfferedTools.Clear();
            offeredCooldowns.Clear();
            judgedCurrent = false;
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
