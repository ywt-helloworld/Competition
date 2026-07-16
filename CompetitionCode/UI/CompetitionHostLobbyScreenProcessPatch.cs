using HarmonyLib;
using Competition.CompetitionCode.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace Competition.CompetitionCode.UI;

/// <summary>
/// Reuses the original character-select node's process callback rather than
/// adding a Competition Node to the scene tree.
/// </summary>
[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen._Process))]
internal static class CompetitionHostLobbyScreenProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        CompetitionHostLobbyScreen.ProcessActiveScreen(__instance);
    }
}
