using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;

namespace Competition.CompetitionCode.Match;

/// <summary>Strictly suppresses persistence only while CompetitionRunContext is active.</summary>
internal static class CompetitionSaveProtectionPatches
{
    private static bool ShouldAllowWrite() => !CompetitionRunContext.IsCompetitionRun;

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
    private static class SaveRunPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref Task __result)
        {
            if (ShouldAllowWrite())
            {
                return true;
            }

            __result = Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveProgressFile))]
    private static class SaveProgressFilePatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => ShouldAllowWrite();
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.UpdateProgressWithRunData))]
    private static class UpdateProgressWithRunDataPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => ShouldAllowWrite();
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.UpdateProgressAfterCombatWon))]
    private static class UpdateProgressAfterCombatWonPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => ShouldAllowWrite();
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentRun))]
    private static class DeleteCurrentRunPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => ShouldAllowWrite();
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRunHistory))]
    private static class SaveRunHistoryPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => ShouldAllowWrite();
    }
}
