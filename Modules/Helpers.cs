using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using RetakesPlugin.Modules.Configs;
using RetakesPlugin.Modules.Enums;

namespace RetakesPlugin.Modules;

public static class Helpers
{
    internal static readonly Random Random = new();
    
    public static bool IsValidPlayer(CCSPlayerController? player)
    {
        return player != null && player.IsValid;
    }
    
    public static bool DoesPlayerHavePawn(CCSPlayerController? player, bool shouldBeAlive = true)
    {
        if (!IsValidPlayer(player))
        {
            return false;
        }
        
        var playerPawn = player!.PlayerPawn.Value;

        if (playerPawn == null || playerPawn is { AbsOrigin: null, AbsRotation: null })
        {
            return false;
        }
        
        if (shouldBeAlive && !(playerPawn.Health > 0))
        {
            return false;
        }

        return true;
    }
    
    public static T GetAndRemoveRandomItem<T>(List<T> list)
    {
        if (list == null || list.Count == 0)
        {
            throw new ArgumentException("List is null or empty");
        }

        var randomIndex = Random.Next(list.Count);
        var randomItem = list[randomIndex];

        list.RemoveAt(randomIndex);

        return randomItem;
    }

    public static List<T> Shuffle<T>(IEnumerable<T> list)
    {
        var shuffledList = new List<T>(list); // Create a copy of the original list

        var n = shuffledList.Count;
        while (n > 1)
        {
            n--;
            var k = Random.Next(n + 1);
            T value = shuffledList[k];
            shuffledList[k] = shuffledList[n];
            shuffledList[n] = value;
        }

        return shuffledList;
    }
    
    public static CCSGameRules GetGameRules()
    {
        var gameRulesEntities = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules");
        var gameRules = gameRulesEntities.First().GameRules;
        
        if (gameRules == null)
        {
            throw new Exception($"{RetakesPlugin.LogPrefix}Game rules not found!");
        }
        
        return gameRules;
    }
    
    public static void RemoveAllWeaponsAndEntities(CCSPlayerController player)
    {
        if (!IsValidPlayer(player))
        {
            return;
        }
        
        if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.WeaponServices == null)
        {
            return;
        }
        
