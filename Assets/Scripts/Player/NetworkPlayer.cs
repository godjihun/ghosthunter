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

        // 흡수 여부는 더 이상 플레이어별로 추적하지 않는다.
        // 시나리오 3-4가 "전원 흡수"에서 <b>현실화 게이지</b>로 바뀌면서,
        // 같은 대상을 몇 번이든 다시 흡수할 수 있게 됐기 때문이다.
        // 진행도는 GameManager.MaterializeGauge 하나가 갖는다.

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

        /// <summary>
        /// 이 클라이언트가 쓸 닉네임. 접속 화면에서 입력받아 여기 담아두고,
        /// 플레이어가 스폰되는 순간 서버로 보낸다.
        ///
        /// <b>static인 이유</b>: 닉네임을 정하는 시점(접속 전)에는 아직 플레이어 오브젝트가 없다.
        /// </summary>
        public static string LocalNickname = string.Empty;

        /// <summary>표시용 이름. 비어 있으면 클라이언트 번호로 대신한다.</summary>
        public string DisplayName
        {
            get
            {
                var n = Nickname.Value.ToString();
                return string.IsNullOrWhiteSpace(n) ? $"플레이어 {OwnerClientId}" : n;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this))
            {
                All.Add(this);
            }

            // 닉네임은 클라이언트가 알고 서버가 소유하므로, 소유자가 한 번 올려준다.
            if (IsOwner && !string.IsNullOrWhiteSpace(LocalNickname))
            {
                SubmitNicknameRpc(LocalNickname);
            }
        }

        /// <summary>
        /// 닉네임 등록. <b>서버가 값을 쓴다</b> — 클라이언트가 직접 쓰면
        /// 남의 이름까지 바꿀 수 있고, 애초에 WritePermission이 Server다.
        /// </summary>
        [Rpc(SendTo.Server)]
        private void SubmitNicknameRpc(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
            {
                return;
            }

            // FixedString32Bytes를 넘치면 예외가 난다. 넉넉히 잘라 담는다.
            const int maxLength = 10;
            if (nickname.Length > maxLength)
            {
                nickname = nickname.Substring(0, maxLength);
            }

            Nickname.Value = nickname;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
        }

        /// <summary>
        /// 지정한 자리로 순간이동. <b>반드시 소유자 클라이언트에서 실행돼야 한다.</b>
        ///
        /// 위치 동기화가 소유자 권한(<see cref="ClientNetworkTransform"/>)이라,
        /// <b>서버가 transform을 직접 바꿔봐야 다음 프레임에 소유자가 보낸 좌표로 덮어써진다.</b>
        /// 호스트만 제자리로 가고 나머지는 그대로 있는 증상이 정확히 이것이다 —
        /// 호스트는 자기 캐릭터의 소유자이기도 해서 우연히 성공한 것뿐이다.
        ///
        /// 서버는 이 RPC를 부르기만 하고, 실제 이동은 각 소유자가 수행한다.
        /// </summary>
        [Rpc(SendTo.Owner)]
        public void TeleportRpc(Vector3 position, Quaternion rotation)
        {
            // CharacterController가 켜져 있으면 위치 대입이 무시된다.
            var cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
            }

            transform.SetPositionAndRotation(position, rotation);

            if (cc != null)
            {
                cc.enabled = true;
            }
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
