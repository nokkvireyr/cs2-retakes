﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using RetakesPlugin.Modules;
using RetakesPlugin.Modules.Enums;
using RetakesPlugin.Modules.Allocators;
using RetakesPlugin.Modules.Configs;
using RetakesPlugin.Modules.Managers;
using Helpers = RetakesPlugin.Modules.Helpers;

namespace RetakesPlugin;

[MinimumApiVersion(147)]
public class RetakesPlugin : BasePlugin
{
    private const string Version = "1.3.4";
    
    #region Plugin info
    public override string ModuleName => "Retakes Plugin";
    public override string ModuleVersion => Version;
    public override string ModuleAuthor => "B3none";
    public override string ModuleDescription => "Community retakes for CS2.";
    #endregion

    #region Constants
    public static readonly string LogPrefix = $"[Retakes {Version}] ";
    public static readonly string MessagePrefix = $"[{ChatColors.Green}Retakes{ChatColors.White}] ";
    #endregion
    
    #region Helpers
    private Translator _translator;
    private GameManager? _gameManager;
    private SpawnManager? _spawnManager;
    private BreakerManager? _breakerManager;
    #endregion
    
    #region Configs
    private MapConfig? _mapConfig;
    private RetakesConfig? _retakesConfig;
    #endregion
    
    #region State
    private Bombsite _currentBombsite = Bombsite.A;
    private CCSPlayerController? _planter;
    private CsTeam _lastRoundWinner;
	private Bombsite? _showingSpawnsForBombsite = null;
    #endregion
    
    public RetakesPlugin()
    {
        _translator = new Translator(Localizer);
    }

    public override void Load(bool hotReload)
    {
        _translator = new Translator(Localizer);
        
        Console.WriteLine($"{LogPrefix}Plugin loaded!");
        
        RegisterListener<Listeners.OnMapStart>(OnMapStart);

        if (hotReload)
        {
            // If a hot reload is detected restart the current map.
            Server.ExecuteCommand($"map {Server.MapName}");
        }
    }
    
    #region Commands
    [ConsoleCommand("css_addspawn", "Adds a spawn point for retakes to the map.")]
    [CommandHelper(minArgs: 2, usage: "[T/CT] [A/B] [Y/N (can be planter / default = if you are stood in a bombsite)]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/root")]
    public void OnCommandAddSpawn(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (!Helpers.DoesPlayerHavePawn(player))
        {
            commandInfo.ReplyToCommand($"{LogPrefix}You must be a player.");
            return;
        }
        
        var team = commandInfo.GetArg(1).ToUpper();
        if (team != "T" && team != "CT")
        {
            commandInfo.ReplyToCommand($"{LogPrefix}You must specify a team [T / CT] - [Value: {team}].");
            return;
        }
        
        var bombsite = commandInfo.GetArg(2).ToUpper();
        if (bombsite != "A" && bombsite != "B")
        {
            commandInfo.ReplyToCommand($"{LogPrefix}You must specify a bombsite [A / B] - [Value: {bombsite}].");
            return;
        }

        var canBePlanter = commandInfo.GetArg(3).ToUpper();
        if (canBePlanter != "" && canBePlanter != "Y" && canBePlanter != "N")
        {
            commandInfo.ReplyToCommand($"{LogPrefix}Invalid value passed to can be a planter [Y / N] - [Value: {canBePlanter}].");
            return;
        }

        if (team != "T" && canBePlanter == "Y")
        {
            commandInfo.ReplyToCommand($"{LogPrefix}It looks like you tried to place a bomb planter spawn for a CT? Is this correct?");
            return;
        }

        var spawn = new Spawn(
            vector: player!.PlayerPawn.Value!.AbsOrigin!,
            qAngle: player!.PlayerPawn.Value!.AbsRotation!
        )
        {
            Team = team == "T" ? CsTeam.Terrorist : CsTeam.CounterTerrorist,
            CanBePlanter = team == "T" && (canBePlanter == "Y" || player.PlayerPawn.Value.InBombZone),
            Bombsite = bombsite == "A" ? Bombsite.A : Bombsite.B
        };

        if (_mapConfig == null)
        {
            commandInfo.ReplyToCommand($"{LogPrefix}Map config not loaded for some reason...");
            return;
        }
        
        var didAddSpawn = _mapConfig.AddSpawn(spawn);
        
        commandInfo.ReplyToCommand($"{LogPrefix}{(didAddSpawn ? "Spawn added" : "Error adding spawn")}");
    }

