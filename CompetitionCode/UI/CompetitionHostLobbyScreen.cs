using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Platform;
using Competition.CompetitionCode.Multiplayer;
using Competition.CompetitionCode.Match;

namespace Competition.CompetitionCode.UI;

/// <summary>
/// A temporary original character-select scene used as Competition's host UI.
/// The real Steam session and StartRunLobby are owned by CompetitionHostController.
/// </summary>
public static class CompetitionHostLobbyScreen
{
    private const string FairPlayReminderName = "CompetitionFairPlayReminder";
    private static NCharacterSelectScreen? _activeScreen;
    private static Action? _networkUpdate;
    private static CompetitionClientController? _activeClientController;
    private static NMainMenu? _activeMainMenu;
    private static NMultiplayerSubmenu? _activeModeSubmenu;
    private static bool _clientReturnScheduled;
    private static CompetitionHostController? _boundHostController;
    private static CompetitionClientController? _boundClientController;
    private static Action? _lobbyStateHandler;
    private static Action? _matchStateHandler;
    private static Action? _screenTreeExitingHandler;
    private static bool _invalidScreenRefreshLogged;

    public static bool Show(NMainMenu mainMenu, NMultiplayerSubmenu modeSubmenu, CompetitionHostController controller)
    {
        NCharacterSelectScreen? hostScreen = NCharacterSelectScreen.Create();
        if (hostScreen == null)
        {
            MainFile.Logger.Error("Could not create the original character select screen.");
            return false;
        }

        // The source scene defaults to Visible. Hide it before it enters the
        // tree so Show() below emits VisibilityChanged after NSubmenu has
        // connected its back-button lifecycle.
        hostScreen.Hide();

        // This scene is not pushed to NSubmenuStack, so its lobby-dependent
        // OnSubmenuOpened/OnSubmenuClosed methods are never invoked.
        mainMenu.SubmenuStack.AddChild(hostScreen);
        hostScreen.SetStack(mainMenu.SubmenuStack);
        HideCharacterSelection(hostScreen);
        RemoveInviteButton(hostScreen);
        ConfigureRandomCharacterPreview(hostScreen);
        ConfigureAscensionPanel(hostScreen, controller);
        ConfigureLocalPlayerPanel(hostScreen, controller.CurrentLobby, isHost: true);
        AddFairPlayReminder(hostScreen);
        BindLocalOnlyActions(hostScreen, mainMenu, modeSubmenu, controller);

        // This is a separate menu layer. Hiding the first submenu prevents its
        // parchment cards from showing through the original character scene.
        modeSubmenu.Hide();
        modeSubmenu.MouseFilter = Control.MouseFilterEnum.Ignore;
        hostScreen.Show();
        hostScreen.MouseFilter = Control.MouseFilterEnum.Stop;
        ActivateHostScreen(hostScreen, controller);
        MainFile.Logger.Info("Competition host screen opened.");
        return true;
    }

    public static bool ShowClient(NMainMenu mainMenu, NMultiplayerSubmenu modeSubmenu, CompetitionClientController controller)
    {
        NCharacterSelectScreen? clientScreen = NCharacterSelectScreen.Create();
        if (clientScreen == null || controller.CurrentLobby == null)
        {
            MainFile.Logger.Error("Could not create the Competition client preparation screen.");
            return false;
        }

        clientScreen.Hide();
        mainMenu.SubmenuStack.AddChild(clientScreen);
        clientScreen.SetStack(mainMenu.SubmenuStack);
        HideCharacterSelection(clientScreen);
        RemoveInviteButton(clientScreen);
        ConfigureRandomCharacterPreview(clientScreen);
        ConfigureClientAscensionPanel(clientScreen, controller.CurrentLobby);
        ConfigureLocalPlayerPanel(clientScreen, controller.CurrentLobby, isHost: false);
        AddFairPlayReminder(clientScreen);
        BindClientActions(clientScreen, mainMenu, modeSubmenu, controller);
        modeSubmenu.Hide();
        modeSubmenu.MouseFilter = Control.MouseFilterEnum.Ignore;
        clientScreen.Show();
        clientScreen.MouseFilter = Control.MouseFilterEnum.Stop;
        ActivateClientScreen(clientScreen, mainMenu, modeSubmenu, controller);
        MainFile.Logger.Info("Competition client screen opened.");
        return true;
    }

