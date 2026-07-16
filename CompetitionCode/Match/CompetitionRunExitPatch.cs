using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace Competition.CompetitionCode.Match;

/// <summary>Releases the Competition-only network session after normal run cleanup.</summary>
[HarmonyPatch(typeof(NGame), nameof(NGame.ReturnToMainMenu))]
internal static class CompetitionRunExitPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref Task __result)
    {
        if (CompetitionRunContext.IsCompetitionRun)
        {
            __result = EndAfterVanillaReturnAsync(__result);
        }
    }

    private static async Task EndAfterVanillaReturnAsync(Task originalReturn)
    {
        await originalReturn;
        CompetitionMatchController.EndCompetitionRunForReturnToMenu();
    }
}
