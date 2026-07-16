using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Competition.CompetitionCode.UI;

/// <summary>
/// Leaves original multiplayer join behaviour untouched unless the screen was
/// explicitly opened from Competition.
/// </summary>
[HarmonyPatch]
internal static class CompetitionJoinFriendScreenPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NJoinFriendScreen), nameof(NJoinFriendScreen.JoinGameAsync))]
    private static bool JoinGameAsyncPrefix(
        NJoinFriendScreen __instance,
        IClientConnectionInitializer connInitializer,
        ref Task __result)
    {
        return !CompetitionJoinFriendScreen.TryInterceptJoin(__instance, connInitializer, ref __result);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NJoinFriendScreen), nameof(NJoinFriendScreen.OnSubmenuClosed))]
    private static void OnSubmenuClosedPostfix(NJoinFriendScreen __instance)
    {
        CompetitionJoinFriendScreen.OnScreenClosed(__instance);
    }
}
