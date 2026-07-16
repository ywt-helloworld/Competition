using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using Competition.CompetitionCode.Multiplayer;

namespace Competition.CompetitionCode.UI;

/// <summary>
/// Temporarily replaces handlers on the currently live multiplayer submenu.
/// No Godot node is retained after TreeExiting; a fresh scene always receives
/// a fresh binding and its own original handler snapshot.
/// </summary>
public static class CompetitionModeSubmenu
{
    private static NMultiplayerSubmenu? _boundSubmenu;
    private static ulong _boundSubmenuId;
    private static List<Callable>? _originalHostReleased;
    private static List<Callable>? _originalJoinReleased;
    private static List<Callable>? _originalBackReleased;
    private static Action? _treeExitingHandler;
    private static bool _isClosing;

    public static void Show(NMainMenu mainMenu)
    {
        if (!IsUsable(mainMenu))
        {
            return;
        }

        NMultiplayerSubmenu submenu = mainMenu.SubmenuStack.GetSubmenuType<NMultiplayerSubmenu>();
        if (!IsUsable(submenu))
        {
            ClearBindingState();
            MainFile.Logger.Error("Could not find a live multiplayer submenu.");
            return;
        }

        if (IsBoundTo(submenu))
        {
            EnsureVisible(mainMenu, submenu);
            return;
        }

        // A previous scene may have disappeared without delivering a usable
        // TreeExiting callback. Its original signals belong to that old scene,
        // so clearing them is correct; they must never be restored on this one.
        ClearBindingState();

        NSubmenuButton? hostButton = GetLiveButton<NSubmenuButton>(submenu, "ButtonContainer/HostButton");
        NSubmenuButton? joinButton = GetLiveButton<NSubmenuButton>(submenu, "ButtonContainer/JoinButton");
        NBackButton? backButton = GetLiveButton<NBackButton>(submenu, "BackButton");
        if (hostButton == null || joinButton == null || backButton == null)
        {
            MainFile.Logger.Error("Could not find the current multiplayer submenu buttons.");
            return;
        }

        _boundSubmenu = submenu;
        _boundSubmenuId = submenu.GetInstanceId();
        _originalHostReleased = GetReleasedHandlers(hostButton);
        _originalJoinReleased = GetReleasedHandlers(joinButton);
        _originalBackReleased = GetReleasedHandlers(backButton);
        _treeExitingHandler = () => OnBoundSubmenuTreeExiting(submenu, _boundSubmenuId);
        submenu.TreeExiting += _treeExitingHandler;

        ReplaceReleasedHandlers(hostButton, _ =>
        {
            MainFile.Logger.Info("Create submenu button released.");
            TaskHelper.RunSafely(StartHostAsync(mainMenu, submenu, hostButton));
        });
        ReplaceReleasedHandlers(joinButton, _ =>
        {
            MainFile.Logger.Info("Join submenu button released.");
            CompetitionJoinFriendScreen.Show(mainMenu, submenu);
        });
        ReplaceReleasedHandlers(backButton, _ =>
        {
            MainFile.Logger.Info("Competition submenu back button released.");
            Close(mainMenu, submenu);
        });

        submenu.GetNodeOrNull<Control>("ButtonContainer/LoadButton")?.Hide();
        submenu.GetNodeOrNull<Control>("ButtonContainer/AbandonButton")?.Hide();
        hostButton.Show();
        joinButton.Show();
        EnsureVisible(mainMenu, submenu);
        MainFile.Logger.Info("Competition submenu activated.");
    }