    internal static bool IsActive(NCharacterSelectScreen screen)
    {
        return ReferenceEquals(_activeScreen, screen);
    }

    /// <summary>Called by the match state machine before it changes scenes.</summary>
    internal static void DetachForCompetitionRun()
    {
        if (_activeScreen == null)
        {
            return;
        }

        DeactivateScreen();
        MainFile.Logger.Info("Competition context remains active after lobby UI disposal.");
    }

    internal static void ProcessActiveScreen(NCharacterSelectScreen screen)
    {
        if (!ReferenceEquals(_activeScreen, screen))
        {
            return;
        }

        _networkUpdate?.Invoke();
        if (_activeClientController?.HostDisconnected == true && !_clientReturnScheduled)
        {
            _clientReturnScheduled = true;
            TaskHelper.RunSafely(ReturnClientAfterHostDisconnected(screen));
        }
    }

    private static void BindLocalOnlyActions(
        NCharacterSelectScreen screen,
        NMainMenu mainMenu,
        NMultiplayerSubmenu modeSubmenu,
        CompetitionHostController controller)
    {
        NBackButton? backButton = screen.GetNodeOrNull<NBackButton>("BackButton");
        NConfirmButton? readyButton = screen.GetNodeOrNull<NConfirmButton>("ConfirmButton");

        if (backButton != null)
        {
            CompetitionModeSubmenu.ReplaceReleasedHandlers(backButton, _ => ReturnToMode(screen, mainMenu, modeSubmenu, controller));
        }

        if (readyButton != null)
        {
            CompetitionModeSubmenu.ReplaceReleasedHandlers(
                readyButton,
                _ => CompetitionMatchController.ToggleLocalReady());
        }

    }

    private static void ReturnToMode(
        NCharacterSelectScreen screen,
        NMainMenu mainMenu,
        NMultiplayerSubmenu modeSubmenu,
        CompetitionHostController controller)
    {
        MainFile.Logger.Info("Back pressed from host lobby.");
        screen.GetNodeOrNull<NAscensionPanel>("%AscensionPanel")?.Cleanup();
        if (ReferenceEquals(_activeScreen, screen))
        {
            DeactivateScreen();
        }

        controller.CloseHostLobby();
        CompetitionModeSubmenu.EnableCreateButtonAfterHostClosed(modeSubmenu, controller);
        modeSubmenu.Show();
        modeSubmenu.MouseFilter = Control.MouseFilterEnum.Stop;
        screen.QueueFree();
    }

    private static void BindClientActions(
        NCharacterSelectScreen screen,
        NMainMenu mainMenu,
        NMultiplayerSubmenu modeSubmenu,
        CompetitionClientController controller)
    {
        if (screen.GetNodeOrNull<NBackButton>("BackButton") is { } backButton)
        {
            CompetitionModeSubmenu.ReplaceReleasedHandlers(
                backButton,
                _ => ReturnClientToMode(screen, mainMenu, modeSubmenu, controller));
        }

        if (screen.GetNodeOrNull<NConfirmButton>("ConfirmButton") is { } readyButton)
        {
            CompetitionModeSubmenu.ReplaceReleasedHandlers(
                readyButton,
                _ => CompetitionMatchController.ToggleLocalReady());
        }
    }

    private static void ReturnClientToMode(
        NCharacterSelectScreen screen,
        NMainMenu mainMenu,
        NMultiplayerSubmenu modeSubmenu,
        CompetitionClientController controller)
    {
        MainFile.Logger.Info("Client back pressed.");
        screen.GetNodeOrNull<NAscensionPanel>("%AscensionPanel")?.Cleanup();
        if (ReferenceEquals(_activeScreen, screen))
        {
            DeactivateScreen();
        }

        controller.CloseClientLobby();
        modeSubmenu.Show();
        modeSubmenu.MouseFilter = Control.MouseFilterEnum.Stop;
        screen.QueueFree();
    }