    [ConsoleCommand("css_debugqueues", "Prints the state of the queues to the console.")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    [RequiresPermissions("@css/root")]
    public void OnCommandDebugState(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (_gameManager == null)
        {
            Console.WriteLine($"{LogPrefix}Game manager not loaded.");
            return;
        }
        
        _gameManager.QueueManager.DebugQueues(true);
    }

    [ConsoleCommand("css_showqangle", "This command shows the players current QAngle")]
    [RequiresPermissions("@css/root")]
    public void OnCommandShowQangle(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (!Helpers.IsValidPlayer(player))
        {
            return;
        }

        var playerPawn = player!.PlayerPawn.Value!;
        var qAngle = playerPawn.AbsRotation;
        var lookTargetPosition = playerPawn.LookTargetPosition;
        var eyeAngles = playerPawn.EyeAngles;

        Server.PrintToChatAll($"{MessagePrefix}lookTargetPosition: x({lookTargetPosition!.X}) y({lookTargetPosition!.Y}) z({lookTargetPosition!.Z})");
        Server.PrintToChatAll($"{MessagePrefix}qAngle: x({qAngle!.X}) y({qAngle!.Y}) z({qAngle!.Z})");
        Server.PrintToChatAll($"{MessagePrefix}eyeAngles: x({eyeAngles!.X}) y({eyeAngles!.Y}) z({eyeAngles!.Z})");
    }

    [ConsoleCommand("css_showspawns", "This command shows the spawns")]
    [CommandHelper(minArgs: 1, usage: "[A/B]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/root")]
    public void OnCommandShowSpawns(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (!Helpers.IsValidPlayer(player))
        {
            return;
        }
        
        var bombsite = commandInfo.GetArg(1).ToUpper();
        if (bombsite != "A" && bombsite != "B")
        {
            commandInfo.ReplyToCommand($"{LogPrefix}You must specify a bombsite [A / B].");
            return;
        }
        
        if (_mapConfig == null)
        {
            commandInfo.ReplyToCommand($"{LogPrefix}Map config not loaded for some reason...");
            return;
        }
        
        var spawns = _mapConfig.GetSpawnsClone().Where(spawn => spawn.Bombsite == (bombsite == "A" ? Bombsite.A : Bombsite.B)).ToList();
        
        if (spawns.Count == 0)
        {
            commandInfo.ReplyToCommand($"{LogPrefix}No spawns found for bombsite {bombsite}.");
            return;
        }
        
        foreach (var spawn in spawns)
        {
			Helpers.ShowSpawn(spawn);
        }

		_showingSpawnsForBombsite = (bombsite == "A" ? Bombsite.A : Bombsite.B);
		commandInfo.ReplyToCommand($"{LogPrefix}Showing {spawns.Count} spawns for bombsite {bombsite}.");
    }

    [ConsoleCommand("css_removespawn", "This command removes the spawn closest to you.")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/root")]
    public void OnCommandRemoveSpawn(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (!Helpers.DoesPlayerHavePawn(player) || _showingSpawnsForBombsite == null || _spawnManager == null)
        {
            return;
        }

        if (_mapConfig == null)
        {
            commandInfo.ReplyToCommand($"{MessagePrefix}Map config not loaded for some reason...");
            return;
        }

		// TODO: Figure out why we need to cast this.
		var spawns = _spawnManager.GetSpawns((Bombsite)_showingSpawnsForBombsite);
        
        if (spawns.Count == 0)
        {
            commandInfo.ReplyToCommand($"{MessagePrefix}No spawns found.");
            return;
        }

		var closestDistance = 9999.9;
		Spawn? closestSpawn = null;

        foreach (var spawn in spawns)
        {
			var distance = Helpers.GetDistanceBetweenVectors(spawn.Vector, player!.PlayerPawn.Value!.AbsOrigin!);

			if (distance > 128.0 || distance > closestDistance)
			{
				continue;
			}

            closestDistance = distance;
            closestSpawn = spawn;
        }

		if (closestSpawn == null)
		{
			commandInfo.ReplyToCommand($"{MessagePrefix}No spawns found within 128 units.");
			return;
		}

		_mapConfig.RemoveSpawn(closestSpawn);
		commandInfo.ReplyToCommand($"{MessagePrefix}Removed spawn.");
    }

    [ConsoleCommand("css_teleport", "This command teleports the player to the given coordinates")]
    [RequiresPermissions("@css/root")]
    public void OnCommandTeleport(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (!Helpers.IsValidPlayer(player))
        {
            return;
        }
        
        if (!player!.PlayerPawn.IsValid)
        {
            return;
        }
        
        if (commandInfo.ArgCount != 4)
        {
            return;
        }

        if (!float.TryParse(commandInfo.ArgByIndex(1), out var positionX))
        {
            return;
        }

        if (!float.TryParse(commandInfo.ArgByIndex(2), out var positionY))
        {
            return;
        }

        if (!float.TryParse(commandInfo.ArgByIndex(3), out var positionZ))
        {
            return;
        }

        player.PlayerPawn.Value?.Teleport(new Vector(positionX, positionY, positionZ), new QAngle(0f,0f,0f), new Vector(0f, 0f, 0f));
    }
    #endregion
    
    #region Listeners
    private void OnMapStart(string mapName)
    {
        Console.WriteLine($"{LogPrefix}OnMapStart listener triggered!");
        
        // Execute the retakes configuration.
        Helpers.ExecuteRetakesConfiguration();
        
        // If we don't have a map config loaded, load it.
        if (!MapConfig.IsLoaded(_mapConfig, Server.MapName))
        {
            _mapConfig = new MapConfig(ModuleDirectory, Server.MapName);
            _mapConfig.Load();
        }
        
        if (!RetakesConfig.IsLoaded(_retakesConfig))
        {
            _retakesConfig = new RetakesConfig(ModuleDirectory);
            _retakesConfig.Load();
        }

		if (_mapConfig == null) 
		{
			throw new Exception("Map config is null");
		}

		_spawnManager = new SpawnManager(
			_translator,
			_mapConfig
		);

        _gameManager = new GameManager(
            _translator,
            new QueueManager(
                _translator,
                _retakesConfig?.RetakesConfigData?.MaxPlayers,
                _retakesConfig?.RetakesConfigData?.TerroristRatio
            ),
            _retakesConfig?.RetakesConfigData?.RoundsToScramble,
            _retakesConfig?.RetakesConfigData?.IsScrambleEnabled
        );
        
        _breakerManager = new BreakerManager(
            _retakesConfig?.RetakesConfigData?.ShouldBreakBreakables,
            _retakesConfig?.RetakesConfigData?.ShouldOpenDoors
        );
    }

    [GameEventHandler]
    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (!Helpers.IsValidPlayer(player))
        {
            return HookResult.Continue;
        }
        
        player.TeamNum = (int)CsTeam.Spectator;
        player.ForceTeamTime = 3600.0f;
        
        Console.WriteLine($"{LogPrefix}OnPlayerConnectFull event fired. {Utilities.GetPlayers().ToList().Count} players connected.");
        if (Utilities.GetPlayers().Where(Helpers.IsPlayerConnected).ToList().Count <= 2)
        {
            Console.WriteLine($"{LogPrefix}First or second player connected, resetting game.");
            Helpers.RestartGame();
        }

        return HookResult.Continue;
    }
    
    [GameEventHandler]
    public HookResult OnRoundPreStart(EventRoundPrestart @event, GameEventInfo info)
    {
        // If we are in warmup, skip.
        if (Helpers.GetGameRules().WarmupPeriod)
        {
            Console.WriteLine($"{LogPrefix}Warmup round, skipping.");
            return HookResult.Continue;
        }
        
        if (_gameManager == null)
        {
            Console.WriteLine($"{LogPrefix}Game manager not loaded.");
            return HookResult.Continue;
        }
        
        // Reset round teams to allow team changes.
        _gameManager.QueueManager.ClearRoundTeams();
        
        // Update Queue status
        Console.WriteLine($"{LogPrefix}Updating queues...");
        _gameManager.QueueManager.DebugQueues(true);
        _gameManager.QueueManager.Update();
        _gameManager.QueueManager.DebugQueues(false);
        Console.WriteLine($"{LogPrefix}Updated queues.");
        
        // Handle team swaps during round pre-start.
        switch (_lastRoundWinner)
        {
            case CsTeam.CounterTerrorist:
                Console.WriteLine($"{LogPrefix}Calling CounterTerroristRoundWin()");
                _gameManager.CounterTerroristRoundWin();
                Console.WriteLine($"{LogPrefix}CounterTerroristRoundWin call complete");
                break;
            
            case CsTeam.Terrorist:
                Console.WriteLine($"{LogPrefix}Calling TerroristRoundWin()");
                _gameManager.TerroristRoundWin();
                Console.WriteLine($"{LogPrefix}TerroristRoundWin call complete");
                break;
        }

        _gameManager.BalanceTeams();
        
        // Set round teams to prevent team changes mid round
        _gameManager.QueueManager.SetRoundTeams();

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // If we are in warmup, skip.
        if (Helpers.GetGameRules().WarmupPeriod)
        {
            Console.WriteLine($"{LogPrefix}Warmup round, skipping.");
            return HookResult.Continue;
        }
        
        if (_gameManager == null)
        {
            Console.WriteLine($"{LogPrefix}Game manager not loaded.");
            return HookResult.Continue;
        }

        if (_spawnManager == null)
        {
            Console.WriteLine($"{LogPrefix}Spawn manager not loaded.");
            return HookResult.Continue;
        }
        
        // Reset round state.
        _breakerManager?.Handle();
        _currentBombsite = Helpers.Random.Next(0, 2) == 0 ? Bombsite.A : Bombsite.B;
        _gameManager.ResetPlayerScores();
		_showingSpawnsForBombsite = null;
        
		_planter = _spawnManager.HandleRoundSpawns(_currentBombsite, _gameManager.QueueManager.ActivePlayers);

        AnnounceBombsite(_currentBombsite);
        
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundPostStart(EventRoundPoststart @event, GameEventInfo info)
    {
        if (_gameManager == null)
        {
            Console.WriteLine($"{LogPrefix}Game manager not loaded.");
            return HookResult.Continue;
        }
        
        // If we are in warmup, skip.
        if (Helpers.GetGameRules().WarmupPeriod)
        {
            Console.WriteLine($"{LogPrefix}Warmup round, skipping.");
            return HookResult.Continue;
        }
        
        Console.WriteLine($"{LogPrefix}Trying to loop valid active players.");
        foreach (var player in _gameManager.QueueManager.ActivePlayers.Where(Helpers.IsValidPlayer))
        {
            Console.WriteLine($"{LogPrefix}[{player.PlayerName}] Adding timer for allocation...");

            if (!Helpers.IsValidPlayer(player))
            {
                continue;
            }
            
            // Strip the player of all of their weapons and the bomb before any spawn / allocation occurs.
            Helpers.RemoveHelmetAndHeavyArmour(player);
            Helpers.RemoveAllWeaponsAndEntities(player);

            // Create a timer to do this as it would occasionally fire too early.
            AddTimer(0.05f, () =>
            {
                if (!Helpers.IsValidPlayer(player))
                {
                    Console.WriteLine($"{LogPrefix}Allocating weapons: Player is not valid.");
                    return;
                }
                
                if (!RetakesConfig.IsLoaded(_retakesConfig) || _retakesConfig!.RetakesConfigData!.EnableFallbackAllocation)
                {
                    Console.WriteLine($"{LogPrefix}Allocating...");
                    WeaponsAllocator.Allocate(player);
                    EquipmentAllocator.Allocate(player);
                    GrenadeAllocator.Allocate(player);
                }
                else
                {
                    Console.WriteLine($"{LogPrefix}Fallback allocation disabled, skipping.");
                }
            });
        }

        return HookResult.Continue;
    }
    
    [GameEventHandler]
    public HookResult OnRoundFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        // If we are in warmup, skip.
        if (Helpers.GetGameRules().WarmupPeriod)
        {
            Console.WriteLine($"{LogPrefix}Warmup round, skipping.");
            return HookResult.Continue;
        }
        
        HandleAutoPlant();
        return HookResult.Continue;
    }
    
    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (_gameManager == null)
        {
            Console.WriteLine($"{LogPrefix}Game manager not loaded.");
            return HookResult.Continue;
        }
        
        var player = @event.Userid;

        if (!Helpers.IsValidPlayer(player) || !Helpers.IsPlayerConnected(player))
        {
            return HookResult.Continue;
        }
        
        // debug and check if the player is in the queue.
        Console.WriteLine($"{LogPrefix}[{player.PlayerName}] Checking ActivePlayers.");
        if (!_gameManager.QueueManager.ActivePlayers.Contains(player))
        {
            Console.WriteLine($"{LogPrefix}[{player.PlayerName}] Checking player pawn {player.PlayerPawn.Value != null}.");
            if (player.PlayerPawn.Value != null && player.PlayerPawn.IsValid && player.PlayerPawn.Value.IsValid)
            {
                Console.WriteLine($"{LogPrefix}[{player.PlayerName}] player pawn is valid {player.PlayerPawn.IsValid} && {player.PlayerPawn.Value.IsValid}.");
                Console.WriteLine($"{LogPrefix}[{player.PlayerName}] calling playerpawn.commitsuicide()");
                player.PlayerPawn.Value.CommitSuicide(false, true);
            }
            
            Console.WriteLine($"{LogPrefix}[{player.PlayerName}] Player not in ActivePlayers, moving to spectator.");
            if (!player.IsBot)
            {
                Console.WriteLine($"{LogPrefix}[{player.PlayerName}] moving to spectator.");
                player.ChangeTeam(CsTeam.Spectator);
            }
            
            return HookResult.Continue;
        }
        else
        {
            Console.WriteLine($"{LogPrefix}[{player.PlayerName}] Player is in ActivePlayers.");
        }

        return HookResult.Continue;
    }
    
