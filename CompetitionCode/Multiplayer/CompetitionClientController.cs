using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Runs;

namespace Competition.CompetitionCode.Multiplayer;

/// <summary>
/// Owns one Competition client session. This is intentionally a normal C#
/// object: it is never placed in the Godot scene tree.
/// </summary>
public sealed class CompetitionClientController
{
    private static CompetitionClientController? _instance;

    private NetClientGameService? _clientNetService;
    private StartRunLobby? _lobby;
    private CompetitionClientLobbyListener? _listener;
    private JoinFlow? _joinFlow;
    private bool _isJoining;
    private bool _isConnected;
    private bool _isClosing;
    private bool _hostDisconnected;
    private bool _lastJoinFailedBecauseLobbyExpired;

    public StartRunLobby? CurrentLobby => _lobby;

    public NetClientGameService? ClientNetService => _clientNetService;

    public bool IsJoining => _isJoining;

    public bool IsConnected => _isConnected;

    public bool IsClosing => _isClosing;

    public bool HostDisconnected => _hostDisconnected;

    public bool LastJoinFailedBecauseLobbyExpired => _lastJoinFailedBecauseLobbyExpired;

    public event Action? LobbyStateChanged;

    public static CompetitionClientController GetOrCreate()
    {
        return _instance ??= new CompetitionClientController();
    }

    public static CompetitionClientController? TryGet()
    {
        return _instance;
    }

    public async Task<CompetitionHostResult> JoinAsync(
        IClientConnectionInitializer initializer,
        SceneTree sceneTree)
    {
        if (_isJoining)
        {
            MainFile.Logger.Warn("Join rejected: already joining.");
            return CompetitionHostResult.Failure(NetError.InternalError);
        }

        if (_isConnected)
        {
            MainFile.Logger.Warn("Join rejected: client is already connected.");
            return CompetitionHostResult.Failure(NetError.InternalError);
        }

        if (_lobby != null)
        {
            MainFile.Logger.Warn("Join rejected: client lobby still exists.");
            return CompetitionHostResult.Failure(NetError.InternalError);
        }

        if (_clientNetService != null)
        {
            MainFile.Logger.Warn("Join rejected: client service reference is not null.");
            return CompetitionHostResult.Failure(NetError.InternalError);
        }

        _isJoining = true;
        _hostDisconnected = false;
        _lastJoinFailedBecauseLobbyExpired = false;
        string stage = "creating JoinFlow";
        NetClientGameService? actualConnectedClientService = null;
        ClientLobbyJoinResponseMessage? joinResponse = null;
        try
        {
            MainFile.Logger.Info($"Joining friend: {initializer}.");
            // In this game build JoinFlow creates and owns its NetService.
            // Keep that service out of the controller until Begin succeeds so
            // a stale controller field cannot affect this join attempt.
            _joinFlow = new JoinFlow();
            MainFile.Logger.Info("Starting JoinFlow.");
            MainFile.Logger.Info("Waiting for lobby handshake.");
            stage = "running JoinFlow";
            JoinResult joinResult = await _joinFlow.Begin(initializer, sceneTree);
            MainFile.Logger.Info("JoinFlow succeeded.");

            if (joinResult.sessionState != RunSessionState.InLobby)
            {
                throw new InvalidOperationException($"Unexpected join session state {joinResult.sessionState}.");
            }

            if (joinResult.gameMode != GameMode.Standard || !joinResult.joinResponse.HasValue)
            {
                throw new InvalidOperationException("Host did not return a standard lobby response.");
            }

            // This is the actual service JoinFlow connected. Do not create or
            // inspect a second service during the handoff to StartRunLobby.
            actualConnectedClientService = _joinFlow.NetService;
            if (actualConnectedClientService == null)
            {
                throw new InvalidOperationException("JoinFlow did not provide a client net service.");
            }

            ClientLobbyJoinResponseMessage response = joinResult.joinResponse.Value;
            joinResponse = response;
            if (response.playersInLobby is not { } responsePlayers)
            {
                throw new InvalidOperationException("Join response player list was null.");
            }

            if (responsePlayers.Count != 2)
            {
                throw new InvalidOperationException(
                    $"Expected two players in the join response, got {responsePlayers.Count}.");
            }

            _clientNetService = actualConnectedClientService;
            MainFile.Logger.Info($"Using connected client service: {actualConnectedClientService}.");
            MainFile.Logger.Info($"Join response player count: {responsePlayers.Count}.");

            stage = "creating client StartRunLobby";
            _listener = new CompetitionClientLobbyListener(this);
            MainFile.Logger.Info("Creating client StartRunLobby.");
            _lobby = new StartRunLobby(GameMode.Standard, actualConnectedClientService, _listener, maxPlayers: -1);
            _lobby.InitializeFromMessage(response);
            MainFile.Logger.Info("Client StartRunLobby initialized.");

            bool containsLocalPlayer = _lobby.Players.Exists(player => player.id == actualConnectedClientService.NetId);
            if (_lobby.Players.Count != 2 || !containsLocalPlayer ||
                !ReferenceEquals(_lobby.NetService, _clientNetService))
            {
                throw new InvalidOperationException("Client StartRunLobby did not pass post-initialization validation.");
            }

            MainFile.Logger.Info($"Client local player: {_lobby.LocalPlayer.id}.");
            MainFile.Logger.Info($"Client lobby player count: {_lobby.Players.Count}.");

            stage = "attaching client network pump";
            CompetitionNetworkPump.SetLobby(_lobby);
            Competition.CompetitionCode.Match.CompetitionMatchController.AttachLobby(_lobby);
            MainFile.Logger.Info("Client network pump attached.");
            _isConnected = true;
            LogPlayers();
            NotifyLobbyStateChanged();
            return CompetitionHostResult.Success;
        }
        catch (ClientConnectionFailedException exception)
        {
            _lastJoinFailedBecauseLobbyExpired = IsExpiredLobby(exception.info);
            LogJoinFailure(stage, actualConnectedClientService, joinResponse, exception.info.GetReason());
            if (_lastJoinFailedBecauseLobbyExpired)
            {
                MainFile.Logger.Error("Join failed: lobby no longer exists.");
            }
            CloseClientLobby();
            DisconnectFailedJoinService(actualConnectedClientService);
            MainFile.Logger.Info("Client cleanup after failed join completed.");
            return CompetitionHostResult.Failure(exception.info.GetReason());
        }
        catch (OperationCanceledException)
        {
            LogJoinFailure(stage, actualConnectedClientService, joinResponse, NetError.Quit);
            CloseClientLobby();
            DisconnectFailedJoinService(actualConnectedClientService);
            MainFile.Logger.Info("Client cleanup after failed join completed.");
            return CompetitionHostResult.Failure(NetError.Quit);
        }
        catch (Exception exception)
        {
            LogJoinFailure(stage, actualConnectedClientService, joinResponse, NetError.InternalError);
            MainFile.Logger.Error($"Join failure detail: {exception}");
            CloseClientLobby();
            DisconnectFailedJoinService(actualConnectedClientService);
            MainFile.Logger.Info("Client cleanup after failed join completed.");
            return CompetitionHostResult.Failure(NetError.InternalError);
        }
        finally
        {
            _isJoining = false;
            _joinFlow = null;
        }
    }