    private static void HideCharacterSelection(NCharacterSelectScreen screen)
    {
        // Verified from the original NCharacterSelectScreen source. This parent
        // contains every selectable and locked character card, including Random.
        screen.GetNodeOrNull<Control>("CharSelectButtons")?.Hide();
        screen.GetNodeOrNull<Control>("CharSelectButtons/ButtonContainer")?.Hide();
        screen.GetNodeOrNull<Control>("ActLabel")?.Hide();
        screen.GetNodeOrNull<Control>("%ActDropdown")?.Hide();
    }

    private static void RemoveInviteButton(NCharacterSelectScreen screen)
    {
        // This is a freshly created, Competition-only copy of the original
        // scene. Removing its invite container leaves no blank layout slot and
        // cannot affect the original multiplayer screens.
        if (screen.FindChild("InviteButton", recursive: true, owned: false) is not Control inviteButton)
        {
            return;
        }

        inviteButton.MouseFilter = Control.MouseFilterEnum.Ignore;
        inviteButton.Hide();
        if (inviteButton.GetParent() is Control inviteContainer)
        {
            inviteContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
            inviteContainer.QueueFree();
        }
        else
        {
            inviteButton.QueueFree();
        }
    }

    private static void ConfigureAscensionPanel(NCharacterSelectScreen screen, CompetitionHostController controller)
    {
        NAscensionPanel? ascensionPanel = screen.GetNodeOrNull<NAscensionPanel>("%AscensionPanel");
        if (ascensionPanel == null)
        {
            return;
        }

        // NCharacterSelectScreen wires this signal to its private lobby method.
        // Competition has no lobby in this phase, so keep the original visual
        // control but detach that one lobby-dependent callback.
        foreach (Godot.Collections.Dictionary connection in ascensionPanel
                     .GetSignalConnectionList(NAscensionPanel.SignalName.AscensionLevelChanged))
        {
            Callable callable = connection["callable"].AsCallable();
            if (ascensionPanel.IsConnected(NAscensionPanel.SignalName.AscensionLevelChanged, callable))
            {
                ascensionPanel.Disconnect(NAscensionPanel.SignalName.AscensionLevelChanged, callable);
            }
        }

        ascensionPanel.Initialize(MultiplayerUiMode.Host);
        ascensionPanel.Connect(
            NAscensionPanel.SignalName.AscensionLevelChanged,
            Callable.From(() => controller.SyncAscension(ascensionPanel.Ascension)));
        controller.SyncAscension(ascensionPanel.Ascension);
        ascensionPanel.Show();
    }

    private static void ConfigureClientAscensionPanel(NCharacterSelectScreen screen, StartRunLobby lobby)
    {
        NAscensionPanel? ascensionPanel = screen.GetNodeOrNull<NAscensionPanel>("%AscensionPanel");
        if (ascensionPanel == null)
        {
            return;
        }

        DisconnectAscensionCallbacks(ascensionPanel);
        ascensionPanel.Initialize(MultiplayerUiMode.Client);
        ascensionPanel.SetMaxAscension(lobby.MaxAscension);
        ascensionPanel.SetAscensionLevel(lobby.Ascension);
        ascensionPanel.Show();
    }

    private static void ConfigureRandomCharacterPreview(NCharacterSelectScreen screen)
    {
        CharacterModel randomCharacter = ModelDb.Character<RandomCharacter>();
        Control? backgroundContainer = screen.GetNodeOrNull<Control>("AnimatedBg");
        if (backgroundContainer != null)
        {
            foreach (Node child in backgroundContainer.GetChildren())
            {
                child.QueueFree();
            }

            Control background = PreloadManager.Cache
                .GetScene(randomCharacter.CharacterSelectBg)
                .Instantiate<Control>(PackedScene.GenEditState.Disabled);
            background.Name = randomCharacter.Id.Entry + "_bg";
            backgroundContainer.AddChild(background);
        }

        if (screen.GetNodeOrNull<MegaLabel>("InfoPanel/VBoxContainer/Name") is { } nameLabel)
        {
            nameLabel.SetTextAutoSize(new LocString("characters", randomCharacter.CharacterSelectTitle).GetFormattedText());
        }

        if (screen.GetNodeOrNull<MegaRichTextLabel>("InfoPanel/VBoxContainer/DescriptionLabel") is { } descriptionLabel)
        {
            descriptionLabel.Text = new LocString("characters", randomCharacter.CharacterSelectDesc).GetFormattedText();
        }

        screen.GetNodeOrNull<MegaLabel>("InfoPanel/VBoxContainer/HpGoldSpacer/HpGold/Hp/Label")?.SetTextAutoSize("??/??");
        screen.GetNodeOrNull<MegaLabel>("InfoPanel/VBoxContainer/HpGoldSpacer/HpGold/Gold/Label")?.SetTextAutoSize("???");
        screen.GetNodeOrNull<Control>("InfoPanel/VBoxContainer/Relic")?.Hide();
        screen.GetNodeOrNull<NConfirmButton>("ConfirmButton")?.Enable();
    }

