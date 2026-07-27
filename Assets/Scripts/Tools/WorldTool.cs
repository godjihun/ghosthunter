using GhostHunter.Core;
using GhostHunter.Game;
using GhostHunter.Player;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Tools
{
    /// <summary>
    /// 맵 바닥에 놓여 있는 도구 하나. 퇴마사가 E 홀드로 주울 수 있다.
    ///
    /// 주우면 서버가 이 오브젝트를 디스폰하고 인벤토리에 종류만 기록한다.
    /// 버리면 서버가 다시 스폰한다. 부모 재설정보다 이 편이 NGO에서 단순하다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class WorldTool : NetworkBehaviour, IInteractable
    {
        public readonly NetworkVariable<ToolType> Type = new(
            ToolType.Camera,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [Tooltip("도구 종류별 외형. 인덱스는 ToolType 순서와 일치해야 한다.")]
        [SerializeField] private GameObject[] visualsByType;

        public override void OnNetworkSpawn()
        {
            ApplyVisual(Type.Value);
            Type.OnValueChanged += (_, next) => ApplyVisual(next);
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
            return inventory != null && !inventory.HasTool.Value;
        }

        public string GetPrompt(NetworkPlayer viewer)
        {
            // 귀신은 도구를 쓰지 않는다 — 프롬프트 자체를 띄우지 않는다.
            if (viewer == null || !viewer.IsExorcist)
            {
                return null;
            }

            var inventory = viewer.GetComponent<Exorcist.ExorcistInventory>();
            if (inventory != null && inventory.HasTool.Value)
            {
                return "이미 도구를 들고 있다";
            }

            return $"{Type.Value.ToKorean()} 줍기 (F)";
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
