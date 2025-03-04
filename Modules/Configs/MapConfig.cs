﻿using System.Text.Json;

namespace RetakesPlugin.Modules.Configs;

public class MapConfig
{
    private readonly string _mapName;
    private readonly string _mapConfigDirectory;
    private readonly string _mapConfigPath;
    private MapConfigData? _mapConfigData;
    
    public MapConfig(string moduleDirectory, string mapName)
    {
        _mapName = mapName;
        _mapConfigDirectory = Path.Combine(moduleDirectory, "map_config");
        _mapConfigPath = Path.Combine(_mapConfigDirectory, $"{mapName}.json");
        _mapConfigData = null;
    }

    public void Load()
    {
        Console.WriteLine($"{RetakesPlugin.LogPrefix}Attempting to load map data from {_mapConfigPath}");
        
        try
        {
            if (!File.Exists(_mapConfigPath))
            {
                throw new FileNotFoundException();
            }

            var jsonData = File.ReadAllText(_mapConfigPath);
            _mapConfigData = JsonSerializer.Deserialize<MapConfigData>(jsonData);

            // TODO: Implement validation to make sure the config is valid / has enough spawns.
            // if (_mapConfigData!.Spawns == null || _mapConfigData.Spawns.Count < 0)
            // {
            //     throw new Exception("No spawns found in config");
            // }
            
            Console.WriteLine($"{RetakesPlugin.LogPrefix}Data loaded from {_mapConfigPath}");
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine($"{RetakesPlugin.LogPrefix}No config for map {_mapName}");
            _mapConfigData = new MapConfigData();
            Save();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{RetakesPlugin.LogPrefix}An error occurred while loading data: {ex.Message}");
        }
    }

    /**
     * This function returns a clone of the spawns list. (free to mutate :>)
     */
    public List<Spawn> GetSpawnsClone()
    {
        if (_mapConfigData == null)
        {
            throw new Exception("Map config data is null");
        }
        
        return _mapConfigData.Spawns.ToList();
    }
    
    public bool AddSpawn(Spawn spawn)
    {
        _mapConfigData ??= new MapConfigData();
        
        // Check if the spawn already exists based on vector and bombsite
        if (_mapConfigData.Spawns.Any(existingSpawn =>
                existingSpawn.Vector == spawn.Vector && existingSpawn.Bombsite == spawn.Bombsite))
        {
            return false; // Spawn already exists, avoid duplication
        }
        
        _mapConfigData.Spawns.Add(spawn);
        
        Save();
        
        // TODO: Figure out why the spawns can't be added on the fly.
        Load();

        return true;
    }

    public void RemoveSpawn(Spawn spawn)
    {
        _mapConfigData ??= new MapConfigData();
        
        if (!_mapConfigData.Spawns.Any(existingSpawn => existingSpawn.Vector == spawn.Vector && existingSpawn.Bombsite == spawn.Bombsite))
        {
            return; // Spawn doesn't exist, avoid removing
        }
  
        _mapConfigData.Spawns.Remove(spawn);
        
        Save();
        
        // TODO: Figure out why the spawns can't be added on the fly.
        Load();
    }
    
    private MapConfigData GetSanitisedMapConfigData()
    {
        if (_mapConfigData == null)
        {
            throw new Exception("Map config data is null");
        }
        
        // Remove any duplicate spawns in the list
        _mapConfigData.Spawns = _mapConfigData.Spawns
            .GroupBy(spawn => new {spawn.Vector, spawn.Bombsite})
            .Select(group => group.First())
            .ToList();

        return _mapConfigData;
    }

    private void Save()
    {
        var jsonString = JsonSerializer.Serialize(GetSanitisedMapConfigData(), new JsonSerializerOptions
        {
            WriteIndented = true
        });

        try
        {
            if (!Directory.Exists(_mapConfigDirectory))
            {
                Directory.CreateDirectory(_mapConfigDirectory);
            }
            
            File.WriteAllText(_mapConfigPath, jsonString);

            Console.WriteLine($"{RetakesPlugin.LogPrefix}Data has been written to {_mapConfigPath}");
        }
        catch (IOException e)
        {
            Console.WriteLine($"{RetakesPlugin.LogPrefix}An error occurred while writing to the file: {e.Message}");
        }
    }

    public static bool IsLoaded(MapConfig? mapConfig, string currentMap)
    {
        if (mapConfig == null || mapConfig._mapName != currentMap)
        {
            return false;
        }

        return true;
    }
}
