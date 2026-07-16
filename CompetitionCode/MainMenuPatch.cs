using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Competition.CompetitionCode;

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class MainMenuPatch
{
    [HarmonyPostfix]
    private static void Postfix(NMainMenu __instance)
    {
        CompetitionRuntime.OnMainMenuReady(__instance);
    }
}
