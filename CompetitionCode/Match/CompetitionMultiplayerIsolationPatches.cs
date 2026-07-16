using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using Competition.CompetitionCode.UI;

namespace Competition.CompetitionCode.Match;

/// <summary>
/// StartRunLobby is retained only for Competition transport. The reused
/// character-select scene has original multiplayer handlers, so these targeted
/// guards prevent an old callback from starting a shared vanilla run.
/// </summary>
[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun))]
internal static class CompetitionCharacterSelectBeginRunIsolationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NCharacterSelectScreen __instance)
    {
        if (!CompetitionRunContext.IsCompetitionRun && !CompetitionHostLobbyScreen.IsActive(__instance))
        {
            return true;
        }

        MainFile.Logger.Warn("Blocked vanilla character-select BeginRun for Competition.");
        return false;
    }
}

[HarmonyPatch(typeof(StartRunLobby), nameof(StartRunLobby.SetReady))]
internal static class CompetitionLobbyReadyIsolationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(StartRunLobby __instance)
    {
        if (!CompetitionMatchController.IsCompetitionLobby(__instance))
        {
            return true;
        }

        MainFile.Logger.Warn("Blocked vanilla StartRunLobby.SetReady for Competition.");
        return false;
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
internal static class CompetitionRunManagerMultiplayerIsolationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(StartRunLobby lobby)
    {
        if (!CompetitionRunContext.IsCompetitionRun && !CompetitionMatchController.IsCompetitionLobby(lobby))
        {
            return true;
        }

        MainFile.Logger.Warn("Blocked vanilla multiplayer run setup for Competition.");
        return false;
    }
}