        foreach (var weapon in player.PlayerPawn.Value.WeaponServices.MyWeapons)
        {
            if (weapon is not { IsValid: true, Value.IsValid: true })
            {
                continue;
            }
            
            // Don't remove a players knife
            if (
                weapon.Value.DesignerName == CsItem.KnifeCT.ToString() 
                || weapon.Value.DesignerName == CsItem.KnifeT.ToString()
            )
            {
                continue;
            }
        
            player.PlayerPawn.Value.RemovePlayerItem(weapon.Value);
            weapon.Value.Remove();
        }
    }
    
    public static bool IsPlayerConnected(CCSPlayerController player)
    {
        return player.Connected == PlayerConnectedState.PlayerConnected;
    }
    
    public static void ExecuteRetakesConfiguration()
    {
        Server.ExecuteCommand("execifexists cs2-retakes/retakes.cfg");
    }
    
    public static int GetCurrentNumPlayers(CsTeam csTeam)
    {
        var players = 0;

        foreach (var player in Utilities.GetPlayers().Where(player => IsValidPlayer(player) && IsPlayerConnected(player)))
        {
            if (player.Team == csTeam)
            {
                players++;
            }
        }

        return players;
    }

    public static bool HasBomb(CCSPlayerController player)
    {
        if (!IsValidPlayer(player))
        {
            return false;
        }
        
        CHandle<CBasePlayerWeapon>? item = null;
        if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.WeaponServices == null)
        {
            return false;
        }

        foreach (var weapon in player.PlayerPawn.Value.WeaponServices.MyWeapons)
        {
            if (weapon is not { IsValid: true, Value.IsValid: true })
            {
                continue;
            }

            if (weapon.Value.DesignerName != "weapon_c4")
            {
                continue;
            }

            item = weapon;
        }

        return item != null && item.Value != null;
    }

    public static void GiveAndSwitchToBomb(CCSPlayerController player)
    {
        player.GiveNamedItem(CsItem.Bomb);
        NativeAPI.IssueClientCommand((int)player.UserId!, "slot5");
    }

    public static void RemoveHelmetAndHeavyArmour(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.ItemServices == null)
        {
            return;
        }
        
        var itemServices = new CCSPlayer_ItemServices(player.PlayerPawn.Value.ItemServices.Handle);
        itemServices.HasHelmet = false;
        itemServices.HasHeavyArmor = false;
    }

    public static void RestartGame()
    {
        if (!GetGameRules().WarmupPeriod)
        {
            CheckRoundDone();
        }

        Server.ExecuteCommand("mp_restartgame 1");
    }
    
    public static void ShowSpawn(Spawn spawn)
    {
		var beam = Utilities.CreateEntityByName<CBeam>("beam") ?? throw new Exception("Failed to create beam entity.");
		beam.StartFrame = 0;
		beam.FrameRate = 0;
		beam.LifeState = 1;
		beam.Width = 5;
		beam.EndWidth = 5;
		beam.Amplitude = 0;
		beam.Speed = 50;
		beam.Flags = 0;
		beam.BeamType = BeamType_t.BEAM_HOSE;
		beam.FadeLength = 10.0f;

		var color = spawn.Team == CsTeam.Terrorist ? (spawn.CanBePlanter ? Color.Orange : Color.Red) : Color.Blue;
		beam.Render = Color.FromArgb(255, color);

		beam.EndPos.X = spawn.Vector.X;
		beam.EndPos.Y = spawn.Vector.Y;
		beam.EndPos.Z = spawn.Vector.Z + 100.0f;

		beam.Teleport(spawn.Vector, new QAngle(IntPtr.Zero), new Vector(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
		beam.DispatchSpawn();
    }
    
    public static void CheckRoundDone()
    {
        var tHumanCount = GetCurrentNumPlayers(CsTeam.Terrorist);
        var ctHumanCount= GetCurrentNumPlayers(CsTeam.CounterTerrorist);
        
        if (tHumanCount == 0 || ctHumanCount == 0) 
        {
            TerminateRound(RoundEndReason.TerroristsWin);
        }
    }

    public static void TerminateRound(RoundEndReason roundEndReason)
    {
        // TODO: once this stops crashing on windows use it there too
        if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            GetGameRules().TerminateRound(0.1f, roundEndReason);
        }
        else
        {
            Console.WriteLine($"{RetakesPlugin.LogPrefix}Windows server detected (Can't use TerminateRound) trying to kill all alive players instead.");
            var alivePlayers = Utilities.GetPlayers()
                .Where(IsValidPlayer)
                .Where(player => player.PawnIsAlive)
                .ToList();

            foreach (var player in alivePlayers)
            {
                player.CommitSuicide(false, true);
            }
        }
    }

    public static CPlantedC4? GetPlantedC4()
    {
        return Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
    }

    public static bool IsInRange(float range, Vector v1, Vector v2)
    {
        var dx = v1.X - v2.X;
        var dy = v1.Y - v2.Y;
        
        return Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dy, 2)) <= range;
    }

	public static double GetDistanceBetweenVectors(Vector v1, Vector v2)
	{
		var dx = v1.X - v2.X;
        var dy = v1.Y - v2.Y;

		return Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dy, 2));
	}

    public static bool IsOnGround(CCSPlayerController player)
    {
        return (player.PlayerPawn.Value!.Flags & (int)PlayerFlags.FL_ONGROUND) != 0;
    }
    
    public static void PlantTickingBomb(CCSPlayerController? player, Bombsite bombsite)
    {
        if (player == null || !player.IsValid)
        {
            throw new Exception("Player controller is not valid");
        }

        var playerPawn = player.PlayerPawn.Value;
        
        if (playerPawn == null || !playerPawn.IsValid)
        {
            throw new Exception("Player pawn is not valid");
        }
        
        if (playerPawn.AbsOrigin == null)
        {
            throw new Exception("Player pawn abs origin is null");
        }
        
        if (playerPawn.AbsRotation == null)
        {
            throw new Exception("Player pawn abs rotation is null");
        }
        
        var plantedC4 = Utilities.CreateEntityByName<CPlantedC4>("planted_c4");

        if (plantedC4 == null)
        {
            throw new Exception("c4 is null");
        }

        if (plantedC4.AbsOrigin == null)
        {
            throw new Exception("c4.AbsOrigin is null");
        }
        
        plantedC4.AbsOrigin.X = playerPawn.AbsOrigin.X;
        plantedC4.AbsOrigin.Y = playerPawn.AbsOrigin.Y;
        plantedC4.AbsOrigin.Z = playerPawn.AbsOrigin.Z;
        plantedC4.HasExploded = false;

        plantedC4.BombSite = (int)bombsite;
        plantedC4.BombTicking = true;
        plantedC4.CannotBeDefused = false;

        plantedC4.DispatchSpawn();

        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
        gameRules.BombPlanted = true;
        gameRules.BombDefused = false;

        SendBombPlantedEvent(player, bombsite);
    }
    
    public static void SendBombPlantedEvent(CCSPlayerController bombCarrier, Bombsite bombsite)
    {
        if (!bombCarrier.IsValid || bombCarrier.PlayerPawn.Value == null)
        {
            return;
        }

        var eventPtr = NativeAPI.CreateEvent("bomb_planted", true);
        NativeAPI.SetEventPlayerController(eventPtr, "userid", bombCarrier.Handle);
        NativeAPI.SetEventInt(eventPtr, "userid", (int)bombCarrier.PlayerPawn.Value.Index);
        NativeAPI.SetEventInt(eventPtr, "site", (int)bombsite);

        NativeAPI.FireEvent(eventPtr, false);
    }
}
