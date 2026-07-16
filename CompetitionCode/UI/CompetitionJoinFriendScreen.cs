using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using Competition.CompetitionCode.Multiplayer;

namespace Competition.CompetitionCode.UI;

/// <summary>
/// Uses the original friend-list submenu. A Harmony prefix redirects only this
/// Competition visit's selected friend into CompetitionClientController.
/// </summary>
public static class CompetitionJoinFriendScreen
{
    private static NJoinFriendScreen? _activeScreen;
    private static NMainMenu? _mainMenu;
    private static NMultiplayerSubmenu? _modeSubmenu;
    private static bool _transitioningToLobby;

    public static void Show(NMainMenu mainMenu, NMultiplayerSubmenu modeSubmenu)
    {
        NJoinFriendScreen joinScreen = mainMenu.SubmenuStack.GetSubmenuType<NJoinFriendScreen>();
        _activeScreen = joinScreen;
        _mainMenu = mainMenu;
        _modeSubmenu = modeSubmenu;
        _transitioningToLobby = false;
        MainFile.Logger.Info("Opening Steam friend join screen.");

        if (mainMenu.SubmenuStack.Peek() != joinScreen)
        {
            mainMenu.SubmenuStack.Push(joinScreen);
        }
    }

    internal static bool TryInterceptJoin(
        NJoinFriendScreen screen,
        IClientConnectionInitializer initializer,
        ref Task result)
    {
        if (!ReferenceEquals(_activeScreen, screen))
        {
            return false;
        }

        result = JoinSelectedAsync(screen, initializer);
        return true;
    }

    internal static void OnScreenClosed(NJoinFriendScreen screen)
    {
        if (!ReferenceEquals(_activeScreen, screen))
        {
            return;
        }

        if (!_transitioningToLobby && CompetitionClientController.TryGet() is { IsJoining: true } controller)
        {
            controller.CloseClientLobby();
        }

        if (!_transitioningToLobby)
        {
            _activeScreen = null;
            _mainMenu = null;
            _modeSubmenu = null;
        }
    }

    private static async Task JoinSelectedAsync(NJoinFriendScreen screen, IClientConnectionInitializer initializer)
    {
        if (_mainMenu == null || _modeSubmenu == null || !GodotObject.IsInstanceValid(screen))
        {
            MainFile.Logger.Error("Join failed: Competition friend join screen is no longer available.");
            return;
        }

        SceneTree? sceneTree = screen.GetTree();
        if (sceneTree == null)
        {
            MainFile.Logger.Error("Join failed: SceneTree is null.");
            return;
        }

        MainFile.Logger.Info("Join pressed.");
        screen.GetNodeOrNull<Control>("%LoadingOverlay")?.Show();
        CompetitionClientController controller = CompetitionClientController.GetOrCreate();
        CompetitionHostResult result = await controller.JoinAsync(initializer, sceneTree);
        if (!result.Succeeded)
        {
            screen.GetNodeOrNull<Control>("%LoadingOverlay")?.Hide();
            if (controller.LastJoinFailedBecauseLobbyExpired)
            {
                ShowExpiredLobbyError();
            }
            return;
        }

        _transitioningToLobby = true;
        NMainMenu mainMenu = _mainMenu;
        NMultiplayerSubmenu modeSubmenu = _modeSubmenu;
        if (mainMenu.SubmenuStack.Peek() == screen)
        {
            mainMenu.SubmenuStack.Pop();
        }

        _activeScreen = null;
        _mainMenu = null;
        _modeSubmenu = null;
        if (!CompetitionHostLobbyScreen.ShowClient(mainMenu, modeSubmenu, controller))
        {
            controller.CloseClientLobby();
        }
    }

    private static void ShowExpiredLobbyError()
    {
        NErrorPopup? popup = NErrorPopup.Create(
            "Competition",
            "房间已关闭或邀请已经过期。",
            showReportBugButton: false);
        if (popup != null && NModalContainer.Instance != null)
        {
            NModalContainer.Instance.Add(popup);
        }
    }
}
