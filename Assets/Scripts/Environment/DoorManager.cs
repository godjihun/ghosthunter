using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Environment
{
    /// <summary>
    /// 저택 문 전체의 상태를 서버 권한으로 관리한다.
    ///
    /// 문 27개에 각각 NetworkObject를 달면 스폰 비용과 관리 지점이 27개로 늘어난다.
    /// 대신 <b>열림 상태를 비트 하나씩 담은 정수 하나</b>만 동기화한다 —
    /// 64개까지는 이 방식이 훨씬 싸고, 상태가 한 곳에 모여 있어 추적하기도 쉽다.
    /// </summary>
    public class DoorManager : NetworkBehaviour
    {
        public static DoorManager Instance { get; private set; }

        /// <summary>비트 i가 1이면 i번 문이 열려 있다.</summary>
        private readonly NetworkVariable<ulong> doorBits = new(
            0ul,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly List<DoorController> doors = new();

        public const int MaxDoors = 64;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            RefreshRegistry();
            doorBits.OnValueChanged += OnBitsChanged;
            ApplyAll(doorBits.Value);
        }

        public override void OnNetworkDespawn()
        {
            doorBits.OnValueChanged -= OnBitsChanged;
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            base.OnDestroy();
        }

        private void RefreshRegistry()
        {
            doors.Clear();
            doors.AddRange(FindObjectsByType<DoorController>(FindObjectsInactive.Include));
        }

        private void OnBitsChanged(ulong previous, ulong current) => ApplyAll(current);

        private void ApplyAll(ulong bits)
        {
            foreach (var door in doors)
            {
                if (door == null || door.DoorIndex < 0 || door.DoorIndex >= MaxDoors)
                {
                    continue;
                }

                door.SetOpen((bits & (1ul << door.DoorIndex)) != 0ul);
            }
        }

        /// <summary>
        /// 클라이언트가 문을 건드렸을 때 호출. 실제 판단은 서버가 한다.
        ///
        /// <b>내 화면에서는 즉시 돌린다.</b> 서버 왕복(Relay 2회)을 기다린 뒤 움직이면
        /// F를 눌러도 반응이 없는 것처럼 느껴진다.
        ///
        /// 문은 <b>누구나 열 수 있어 서버가 거절할 일이 없다</b> — 그래서 미리 돌려도
        /// 어긋날 위험이 사실상 없다. 설령 어긋나도 서버 값이 도착하는 순간
        /// <see cref="ApplyAll"/>이 덮어쓰므로 저절로 맞춰진다.
        /// </summary>
        public void RequestToggle(int doorIndex)
        {
            if (doorIndex < 0 || doorIndex >= MaxDoors)
            {
                return;
            }

            // 서버(호스트)는 아래 RPC가 곧바로 실행되므로 미리 돌릴 필요가 없다.
            // 두 번 뒤집으면 도로 닫힌다.
            if (!IsServer)
            {
                foreach (var door in doors)
                {
                    if (door != null && door.DoorIndex == doorIndex)
                    {
                        door.SetOpen(!door.IsOpen);
                        break;
                    }
                }
            }

            ToggleRpc(doorIndex);
        }

        [Rpc(SendTo.Server)]
        private void ToggleRpc(int doorIndex)
        {
            doorBits.Value ^= 1ul << doorIndex;
        }
    }
}
