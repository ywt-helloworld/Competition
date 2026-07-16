using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using System.Runtime.CompilerServices;

namespace Competition.CompetitionCode.Multiplayer;

/// <summary>
/// The persistent owner of Competition's real host service and StartRunLobby.
/// It deliberately has no Godot base type. A Harmony postfix on the original
/// host screen pumps networking so this controller never enters Godot's script
/// dispatch path.
/// </summary>
public sealed class CompetitionHostController
{
    private static CompetitionHostController? _instance;

    private NetHostGameService? _hostNetService;
    private StartRunLobby? _lobby;
    private CompetitionLobbyListener? _listener;
    private bool _isHosting;
    private bool _isStarting;
    private bool _isClosing;

    public StartRunLobby? CurrentLobby => _lobby;

    public NetHostGameService? HostNetService => _hostNetService;

    public bool IsHosting => _isHosting;

    public bool IsStarting => _isStarting;

    public bool IsClosing => _isClosing;

    public event Action? LobbyStateChanged;

    public static CompetitionHostController GetOrCreate()
    {
        return _instance ??= new CompetitionHostController();
    }

    public static CompetitionHostController? TryGet()
    {
        return _instance;
    }

    public async Task<CompetitionHostResult> StartHostAsync()
    {
        if (_isClosing)
        {
            MainFile.Logger.Warn("Create rejected: host lobby is closing.");
            return CompetitionHostResult.Failure(NetError.InternalError);
        }

        if (_isStarting)
        {
            MainFile.Logger.Warn("Create rejected: _isStarting is still true.");
            return CompetitionHostResult.Failure(NetError.InternalError);
        }

        if (_isHosting)
        {
            MainFile.Logger.Warn("Create rejected: _isHosting is still true.");
            return CompetitionHostResult.Failure(NetError.InternalError);
        }

        if (_lobby != null)
        {
            MainFile.Logger.Warn("Create rejected: lobby reference is not null.");
            return CompetitionHostResult.Failure(NetError.InternalError);
        }

        if (_hostNetService != null)
        {
            MainFile.Logger.Warn("Create rejected: host service reference is not null.");
            return CompetitionHostResult.Failure(NetError.InternalError);
        }

        _isStarting = true;
        try
        {
            MainFile.Logger.Info("Starting a new host lobby.");
            MainFile.Logger.Info("Starting Steam host.");
            NetHostGameService hostNetService = new();
            _hostNetService = hostNetService;

            NetErrorInfo? hostError;
            if (SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp"))
            {
                hostError = await hostNetService.StartSteamHost(2);
            }
            else
            {
                hostError = hostNetService.StartENetHost(33771, 2);
            }

            // The user can leave the submenu while Steam is creating the host.
            // In that case the controller has already released this service;
            // never resurrect that stale session.
            if (_isClosing || !ReferenceEquals(_hostNetService, hostNetService))
            {
                if (hostNetService.IsConnected)
                {
                    hostNetService.Disconnect(NetError.Quit);
                }

                MainFile.Logger.Warn("Steam host creation was cancelled before completion.");
                return CompetitionHostResult.Failure(NetError.Quit);
            }

            if (hostError.HasValue)
            {
                MainFile.Logger.Error($"Steam host failed: {hostError.Value.GetReason()}.");
                CloseHostLobby();
                return CompetitionHostResult.Failure(hostError.Value.GetReason());
            }

            if (!hostNetService.IsConnected)
            {
                MainFile.Logger.Error("Steam host failed: the service is not connected.");
                CloseHostLobby();
                return CompetitionHostResult.Failure(NetError.InternalError);
            }

            MainFile.Logger.Info("Steam host created.");
            _listener = new CompetitionLobbyListener(this);
            _lobby = new StartRunLobby(GameMode.Standard, hostNetService, _listener, maxPlayers: 2);
            MainFile.Logger.Info("StartRunLobby created.");

            LobbyPlayer? hostPlayer = _lobby.AddLocalHostPlayer(
                new UnlockState(SaveManager.Instance.Progress),
                SaveManager.Instance.Progress.MaxMultiplayerAscension);
            if (!hostPlayer.HasValue)
            {
                throw new InvalidOperationException("Could not add the local host player to Competition lobby.");
            }

            MainFile.Logger.Info("Local host player added.");
            MainFile.Logger.Info("Character selection deferred until match start.");

            if (!ReferenceEquals(_lobby.NetService, _hostNetService) || _lobby.Players.Count != 1 ||
                _lobby.LocalPlayer.id != hostNetService.NetId || !_lobby.NetService.IsConnected)
            {
                throw new InvalidOperationException("Competition host lobby did not pass post-creation validation.");
            }

            _isHosting = true;
            CompetitionNetworkPump.SetLobby(_lobby);
            MainFile.Logger.Info(
                "Host transport pump active: " +
                $"service={hostNetService.GetType().FullName}, " +
                $"connected={hostNetService.IsConnected.ToString().ToLowerInvariant()}, " +
                $"pumpProcessing={CompetitionNetworkPump.IsProcessing.ToString().ToLowerInvariant()}, " +
                $"lobbyInstance={RuntimeHelpers.GetHashCode(_lobby)}, " +
                $"serviceInstance={RuntimeHelpers.GetHashCode(hostNetService)}.");
            Competition.CompetitionCode.Match.CompetitionMatchController.AttachLobby(_lobby);
            MainFile.Logger.Info("Lobby player count: 1.");
            NotifyLobbyStateChanged();
            return CompetitionHostResult.Success;
        }
        catch (Exception exception)
        {
            MainFile.Logger.Error($"Steam host failed: {exception}");
            CloseHostLobby();
            return CompetitionHostResult.Failure(NetError.InternalError);
        }
        finally
        {
            _isStarting = false;
        }
    }

    public void SyncAscension(int ascension)
    {
        if (_isHosting && _lobby != null && _lobby.NetService.IsConnected)
        {
            _lobby.SyncAscensionChange(ascension);
            Competition.CompetitionCode.Match.CompetitionMatchController.ResetReadyForLobbyChange(_lobby, "ascension changed");
        }
    }

    public void CloseHostLobby()
    {
        if (_isClosing)
        {
            MainFile.Logger.Warn("Host lobby close ignored: cleanup is already in progress.");
            return;
        }

        _isClosing = true;
        // Stop the only network pump before CleanUp disconnects the service.
        _isHosting = false;
        _isStarting = false;
        StartRunLobby? lobbyToClose = _lobby;
        NetHostGameService? serviceToClose = _hostNetService;
        try
        {
            MainFile.Logger.Info("Closing host lobby.");
            MainFile.Logger.Info("Stopping network pump.");
            Competition.CompetitionCode.Match.CompetitionMatchController.DetachLobby(lobbyToClose);
            CompetitionNetworkPump.ClearLobby(lobbyToClose);
            if (lobbyToClose != null)
            {
                try
                {
                    MainFile.Logger.Info("Cleaning StartRunLobby.");
                    // Verified against the original source: this unregisters all
                    // lobby handlers and disconnects the host session when true.
                    lobbyToClose.CleanUp(disconnectSession: true, NetError.Quit);
                }
                catch (Exception exception)
                {
                    MainFile.Logger.Error($"Failed to clean StartRunLobby: {exception}");
                }
            }

            // CleanUp(true) normally performs this disconnect. Keep this
            // fallback so a partial cleanup cannot leave a Steam lobby alive.
            if (serviceToClose?.IsConnected == true)
            {
                MainFile.Logger.Info("Disconnecting host net service.");
                serviceToClose.Disconnect(NetError.Quit);
            }
        }
        catch (Exception exception)
        {
            MainFile.Logger.Error($"Failed to close host lobby: {exception}");
        }
        finally
        {
            _lobby = null;
            _hostNetService = null;
            _listener = null;
            _isHosting = false;
            _isStarting = false;
            _isClosing = false;
            LobbyStateChanged = null;
            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }

            NotifyLobbyStateChanged();
            MainFile.Logger.Info("Host lobby closed.");
            MainFile.Logger.Info("Host state reset.");
        }
    }

    internal void UpdateNetwork()
    {
        // Kept for compatibility with the temporary lobby screen. The global
        // CompetitionNetworkPump is the sole updater now.
    }

    internal void NotifyLobbyStateChanged()
    {
        LobbyStateChanged?.Invoke();
    }
}
