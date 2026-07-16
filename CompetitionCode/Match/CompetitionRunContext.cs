namespace Competition.CompetitionCode.Match;

/// <summary>
/// Marks the one locally active run as an ephemeral Competition run. Save
/// patches always check this flag first, leaving normal singleplayer and
/// original multiplayer sessions untouched.
/// </summary>
public static class CompetitionRunContext
{
    public static bool IsCompetitionRun { get; private set; }

    public static string? MatchId { get; private set; }

    public static void Enter(string matchId)
    {
        IsCompetitionRun = true;
        MatchId = matchId;
    }

    public static void Exit(string reason = "Competition session ended")
    {
        IsCompetitionRun = false;
        MatchId = null;
        MainFile.Logger.Info($"Competition context cleared because: {reason}.");
    }
}