    public static void Close(NMainMenu mainMenu, NMultiplayerSubmenu submenu)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        try
        {
            CompetitionHostController.TryGet()?.CloseHostLobby();
            CompetitionClientController.TryGet()?.CloseClientLobby();

            if (IsBoundTo(submenu))
            {
                RestoreOriginalHandlers(submenu);
            }
            ClearBindingState();

            if (IsUsable(mainMenu) && IsUsable(submenu) && mainMenu.SubmenuStack.Peek() == submenu)
            {
                mainMenu.SubmenuStack.Pop();
            }
        }
        finally
        {
            _isClosing = false;
        }
    }

    internal static void RestoreOriginalHandlers(NMultiplayerSubmenu submenu)
    {
        if (!IsBoundTo(submenu))
        {
            return;
        }

        RestoreReleasedHandlers(GetLiveButton<NSubmenuButton>(submenu, "ButtonContainer/HostButton"), _originalHostReleased);
        RestoreReleasedHandlers(GetLiveButton<NSubmenuButton>(submenu, "ButtonContainer/JoinButton"), _originalJoinReleased);
        RestoreReleasedHandlers(GetLiveButton<NBackButton>(submenu, "BackButton"), _originalBackReleased);
    }

    internal static void EnableCreateButtonAfterHostClosed(NMultiplayerSubmenu submenu, CompetitionHostController controller)
    {
        if (controller.IsHosting || controller.IsStarting || controller.IsClosing)
        {
            return;
        }

        if (GetLiveButton<NSubmenuButton>(submenu, "ButtonContainer/HostButton") is not { } hostButton)
        {
            MainFile.Logger.Info("Stale submenu button reference skipped.");
            return;
        }

        hostButton.Enable();
        MainFile.Logger.Info("Create button enabled.");
    }

    internal static List<Callable> GetReleasedHandlers(NClickableControl button)
    {
        List<Callable> handlers = new();
        if (!IsUsable(button))
        {
            MainFile.Logger.Info("Stale submenu button reference skipped.");
            return handlers;
        }

        foreach (Godot.Collections.Dictionary connection in button.GetSignalConnectionList(NClickableControl.SignalName.Released))
        {
            handlers.Add(connection["callable"].AsCallable());
        }
        return handlers;
    }

    internal static void ReplaceReleasedHandlers(NClickableControl button, Action<NButton> handler)
    {
        if (!IsUsable(button))
        {
            MainFile.Logger.Info("Stale submenu button reference skipped.");
            return;
        }

        foreach (Callable callable in GetReleasedHandlers(button))
        {
            if (button.IsConnected(NClickableControl.SignalName.Released, callable))
            {
                button.Disconnect(NClickableControl.SignalName.Released, callable);
            }
        }
        ConnectReleasedIfNeeded(button, Callable.From(handler));
    }

    private static void RestoreReleasedHandlers(NClickableControl? button, List<Callable>? originalHandlers)
    {
        if (button == null || originalHandlers == null || !IsUsable(button))
        {
            if (button != null)
            {
                MainFile.Logger.Info("Stale submenu button reference skipped.");
            }
            return;
        }

        foreach (Callable callable in GetReleasedHandlers(button))
        {
            if (button.IsConnected(NClickableControl.SignalName.Released, callable))
            {
                button.Disconnect(NClickableControl.SignalName.Released, callable);
            }
        }
        foreach (Callable callable in originalHandlers)
        {
            ConnectReleasedIfNeeded(button, callable);
        }
    }

    private static void ConnectReleasedIfNeeded(NClickableControl button, Callable callable)
    {
        if (!IsUsable(button))
        {
            MainFile.Logger.Info("Stale submenu button reference skipped.");
            return;
        }
        if (!button.IsConnected(NClickableControl.SignalName.Released, callable))
        {
            button.Connect(NClickableControl.SignalName.Released, callable);
        }
    }

    private static async Task StartHostAsync(NMainMenu mainMenu, NMultiplayerSubmenu submenu, NSubmenuButton hostButton)
    {
        if (!IsBoundTo(submenu) || !IsUsable(hostButton))
        {
            return;
        }

        CompetitionHostController controller = CompetitionHostController.GetOrCreate();
        if (controller.IsStarting)
        {
            return;
        }

        MainFile.Logger.Info("Create pressed.");
        hostButton.Disable();
        CompetitionHostResult result = await controller.StartHostAsync();
        if (!result.Succeeded)
        {
            if (IsUsable(hostButton))
            {
                hostButton.Enable();
            }
            return;
        }

        if (!CompetitionHostLobbyScreen.Show(mainMenu, submenu, controller))
        {
            controller.CloseHostLobby();
            if (IsUsable(hostButton))
            {
                hostButton.Enable();
            }
        }
    }

    private static T? GetLiveButton<T>(NMultiplayerSubmenu submenu, NodePath path) where T : Node
    {
        if (!IsUsable(submenu))
        {
            return null;
        }

        T? button = submenu.GetNodeOrNull<T>(path);
        return button != null && IsUsable(button) && submenu.IsAncestorOf(button) ? button : null;
    }

    private static bool IsBoundTo(NMultiplayerSubmenu submenu) =>
        IsUsable(submenu) && ReferenceEquals(_boundSubmenu, submenu) && _boundSubmenuId == submenu.GetInstanceId();

    private static bool IsUsable(GodotObject? node) => node != null && GodotObject.IsInstanceValid(node) && node is Node sceneNode && sceneNode.IsInsideTree();

    private static void EnsureVisible(NMainMenu mainMenu, NMultiplayerSubmenu submenu)
    {
        if (mainMenu.SubmenuStack.Peek() != submenu)
        {
            mainMenu.SubmenuStack.Push(submenu);
        }
    }

    private static void OnBoundSubmenuTreeExiting(NMultiplayerSubmenu submenu, ulong instanceId)
    {
        if (ReferenceEquals(_boundSubmenu, submenu) && _boundSubmenuId == instanceId)
        {
            ClearBindingState();
        }
    }

    private static void ClearBindingState()
    {
        if (_boundSubmenu != null && GodotObject.IsInstanceValid(_boundSubmenu) && _boundSubmenu.IsInsideTree() && _treeExitingHandler != null)
        {
            _boundSubmenu.TreeExiting -= _treeExitingHandler;
        }

        _boundSubmenu = null;
        _boundSubmenuId = 0;
        _originalHostReleased = null;
        _originalJoinReleased = null;
        _originalBackReleased = null;
        _treeExitingHandler = null;
        MainFile.Logger.Info("Competition submenu handlers detached.");
    }
}
