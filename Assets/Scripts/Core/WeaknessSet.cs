using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace GhostHunter.Core
{
    /// <summary>
    /// 귀신의 약점 도구 3종. 이 게임에서 가장 중요한 비밀이다.
    ///
    /// 서버가 생성하고, 귀신의 NetworkObject에 ReadPermission.Owner인 NetworkVariable로 담는다.
    /// → 서버와 귀신 본인만 읽을 수 있고 퇴마사 클라이언트에는 전송조차 되지 않는다 (기술 문서 2-2).
    ///
    /// NetworkVariable에 담으려면 INetworkSerializable + IEquatable이 필요하다.
    /// </summary>
    [Serializable]
    public struct WeaknessSet : INetworkSerializable, IEquatable<WeaknessSet>
    {
        public ToolType A;
        public ToolType B;
        public ToolType C;
        public bool Assigned;

        public static WeaknessSet CreateRandom(int count, Random rng)
        {
            var pool = new List<ToolType>(ToolTypeExtensions.Count);
            for (int i = 0; i < ToolTypeExtensions.Count; i++)
            {
                pool.Add((ToolType)i);
            }

            // Fisher-Yates로 앞쪽 count개만 뽑는다.
            for (int i = 0; i < count; i++)
            {
                int j = rng.Next(i, pool.Count);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            return new WeaknessSet
            {
                A = pool[0],
                B = pool[1],
                C = pool[2],
                Assigned = true,
            };
        }

        public bool Contains(ToolType tool)
        {
            return Assigned && (A == tool || B == tool || C == tool);
        }

        /// <summary>제단에 헌납된 도구 3종이 약점과 정확히 일치하는지. 순서는 무관하다.</summary>
        public bool Matches(IReadOnlyList<ToolType> offered)
        {
            if (!Assigned || offered == null || offered.Count != 3)
            {
                return false;
            }

            // 제단은 서로 다른 3종만 받으므로 중복 검사는 불필요하다.
            foreach (var tool in offered)
            {
                if (!Contains(tool))
                {
                    return false;
                }
            }

            return true;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref A);
            serializer.SerializeValue(ref B);
            serializer.SerializeValue(ref C);
            serializer.SerializeValue(ref Assigned);
        }

        public bool Equals(WeaknessSet other)
        {
            return A == other.A && B == other.B && C == other.C && Assigned == other.Assigned;
        }

        public override bool Equals(object obj) => obj is WeaknessSet other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(A, B, C, Assigned);

        public override string ToString()
        {
            return Assigned
                ? $"{A.ToKorean()}, {B.ToKorean()}, {C.ToKorean()}"
                : "(미배정)";
        }
    }
}