    private static void ConfigureLocalPlayerPanel(NCharacterSelectScreen screen, StartRunLobby? lobby, bool isHost)
    {
        Control? playerContainer = screen.GetNodeOrNull<Control>("RemotePlayerContainer");
        playerContainer?.Show();

        if (playerContainer?.FindChild("SoloLabel", recursive: true, owned: false) is MegaLabel soloLabel)
        {
            soloLabel.SetTextAutoSize(BuildPlayerStatusText(lobby, isHost));
            soloLabel.Show();
        }

    }

    /// <summary>
    /// A visual-only reminder. It has no network, readiness, or Mod-manager
    /// behavior and intentionally does not keep a static Godot node reference.
    /// </summary>
    private static void AddFairPlayReminder(NCharacterSelectScreen screen)
    {
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() ||
            screen.FindChild(FairPlayReminderName, recursive: true, owned: false) != null)
        {
            return;
        }

        try
        {
            // Right-anchored offsets keep this compact note in the empty upper
            // right area at every supported aspect ratio, below the version
            // text and away from the character card and bottom controls.
            Control reminder = new()
            {
                Name = FairPlayReminderName,
                AnchorLeft = 1f,
                AnchorTop = 0f,
                AnchorRight = 1f,
                AnchorBottom = 0f,
                OffsetLeft = -760f,
                OffsetTop = 142f,
                OffsetRight = -64f,
                OffsetBottom = 230f,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            Label title = CreateFairPlayLabel("公平比赛提示：", 34, new Color("FFE17A"));
            title.Position = new Vector2(70f, -16f);
            title.Size = new Vector2(626f, 42f);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            Label body = CreateFairPlayLabel("请自觉关闭可能影响游戏平衡的 Mod。", 30, new Color("FFF5C6"));
            body.Position = new Vector2(0f, 43f);
            body.Size = new Vector2(696f, 40f);
            reminder.AddChild(title);
            reminder.AddChild(body);
            screen.AddChild(reminder);
            MainFile.Logger.Info("Fair play reminder displayed.");
        }
        catch (Exception exception)
        {
            // The reminder is strictly optional; a scene/theme difference must
            // never prevent the actual lobby from opening.
            MainFile.Logger.Warn($"Could not display fair play reminder: {exception.Message}");
        }
    }

