namespace GhostHunter.Core
{
    /// <summary>
    /// 기술 문서 3번의 게임 상태 머신. 서버만 전이시킨다.
    /// Lobby → Hiding → Investigation → (Hunt) → Result → Lobby
    /// </summary>
    public enum GamePhase
    {
        Lobby = 0,
        Hiding = 1,
        Investigation = 2,
        Hunt = 3,
        Result = 4,
    }

    /// <summary>진영. 시나리오 2번에 따라 정체는 숨기지 않으므로 공개 동기화된다.</summary>
    public enum Faction
    {
        Unassigned = 0,
        Exorcist = 1,
        Ghost = 2,
    }

    /// <summary>승패 결과.</summary>
    public enum GameResult
    {
        None = 0,
        ExorcistWin = 1,
        GhostWin = 2,
    }
}