    public void CloseClientLobby()
    {
        if (_isClosing)
        {
            MainFile.Logger.Warn("Client cleanup ignored: cleanup is already in progress.");
            return;
        }

        _isClosing = true;
        _isConnected = false;
        _isJoining = false;
        StartRunLobby? lobbyToClose = _lobby;
        NetClientGameService? serviceToClose = _clientNetService;
        try
        {
            MainFile.Logger.Info("Leaving host lobby.");
            MainFile.Logger.Info("Stopping client network pump.");
            CompetitionNetworkPump.ClearLobby(lobbyToClose);
            Competition.CompetitionCode.Match.CompetitionMatchController.DetachLobby(lobbyToClose);
            _joinFlow?.CancelToken.Cancel();
            if (lobbyToClose != null)
            {
                lobbyToClose.CleanUp(disconnectSession: true, NetError.Quit);
            }

            if (serviceToClose?.IsConnected == true)
            {
                serviceToClose.Disconnect(NetError.Quit);
            }

            MainFile.Logger.Info("Client network disconnected.");
        }
        catch (Exception exception)
        {
            MainFile.Logger.Error($"Failed to leave host lobby: {exception}");
        }
        finally
        {
            _lobby = null;
            _clientNetService = null;
            _listener = null;
            _joinFlow = null;
            _isJoining = false;
            _isConnected = false;
            _isClosing = false;
            _hostDisconnected = false;
            LobbyStateChanged = null;
            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }

            MainFile.Logger.Info("Client state reset.");
        }
    }

    internal void UpdateNetwork()
    {
        // The global CompetitionNetworkPump owns the only update loop.
    }

    internal void OnHostDisconnected(NetErrorInfo info)
    {
        if (_isClosing)
        {
            return;
        }

        _hostDisconnected = true;
        _isConnected = false;
        CompetitionNetworkPump.ClearLobby(_lobby);
        MainFile.Logger.Warn($"Host disconnected: {info.GetReason()}.");
        NotifyLobbyStateChanged();
    }

    internal void NotifyLobbyStateChanged()
    {
        LobbyStateChanged?.Invoke();
    }

    private void LogPlayers()
    {
        if (_lobby == null)
        {
            return;
        }

        foreach (LobbyPlayer player in _lobby.Players)
        {
            string role = player.id == _clientNetService?.HostNetId ? "Host" : "Guest";
            MainFile.Logger.Info($"{role} player: {player.id}.");
        }
    }

    private static bool IsExpiredLobby(NetErrorInfo info)
    {
        return info.GetErrorString().Contains(
            "k_EChatRoomEnterResponseDoesntExist",
            StringComparison.Ordinal);
    }

    private void LogJoinFailure(
        string stage,
        NetClientGameService? actualService,
        ClientLobbyJoinResponseMessage? response,
        NetError reason)
    {
        MainFile.Logger.Error($"Join failed at stage: {stage}.");
        MainFile.Logger.Error($"Actual client service null: {actualService == null}.");
        MainFile.Logger.Error($"Actual client service connected: {actualService?.IsConnected ?? false}.");
        MainFile.Logger.Error($"Controller service is same instance: {ReferenceEquals(_clientNetService, actualService)}.");
        MainFile.Logger.Error($"Join response null: {!response.HasValue}.");
        MainFile.Logger.Error($"Join failure reason: {reason}.");
    }

    private static void DisconnectFailedJoinService(NetClientGameService? service)
    {
        if (service?.IsConnected == true)
        {
            service.Disconnect(NetError.Quit);
        }
    }
}
