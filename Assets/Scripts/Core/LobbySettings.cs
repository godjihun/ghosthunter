using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Core
{
    /// <summary>
    /// 방장이 대기방에서 조절하는 밸런스 값 (<see cref="GameConfig"/>의 "로비에서 조절 가능" 절).
    ///
    /// <b>왜 구조체 하나로 묶는가</b> — 값마다 NetworkVariable을 두면 6개가 따로 도착해서,
    /// 값이 섞인 중간 상태가 잠깐 존재한다. 한 덩어리로 보내면 항상 일관된 한 벌이다.
    ///
    /// <b>왜 GameConfig를 직접 동기화하지 않는가</b> — ScriptableObject는 에셋이라
    /// 런타임에 값을 쓰면 <b>에디터에서 그대로 파일에 남는다.</b> 플레이 모드를 껐다 켜도
    /// 안 돌아오고, git에도 올라간다. 그래서 전송은 이 구조체로 하고,
    /// 적용은 GameManager가 만든 <b>런타임 사본</b>에만 한다.
    /// </summary>
    public struct LobbySettings : INetworkSerializable, System.IEquatable<LobbySettings>
    {
        public float HidingDuration;
        public float InvestigationDuration;
        public float FearSkillCooldown;
        public float ToolNullifyDuration;
        public float DetectionRange;
        public float AbsorbGaugePerHit;

        /// <summary>
        /// 도구 사용 후 쿨타임(초).
        ///
        /// 도구는 이제 소모품이 아니라 <b>쿨타임이 도는 재사용 도구</b>다. 종류당 1개뿐이라
        /// 이 값이 곧 "저택 전체에서 그 도구를 얼마나 자주 쓸 수 있는가"가 된다.
        /// </summary>
        public float ToolCooldown;

        /// <summary>사냥 단계 지속 시간(초).</summary>
        public float HuntDuration;

        public static LobbySettings From(GameConfig config)
        {
            return new LobbySettings
            {
                HidingDuration = config.HidingDuration,
                InvestigationDuration = config.InvestigationDuration,
                FearSkillCooldown = config.FearSkillCooldown,
                ToolNullifyDuration = config.ToolNullifyDuration,
                DetectionRange = config.DetectionRange,
                AbsorbGaugePerHit = config.AbsorbGaugePerHit,
                ToolCooldown = config.ToolCooldown,
                HuntDuration = config.HuntDuration,
            };
        }

        /// <summary>런타임 사본에 값을 옮긴다. <b>에셋 원본에 쓰지 말 것.</b></summary>
        public void ApplyTo(GameConfig config)
        {
            config.HidingDuration = HidingDuration;
            config.InvestigationDuration = InvestigationDuration;
            config.FearSkillCooldown = FearSkillCooldown;
            config.ToolNullifyDuration = ToolNullifyDuration;
            config.DetectionRange = DetectionRange;
            config.AbsorbGaugePerHit = AbsorbGaugePerHit;
            config.ToolCooldown = ToolCooldown;
            config.HuntDuration = HuntDuration;
        }

        /// <summary>
        /// 슬라이더로 만질 수 있는 항목들. UI가 이 표를 그대로 그린다.
        ///
        /// <c>Step</c>은 슬라이더가 몇 단위로 끊길지다.
        /// </summary>
        public static readonly (string Label, float Min, float Max, float Step, string Unit)[] Fields =
        {
            ("은신 시간", 10f, 90f, 1f, "초"),
            ("조사 시간", 120f, 900f, 1f, "초"),
            ("공포스킬 쿨타임", 5f, 90f, 1f, "초"),
            ("재은신 시간", 5f, 40f, 1f, "초"),
            ("도구 탐지 사거리", 5f, 60f, 1f, "m"),
            ("게이지 상승량", 5f, 50f, 1f, "%"),
            ("도구 쿨타임", 5f, 60f, 1f, "초"),
            ("사냥 시간", 30f, 180f, 5f, "초"),
        };

        public float this[int index]
        {
            get => index switch
            {
                0 => HidingDuration,
                1 => InvestigationDuration,
                2 => FearSkillCooldown,
                3 => ToolNullifyDuration,
                4 => DetectionRange,
                5 => AbsorbGaugePerHit,
                6 => ToolCooldown,
                _ => HuntDuration,
            };
            set
            {
                switch (index)
                {
                    case 0: HidingDuration = value; break;
                    case 1: InvestigationDuration = value; break;
                    case 2: FearSkillCooldown = value; break;
                    case 3: ToolNullifyDuration = value; break;
                    case 4: DetectionRange = value; break;
                    case 5: AbsorbGaugePerHit = value; break;
                    case 6: ToolCooldown = value; break;
                    default: HuntDuration = value; break;
                }
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref HidingDuration);
            serializer.SerializeValue(ref InvestigationDuration);
            serializer.SerializeValue(ref FearSkillCooldown);
            serializer.SerializeValue(ref ToolNullifyDuration);
            serializer.SerializeValue(ref DetectionRange);
            serializer.SerializeValue(ref AbsorbGaugePerHit);
            serializer.SerializeValue(ref ToolCooldown);
            serializer.SerializeValue(ref HuntDuration);
        }

        // NetworkVariable은 값이 실제로 바뀌었는지 비교해야 하므로 필요하다.
        public bool Equals(LobbySettings other)
        {
            return Mathf.Approximately(HidingDuration, other.HidingDuration)
                && Mathf.Approximately(InvestigationDuration, other.InvestigationDuration)
                && Mathf.Approximately(FearSkillCooldown, other.FearSkillCooldown)
                && Mathf.Approximately(ToolNullifyDuration, other.ToolNullifyDuration)
                && Mathf.Approximately(DetectionRange, other.DetectionRange)
                && Mathf.Approximately(AbsorbGaugePerHit, other.AbsorbGaugePerHit)
                && Mathf.Approximately(ToolCooldown, other.ToolCooldown)
                && Mathf.Approximately(HuntDuration, other.HuntDuration);
        }

        public override bool Equals(object obj) => obj is LobbySettings other && Equals(other);

        public override int GetHashCode() => System.HashCode.Combine(
            HidingDuration, InvestigationDuration, FearSkillCooldown,
            ToolNullifyDuration, DetectionRange, AbsorbGaugePerHit, ToolCooldown, HuntDuration);
    }
}
