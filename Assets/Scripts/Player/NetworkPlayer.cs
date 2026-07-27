using System.Collections.Generic;
using GhostHunter.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Player
{
    /// <summary>
    /// 플레이어 한 명의 네트워크 상태. 플레이어 프리팹에 붙는다.
    ///
    /// 공개 정보(진영·생존·흡수 여부)는 전체 공개 NetworkVariable로,
    /// 비밀 정보(약점 3종)는 ReadPermission.Owner로 담는다 — 기술 문서 2-3의 분류표 참고.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPlayer : NetworkBehaviour
    {
        /// <summary>스폰된 모든 플레이어. 서버·클라이언트 양쪽에서 유지된다.</summary>
        public static readonly List<NetworkPlayer> All = new();

        // ── 공개 상태 ──────────────────────────────────────────────

        public readonly NetworkVariable<Faction> Faction = new(
            Core.Faction.Unassigned,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>사망 여부. 흡수와는 완전히 별개다 (시나리오 3-4).</summary>
        public readonly NetworkVariable<bool> IsAlive = new(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// 영혼을 흡수당했는지. 흡수는 죽음이 아니라 귀신의 현실화 진행도일 뿐이므로,
        /// 이 값이 true여도 조작·도구 사용·제단 헌납이 전부 그대로 가능해야 한다.
        /// </summary>
        public readonly NetworkVariable<bool> IsAbsorbed = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<FixedString32Bytes> Nickname = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // ── 비밀 상태 ──────────────────────────────────────────────

        /// <summary>
        /// 귀신의 약점 3종. ReadPermission.Owner이므로 <b>소유자와 서버만</b> 읽는다.
        /// 퇴마사 클라이언트에는 전송조차 되지 않는다 (기술 문서 2-2).
        /// 퇴마사에게는 배정되지 않은 채로 남는다.
        /// </summary>
        public readonly NetworkVariable<WeaknessSet> Weakness = new(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        // ── 편의 속성 ──────────────────────────────────────────────

        public bool IsGhost => Faction.Value == Core.Faction.Ghost;
        public bool IsExorcist => Faction.Value == Core.Faction.Exorcist;

        /// <summary>관전 대상이 될 수 있는가 = 살아있는 퇴마사인가 (기술 문서 5-6).</summary>
        public bool IsSpectatable => IsExorcist && IsAlive.Value;

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this))
            {
                All.Add(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
        }

        /// <summary>살아있는 퇴마사 목록. 서버 판정과 관전 목록 양쪽에서 쓴다.</summary>
        public static List<NetworkPlayer> GetLivingExorcists()
        {
            var result = new List<NetworkPlayer>();
            foreach (var p in All)
            {
                if (p != null && p.IsExorcist && p.IsAlive.Value)
                {
                    result.Add(p);
                }
            }
            return result;
        }

        public static NetworkPlayer GetGhost()
        {
            foreach (var p in All)
            {
                if (p != null && p.IsGhost)
                {
                    return p;
                }
            }
            return null;
        }

        /// <summary>내 클라이언트가 조종하는 플레이어.</summary>
        public static NetworkPlayer GetLocal()
        {
            foreach (var p in All)
            {
                if (p != null && p.IsOwner)
                {
                    return p;
                }
            }
            return null;
        }
    }
}
