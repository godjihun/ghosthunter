using GhostHunter.Core;
using GhostHunter.Game;
using GhostHunter.Player;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Tools
{
    /// <summary>
    /// 맵 바닥에 놓여 있는 도구 하나. 퇴마사가 F로 주울 수 있다.
    ///
    /// 주우면 서버가 이 오브젝트를 디스폰하고 인벤토리에 종류를 기록한다.
    /// 버리면 서버가 다시 스폰한다. 부모 재설정보다 이 편이 NGO에서 단순하다.
    ///
    /// <b>도구는 소모되지 않는다.</b> 종류당 저택에 하나뿐이고, 쓰면 쿨타임이 돈다.
    /// 그 쿨타임은 <b>사람이 아니라 도구에 붙어</b> 있어서, 버리거나 남에게 넘어가거나
    /// 제단에 들어갔다 나와도 그대로 이어진다 — 그래야 "쿨타임 도는 도구를 넘기고
    /// 새 걸 받는" 식으로 규칙을 우회할 수 없다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class WorldTool : NetworkBehaviour, IInteractable
    {
        public readonly NetworkVariable<ToolType> Type = new(
            ToolType.Camera,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>남은 쿨타임(초). 0이면 바로 쓸 수 있다.</summary>
        public readonly NetworkVariable<float> Cooldown = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [Tooltip("도구 종류별 외형. 인덱스는 ToolType 순서와 일치해야 한다.")]
        [SerializeField] private GameObject[] visualsByType;

        public override void OnNetworkSpawn()
        {
            ApplyVisual(Type.Value);
            Type.OnValueChanged += (_, next) => ApplyVisual(next);
        }

        private void Update()
        {
            // 바닥에 놓여 있는 동안에도 쿨타임은 돈다. 서버만 깎는다.
            if (IsServer && Cooldown.Value > 0f)
            {
                Cooldown.Value = Mathf.Max(0f, Cooldown.Value - Time.deltaTime);
            }
        }

        private void ApplyVisual(ToolType type)
        {
            if (visualsByType == null || visualsByType.Length == 0)
            {
                return;
            }

            for (int i = 0; i < visualsByType.Length; i++)
            {
                if (visualsByType[i] != null)
                {
                    visualsByType[i].SetActive(i == (int)type);
                }
            }
        }

        // ── IInteractable ──────────────────────────────────────────

        public Transform PromptAnchor => transform;

        /// <summary>도구는 퇴마사만 다룬다. 귀신에게는 프롬프트도 띄우지 않는다.</summary>
        public bool CanInteract(NetworkPlayer viewer)
        {
            if (viewer == null || !viewer.IsExorcist)
            {
                return false;
            }

            var inventory = viewer.GetComponent<Exorcist.ExorcistInventory>();
            return inventory != null && inventory.SelectedIsEmpty;
        }

        public string GetPrompt(NetworkPlayer viewer)
        {
            // 귀신은 도구를 쓰지 않는다 — 프롬프트 자체를 띄우지 않는다.
            if (viewer == null || !viewer.IsExorcist)
            {
                return null;
            }

            var inventory = viewer.GetComponent<Exorcist.ExorcistInventory>();
            if (inventory != null && !inventory.SelectedIsEmpty)
            {
                // 칸이 고정이라 "고른 칸"에 들어간다. 어느 칸인지 본인이 정해야 한다.
                return inventory.HasSpace
                    ? "빈 칸을 고르세요 (1~4)"
                    : "손이 가득 찼다 (G로 내려놓기)";
            }

            // 쿨타임이 남아 있으면 주울 수는 있지만 바로 못 쓴다. 미리 알려준다.
            string cooldown = Cooldown.Value > 0.05f ? $" — 쿨타임 {Mathf.CeilToInt(Cooldown.Value)}초" : "";
            return $"{Type.Value.ToKorean()} 줍기 (F){cooldown}";
        }

        /// <summary>서버 전용. 도구를 월드에서 제거한다.</summary>
        public void ServerConsume()
        {
            if (!IsServer)
            {
                return;
            }

            NetworkObject.Despawn(true);
        }
    }
}
