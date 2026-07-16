using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;

namespace Competition.CompetitionCode.Multiplayer;

/// <summary>
/// Owns the one per-frame update path for the active Competition lobby. Its
/// SceneTree loop outlives the temporary preparation screen and therefore
/// preserves the Steam session while an isolated run scene is entered.
/// </summary>
public static class CompetitionNetworkPump
{
    // Keep the exact StartRunLobby that owns the host/client transport. This
    // is the pre-run lifecycle that JoinFlow expects; it is never replaced by
    // RunManager.Instance.NetService.
    private static StartRunLobby? _lobby;
    private static bool _updateLoopStarted;

    public static INetGameService? TransportService => _lobby?.NetService;
    public static bool IsProcessing => _updateLoopStarted && _lobby?.NetService.IsConnected == true;

    public static void SetLobby(StartRunLobby lobby)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        _lobby = lobby;
        EnsureUpdateLoop();
    }

    public static void ClearLobby(StartRunLobby? lobby = null)
    {
        if (lobby == null || ReferenceEquals(_lobby, lobby))
        {
            _lobby = null;
        }
    }

    public static void Update()
    {
        // This remains active throughout the lobby phase and scene transition.
        // It never reads the current Run's service.
        if (_lobby?.NetService.IsConnected == true)
        {
            _lobby.NetService.Update();
        }
    }

    private static void EnsureUpdateLoop()
    {
        if (_updateLoopStarted || Engine.GetMainLoop() is not SceneTree sceneTree)
        {
            return;
        }

        _updateLoopStarted = true;
        TaskHelper.RunSafely(UpdateLoop(sceneTree));
    }

    private static async Task UpdateLoop(SceneTree sceneTree)
    {
        while (GodotObject.IsInstanceValid(sceneTree))
        {
            Update();
            await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
        }

        _updateLoopStarted = false;
    }
}
