namespace GhostHunter.Core
{
    /// <summary>
    /// 시나리오 3-1의 도구 6종. 이 중 3종이 귀신의 약점이 된다.
    /// 순서를 바꾸거나 중간에 끼워넣지 말 것 — 직렬화에 정수값이 그대로 쓰인다.
    /// </summary>
    public enum ToolType
    {
        Camera = 0,     // 카메라
        Cross = 1,      // 십자가
        Bible = 2,      // 성서
        HolyWater = 3,  // 성수
        Detector = 4,   // 탐지기
        Incense = 5,    // 향초
    }

    public static class ToolTypeExtensions
    {
        public const int Count = 6;

        public static string ToKorean(this ToolType type) => type switch
        {
            ToolType.Camera => "카메라",
            ToolType.Cross => "십자가",
            ToolType.Bible => "성서",
            ToolType.HolyWater => "성수",
            ToolType.Detector => "탐지기",
            ToolType.Incense => "향초",
            _ => type.ToString(),
        };
    }
}