    private static Label CreateFairPlayLabel(string text, int fontSize, Color color)
    {
        Label label = new()
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Off
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.82f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.72f));
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeConstantOverride("outline_size", 2);
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        return label;
    }

    private static void RemoveFairPlayReminder(NCharacterSelectScreen? screen)
    {
        if (screen == null || !GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() ||
            screen.FindChild(FairPlayReminderName, recursive: true, owned: false) is not Node reminder)
        {
            return;
        }

        reminder.QueueFree();
        MainFile.Logger.Info("Fair play reminder removed.");
    }

    private static void ActivateHostScreen(NCharacterSelectScreen screen, CompetitionHostController controller)
    {
        DeactivateScreen();
        _activeScreen = screen;
        _networkUpdate = null;
        _activeClientController = null;
        _activeMainMenu = null;
        _activeModeSubmenu = null;
        _clientReturnScheduled = false;
        _invalidScreenRefreshLogged = false;
        _boundHostController = controller;
        _lobbyStateHandler = () => RefreshPlayerStatus(screen, controller.CurrentLobby, isHost: true);
        _matchStateHandler = () => RefreshPlayerStatus(screen, controller.CurrentLobby, isHost: true);
        _screenTreeExitingHandler = () => OnScreenTreeExiting(screen);
        controller.LobbyStateChanged += _lobbyStateHandler;
        CompetitionMatchController.StateChanged += _matchStateHandler;
        screen.TreeExiting += _screenTreeExitingHandler;
        RefreshPlayerStatus(screen, controller.CurrentLobby, isHost: true);
    }

    private static void ActivateClientScreen(
        NCharacterSelectScreen screen,
        NMainMenu mainMenu,
        NMultiplayerSubmenu modeSubmenu,
        CompetitionClientController controller)
    {
        DeactivateScreen();
        _activeScreen = screen;
        _networkUpdate = null;
        _activeClientController = controller;
        _activeMainMenu = mainMenu;
        _activeModeSubmenu = modeSubmenu;
        _clientReturnScheduled = false;
        _invalidScreenRefreshLogged = false;
        _boundClientController = controller;
        _lobbyStateHandler = () => RefreshPlayerStatus(screen, controller.CurrentLobby, isHost: false);
        _matchStateHandler = () => RefreshPlayerStatus(screen, controller.CurrentLobby, isHost: false);
        _screenTreeExitingHandler = () => OnScreenTreeExiting(screen);
        controller.LobbyStateChanged += _lobbyStateHandler;
        CompetitionMatchController.StateChanged += _matchStateHandler;
        screen.TreeExiting += _screenTreeExitingHandler;
        RefreshPlayerStatus(screen, controller.CurrentLobby, isHost: false);
    }

    private static void DeactivateScreen()
    {
        bool hadActiveScreen = _activeScreen != null;
        if (hadActiveScreen)
        {
            MainFile.Logger.Info("Detaching lobby UI state listeners.");
        }

        RemoveFairPlayReminder(_activeScreen);

        if (_boundHostController != null && _lobbyStateHandler != null)
        {
            _boundHostController.LobbyStateChanged -= _lobbyStateHandler;
        }
        if (_boundClientController != null && _lobbyStateHandler != null)
        {
            _boundClientController.LobbyStateChanged -= _lobbyStateHandler;
        }
        if (_matchStateHandler != null)
        {
            CompetitionMatchController.StateChanged -= _matchStateHandler;
        }
        if (_activeScreen != null && GodotObject.IsInstanceValid(_activeScreen) && _screenTreeExitingHandler != null)
        {
            _activeScreen.TreeExiting -= _screenTreeExitingHandler;
        }

        _activeScreen = null;
        _networkUpdate = null;
        _activeClientController = null;
        _activeMainMenu = null;
        _activeModeSubmenu = null;
        _clientReturnScheduled = false;
        _boundHostController = null;
        _boundClientController = null;
        _lobbyStateHandler = null;
        _matchStateHandler = null;
        _screenTreeExitingHandler = null;
        if (hadActiveScreen)
        {
            MainFile.Logger.Info("Lobby UI listeners detached.");
        }
    }

    private static void OnScreenTreeExiting(NCharacterSelectScreen screen)
    {
        if (!ReferenceEquals(_activeScreen, screen))
        {
            return;
        }

        DeactivateScreen();
    }

    private static async Task ReturnClientAfterHostDisconnected(NCharacterSelectScreen screen)
    {
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree())
        {
            return;
        }

        SceneTree tree = screen.GetTree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (GodotObject.IsInstanceValid(screen) && screen.IsInsideTree() && ReferenceEquals(_activeScreen, screen) && _activeClientController != null &&
            _activeMainMenu != null && _activeModeSubmenu != null)
        {
            MainFile.Logger.Warn("Host left Competition lobby; returning to Competition menu.");
            NErrorPopup? popup = NErrorPopup.Create(new NetErrorInfo(NetError.Quit, selfInitiated: false));
            NModalContainer? modalContainer = NModalContainer.Instance;
            if (popup != null && modalContainer != null)
            {
                modalContainer.Add(popup);
            }

            ReturnClientToMode(screen, _activeMainMenu, _activeModeSubmenu, _activeClientController);
        }
    }

    private static void RefreshPlayerStatus(NCharacterSelectScreen screen, StartRunLobby? lobby, bool isHost)
    {
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !IsActive(screen))
        {
            if (!_invalidScreenRefreshLogged)
            {
                _invalidScreenRefreshLogged = true;
                MainFile.Logger.Info("Lobby UI refresh skipped because the screen is no longer valid.");
            }
            return;
        }

        if (screen.GetNodeOrNull<Control>("RemotePlayerContainer")?.FindChild("SoloLabel", recursive: true, owned: false) is MegaLabel soloLabel)
        {
            soloLabel.SetTextAutoSize(BuildPlayerStatusText(lobby, isHost));
        }

        bool starting = CompetitionMatchController.Current is { IsStarting: true };
        if (starting)
        {
            screen.GetNodeOrNull<NConfirmButton>("ConfirmButton")?.Disable();
            screen.GetNodeOrNull<NBackButton>("BackButton")?.Disable();
            if (screen.GetNodeOrNull<NAscensionPanel>("%AscensionPanel") is { } startingAscensionPanel)
            {
                startingAscensionPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
        }
        else if (screen.GetNodeOrNull<NConfirmButton>("ConfirmButton") is { } readyButton)
        {
            readyButton.Enable();
        }

        if (!isHost && lobby != null && screen.GetNodeOrNull<NAscensionPanel>("%AscensionPanel") is { } ascensionPanel)
        {
            ascensionPanel.SetMaxAscension(lobby.MaxAscension);
            ascensionPanel.SetAscensionLevel(lobby.Ascension);
        }
    }

    private static string BuildPlayerStatusText(StartRunLobby? lobby, bool isHost)
    {
        if (lobby == null)
        {
            return $"你目前是唯一的玩家！\n{GetLocalPlayerName()}（Host）\n等待另一名玩家加入";
        }

        if (lobby.Players.Count <= 1)
        {
            LobbyPlayer localPlayer = lobby.LocalPlayer;
            string ready = CompetitionMatchController.IsReady(lobby, localPlayer.id) ? "已准备" : "未准备";
            return $"房间玩家：\n{GetPlayerName(localPlayer.id)}（Host） {ready}\n等待另一名玩家加入";
        }

        List<string> lines = new();
        foreach (LobbyPlayer player in lobby.Players.OrderBy(player => player.slotId))
        {
            string role = player.slotId == 0 ? "Host" : "Guest";
            string ready = CompetitionMatchController.IsReady(lobby, player.id) ? "已准备" : "未准备";
            lines.Add($"{GetPlayerName(player.id)}（{role}） {ready}");
        }

        return "房间玩家：\n" + string.Join("\n", lines);
    }

    private static string GetPlayerName(ulong playerId)
    {
        try
        {
            string name = PlatformUtil.GetPlayerNameRaw(PlatformUtil.PrimaryPlatform, playerId);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (System.Exception)
        {
        }

        return playerId.ToString();
    }

    private static void DisconnectAscensionCallbacks(NAscensionPanel ascensionPanel)
    {
        foreach (Godot.Collections.Dictionary connection in ascensionPanel
                     .GetSignalConnectionList(NAscensionPanel.SignalName.AscensionLevelChanged))
        {
            Callable callable = connection["callable"].AsCallable();
            if (ascensionPanel.IsConnected(NAscensionPanel.SignalName.AscensionLevelChanged, callable))
            {
                ascensionPanel.Disconnect(NAscensionPanel.SignalName.AscensionLevelChanged, callable);
            }
        }
    }

    private static string GetLocalPlayerName()
    {
        try
        {
            PlatformType platform = PlatformUtil.PrimaryPlatform;
            string playerName = PlatformUtil.GetPlayerNameRaw(platform, PlatformUtil.GetLocalPlayerId(platform));
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                return playerName;
            }
        }
        catch (System.Exception exception)
        {
            MainFile.Logger.Warn($"Could not read the platform player name: {exception.Message}");
        }

        return "本地玩家";
    }
}