    [GameEventHandler(HookMode.Pre)]
    public HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        Console.WriteLine($"{LogPrefix}OnBombPlanted event fired");
        
        AddTimer(4.1f, () => AnnounceBombsite(_currentBombsite, true));
        
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (_gameManager == null)
        {
            Console.WriteLine($"{LogPrefix}Game manager not loaded.");
            return HookResult.Continue;
        }

        var attacker = @event.Attacker;
        var assister = @event.Assister;

        if (Helpers.IsValidPlayer(attacker))
        {
            _gameManager.AddScore(attacker, GameManager.ScoreForKill);
        }

        if (Helpers.IsValidPlayer(assister))
        {
            _gameManager.AddScore(assister, GameManager.ScoreForAssist);
        }

        return HookResult.Continue;
    }
    
    [GameEventHandler]
    public HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info)
    {
        if (_gameManager == null)
        {
            Console.WriteLine($"{LogPrefix}Game manager not loaded.");
            return HookResult.Continue;
        }
        
        var player = @event.Userid;

        if (Helpers.IsValidPlayer(player))
        {
            _gameManager.AddScore(player, GameManager.ScoreForDefuse);
        }

        return HookResult.Continue;
    }
    
    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _lastRoundWinner = (CsTeam)@event.Winner;

        return HookResult.Continue;
    }
    
    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        Console.WriteLine($"{LogPrefix}OnPlayerTeam event fired.");
        
        // Ensure all team join events are silent.
        @event.Silent = true;
        
        if (_gameManager == null)
        {
            Console.WriteLine($"{LogPrefix}Game manager not loaded.");
            return HookResult.Continue;
        }
        
        var player = @event.Userid;

        if (!Helpers.IsValidPlayer(player))
        {
            return HookResult.Continue;
        }
        
        Console.WriteLine($"{LogPrefix}[{player.PlayerName}] {(CsTeam)@event.Oldteam} -> {(CsTeam)@event.Team}");
        
        _gameManager.QueueManager.DebugQueues(true);
        _gameManager.QueueManager.PlayerJoinedTeam(player, (CsTeam)@event.Oldteam, (CsTeam)@event.Team);
        _gameManager.QueueManager.DebugQueues(false);
        
        Console.WriteLine($"{LogPrefix}[{player.PlayerName}] checking to ensure we have active players");
        // If we don't have any active players, setup the active players and restart the game.
        if (_gameManager.QueueManager.ActivePlayers.Count == 0)
        {
            Console.WriteLine($"{LogPrefix}[{player.PlayerName}] clearing round teams to allow team changes");
            _gameManager.QueueManager.ClearRoundTeams();
         
            Console.WriteLine($"{LogPrefix}[{player.PlayerName}] no active players found, calling QueueManager.Update()");
            _gameManager.QueueManager.DebugQueues(true);
            _gameManager.QueueManager.Update();
            _gameManager.QueueManager.DebugQueues(false);
            
            Helpers.RestartGame();
        }

        return HookResult.Continue;
    }
    
    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        
        if (!Helpers.IsValidPlayer(player))
        {
            return HookResult.Continue;
        }
        
        if (_gameManager == null)
        {
            Console.WriteLine($"{LogPrefix}Game manager not loaded.");
            return HookResult.Continue;
        }
        
        _gameManager.QueueManager.RemovePlayerFromQueues(player);
        
        return HookResult.Continue;
    }
    #endregion
    
    // Helpers (with localization so they must be in here until I can figure out how to use it nicely elsewhere)
    private void AnnounceBombsite(Bombsite bombsite, bool onlyCenter = false)
    {
        string[] bombsiteAnnouncers =
        {
            "balkan_epic",
            "leet_epic",
            "professional_epic",
            "professional_fem",
            "seal_epic",
            "swat_epic",
            "swat_fem"
        };

        // Get translation message
        var bombsiteLetter = bombsite == Bombsite.A ? "A" : "B";
        var numTerrorist = Helpers.GetCurrentNumPlayers(CsTeam.Terrorist);
        var numCounterTerrorist = Helpers.GetCurrentNumPlayers(CsTeam.CounterTerrorist);
        
        var isRetakesConfigLoaded = RetakesConfig.IsLoaded(_retakesConfig);
        
        // TODO: Once we implement per client translations this will need to be inside the loop
        var announcementMessage = _translator["bombsite.announcement", bombsiteLetter, numTerrorist, numCounterTerrorist];
        
        foreach (var player in Utilities.GetPlayers())
        {
            if (!onlyCenter)
            {
                // Don't use Server.PrintToChat as it'll add another loop through the players.
                player.PrintToChat($"{MessagePrefix}{announcementMessage}");

                if (!isRetakesConfigLoaded || _retakesConfig!.RetakesConfigData!.EnableBombsiteAnnouncementVoices)
                {
                    // Do this here so every player hears a random announcer each round.
                    var bombsiteAnnouncer = bombsiteAnnouncers[Helpers.Random.Next(bombsiteAnnouncers.Length)];

                    player.ExecuteClientCommand(
                        $"play sounds/vo/agents/{bombsiteAnnouncer}/loc_{bombsite.ToString().ToLower()}_01");
                }
                continue;
            }

            if (isRetakesConfigLoaded && !_retakesConfig!.RetakesConfigData!.EnableBombsiteAnnouncementCenter)
            {
                continue;
            }

            if (player.Team == CsTeam.CounterTerrorist)
            {
                player.PrintToCenter(announcementMessage);
            }
        }
    }

    private void HandleAutoPlant()
    {
        // Ensure the round time for defuse is always set to 1.92
        Server.ExecuteCommand("mp_roundtime_defuse 1.92");
        
        if (_planter != null && Helpers.IsValidPlayer(_planter))
        {
            Helpers.PlantTickingBomb(_planter, _currentBombsite);
        }
        else
        {
            Helpers.TerminateRound(RoundEndReason.RoundDraw);
        }
    }
}
